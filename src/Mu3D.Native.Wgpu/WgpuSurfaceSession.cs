using System.Diagnostics;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

/// <summary>Owns a configured wgpu presentation surface and either owns or borrows its device.</summary>
/// <remarks>
/// Dispose a session created for a Windows swap-chain panel on the panel's UI thread. The
/// <c>Mu3DView</c> handler performs this automatically for control-owned sessions.
/// </remarks>
public sealed class WgpuSurfaceSession : IPresentationSurfaceSession, IWgpuPresentationSessionLifecycle
{
    private readonly object surfaceGate = new();
    private readonly NativeSurfaceSource source;
    private readonly WgpuSurfaceHandle surface;
    private readonly WgpuGraphicsDevice graphicsDevice;
    private readonly bool ownsGraphicsDevice;
    private readonly GraphicsTextureUsage textureUsage;
    private uint width;
    private uint height;
    private bool frameInProgress;
    private bool dependentResourceReleaseStarted;
    private bool submittedWorkDrainedForDisposal;
    private bool nativePresentationDetached;
    private bool disposed;

    private WgpuSurfaceSession(
        NativeSurfaceSource source,
        WgpuSurfaceHandle surface,
        WgpuGraphicsDevice graphicsDevice,
        SurfaceCapabilities capabilities,
        SurfaceOutputPlan outputPlan,
        SurfaceAlphaMode alphaMode,
        uint width,
        uint height,
        GraphicsTextureUsage textureUsage,
        bool ownsGraphicsDevice)
    {
        this.source = source;
        this.surface = surface;
        this.graphicsDevice = graphicsDevice;
        this.width = width;
        this.height = height;
        Capabilities = capabilities;
        OutputPlan = outputPlan;
        AlphaMode = alphaMode;
        this.textureUsage = textureUsage;
        this.ownsGraphicsDevice = ownsGraphicsDevice;
    }

    /// <summary>Gets the capabilities used to configure this surface.</summary>
    public SurfaceCapabilities Capabilities { get; }

    /// <summary>Gets the selected format and explicit fallback state.</summary>
    public SurfaceOutputPlan OutputPlan { get; }

    /// <summary>Gets the alpha mode used by the configured native compositor surface.</summary>
    public SurfaceAlphaMode AlphaMode { get; }

    internal GraphicsTextureUsage TextureUsage => textureUsage;

    /// <summary>Gets the backend-independent graphics device compatible with this surface.</summary>
    public GraphicsDevice Device => graphicsDevice;

    /// <summary>Gets the configured surface width in physical pixels.</summary>
    public uint Width
    {
        get
        {
            lock (surfaceGate)
            {
                return width;
            }
        }
    }

    /// <summary>Gets the configured surface height in physical pixels.</summary>
    public uint Height
    {
        get
        {
            lock (surfaceGate)
            {
                return height;
            }
        }
    }

    /// <summary>Gets native presentation timings for the most recently attempted frame.</summary>
    /// <remarks>
    /// These are CPU wall-clock intervals around the native calls. Render includes the caller's
    /// command encoding and queue submission; Present includes backend polling and may therefore
    /// expose GPU/driver back-pressure. They are diagnostics, not GPU timestamp queries.
    /// </remarks>
    public PresentationSurfaceFrameTimings LastFrameTimings { get; private set; }

    /// <summary>Queries the current backing display's HDR characteristics.</summary>
    /// <returns>
    /// An unknown snapshot because the pinned wgpu-native v29 C ABI exposes no display HDR query.
    /// </returns>
    /// <remarks>
    /// The backend-neutral API is present now so a future wgpu-native version can map its live
    /// display query without changing application or renderer interfaces.
    /// </remarks>
    public DisplayHdrInfo QueryDisplayHdrInfo() => DisplayHdrInfo.Unknown;

    /// <summary>Creates a compatible device and configures a platform surface.</summary>
    public static async Task<WgpuSurfaceSession> CreateAsync(
        NativeSurfaceSource source,
        uint width,
        uint height,
        OutputSettings settings,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return await CreateCoreAsync(
            source,
            width,
            height,
            settings,
            timeout,
            cancellationToken,
            cooperativeCancellation: false).ConfigureAwait(false) ??
            throw new InvalidOperationException("The throwing surface request unexpectedly returned no session.");
    }

    internal static Task<WgpuSurfaceSession?> TryCreateForLifecycleAsync(
        NativeSurfaceSource source,
        uint width,
        uint height,
        OutputSettings settings,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        CreateCoreAsync(
            source,
            width,
            height,
            settings,
            timeout,
            cancellationToken,
            cooperativeCancellation: true);

    private static async Task<WgpuSurfaceSession?> CreateCoreAsync(
        NativeSurfaceSource source,
        uint width,
        uint height,
        OutputSettings settings,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        bool cooperativeCancellation)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Surface creation timeout must be positive.");
        }
        if (cooperativeCancellation && cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        WgpuInstanceHandle? instance = null;
        WgpuSurfaceHandle? surface = null;
        WgpuAdapterHandle? adapter = null;
        WgpuDeviceHandle? device = null;
        WgpuGraphicsDevice? graphicsDevice = null;
        try
        {
            instance = WgpuSurfaceProbe.CreateInstance(source.Kind);
            surface = WgpuSurfaceProbe.CreateSurface(instance, source);
            Task<WgpuAdapterHandle> adapterRequest =
                WgpuSurfaceProbe.RequestCompatibleAdapterAsync(instance, surface);
            adapter = cooperativeCancellation
                ? await WgpuSurfaceProbe.TryWaitForOwnedHandleAsync(
                    adapterRequest,
                    timeout,
                    cancellationToken).ConfigureAwait(false)
                : await WgpuSurfaceProbe.WaitForOwnedHandleAsync(
                    adapterRequest,
                    timeout,
                    cancellationToken).ConfigureAwait(false);
            if (adapter is null)
            {
                return null;
            }
            SurfaceCapabilities capabilities = WgpuSurfaceProbe.ReadCapabilities(surface, adapter);
            SurfaceOutputPlan outputPlan = SurfaceOutputNegotiator.Negotiate(capabilities, settings);
            WgpuEnabledFeatures enabledFeatures = WgpuGraphicsDevice.ReadEnabledFeatures(adapter);
            WgpuDeviceLostSink deviceLostSink = new();
            Task<WgpuDeviceHandle> deviceRequest =
                WgpuGraphicsDevice.RequestDeviceAsync(adapter, enabledFeatures, deviceLostSink);
            device = cooperativeCancellation
                ? await WgpuSurfaceProbe.TryWaitForOwnedHandleAsync(
                    deviceRequest,
                    timeout,
                    cancellationToken).ConfigureAwait(false)
                : await WgpuSurfaceProbe.WaitForOwnedHandleAsync(
                    deviceRequest,
                    timeout,
                    cancellationToken).ConfigureAwait(false);
            if (device is null)
            {
                return null;
            }
            graphicsDevice = new WgpuGraphicsDevice(
                instance,
                adapter,
                device,
                deviceLostSink,
                enabledFeatures);
            instance = null;
            adapter = null;
            device = null;
            if (cooperativeCancellation)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }
            else
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            // wgpu-native v29 treats an unsupported surface mode as a fatal native error.
            // Always configure with values from this exact surface's capability snapshot.
            SurfaceAlphaMode alphaMode = SelectAlphaMode(capabilities, settings.AlphaMode);
            Configure(
                surface,
                graphicsDevice,
                outputPlan.Output.Format,
                SelectPresentMode(capabilities),
                alphaMode,
                width,
                height,
                GraphicsTextureUsage.RenderAttachment);

            WgpuSurfaceSession result = new(
                source,
                surface,
                graphicsDevice,
                capabilities,
                outputPlan,
                alphaMode,
                width,
                height,
                GraphicsTextureUsage.RenderAttachment,
                ownsGraphicsDevice: true);
            surface = null;
            graphicsDevice = null;
            return result;
        }
        finally
        {
            surface?.Dispose();
            graphicsDevice?.Dispose();
            device?.Dispose();
            adapter?.Dispose();
            instance?.Dispose();
        }
    }

    /// <summary>
    /// Creates another presentation surface against an existing compatible wgpu device.
    /// </summary>
    /// <param name="source">Native platform presentation source owned by the visual host.</param>
    /// <param name="width">Initial non-zero physical-pixel width.</param>
    /// <param name="height">Initial non-zero physical-pixel height.</param>
    /// <param name="settings">HDR and compositor-alpha policy for this surface.</param>
    /// <param name="sharedDevice">Existing device borrowed for the complete session lifetime.</param>
    /// <returns>An independently disposable surface session that does not own the shared device.</returns>
    /// <remarks>
    /// Dispose every compatible session before disposing the original device-owning session. This
    /// avoids adapter/device creation per lightweight visual while retaining independent native
    /// Surface lifetime and presentation.
    /// </remarks>
    public static WgpuSurfaceSession CreateCompatible(
        NativeSurfaceSource source,
        uint width,
        uint height,
        OutputSettings settings,
        WgpuGraphicsDevice sharedDevice)
        => CreateCompatibleCore(
            source,
            width,
            height,
            settings,
            sharedDevice,
            GraphicsTextureUsage.RenderAttachment,
            GraphicsTextureUsage.None);

    internal static WgpuSurfaceSession CreateSynchronizedColorCompatible(
        NativeSurfaceSource source,
        uint width,
        uint height,
        OutputSettings settings,
        WgpuGraphicsDevice sharedDevice) => CreateCompatibleCore(
            source,
            width,
            height,
            settings,
            sharedDevice,
            GraphicsTextureUsage.CopyDestination,
            GraphicsTextureUsage.RenderAttachment);

    private static WgpuSurfaceSession CreateCompatibleCore(
        NativeSurfaceSource source,
        uint width,
        uint height,
        OutputSettings settings,
        WgpuGraphicsDevice sharedDevice,
        GraphicsTextureUsage preferredTextureUsage,
        GraphicsTextureUsage fallbackTextureUsage)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sharedDevice);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ObjectDisposedException.ThrowIf(
            sharedDevice.State == GraphicsDeviceState.Disposed,
            sharedDevice);

        WgpuSurfaceHandle? surface = null;
        try
        {
            surface = WgpuSurfaceProbe.CreateSurface(sharedDevice.NativeInstance, source);
            SurfaceCapabilities capabilities = WgpuSurfaceProbe.ReadCapabilities(
                surface,
                sharedDevice.NativeAdapter,
                out GraphicsTextureUsage supportedUsages);
            GraphicsTextureUsage textureUsage = SelectCompatibleTextureUsage(
                supportedUsages,
                preferredTextureUsage,
                fallbackTextureUsage);
            SurfaceOutputPlan outputPlan = SurfaceOutputNegotiator.Negotiate(capabilities, settings);
            SurfaceAlphaMode alphaMode = SelectAlphaMode(capabilities, settings.AlphaMode);
            Configure(
                surface,
                sharedDevice,
                outputPlan.Output.Format,
                SelectPresentMode(capabilities),
                alphaMode,
                width,
                height,
                textureUsage);

            WgpuSurfaceSession result = new(
                source,
                surface,
                sharedDevice,
                capabilities,
                outputPlan,
                alphaMode,
                width,
                height,
                textureUsage,
                ownsGraphicsDevice: false);
            surface = null;
            return result;
        }
        finally
        {
            surface?.Dispose();
        }
    }

    internal static GraphicsTextureUsage SelectCompatibleTextureUsage(
        GraphicsTextureUsage supportedUsages,
        GraphicsTextureUsage preferredTextureUsage,
        GraphicsTextureUsage fallbackTextureUsage)
    {
        if ((supportedUsages & preferredTextureUsage) == preferredTextureUsage)
        {
            return preferredTextureUsage;
        }
        if (fallbackTextureUsage != GraphicsTextureUsage.None &&
            (supportedUsages & fallbackTextureUsage) == fallbackTextureUsage)
        {
            return fallbackTextureUsage;
        }
        throw new NotSupportedException(
            $"The native presentation surface supports {supportedUsages}, not the requested " +
            $"{preferredTextureUsage} or fallback {fallbackTextureUsage} usage.");
    }

    /// <summary>Reconfigures the surface after a non-zero pixel-size change.</summary>
    public void Resize(uint width, uint height)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        lock (surfaceGate)
        {
            ObjectDisposedException.ThrowIf(disposed || dependentResourceReleaseStarted, this);
            Configure(
                surface,
                graphicsDevice,
                OutputPlan.Output.Format,
                SelectPresentMode(Capabilities),
                AlphaMode,
                width,
                height,
                textureUsage);
            this.width = width;
            this.height = height;
        }
    }

    /// <summary>Clears one acquired surface texture to an FP32 linear color and presents it.</summary>
    /// <param name="red">The finite linear red component. Extended-range values are allowed.</param>
    /// <param name="green">The finite linear green component. Extended-range values are allowed.</param>
    /// <param name="blue">The finite linear blue component. Extended-range values are allowed.</param>
    /// <param name="alpha">The finite linear alpha component.</param>
    /// <returns>The surface acquisition and presentation result.</returns>
    /// <remarks>
    /// A timeout, outdated surface, or lost surface is reported without submitting a frame. Call
    /// <see cref="Resize(uint, uint)"/> before retrying an outdated or lost surface. This method
    /// reports successful GPU presentation, not independently measured physical HDR luminance.
    /// </remarks>
    public PresentationSurfaceFrameStatus ClearAndPresent(
        float red,
        float green,
        float blue,
        float alpha = 1.0f)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ThrowIfNotFinite(red, nameof(red));
        ThrowIfNotFinite(green, nameof(green));
        ThrowIfNotFinite(blue, nameof(blue));
        ThrowIfNotFinite(alpha, nameof(alpha));

        return PresentGraphicsFrame(
            pipeline: null,
            new GraphicsClearColor(red, green, blue, alpha));
    }

    /// <summary>
    /// Presents one visible HDR-capable triangle through the backend-independent Mu3D graphics API.
    /// </summary>
    /// <returns>The surface acquisition and presentation result.</returns>
    /// <remarks>
    /// The triangle is authored in the configured presentation space with linear RGB
    /// <c>(0.05, 0.15, 2.0)</c>. A successful result proves the shared device/resource/command path
    /// reached the surface; physical HDR and color accuracy remain separate measurements.
    /// </remarks>
    public PresentationSurfaceFrameStatus PresentBackendIndependentTriangle()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        const string shaderCode = """
            @vertex fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4f {
                let positions = array(vec2f(-0.72, -0.65), vec2f(0.72, -0.65), vec2f(0.0, 0.72));
                return vec4f(positions[vertex_index], 0.0, 1.0);
            }

            @fragment fn fs_main() -> @location(0) vec4f {
                return vec4f(0.05, 0.15, 2.0, 1.0);
            }
            """;
        GraphicsTextureFormat format = MapGraphicsFormat(OutputPlan.Output.Format);
        using GraphicsShaderModule shader = graphicsDevice.CreateShaderModule(
            new GraphicsShaderModuleDescriptor(shaderCode, "surface triangle shader"));
        using GraphicsRenderPipeline pipeline = graphicsDevice.CreateRenderPipeline(
            new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_main",
                shader,
                "fs_main",
                format,
                label: "surface triangle pipeline"));
        return PresentGraphicsFrame(pipeline, new GraphicsClearColor(0, 0, 0, 1));
    }

    /// <summary>
    /// Presents an extended-linear sRGB diagnostic with a smooth 0–4 ramp and six reference bands.
    /// </summary>
    /// <returns>The surface acquisition and presentation result.</returns>
    /// <remarks>
    /// The upper half is a continuous neutral ramp from 0 to 4. The lower half contains equal-width
    /// neutral bands at 0, 0.18, 0.5, 1, 2 and 4. Values are authored directly in the configured
    /// extended-linear sRGB presentation space; this does not exercise document-profile conversion.
    /// </remarks>
    public PresentationSurfaceFrameStatus PresentExtendedLinearSrgbReferencePattern()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return AcquireRenderAndPresent(target => WgpuReferencePatternRenderer.Render(graphicsDevice, target));
    }

    /// <summary>Acquires one surface texture, records caller-provided graphics work and presents it.</summary>
    /// <param name="render">
    /// A synchronous callback that records and submits work targeting the supplied backend-independent
    /// texture. The texture is valid only for the callback duration and must not be disposed or retained.
    /// </param>
    /// <returns>The surface acquisition and presentation result.</returns>
    /// <remarks>
    /// Timeout, outdated and lost acquisition states return without invoking the callback. The caller
    /// owns every non-surface resource and must finish queue submission before returning. Calling a
    /// presentation method on this session from inside the callback is rejected before native surface
    /// acquisition because a surface can have only one current presentation texture.
    /// </remarks>
    public PresentationSurfaceFrameStatus RenderAndPresent(Action<GraphicsTexture> render)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(render);
        return AcquireRenderAndPresent(render);
    }

    private unsafe PresentationSurfaceFrameStatus PresentGraphicsFrame(
        GraphicsRenderPipeline? pipeline,
        GraphicsClearColor clearColor)
    {
        return AcquireRenderAndPresent(target =>
        {
            using GraphicsCommandEncoder encoder = graphicsDevice.CreateCommandEncoder(
                "surface frame encoder");
            using (GraphicsRenderPassEncoder renderPass = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(target, clearColor: clearColor),
                    "surface frame pass")))
            {
                if (pipeline is not null)
                {
                    renderPass.SetPipeline(pipeline);
                    renderPass.Draw(3);
                }
            }
            using GraphicsCommandBuffer commands = encoder.Finish("surface frame commands");
            graphicsDevice.Queue.Submit(commands);
        });
    }

    private unsafe PresentationSurfaceFrameStatus AcquireRenderAndPresent(Action<GraphicsTexture> render)
    {
        lock (surfaceGate)
        {
            ObjectDisposedException.ThrowIf(disposed || dependentResourceReleaseStarted, this);
            if (frameInProgress)
            {
                throw new InvalidOperationException(
                    "A presentation surface frame cannot be acquired while its previous render callback is still active.");
            }

            frameInProgress = true;
            try
            {
                long totalStarted = Stopwatch.GetTimestamp();
                long acquireStarted = totalStarted;
                WGPUSurfaceTexture surfaceTexture = default;
                WgpuNative.wgpuSurfaceGetCurrentTexture(surface.DangerousGetPointer(), &surfaceTexture);
                double acquireMilliseconds = Stopwatch.GetElapsedTime(acquireStarted).TotalMilliseconds;
                PresentationSurfaceFrameStatus frameStatus = MapFrameStatus(surfaceTexture.status);
                if (frameStatus is not (PresentationSurfaceFrameStatus.PresentedOptimal or
                    PresentationSurfaceFrameStatus.PresentedSuboptimal))
                {
                    ReleaseTexture(surfaceTexture.texture);
                    LastFrameTimings = new PresentationSurfaceFrameTimings(
                        acquireMilliseconds,
                        0,
                        0,
                        Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds);
                    return frameStatus;
                }
                if (surfaceTexture.texture is null)
                {
                    throw new InvalidOperationException(
                        $"Surface acquisition reported {surfaceTexture.status} without a texture.");
                }

                GraphicsTextureDescriptor descriptor = new(
                    new GraphicsExtent3D(width, height),
                    MapGraphicsFormat(OutputPlan.Output.Format),
                    textureUsage,
                    label: "acquired surface texture");
                using GraphicsTexture target = new WgpuGraphicsTexture(
                    graphicsDevice,
                    descriptor,
                    surfaceTexture.texture);
                long renderStarted = Stopwatch.GetTimestamp();
                render(target);
                double renderMilliseconds = Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds;
                long presentStarted = Stopwatch.GetTimestamp();
                WGPUStatus presentStatus = WgpuNative.wgpuSurfacePresent(surface.DangerousGetPointer());
                graphicsDevice.ThrowIfNativeErrors("backend-independent frame/present");
                double presentMilliseconds = Stopwatch.GetElapsedTime(presentStarted).TotalMilliseconds;
                LastFrameTimings = new PresentationSurfaceFrameTimings(
                    acquireMilliseconds,
                    renderMilliseconds,
                    presentMilliseconds,
                    Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds);
                if (presentStatus != WGPUStatus.Success)
                {
                    throw new InvalidOperationException(
                        $"wgpuSurfacePresent failed with status {presentStatus}.");
                }
                return frameStatus;
            }
            finally
            {
                frameInProgress = false;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (surfaceGate)
        {
            if (disposed)
            {
                return;
            }

            dependentResourceReleaseStarted = true;
            disposed = true;
            if (ownsGraphicsDevice)
            {
                DrainSubmittedWorkForDisposalOnce();
            }
            DetachNativePresentation();
            Unconfigure(surface);
            surface.Dispose();
            if (ownsGraphicsDevice)
            {
                graphicsDevice.Dispose();
            }
        }
    }

    internal void DrainAndReleaseDependentResources(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        lock (surfaceGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            dependentResourceReleaseStarted = true;
            if (ownsGraphicsDevice)
            {
                DrainSubmittedWorkForDisposalOnce();
            }
            release();
        }
    }

    void IWgpuPresentationSessionLifecycle.DrainAndReleaseDependentResources(Action release) =>
        DrainAndReleaseDependentResources(release);

    private void DrainSubmittedWorkForDisposalOnce()
    {
        if (submittedWorkDrainedForDisposal)
        {
            return;
        }

        graphicsDevice.DrainSubmittedWorkForDisposal();
        submittedWorkDrainedForDisposal = true;
    }

    private void DetachNativePresentation()
    {
        if (nativePresentationDetached ||
            source.Kind != NativeSurfaceKind.WindowsSwapChainPanel)
        {
            return;
        }

        nativePresentationDetached = true;
        try
        {
            int result = WindowsSwapChainPanelInterop.DetachSwapChain(source.Handle);
            if (result < 0)
            {
                Debug.WriteLine(
                    $"[Mu3D] ISwapChainPanelNative.SetSwapChain(null) failed with HRESULT 0x{result:X8}.");
            }
        }
        catch (Exception exception)
        {
            // Disposal must continue so wgpu can release its surface/device even if the platform
            // panel has already become unavailable. Normal MAUI teardown reaches this on the UI
            // thread before the handler releases its retained panel interface.
            Debug.WriteLine($"[Mu3D] Failed to detach the Windows swap chain: {exception}");
        }
    }

    private static unsafe void Configure(
        WgpuSurfaceHandle surface,
        WgpuGraphicsDevice graphicsDevice,
        PresentationFormat format,
        SurfacePresentMode presentMode,
        SurfaceAlphaMode alphaMode,
        uint width,
        uint height,
        GraphicsTextureUsage textureUsage)
    {
        WGPUSurfaceColorManagement colorManagement = new()
        {
            chain = new WGPUChainedStruct { sType = WGPUSType.SurfaceColorManagement },
            colorSpace = WGPUPredefinedColorSpace.SRGB,
            toneMappingMode = WGPUToneMappingMode.Extended,
        };
        bool isHdr = format == PresentationFormat.Rgba16Float;
        WGPUSurfaceConfiguration configuration = new()
        {
            nextInChain = isHdr ? &colorManagement.chain : null,
            device = graphicsDevice.NativeDevice,
            format = MapFormat(format),
            usage = WgpuGraphicsMapper.MapTextureUsage(textureUsage),
            width = width,
            height = height,
            alphaMode = MapAlphaMode(alphaMode),
            presentMode = MapPresentMode(presentMode),
        };
        WgpuNative.wgpuSurfaceConfigure(surface.DangerousGetPointer(), &configuration);
        graphicsDevice.ThrowIfNativeErrors("surface configuration");
    }

    private static unsafe void Unconfigure(WgpuSurfaceHandle surface) =>
        WgpuNative.wgpuSurfaceUnconfigure(surface.DangerousGetPointer());

    internal static PresentationSurfaceFrameStatus MapFrameStatus(
        WGPUSurfaceGetCurrentTextureStatus status) => status switch
        {
            WGPUSurfaceGetCurrentTextureStatus.SuccessOptimal =>
                PresentationSurfaceFrameStatus.PresentedOptimal,
            WGPUSurfaceGetCurrentTextureStatus.SuccessSuboptimal =>
                PresentationSurfaceFrameStatus.PresentedSuboptimal,
            WGPUSurfaceGetCurrentTextureStatus.Timeout => PresentationSurfaceFrameStatus.Timeout,
            WGPUSurfaceGetCurrentTextureStatus.Outdated => PresentationSurfaceFrameStatus.Outdated,
            WGPUSurfaceGetCurrentTextureStatus.Lost => PresentationSurfaceFrameStatus.Lost,
            _ => PresentationSurfaceFrameStatus.Error,
        };

    private static unsafe void ReleaseTexture(WGPUTextureImpl* texture)
    {
        if (texture is not null)
        {
            WgpuNative.wgpuTextureRelease(texture);
        }
    }

    private static void ThrowIfNotFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                value,
                "Clear color components must be finite.");
        }
    }

    private static WGPUTextureFormat MapFormat(PresentationFormat format) => format switch
    {
        PresentationFormat.Bgra8Unorm => WGPUTextureFormat.BGRA8Unorm,
        PresentationFormat.Bgra8UnormSrgb => WGPUTextureFormat.BGRA8UnormSrgb,
        PresentationFormat.Rgba8Unorm => WGPUTextureFormat.RGBA8Unorm,
        PresentationFormat.Rgba8UnormSrgb => WGPUTextureFormat.RGBA8UnormSrgb,
        PresentationFormat.Rgba16Float => WGPUTextureFormat.RGBA16Float,
        PresentationFormat.Rgb10A2Unorm => WGPUTextureFormat.RGB10A2Unorm,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported surface format."),
    };

    private static GraphicsTextureFormat MapGraphicsFormat(PresentationFormat format) => format switch
    {
        PresentationFormat.Bgra8Unorm => GraphicsTextureFormat.Bgra8Unorm,
        PresentationFormat.Bgra8UnormSrgb => GraphicsTextureFormat.Bgra8UnormSrgb,
        PresentationFormat.Rgba8Unorm => GraphicsTextureFormat.Rgba8Unorm,
        PresentationFormat.Rgba8UnormSrgb => GraphicsTextureFormat.Rgba8UnormSrgb,
        PresentationFormat.Rgba16Float => GraphicsTextureFormat.Rgba16Float,
        PresentationFormat.Rgb10A2Unorm => GraphicsTextureFormat.Rgb10A2Unorm,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported surface format."),
    };

    private static SurfacePresentMode SelectPresentMode(SurfaceCapabilities capabilities)
    {
        if (capabilities.PresentModes.Contains(SurfacePresentMode.Fifo))
        {
            return SurfacePresentMode.Fifo;
        }

        return capabilities.PresentModes.FirstOrDefault(
            static mode => mode != SurfacePresentMode.Unknown,
            SurfacePresentMode.Unknown) is { } selected && selected != SurfacePresentMode.Unknown
            ? selected
            : throw new InvalidOperationException("Surface advertised no supported present mode.");
    }

    private static SurfaceAlphaMode SelectAlphaMode(
        SurfaceCapabilities capabilities,
        SurfaceAlphaMode requested)
    {
        if (requested == SurfaceAlphaMode.Unknown || !Enum.IsDefined(requested))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                requested,
                "The requested surface alpha mode is not valid for configuration.");
        }
        if (requested != SurfaceAlphaMode.Automatic)
        {
            return capabilities.AlphaModes.Contains(requested)
                ? requested
                : throw new NotSupportedException(
                    $"The surface does not support requested alpha mode {requested}.");
        }
        if (capabilities.AlphaModes.Contains(SurfaceAlphaMode.Opaque))
        {
            return SurfaceAlphaMode.Opaque;
        }

        return capabilities.AlphaModes.FirstOrDefault(
            static mode => mode != SurfaceAlphaMode.Unknown,
            SurfaceAlphaMode.Unknown) is { } selected && selected != SurfaceAlphaMode.Unknown
            ? selected
            : throw new InvalidOperationException("Surface advertised no supported alpha mode.");
    }

    private static WGPUPresentMode MapPresentMode(SurfacePresentMode mode) => mode switch
    {
        SurfacePresentMode.Fifo => WGPUPresentMode.Fifo,
        SurfacePresentMode.FifoRelaxed => WGPUPresentMode.FifoRelaxed,
        SurfacePresentMode.Immediate => WGPUPresentMode.Immediate,
        SurfacePresentMode.Mailbox => WGPUPresentMode.Mailbox,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported present mode."),
    };

    private static WGPUCompositeAlphaMode MapAlphaMode(SurfaceAlphaMode mode) => mode switch
    {
        SurfaceAlphaMode.Automatic => WGPUCompositeAlphaMode.Auto,
        SurfaceAlphaMode.Opaque => WGPUCompositeAlphaMode.Opaque,
        SurfaceAlphaMode.Premultiplied => WGPUCompositeAlphaMode.Premultiplied,
        SurfaceAlphaMode.Unpremultiplied => WGPUCompositeAlphaMode.Unpremultiplied,
        SurfaceAlphaMode.Inherit => WGPUCompositeAlphaMode.Inherit,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported alpha mode."),
    };
}

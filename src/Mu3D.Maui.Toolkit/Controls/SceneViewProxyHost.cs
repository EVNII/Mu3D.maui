using MauiColor = Microsoft.Maui.Graphics.Color;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>
/// Owns one graphics device and renderer shared by lightweight <see cref="SceneViewProxy"/> outputs.
/// </summary>
/// <remarks>
/// Every proxy remains a GPU-native presentation output in the MAUI visual hierarchy without GPU
/// readback or image encoding. Opaque proxies use direct HDR Surfaces; transparent proxies use the
/// platform compositor's alpha carrier and retain HDR where that carrier supports it. The
/// hidden bootstrap view owns the default device lifecycle; compatible proxy sessions borrow that
/// device and own only their individual output.
/// </remarks>
public sealed class SceneViewProxyHost : Grid
{
    private static readonly OutputSettings DefaultProxyOutputSettings = OutputSettings.Default with
    {
        AlphaMode = SceneViewProxy.PlatformTransparentAlphaMode,
    };
    private readonly Mu3DView deviceView;
    private readonly HashSet<SceneViewProxy> proxies =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SceneViewProxy, ProxySurfaceState> surfaceStates =
        new(ReferenceEqualityComparer.Instance);
    private readonly Queue<SceneViewProxy> pendingFrames = [];
    private readonly HashSet<SceneViewProxy> queuedFrames =
        new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<Scene> retainedScenes = [];
    private WgpuGraphicsDevice? sharedDevice;
    private SceneRenderer? renderer;
    private GraphicsTexture? depthTexture;
    private GraphicsExtent3D depthExtent;
    private GraphicsTexture? alphaConversionTexture;
    private GraphicsExtent3D alphaConversionExtent;
    private GraphicsTextureFormat? alphaConversionFormat;
    private GraphicsShaderModule? colorTransferShader;
    private GraphicsBindGroupLayout? colorTransferBindGroupLayout;
    private GraphicsPipelineLayout? colorTransferPipelineLayout;
    private GraphicsRenderPipeline? colorTransferPipeline;
    private GraphicsTextureFormat? colorTransferFormat;
    private ColorTransferMode? colorTransferMode;
    private bool pumpScheduled;
    private bool frameInProgress;
    private ProxySurfaceState? activeSurfaceState;
    private Task<PresentationSurfaceFrameStatus>? activeFrameTask;

    /// <summary>Identifies the <see cref="OutputSettings"/> bindable property.</summary>
    public static readonly BindableProperty OutputSettingsProperty = BindableProperty.Create(
        nameof(OutputSettings),
        typeof(OutputSettings),
        typeof(SceneViewProxyHost),
        DefaultProxyOutputSettings,
        validateValue: static (_, value) => value is OutputSettings,
        propertyChanged: static (bindable, _, value) =>
            ((SceneViewProxyHost)bindable).ApplyOutputSettings((OutputSettings)value));

    /// <summary>Identifies the <see cref="IsRenderingEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsRenderingEnabledProperty = BindableProperty.Create(
        nameof(IsRenderingEnabled),
        typeof(bool),
        typeof(SceneViewProxyHost),
        true,
        propertyChanged: static (bindable, _, value) =>
        {
            SceneViewProxyHost host = (SceneViewProxyHost)bindable;
            if ((bool)value)
            {
                host.QueueAllFrames();
            }
            else
            {
                host.SuspendAllProxySurfaces();
            }
        });

    /// <summary>Initializes one visual-tree-scoped shared-device Surface host.</summary>
    public SceneViewProxyHost()
    {
        deviceView = new Mu3DView
        {
            WidthRequest = 1d,
            HeightRequest = 1d,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
            Opacity = 0d,
        };
        deviceView.Draw += OnDeviceDraw;
        deviceView.PresentationSessionChanged += OnPresentationSessionChanged;
        deviceView.SurfaceError += OnSurfaceError;
        Children.Add(deviceView);
    }

    /// <summary>
    /// Gets or sets the dynamic-range policy shared by every Proxy output.
    /// </summary>
    /// <remarks>
    /// The default is HDR-first. XAML consumers use an opaque
    /// <see cref="SceneViewProxy.SceneBackgroundColor"/> for direct HDR presentation or transparent
    /// for the compositor alpha carrier; fractional background composition is not part of the
    /// current Proxy contract.
    /// </remarks>
    public OutputSettings OutputSettings
    {
        get => (OutputSettings)GetValue(OutputSettingsProperty);
        set => SetValue(OutputSettingsProperty, value);
    }

    /// <summary>Gets or sets whether this host may create surfaces and present queued frames.</summary>
    public bool IsRenderingEnabled
    {
        get => (bool)GetValue(IsRenderingEnabledProperty);
        set => SetValue(IsRenderingEnabledProperty, value);
    }

    /// <summary>Gets the number of loaded proxies registered with this host.</summary>
    public int ProxyCount => proxies.Count;

    /// <summary>Gets the number of configured native Proxy Surfaces.</summary>
    public int ActiveSurfaceCount => surfaceStates.Values.Sum(static state =>
        (state.Session is null ? 0 : 1) + (state.MaskSession is null ? 0 : 1));

    /// <summary>Gets the number of active direct native Surfaces eligible for physical HDR.</summary>
    public int ActiveDirectSurfaceCount => surfaceStates.Values.Count(
        static state => state.Session is WgpuSurfaceSession);

    /// <summary>Gets the number of active compositor-owned binary-mask drawing surfaces.</summary>
    public int ActiveMaskSurfaceCount => surfaceStates.Values.Count(
        static state => state.Session is WgpuTexturePresentationSession ||
            state.MaskSession is WgpuTexturePresentationSession);

    /// <summary>Gets the number of proxies waiting for Surface creation or presentation.</summary>
    public int PendingFrameCount => pendingFrames.Count;

    /// <summary>Gets whether the bootstrap surface has published the shared graphics device.</summary>
    /// <remarks>Proxy frames queue only while a shared device exists; a false value means every
    /// proxy is still waiting for the hidden bootstrap surface's session.</remarks>
    public bool HasSharedDevice => sharedDevice is not null;

    /// <summary>Gets the worker-owned renderer shared by every ready proxy after bootstrap.</summary>
    /// <remarks>
    /// Treat this reference as diagnostic while rendering is enabled. The host serializes renderer
    /// mutation on its frame worker; applications must not invoke the non-thread-safe renderer
    /// concurrently.
    /// </remarks>
    public SceneRenderer? Renderer => renderer;

    /// <summary>Forwards errors from the MAUI-owned graphics bootstrap Surface.</summary>
    public event EventHandler<SurfaceErrorEventArgs>? SurfaceError;

    /// <summary>Replaces the borrowed scene set retained in the shared renderer GPU cache.</summary>
    public void SetRetainedScenes(IEnumerable<Scene>? warmScenes)
    {
        Scene[] snapshot = warmScenes?.ToArray() ?? [];
        if (snapshot.Any(static scene => scene is null))
        {
            throw new ArgumentException("A warm scene snapshot cannot contain null.", nameof(warmScenes));
        }
        retainedScenes = snapshot;
    }

    internal void RegisterProxy(SceneViewProxy proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (!proxies.Add(proxy))
        {
            return;
        }

        surfaceStates.Add(proxy, new ProxySurfaceState(
            proxy.NativeSurfaceSource,
            proxy.TexturePresentationSink));
        proxy.SetOutputSettings(OutputSettings);
        QueueFrame(proxy);
    }

    internal void UnregisterProxy(SceneViewProxy proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        proxies.Remove(proxy);
        queuedFrames.Remove(proxy);
        if (surfaceStates.Remove(proxy, out ProxySurfaceState? state))
        {
            DisposeStateSession(state);
        }
    }

    internal void SuspendProxy(SceneViewProxy proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (Dispatcher.IsDispatchRequired)
        {
            _ = Dispatcher.Dispatch(() => SuspendProxy(proxy));
            return;
        }

        RemovePendingFrame(proxy);
        if (surfaceStates.TryGetValue(proxy, out ProxySurfaceState? state))
        {
            DisposeStateSession(state);
        }
    }

    internal void OnProxyNativeSurfaceChanged(
        SceneViewProxy proxy,
        NativeSurfaceSource? source)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (Dispatcher.IsDispatchRequired)
        {
            _ = Dispatcher.Dispatch(() => OnProxyNativeSurfaceChanged(proxy, source));
            return;
        }
        if (!surfaceStates.TryGetValue(proxy, out ProxySurfaceState? state))
        {
            return;
        }
        if (state.Source == source)
        {
            QueueFrame(proxy);
            return;
        }

        DisposeStateSession(state);
        state.Source = source;
        QueueFrame(proxy);
    }

    internal void OnProxyTexturePresentationSinkChanged(
        SceneViewProxy proxy,
        IWgpuTexturePresentationSink? sink)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (Dispatcher.IsDispatchRequired)
        {
            _ = Dispatcher.Dispatch(() =>
                OnProxyTexturePresentationSinkChanged(proxy, sink));
            return;
        }
        if (!surfaceStates.TryGetValue(proxy, out ProxySurfaceState? state))
        {
            return;
        }
        if (ReferenceEquals(state.TextureSink, sink))
        {
            QueueFrame(proxy);
            return;
        }

        DisposeStateSession(state);
        state.TextureSink = sink;
        QueueFrame(proxy);
    }

    internal void QueueFrame(SceneViewProxy proxy)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (Dispatcher.IsDispatchRequired)
        {
            _ = Dispatcher.Dispatch(() => QueueFrame(proxy));
            return;
        }
        if (!IsRenderingEnabled || sharedDevice is null ||
            !proxies.Contains(proxy) || !proxy.IsRenderingEnabled ||
            proxy.Scene is null || proxy.Camera is null ||
            (proxy.NativeSurfaceSource is null && proxy.TexturePresentationSink is null) ||
            !queuedFrames.Add(proxy))
        {
            return;
        }

        pendingFrames.Enqueue(proxy);
        SchedulePump();
    }

    private void SchedulePump()
    {
        if (pumpScheduled || frameInProgress || !IsRenderingEnabled || sharedDevice is null)
        {
            return;
        }
        pumpScheduled = true;
        // A silently dropped dispatch would otherwise latch pumpScheduled and starve every proxy.
        if (!Dispatcher.Dispatch(PumpOneFrame))
        {
            pumpScheduled = false;
        }
    }

    private void PumpOneFrame()
    {
        pumpScheduled = false;
        if (frameInProgress || !IsRenderingEnabled || sharedDevice is null)
        {
            return;
        }

        while (pendingFrames.TryDequeue(out SceneViewProxy? proxy))
        {
            queuedFrames.Remove(proxy);
            if (!proxies.Contains(proxy) || !proxy.IsRenderingEnabled ||
                proxy.Scene is not Scene scene || proxy.Camera is not Camera camera ||
                (proxy.NativeSurfaceSource is null && proxy.TexturePresentationSink is null) ||
                !surfaceStates.TryGetValue(proxy, out ProxySurfaceState? state))
            {
                continue;
            }

            long version = proxy.RenderVersion;
            IPresentationSurfaceSession session;
            IPresentationSurfaceSession? maskSession;
            Scene[] retainedSnapshot = [.. retainedScenes];
            uint width;
            uint height;
            try
            {
                (width, height) = proxy.GetSurfaceSize();
                session = EnsureProxySession(
                    state,
                    proxy.NativeSurfaceSource,
                    proxy.TexturePresentationSink,
                    width,
                    height,
                    proxy.EffectiveOutputSettings,
                    sharedDevice);
                maskSession = state.MaskSession;
                if (session.Width != width || session.Height != height)
                {
                    session.Resize(width, height);
                }
                if (maskSession is not null &&
                    (maskSession.Width != width || maskSession.Height != height))
                {
                    maskSession.Resize(width, height);
                }
            }
            catch (Exception exception)
            {
                DisposeStateSession(state);
                proxy.ReportSurfaceError(version, exception);
                continue;
            }

            frameInProgress = true;
            activeSurfaceState = state;
            state.FrameInProgress = true;
            MauiColor background = proxy.SceneBackgroundColor;
            WgpuGraphicsDevice renderDevice = sharedDevice;
            Task<PresentationSurfaceFrameStatus> frameTask = Task.Run(() => RenderProxyFrame(
                    session,
                    maskSession,
                    renderDevice,
                    scene,
                    camera,
                    width,
                    height,
                    background,
                    retainedSnapshot));
            activeFrameTask = frameTask;
            _ = frameTask.ContinueWith(
                    task => Dispatcher.Dispatch(() => CompleteProxyFrame(
                        proxy,
                        state,
                        session,
                        version,
                        width,
                        height,
                        task)),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            return;
        }

        if (pendingFrames.Count != 0)
        {
            SchedulePump();
        }
    }

    private PresentationSurfaceFrameStatus RenderProxyFrame(
        IPresentationSurfaceSession session,
        IPresentationSurfaceSession? maskSession,
        WgpuGraphicsDevice device,
        Scene scene,
        Camera camera,
        uint width,
        uint height,
        MauiColor background,
        IReadOnlyList<Scene> retainedSnapshot)
    {
        if (maskSession is not null)
        {
            GraphicsTexture? masterTexture = null;
            PresentationSurfaceFrameStatus maskStatus = RenderProxyFrameCore(
                maskSession,
                device,
                scene,
                camera,
                width,
                height,
                Colors.Transparent,
                retainedSnapshot,
                target => masterTexture = target);
            if (maskStatus is not (PresentationSurfaceFrameStatus.PresentedOptimal or
                PresentationSurfaceFrameStatus.PresentedSuboptimal))
            {
                return maskStatus;
            }

            return session.RenderAndPresent(destination => TransferMasterFrame(
                device,
                session,
                masterTexture ?? throw new InvalidOperationException(
                    "The synchronized HDR-mask frame did not produce a master texture."),
                destination,
                width,
                height,
                ColorTransferMode.Identity));
        }

        if (GetAlphaMode(session) == SurfaceAlphaMode.Unpremultiplied)
        {
            GraphicsTextureFormat format = MapGraphicsFormat(
                session.OutputPlan.Output.Format);
            _ = EnsureRenderer(device, format);
            GraphicsTexture premultipliedFrame = EnsureAlphaConversionTexture(
                device,
                width,
                height,
                format);
            RenderProxyScene(
                device,
                scene,
                camera,
                width,
                height,
                background,
                SurfaceAlphaMode.Premultiplied,
                retainedSnapshot,
                premultipliedFrame);
            return session.RenderAndPresent(destination => TransferMasterFrame(
                device,
                session,
                premultipliedFrame,
                destination,
                width,
                height,
                ColorTransferMode.Unpremultiply));
        }

        return RenderProxyFrameCore(
            session,
            device,
            scene,
            camera,
            width,
            height,
            background,
            retainedSnapshot);
    }

    private PresentationSurfaceFrameStatus RenderProxyFrameCore(
        IPresentationSurfaceSession session,
        WgpuGraphicsDevice device,
        Scene scene,
        Camera camera,
        uint width,
        uint height,
        MauiColor background,
        IReadOnlyList<Scene> retainedSnapshot,
        Action<GraphicsTexture>? targetCaptured = null) => session.RenderAndPresent(target =>
    {
        targetCaptured?.Invoke(target);
        RenderProxyScene(
            device,
            scene,
            camera,
            width,
            height,
            background,
            GetAlphaMode(session),
            retainedSnapshot,
            target);
    });

    private void RenderProxyScene(
        WgpuGraphicsDevice device,
        Scene scene,
        Camera camera,
        uint width,
        uint height,
        MauiColor background,
        SurfaceAlphaMode alphaMode,
        IReadOnlyList<Scene> retainedSnapshot,
        GraphicsTexture target)
    {
        SceneRenderer currentRenderer = EnsureRenderer(device, target.Descriptor.Format);
        if (camera is PerspectiveCamera perspective)
        {
            perspective.AspectRatio = (float)width / height;
        }
        currentRenderer.RenderViewports(
            [new SceneRenderViewport(scene, camera, 0, 0, width, height)],
            target,
            EnsureDepthTexture(device, width, height),
            ConvertBackground(background, alphaMode),
            retainedSnapshot);
    }

    private void CompleteProxyFrame(
        SceneViewProxy proxy,
        ProxySurfaceState state,
        IPresentationSurfaceSession session,
        long version,
        uint width,
        uint height,
        Task<PresentationSurfaceFrameStatus> task)
    {
        state.FrameInProgress = false;
        activeSurfaceState = null;
        frameInProgress = false;
        if (ReferenceEquals(activeFrameTask, task))
        {
            activeFrameTask = null;
        }

        if (task.IsFaulted)
        {
            DisposeStateSession(state);
            proxy.ReportSurfaceError(
                version,
                task.Exception?.GetBaseException() ??
                    new InvalidOperationException("The Proxy render worker failed."));
        }
        else if (!task.IsCanceled &&
            !state.DisposeAfterFrame &&
            proxies.Contains(proxy) &&
            proxy.IsRenderingEnabled &&
            ReferenceEquals(state.Session, session))
        {
            PresentationSurfaceFrameStatus status = task.Result;
            if (status is PresentationSurfaceFrameStatus.PresentedOptimal or
                PresentationSurfaceFrameStatus.PresentedSuboptimal)
            {
                proxy.NotifyFramePresented(
                    version,
                    width,
                    height,
                    session.OutputPlan.Output.Format);
            }
            else if (status is PresentationSurfaceFrameStatus.Outdated or
                PresentationSurfaceFrameStatus.Lost)
            {
                state.DisposeAfterFrame = true;
            }
        }

        if (state.DisposeAfterFrame)
        {
            state.DisposeAfterFrame = false;
            DisposeStateSession(state);
        }
        if (task.IsCompletedSuccessfully &&
            task.Result is PresentationSurfaceFrameStatus.Outdated or
                PresentationSurfaceFrameStatus.Lost)
        {
            QueueFrame(proxy);
        }
        SchedulePump();
    }

    private IPresentationSurfaceSession EnsureProxySession(
        ProxySurfaceState state,
        NativeSurfaceSource? source,
        IWgpuTexturePresentationSink? textureSink,
        uint width,
        uint height,
        OutputSettings settings,
        WgpuGraphicsDevice device)
    {
        if (state.Session is not null &&
            state.Source == source &&
            ReferenceEquals(state.TextureSink, textureSink) &&
            ((source is null || textureSink is null) == (state.MaskSession is null)))
        {
            return state.Session;
        }

        state.Session?.Dispose();
        state.MaskSession?.Dispose();
        state.MaskSession = null;
        state.Source = source;
        state.TextureSink = textureSink;
        if (source is not null && textureSink is not null)
        {
            state.Session = WgpuSurfaceSession.CreateSynchronizedColorCompatible(
                source.Value,
                width,
                height,
                settings with { AlphaMode = SurfaceAlphaMode.Opaque },
                device);
            try
            {
                state.MaskSession = WgpuTexturePresentationSession.CreateCompatible(
                    textureSink,
                    width,
                    height,
                    settings with { AlphaMode = SurfaceAlphaMode.Premultiplied },
                    device);
            }
            catch
            {
                state.Session.Dispose();
                state.Session = null;
                throw;
            }
        }
        else
        {
            state.Session = textureSink is not null
                ? WgpuTexturePresentationSession.CreateCompatible(
                    textureSink,
                    width,
                    height,
                    settings,
                    device)
                : WgpuSurfaceSession.CreateCompatible(
                    source ?? throw new InvalidOperationException(
                        "A proxy presentation target is required."),
                    width,
                    height,
                    settings,
                    device);
        }
        return state.Session;
    }

    private static SurfaceAlphaMode GetAlphaMode(IPresentationSurfaceSession session) =>
        session.AlphaMode;

    private SceneRenderer EnsureRenderer(
        WgpuGraphicsDevice device,
        GraphicsTextureFormat colorFormat)
    {
        if (renderer is not null && ReferenceEquals(renderer.Device, device) &&
            renderer.ColorFormat == colorFormat)
        {
            return renderer;
        }

        DisposeRenderResources();
        renderer = new SceneRenderer(device, colorFormat);
        return renderer;
    }

    private GraphicsTexture EnsureDepthTexture(
        GraphicsDevice device,
        uint width,
        uint height)
    {
        GraphicsExtent3D extent = new(width, height);
        if (depthTexture is not null && ReferenceEquals(depthTexture.Device, device) &&
            depthExtent == extent)
        {
            return depthTexture;
        }

        depthTexture?.Dispose();
        depthTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "shared Proxy Surface depth"));
        depthExtent = extent;
        return depthTexture;
    }

    private GraphicsTexture EnsureAlphaConversionTexture(
        GraphicsDevice device,
        uint width,
        uint height,
        GraphicsTextureFormat format)
    {
        GraphicsExtent3D extent = new(width, height);
        if (alphaConversionTexture is not null &&
            ReferenceEquals(alphaConversionTexture.Device, device) &&
            alphaConversionExtent == extent &&
            alphaConversionFormat == format)
        {
            return alphaConversionTexture;
        }

        alphaConversionTexture?.Dispose();
        alphaConversionTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            format,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: "Proxy premultiplied-alpha conversion source"));
        alphaConversionExtent = extent;
        alphaConversionFormat = format;
        return alphaConversionTexture;
    }

    private void TransferMasterFrame(
        WgpuGraphicsDevice device,
        IPresentationSurfaceSession session,
        GraphicsTexture source,
        GraphicsTexture destination,
        uint width,
        uint height,
        ColorTransferMode transferMode)
    {
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder(
            "Proxy color-transfer encoder");
        if (transferMode == ColorTransferMode.Identity &&
            session is WgpuSurfaceSession surface &&
            (surface.TextureUsage & GraphicsTextureUsage.CopyDestination) != 0)
        {
            encoder.CopyTextureToTexture(
                source,
                0,
                default,
                destination,
                0,
                default,
                new GraphicsExtent3D(width, height));
        }
        else
        {
            GraphicsRenderPipeline pipeline = EnsureColorTransferPipeline(
                device,
                destination.Descriptor.Format,
                transferMode);
            using GraphicsTextureView sourceView = device.CreateTextureView(
                new GraphicsTextureViewDescriptor(
                    source,
                    label: "Proxy color-transfer source view"));
            using GraphicsBindGroup bindGroup = device.CreateBindGroup(
                new GraphicsBindGroupDescriptor(
                    colorTransferBindGroupLayout ?? throw new InvalidOperationException(
                        "The synchronized color-transfer layout was not created."),
                    [new GraphicsBindGroupEntry(0, sourceView)],
                    "Proxy color-transfer bind group"));
            using GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(destination),
                    "Proxy color-transfer pass"));
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, bindGroup);
            pass.Draw(3);
        }

        using GraphicsCommandBuffer commands = encoder.Finish(
            "Proxy color-transfer commands");
        device.Queue.Submit(commands);
    }

    private GraphicsRenderPipeline EnsureColorTransferPipeline(
        GraphicsDevice device,
        GraphicsTextureFormat colorFormat,
        ColorTransferMode transferMode)
    {
        if (colorTransferPipeline is not null &&
            colorTransferFormat == colorFormat &&
            colorTransferMode == transferMode &&
            ReferenceEquals(colorTransferPipeline.Device, device))
        {
            return colorTransferPipeline;
        }

        DisposeColorTransferResources();
        colorTransferBindGroupLayout = device.CreateBindGroupLayout(
            new GraphicsBindGroupLayoutDescriptor(
                [new GraphicsBindGroupLayoutEntry(
                    0,
                    GraphicsShaderStage.Fragment,
                    GraphicsTextureSampleType.UnfilterableFloat,
                    GraphicsTextureViewDimension.TwoD)],
                "Proxy color-transfer layout"));
        colorTransferPipelineLayout = device.CreatePipelineLayout(
            new GraphicsPipelineLayoutDescriptor(
                [colorTransferBindGroupLayout],
                "Proxy color-transfer pipeline layout"));
        const string identityShaderSource = """
            @group(0) @binding(0) var master_texture: texture_2d<f32>;

            @vertex fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
                var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
                return vec4f(positions[index], 0.0, 1.0);
            }

            @fragment fn fs_main(@builtin(position) position: vec4f) -> @location(0) vec4f {
                return textureLoad(master_texture, vec2i(position.xy), 0);
            }
            """;
        const string unpremultiplyShaderSource = """
            @group(0) @binding(0) var master_texture: texture_2d<f32>;

            @vertex fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
                var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
                return vec4f(positions[index], 0.0, 1.0);
            }

            @fragment fn fs_main(@builtin(position) position: vec4f) -> @location(0) vec4f {
                let premultiplied = textureLoad(master_texture, vec2i(position.xy), 0);
                if (premultiplied.a <= 0.000001) {
                    return vec4f(0.0, 0.0, 0.0, 0.0);
                }
                return vec4f(
                    premultiplied.rgb / premultiplied.a,
                    premultiplied.a);
            }
            """;
        string shaderSource = transferMode == ColorTransferMode.Unpremultiply
            ? unpremultiplyShaderSource
            : identityShaderSource;
        colorTransferShader = device.CreateShaderModule(
            new GraphicsShaderModuleDescriptor(
                shaderSource,
                "Proxy color-transfer shader"));
        colorTransferPipeline = device.CreateRenderPipeline(
            new GraphicsRenderPipelineDescriptor(
                colorTransferShader,
                "vs_main",
                colorTransferShader,
                "fs_main",
                colorFormat,
                layout: colorTransferPipelineLayout,
                label: "Proxy color-transfer pipeline"));
        colorTransferFormat = colorFormat;
        colorTransferMode = transferMode;
        return colorTransferPipeline;
    }

    private void ApplyOutputSettings(OutputSettings settings)
    {
        foreach (SceneViewProxy proxy in proxies)
        {
            proxy.SetOutputSettings(settings);
        }
        DisposeProxySessions();
        QueueAllFrames();
    }

    private void QueueAllFrames()
    {
        foreach (SceneViewProxy proxy in proxies)
        {
            QueueFrame(proxy);
        }
    }

    private void SuspendAllProxySurfaces()
    {
        if (Dispatcher.IsDispatchRequired)
        {
            _ = Dispatcher.Dispatch(SuspendAllProxySurfaces);
            return;
        }

        pendingFrames.Clear();
        queuedFrames.Clear();
        foreach (SceneViewProxy proxy in proxies)
        {
            proxy.ResetForSuspension();
        }
        DisposeProxySessions();
    }

    private void RemovePendingFrame(SceneViewProxy proxy)
    {
        queuedFrames.Remove(proxy);
        int count = pendingFrames.Count;
        for (int index = 0; index < count; index++)
        {
            SceneViewProxy candidate = pendingFrames.Dequeue();
            if (!ReferenceEquals(candidate, proxy))
            {
                pendingFrames.Enqueue(candidate);
            }
        }
    }

    private void OnDeviceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        ClearBootstrapTarget(e.Device, e.Target);
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        _ = sender;
        DrainActiveFrameForDeviceChange();
        DisposeProxySessions();
        DisposeRenderResources();
        sharedDevice = e.Session?.Device as WgpuGraphicsDevice;
        if (sharedDevice is not null)
        {
            QueueAllFrames();
        }
    }

    private void DrainActiveFrameForDeviceChange()
    {
        Task<PresentationSurfaceFrameStatus>? frameTask = activeFrameTask;
        if (frameTask is null)
        {
            return;
        }
        try
        {
            _ = frameTask.GetAwaiter().GetResult();
        }
        catch
        {
            // The already-queued UI completion reports the original render failure.
        }

        if (activeSurfaceState is ProxySurfaceState state)
        {
            state.FrameInProgress = false;
            state.DisposeAfterFrame = false;
        }
        activeSurfaceState = null;
        activeFrameTask = null;
        frameInProgress = false;
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        SurfaceError?.Invoke(this, e);
    }

    private void DisposeProxySessions()
    {
        foreach (ProxySurfaceState state in surfaceStates.Values)
        {
            DisposeStateSession(state);
        }
    }

    private void DisposeStateSession(ProxySurfaceState state)
    {
        if (state.FrameInProgress || ReferenceEquals(activeSurfaceState, state))
        {
            state.DisposeAfterFrame = true;
            return;
        }
        state.Session?.Dispose();
        state.Session = null;
        state.MaskSession?.Dispose();
        state.MaskSession = null;
    }

    private void DisposeRenderResources()
    {
        DisposeColorTransferResources();
        alphaConversionTexture?.Dispose();
        alphaConversionTexture = null;
        alphaConversionExtent = default;
        alphaConversionFormat = null;
        depthTexture?.Dispose();
        depthTexture = null;
        depthExtent = default;
        renderer?.Dispose();
        renderer = null;
    }

    private void DisposeColorTransferResources()
    {
        colorTransferPipeline?.Dispose();
        colorTransferPipeline = null;
        colorTransferShader?.Dispose();
        colorTransferShader = null;
        colorTransferPipelineLayout?.Dispose();
        colorTransferPipelineLayout = null;
        colorTransferBindGroupLayout?.Dispose();
        colorTransferBindGroupLayout = null;
        colorTransferFormat = null;
        colorTransferMode = null;
    }

    private static LinearRgba ConvertBackground(
        MauiColor color,
        SurfaceAlphaMode alphaMode)
    {
        float alpha = color.Alpha;
        float red = DecodeSrgb(color.Red);
        float green = DecodeSrgb(color.Green);
        float blue = DecodeSrgb(color.Blue);
        if (alphaMode == SurfaceAlphaMode.Premultiplied)
        {
            red *= alpha;
            green *= alpha;
            blue *= alpha;
        }
        return new LinearRgba(red, green, blue, alpha, StandardColorSpaces.LinearSrgb);
    }

    private static float DecodeSrgb(float encoded) => encoded <= 0.04045f
        ? encoded / 12.92f
        : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);

    private static void ClearBootstrapTarget(GraphicsDevice device, GraphicsTexture target)
    {
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder(
            "Proxy Surface device bootstrap clear");
        using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(target),
            "Proxy Surface device bootstrap pass")))
        {
        }
        using GraphicsCommandBuffer commands = encoder.Finish(
            "Proxy Surface device bootstrap commands");
        device.Queue.Submit(commands);
    }

    private static GraphicsTextureFormat MapGraphicsFormat(PresentationFormat format) =>
        format switch
        {
            PresentationFormat.Bgra8Unorm => GraphicsTextureFormat.Bgra8Unorm,
            PresentationFormat.Bgra8UnormSrgb => GraphicsTextureFormat.Bgra8UnormSrgb,
            PresentationFormat.Rgba8Unorm => GraphicsTextureFormat.Rgba8Unorm,
            PresentationFormat.Rgba8UnormSrgb => GraphicsTextureFormat.Rgba8UnormSrgb,
            PresentationFormat.Rgba16Float => GraphicsTextureFormat.Rgba16Float,
            PresentationFormat.Rgb10A2Unorm => GraphicsTextureFormat.Rgb10A2Unorm,
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "Unsupported Proxy presentation format."),
        };

    private enum ColorTransferMode
    {
        Identity,
        Unpremultiply,
    }

    private sealed class ProxySurfaceState(
        NativeSurfaceSource? source,
        IWgpuTexturePresentationSink? textureSink)
    {
        internal NativeSurfaceSource? Source { get; set; } = source;

        internal IWgpuTexturePresentationSink? TextureSink { get; set; } = textureSink;

        internal IPresentationSurfaceSession? Session { get; set; }

        internal IPresentationSurfaceSession? MaskSession { get; set; }

        internal bool FrameInProgress { get; set; }

        internal bool DisposeAfterFrame { get; set; }
    }
}

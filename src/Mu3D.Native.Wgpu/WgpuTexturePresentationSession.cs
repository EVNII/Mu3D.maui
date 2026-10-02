using System.Diagnostics;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

internal sealed class WgpuTexturePresentationSession :
    IPresentationSurfaceSession,
    IWgpuPresentationSessionLifecycle
{
    private readonly object sessionGate = new();
    private readonly WgpuGraphicsDevice graphicsDevice;
    private readonly IWgpuTexturePresentationSink sink;
    private readonly bool ownsGraphicsDevice;
    private GraphicsTexture target;
    private bool dependentResourceReleaseStarted;
    private bool frameInProgress;
    private bool disposed;
    private uint width;
    private uint height;

    private WgpuTexturePresentationSession(
        WgpuGraphicsDevice graphicsDevice,
        IWgpuTexturePresentationSink sink,
        uint width,
        uint height,
        OutputSettings settings,
        bool ownsGraphicsDevice)
    {
        this.graphicsDevice = graphicsDevice;
        this.sink = sink;
        this.width = width;
        this.height = height;
        this.ownsGraphicsDevice = ownsGraphicsDevice;
        Capabilities = sink.Capabilities ??
            throw new InvalidOperationException(
                "The texture presentation sink did not report its capabilities.");
        OutputPlan = SurfaceOutputNegotiator.Negotiate(Capabilities, settings);
        AlphaMode = settings.AlphaMode is SurfaceAlphaMode.Opaque
            ? SurfaceAlphaMode.Opaque
            : SurfaceAlphaMode.Premultiplied;
        target = CreateTarget(width, height);
        AttachTarget();
    }

    public GraphicsDevice Device => graphicsDevice;

    public SurfaceCapabilities Capabilities { get; }

    public SurfaceOutputPlan OutputPlan { get; }

    public PresentationSurfaceFrameTimings LastFrameTimings { get; private set; }

    public uint Width => width;

    public uint Height => height;

    internal SurfaceAlphaMode AlphaMode { get; }

    internal static async Task<WgpuTexturePresentationSession> CreateAsync(
        IWgpuTexturePresentationSink sink,
        uint width,
        uint height,
        OutputSettings settings,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);

        WgpuGraphicsDevice device = await WgpuGraphicsDevice.CreateForTestingAsync(
            timeout,
            forceFallbackAdapter: false,
            WGPUBackendType.D3D12,
            cancellationToken).ConfigureAwait(false);
        try
        {
            return new WgpuTexturePresentationSession(
                device,
                sink,
                width,
                height,
                settings,
                ownsGraphicsDevice: true);
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    internal static async Task<WgpuTexturePresentationSession?> TryCreateForLifecycleAsync(
        IWgpuTexturePresentationSink sink,
        uint width,
        uint height,
        OutputSettings settings,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);

        WgpuGraphicsDevice? device = await WgpuGraphicsDevice.TryCreateForLifecycleAsync(
            timeout,
            forceFallbackAdapter: false,
            WGPUBackendType.D3D12,
            cancellationToken).ConfigureAwait(false);
        if (device is null)
        {
            return null;
        }
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                device.Dispose();
                return null;
            }
            return new WgpuTexturePresentationSession(
                device,
                sink,
                width,
                height,
                settings,
                ownsGraphicsDevice: true);
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }

    internal static WgpuTexturePresentationSession CreateCompatible(
        IWgpuTexturePresentationSink sink,
        uint width,
        uint height,
        OutputSettings settings,
        WgpuGraphicsDevice sharedDevice)
    {
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sharedDevice);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ObjectDisposedException.ThrowIf(
            sharedDevice.State == GraphicsDeviceState.Disposed,
            sharedDevice);
        return new WgpuTexturePresentationSession(
            sharedDevice,
            sink,
            width,
            height,
            settings,
            ownsGraphicsDevice: false);
    }

    public void Resize(uint width, uint height)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        lock (sessionGate)
        {
            ObjectDisposedException.ThrowIf(disposed || dependentResourceReleaseStarted, this);
            if (this.width == width && this.height == height)
            {
                return;
            }

            sink.Detach();
            target.Dispose();
            this.width = width;
            this.height = height;
            target = CreateTarget(width, height);
            AttachTarget();
        }
    }

    public PresentationSurfaceFrameStatus RenderAndPresent(Action<GraphicsTexture> render)
    {
        ArgumentNullException.ThrowIfNull(render);
        lock (sessionGate)
        {
            ObjectDisposedException.ThrowIf(disposed || dependentResourceReleaseStarted, this);
            if (frameInProgress)
            {
                throw new InvalidOperationException(
                    "A texture presentation frame cannot re-enter its render callback.");
            }

            frameInProgress = true;
            try
            {
                long totalStarted = Stopwatch.GetTimestamp();
                long renderStarted = totalStarted;
                render(target);
                graphicsDevice.ThrowIfNativeErrors("texture presentation frame");
                double renderMilliseconds = Stopwatch.GetElapsedTime(renderStarted).TotalMilliseconds;
                long presentStarted = Stopwatch.GetTimestamp();
                sink.Present();
                double presentMilliseconds = Stopwatch.GetElapsedTime(presentStarted).TotalMilliseconds;
                LastFrameTimings = new PresentationSurfaceFrameTimings(
                    0,
                    renderMilliseconds,
                    presentMilliseconds,
                    Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds);
                return PresentationSurfaceFrameStatus.PresentedOptimal;
            }
            finally
            {
                frameInProgress = false;
            }
        }
    }

    public void DrainAndReleaseDependentResources(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        lock (sessionGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            dependentResourceReleaseStarted = true;
            if (ownsGraphicsDevice)
            {
                graphicsDevice.WaitForSubmittedWork("texture-presentation dependent-resource release");
            }
            release();
        }
    }

    public void Dispose()
    {
        lock (sessionGate)
        {
            if (disposed)
            {
                return;
            }

            dependentResourceReleaseStarted = true;
            disposed = true;
            if (ownsGraphicsDevice)
            {
                graphicsDevice.DrainSubmittedWorkForDisposal();
            }
            sink.Detach();
            target.Dispose();
            if (ownsGraphicsDevice)
            {
                graphicsDevice.Dispose();
            }
        }
    }

    private GraphicsTexture CreateTarget(uint width, uint height) =>
        graphicsDevice.CreateTexture(new GraphicsTextureDescriptor(
            new GraphicsExtent3D(width, height),
            MapGraphicsFormat(OutputPlan.Output.Format),
            GraphicsTextureUsage.RenderAttachment |
                GraphicsTextureUsage.CopySource |
                GraphicsTextureUsage.TextureBinding,
            label: "Texture presentation source"));

    private void AttachTarget()
    {
        nint device = graphicsDevice.NativeD3D12Device;
        nint commandQueue = graphicsDevice.NativeD3D12CommandQueue;
        nint resource = WgpuGraphicsDevice.GetNativeD3D12Resource(target);
        if (device == 0 || commandQueue == 0 || resource == 0)
        {
            throw new PlatformNotSupportedException(
                "The pinned wgpu-native runtime does not expose the required D3D12 composition bridge.");
        }
        sink.Attach(
            device,
            commandQueue,
            resource,
            width,
            height,
            OutputPlan.Output.Format,
            AlphaMode);
    }

    private static GraphicsTextureFormat MapGraphicsFormat(PresentationFormat format) => format switch
    {
        PresentationFormat.Rgba16Float => GraphicsTextureFormat.Rgba16Float,
        PresentationFormat.Bgra8Unorm => GraphicsTextureFormat.Bgra8Unorm,
        PresentationFormat.Bgra8UnormSrgb => GraphicsTextureFormat.Bgra8UnormSrgb,
        _ => throw new NotSupportedException(
            $"Texture presentation does not support {format}."),
    };
}

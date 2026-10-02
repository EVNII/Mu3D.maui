using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Experimental Gallery/validation page lifetime. No native polling or synchronous browser waits.
internal sealed partial class BrowserGpu : IDisposable
{
    private static readonly List<WeakReference<BrowserGpu>> liveContexts = [];
    private nint context;
    private WgpuDeviceLostSink? lostSink;
    private BrowserGpu(nint context)
    {
        this.context = context;
        liveContexts.Add(new(this));
    }

    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_open")]
    private static partial void Open(nint callback, nint state);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_release")]
    private static partial void Release(nint context);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_instance")]
    private static partial nint Instance(nint context);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_adapter")]
    private static partial nint Adapter(nint context);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_device")]
    private static partial nint Device(nint context);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_create_surface", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint CreateSurface(nint context, string selector);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_configure")]
    private static partial int Configure(nint context, nint surface, int width, int height, int hdr, int transparent);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_max_dimension")]
    private static partial int MaximumDimension(nint context);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_acquire")]
    private static partial nint Acquire(nint surface);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_error")]
    private static partial nint Error();
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_lost_reason")]
    private static partial nint LostReason(nint context);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_read")]
    private static partial void Read(nint buffer, nuint offset, nuint size, nint callback, nint state);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_present")]
    private static partial void Present(nint callback, nint state);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_wait_for_work")]
    private static partial void WaitForWork(nint context, nint callback, nint state);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_begin_validation")]
    private static partial void BeginValidation(nint context);
    [LibraryImport("wgpu_native", EntryPoint = "mu3d_browser_end_validation")]
    private static partial void EndValidation(nint context, nint callback, nint state);

    internal static async Task<BrowserGpu> OpenAsync()
    {
        TaskCompletionSource<BrowserGpu> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GCHandle handle = GCHandle.Alloc(completion);
        try
        {
            unsafe { Open((nint)(delegate* unmanaged[Cdecl]<int, nint, byte*, nuint, nint, void>)&Opened, GCHandle.ToIntPtr(handle)); }
        }
        catch { handle.Free(); throw; }
        try { return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
        catch
        {
            _ = completion.Task.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully) task.Result.Dispose();
                else _ = task.Exception;
            }, TaskScheduler.Default);
            throw;
        }
    }

    internal unsafe WgpuGraphicsDevice CreateDevice()
    {
        ObjectDisposedException.ThrowIf(context == 0, this);
        var instance = (WGPUInstanceImpl*)Instance(context);
        var adapter = (WGPUAdapterImpl*)Adapter(context);
        var device = (WGPUDeviceImpl*)Device(context);
        WgpuNative.wgpuInstanceAddRef(instance);
        WgpuNative.wgpuAdapterAddRef(adapter);
        WgpuNative.wgpuDeviceAddRef(device);
        lostSink = new();
        bool Has(WGPUFeatureName feature) => WgpuNative.wgpuDeviceHasFeature(device, feature) != 0;
        WgpuInstanceHandle instanceOwner = new(instance);
        WgpuAdapterHandle adapterOwner = new(adapter);
        WgpuDeviceHandle deviceOwner = new(device);
        try
        {
            return new(instanceOwner, adapterOwner, deviceOwner, lostSink, new(
                Has(WGPUFeatureName.ShaderF16), Has(WGPUFeatureName.TextureCompressionBC),
                Has(WGPUFeatureName.TextureCompressionETC2), Has(WGPUFeatureName.TextureCompressionASTC)));
        }
        catch { deviceOwner.Dispose(); adapterOwner.Dispose(); instanceOwner.Dispose(); throw; }
    }

    internal int MaxDimension => MaximumDimension(context);

    internal unsafe WgpuSurfaceHandle CreateCanvas(string selector)
    {
        ObjectDisposedException.ThrowIf(context == 0, this);
        nint surface = CreateSurface(context, selector);
        if (surface == 0) throw new InvalidOperationException($"Canvas surface creation failed: {selector}.");
        return new((WGPUSurfaceImpl*)surface);
    }

    internal void ConfigureCanvas(nint surface, int width, int height, bool hdr, bool transparent)
    {
        if (Configure(context, surface, width, height, hdr ? 1 : 0, transparent ? 1 : 0) == 0) throw new InvalidOperationException("Canvas configuration failed.");
        ThrowIfErrors("canvas configuration");
    }

    internal unsafe SurfaceCapabilities QueryCanvasCapabilities(nint surface)
    {
        WGPUSurfaceCapabilities capabilities = default;
        if (WgpuNative.wgpuSurfaceGetCapabilities((WGPUSurfaceImpl*)surface,
            (WGPUAdapterImpl*)Adapter(context), &capabilities) != WGPUStatus.Success)
            throw new InvalidOperationException("Canvas capability query failed.");
        try
        {
            List<PresentationFormat> formats = [];
            List<string> unmapped = [];
            for (int i = 0; i < checked((int)capabilities.formatCount); i++)
            {
                switch (capabilities.formats[i])
                {
                    case WGPUTextureFormat.RGBA16Float: formats.Add(PresentationFormat.Rgba16Float); break;
                    case WGPUTextureFormat.RGBA8Unorm: formats.Add(PresentationFormat.Rgba8Unorm); break;
                    case WGPUTextureFormat.BGRA8Unorm: formats.Add(PresentationFormat.Bgra8Unorm); break;
                    default: unmapped.Add(capabilities.formats[i].ToString()); break;
                }
            }
            List<SurfacePresentMode> modes = [];
            for (int i = 0; i < checked((int)capabilities.presentModeCount); i++)
                modes.Add(capabilities.presentModes[i] == WGPUPresentMode.Fifo ? SurfacePresentMode.Fifo : SurfacePresentMode.Unknown);
            List<SurfaceAlphaMode> alpha = [];
            for (int i = 0; i < checked((int)capabilities.alphaModeCount); i++)
                alpha.Add(capabilities.alphaModes[i] switch {
                    WGPUCompositeAlphaMode.Opaque => SurfaceAlphaMode.Opaque,
                    WGPUCompositeAlphaMode.Premultiplied => SurfaceAlphaMode.Premultiplied,
                    _ => SurfaceAlphaMode.Unknown,
                });
            // Emdawn reports API-permitted formats, not measured hardware or display HDR support.
            return new(formats, modes, alpha, formats.Contains(PresentationFormat.Rgba16Float), unmapped);
        }
        finally { WgpuNative.wgpuSurfaceCapabilitiesFreeMembers(capabilities); }
    }

    internal unsafe GraphicsTexture AcquireCanvas(WgpuGraphicsDevice device, nint surface, uint width, uint height, bool hdr)
    {
        var texture = (WGPUTextureImpl*)Acquire(surface);
        if (texture is null) throw new InvalidOperationException("Canvas acquisition failed.");
        return new WgpuGraphicsTexture(device, new(new(width, height),
            hdr ? GraphicsTextureFormat.Rgba16Float : GraphicsTextureFormat.Bgra8Unorm,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource), texture);
    }

    internal static void ThrowIfErrors(string operation)
    {
        // Propagate observed device loss at existing frame/probe boundaries, without a render clock.
        for (int i = liveContexts.Count - 1; i >= 0; i--)
        {
            if (i >= liveContexts.Count) continue;
            WeakReference<BrowserGpu> reference = liveContexts[i];
            if (!reference.TryGetTarget(out BrowserGpu? browser) || browser.context == 0) continue;
            string? reason = Marshal.PtrToStringUTF8(LostReason(browser.context));
            if (!string.IsNullOrEmpty(reason)) browser.lostSink?.Report(reason);
        }
        string? error = Marshal.PtrToStringUTF8(Error());
        if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException($"{operation}: {error}");
    }

    internal void BeginValidationScope()
    {
        ObjectDisposedException.ThrowIf(context == 0, this);
        BeginValidation(context);
    }

    internal unsafe Task EndValidationScopeAsync()
    {
        ObjectDisposedException.ThrowIf(context == 0, this);
        TaskCompletionSource<byte[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GCHandle handle = GCHandle.Alloc(completion);
        try { EndValidation(context,
            (nint)(delegate* unmanaged[Cdecl]<int, nint, byte*, nuint, nint, void>)&ReadCompleted, GCHandle.ToIntPtr(handle)); }
        catch { handle.Free(); throw; }
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    internal unsafe Task WaitForSubmittedWorkAsync()
    {
        ObjectDisposedException.ThrowIf(context == 0, this);
        TaskCompletionSource<byte[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GCHandle handle = GCHandle.Alloc(completion);
        try { WaitForWork(context,
            (nint)(delegate* unmanaged[Cdecl]<int, nint, byte*, nuint, nint, void>)&ReadCompleted, GCHandle.ToIntPtr(handle)); }
        catch { handle.Free(); throw; }
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    internal static async Task<string> ProbeDeviceAsync()
    {
        using BrowserGpu browser = await OpenAsync();
        using WgpuGraphicsDevice device = browser.CreateDevice();
        using GraphicsTexture texture = device.CreateTexture(new(new(1, 1), GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding, label: "device-probe FP16 texture"));
        await browser.WaitForSubmittedWorkAsync();
        ThrowIfErrors("FP16 texture probe");
        return browser.FormatDevice(device) + $"\nRGBA16Float texture created: {texture.Descriptor.Format == GraphicsTextureFormat.Rgba16Float}\nSurface/HDR probed: false";
    }

    internal static async Task<string> ProbeTriangleAsync()
    {
        using BrowserGpu browser = await OpenAsync();
        using WgpuGraphicsDevice device = browser.CreateDevice();
        using GraphicsTexture target = device.CreateTexture(new(new(32, 32), GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource, label: "M2 offscreen FP16 target"));
        WgpuOffscreenTriangleRenderer.Render(device, target);
        using GraphicsBuffer readback = device.CreateBuffer(new(256,
            GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("probe center readback");
        encoder.CopyTextureToBuffer(target, 0, new(16, 16), new(1, 1), readback, 0, 256, 1);
        using GraphicsCommandBuffer commands = encoder.Finish();
        device.Queue.Submit(commands);
        await browser.WaitForSubmittedWorkAsync();
        byte[] pixel = await readback.ReadAsync(0, 256).WaitAsync(TimeSpan.FromSeconds(20));
        ThrowIfErrors("offscreen triangle completion and readback");
        float[] channels = DecodeTrianglePixel(pixel);
        return $"Target format: {target.Descriptor.Format}\nExtent: 32 x 32\nCommand submitted: true\nQueue completion observed: true\n" +
            $"ShaderF16 enabled: {device.Capabilities.SupportsShaderFloat16}\nDevice state: {device.State}\n" +
            $"Center pixel RGBA: {string.Join(", ", channels.Select(value => value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)))}\n" +
            "Surface probed: false\nHDR output verified: false";
    }

    private static float[] DecodeTrianglePixel(byte[] bytes)
    {
        ReadOnlySpan<Half> pixel = MemoryMarshal.Cast<byte, Half>(bytes.AsSpan(0, 8));
        float[] expected = [0.05f, 0.15f, 2, 1], channels = new float[4];
        for (int i = 0; i < channels.Length; i++)
        {
            channels[i] = (float)pixel[i];
            if (!float.IsFinite(channels[i]) || MathF.Abs(channels[i] - expected[i]) > 0.002f)
                throw new InvalidOperationException($"Offscreen triangle center channel {i}: {channels[i]} vs {expected[i]}.");
        }
        return channels;
    }

    private unsafe string FormatDevice(WgpuGraphicsDevice device)
    {
        WGPUAdapterImpl* adapter = (WGPUAdapterImpl*)Adapter(context);
        WGPUAdapterInfo info = default;
        if (WgpuNative.wgpuAdapterGetInfo(adapter, &info) != WGPUStatus.Success)
            throw new InvalidOperationException("Browser adapter information query failed.");
        WGPUSupportedFeatures features = default;
        WgpuNative.wgpuAdapterGetFeatures(adapter, &features);
        try
        {
            List<string> names = [];
            for (int i = 0; i < checked((int)features.featureCount); i++) names.Add(features.features[i].ToString());
            names.Sort(StringComparer.Ordinal);
            WGPULimits limits = default;
            bool limitsAvailable = WgpuNative.wgpuDeviceGetLimits((WGPUDeviceImpl*)Device(context), &limits) == WGPUStatus.Success;
            string Text(WGPUStringView value) => value.data is null || value.length == 0
                ? "unavailable (browser privacy/API)" : Decode((byte*)value.data, value.length);
            StringBuilder text = new();
            text.AppendLine($"Backend: {info.backendType}");
            text.AppendLine($"Adapter type: {(info.adapterType == WGPUAdapterType.Unknown || (uint)info.adapterType == 0 ? "unavailable (browser API)" : info.adapterType.ToString())}");
            text.AppendLine($"Vendor: {Text(info.vendor)}\nArchitecture: {Text(info.architecture)}\nDevice: {Text(info.device)}\nDescription: {Text(info.description)}");
            text.AppendLine($"ShaderF16 available: {names.Contains(WGPUFeatureName.ShaderF16.ToString())}");
            text.AppendLine($"ShaderF16 enabled: {device.Capabilities.SupportsShaderFloat16}");
            text.AppendLine($"Max sampled textures per shader stage: {(limitsAvailable ? limits.maxSampledTexturesPerShaderStage.ToString() : "unknown")}");
            text.AppendLine($"Max 2D texture dimension: {(limitsAvailable ? limits.maxTextureDimension2D.ToString() : "unknown")}");
            text.AppendLine($"Features:\n  {string.Join("\n  ", names)}");
            text.Append($"Device state: {device.State}");
            return text.ToString();
        }
        finally
        {
            WgpuNative.wgpuSupportedFeaturesFreeMembers(features);
            WgpuNative.wgpuAdapterInfoFreeMembers(info);
        }
    }

    internal static unsafe Task<byte[]> ReadAsync(nint buffer, ulong offset, int size, CancellationToken token)
    {
        TaskCompletionSource<byte[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GCHandle handle = GCHandle.Alloc(completion);
        try { Read(buffer, checked((nuint)offset), checked((nuint)size),
            (nint)(delegate* unmanaged[Cdecl]<int, nint, byte*, nuint, nint, void>)&ReadCompleted, GCHandle.ToIntPtr(handle)); }
        catch { handle.Free(); throw; }
        // Cancellation stops the wait; the real map callback still owns cleanup and the retained buffer.
        return completion.Task.WaitAsync(token);
    }

    internal static unsafe Task PresentAsync()
    {
        TaskCompletionSource<byte[]> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        GCHandle handle = GCHandle.Alloc(completion);
        try { Present((nint)(delegate* unmanaged[Cdecl]<int, nint, byte*, nuint, nint, void>)&ReadCompleted, GCHandle.ToIntPtr(handle)); }
        catch { handle.Free(); throw; }
        return completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void Opened(int status, nint context, byte* message, nuint length, nint state)
    {
        GCHandle handle = GCHandle.FromIntPtr(state);
        var completion = (TaskCompletionSource<BrowserGpu>)handle.Target!;
        try
        {
            if (status != 0) throw new InvalidOperationException($"GPU startup {status}: {Decode(message, length)}");
            completion.TrySetResult(new(context));
        }
        catch (Exception error) { Release(context); completion.TrySetException(error); }
        finally { handle.Free(); }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void ReadCompleted(int status, nint data, byte* message, nuint length, nint state)
    {
        GCHandle handle = GCHandle.FromIntPtr(state);
        var completion = (TaskCompletionSource<byte[]>)handle.Target!;
        try
        {
            if (status != 0) throw new InvalidOperationException($"GPU completion {status}: {Decode(message, length)}");
            completion.TrySetResult(length == 0 ? [] : new ReadOnlySpan<byte>((void*)data, checked((int)length)).ToArray());
        }
        catch (Exception error) { completion.TrySetException(error); }
        finally { handle.Free(); }
    }
    private static unsafe string Decode(byte* message, nuint length) => message is null ? "" : Encoding.UTF8.GetString(message, checked((int)length));
    public void Dispose()
    {
        nint owned = context;
        context = 0;
        if (owned != 0) Release(owned);
        liveContexts.RemoveAll(reference => !reference.TryGetTarget(out BrowserGpu? browser) || ReferenceEquals(browser, this));
    }
}

// This experimental backend implements the same presentation seam used by native handlers.
// It borrows the device/context; asynchronous readback/RAF waits stay outside a frame.
internal sealed class BrowserPresentationSurfaceSession(BrowserGpu browser, WgpuGraphicsDevice device,
    string selector = "#mu3d-canvas", bool transparent = false)
    : IPresentationSurfaceSession
{
    private static readonly SurfaceOutputPlan HdrOutput = new(
        new(PresentationFormat.Rgba16Float, OutputDynamicRange.Hdr, ColorEncoding.ExtendedSrgb, null, null), false);
    private static readonly SurfaceOutputPlan SdrOutput = new(
        new(PresentationFormat.Bgra8Unorm, OutputDynamicRange.Sdr, ColorEncoding.Srgb, null, null), false);
    private bool hdr = true;
    private bool disposed;
    private readonly WgpuSurfaceHandle canvasSurface = browser.CreateCanvas(selector);
    public GraphicsDevice Device => device;
    public SurfaceCapabilities Capabilities { get; private set; } = new([], [], [], false, []);
    public SurfaceOutputPlan OutputPlan { get; private set; } = HdrOutput;
    public SurfaceAlphaMode AlphaMode => transparent ? SurfaceAlphaMode.Premultiplied : SurfaceAlphaMode.Opaque;
    public PresentationSurfaceFrameTimings LastFrameTimings { get; private set; }
    public uint Width { get; private set; }
    public uint Height { get; private set; }

    internal void SetOutput(bool hdr) => this.hdr = hdr;

    public void Resize(uint width, uint height)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        browser.ConfigureCanvas(canvasSurface.DangerousGetHandle(), checked((int)width), checked((int)height), hdr, transparent);
        Capabilities = browser.QueryCanvasCapabilities(canvasSurface.DangerousGetHandle());
        Width = width;
        Height = height;
        OutputPlan = hdr ? HdrOutput : SdrOutput;
    }

    public PresentationSurfaceFrameStatus RenderAndPresent(Action<GraphicsTexture> render)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(render);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        double acquire = 0;
        long renderStarted = 0;
        try
        {
            using GraphicsTexture target = browser.AcquireCanvas(device, canvasSurface.DangerousGetHandle(), Width, Height,
                OutputPlan.Output.DynamicRange == OutputDynamicRange.Hdr);
            acquire = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            renderStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            render(target);
            BrowserGpu.ThrowIfErrors("canvas submission");
            return PresentationSurfaceFrameStatus.PresentedOptimal;
        }
        finally
        {
            long finished = System.Diagnostics.Stopwatch.GetTimestamp();
            double total = System.Diagnostics.Stopwatch.GetElapsedTime(started, finished).TotalMilliseconds;
            LastFrameTimings = new(renderStarted == 0 ? total : acquire,
                renderStarted == 0 ? 0 : System.Diagnostics.Stopwatch.GetElapsedTime(renderStarted, finished).TotalMilliseconds,
                0, total);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        canvasSurface.Dispose();
    }
}

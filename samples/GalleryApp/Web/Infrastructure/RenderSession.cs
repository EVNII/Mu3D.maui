using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Diagnostics;
using Mu3D.Web;

namespace Mu3D.GalleryApp.Web.Infrastructure;

internal sealed record RenderFrameReport(int Width, int Height, bool Hdr, float Exposure,
    float[] ScenePatches, float[] CanvasPatches, int Frame);

internal sealed class RenderSession : IDisposable
{
    private sealed class SharedDevice(BrowserGpu browser, WgpuGraphicsDevice device)
    {
        internal BrowserGpu Browser => browser;
        internal WgpuGraphicsDevice Device => device;
        private int users = 1;
        internal SharedDevice Retain() { users++; return this; }
        internal void Release() { if (--users == 0) { device.Dispose(); browser.Dispose(); } }
    }
    private readonly SharedDevice sharedDevice;
    private readonly BrowserGpu browser;
    private readonly WgpuGraphicsDevice device;
    private readonly BrowserPresentationSurfaceSession surface;
    private readonly CanvasViewHandler handler;
    private readonly List<IDisposable> owned = [];
    private Scene? scene;
    private Scene ValidationScene => scene ??= SharedValidationScene.Create();
    private readonly PerspectiveCamera camera = new() { Transform = { Position = new(0, 0, 5.6f) } };
    private SceneRenderer? renderer;
    private SceneRenderer Renderer => renderer ??= Own(new SceneRenderer(device, GraphicsTextureFormat.Rgba16Float));
    private readonly GraphicsBuffer displayParameters;
    private readonly GraphicsBindGroupLayout bindings;
    private readonly GraphicsRenderPipeline hdrPipeline, sdrPipeline;
    private readonly GraphicsBuffer readback;
    private GraphicsTexture? color, depth;
    private GraphicsTextureView? source;
    private GraphicsBindGroup? group;
    private int width, height, frame;
    private bool? configuredHdr;
    private bool disposed;
    private ViewerScene? viewer;
    internal int MaximumDimension { get; }
    internal GraphicsDevice Device => device;
    internal IPresentationSurfaceSession PresentationSurface => surface;

    internal async Task<RenderFrameReport> DrawExampleAsync(int width, int height, float exposure,
        bool hdr, bool refreshSurface, Action<GraphicsDevice, GraphicsTexture, GraphicsTexture?> draw,
        bool verifySubmission = false)
    {
        // Check the first actual example frame, rather than consuming the check on its loading clear.
        // Subsequent frames keep RAF pacing and never wait for GPU completion.
        if (verifySubmission) browser.BeginValidationScope();
        RenderFrameReport report;
        try
        {
            report = await DrawAsync(width, height, exposure, hdr, refreshSurface, validatePixels: false,
                customDraw: (device, target) => draw(device, target, depth));
        }
        finally
        {
            if (verifySubmission) await browser.EndValidationScopeAsync();
        }
        if (verifySubmission)
        {
            await browser.WaitForSubmittedWorkAsync();
            BrowserGpu.ThrowIfErrors("first example frame completion");
        }
        return report;
    }

    private RenderSession(SharedDevice sharedDevice, string selector = "#mu3d-canvas", bool transparent = false)
    {
        this.sharedDevice = sharedDevice;
        browser = sharedDevice.Browser;
        device = sharedDevice.Device;
        MaximumDimension = browser.MaxDimension;
        if (MaximumDimension <= 0) throw new InvalidOperationException("GPU texture limit unavailable.");
        try
        {
            surface = Own(new BrowserPresentationSurfaceSession(browser, device, selector, transparent));
            handler = Own(new CanvasViewHandler(surface));
            displayParameters = Own(device.CreateBuffer(new(16, GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination)));
            bindings = Own(device.CreateBindGroupLayout(new([
                new(0, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.Float, GraphicsTextureViewDimension.TwoD),
                new(1, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 16)])));
            GraphicsPipelineLayout layout = Own(device.CreatePipelineLayout(new([bindings])));
            GraphicsShaderModule shader = Own(device.CreateShaderModule(new("""
                @group(0) @binding(0) var source: texture_2d<f32>;
                @group(0) @binding(1) var<uniform> display: vec4f;
                @vertex fn vs(@builtin(vertex_index) i: u32) -> @builtin(position) vec4f {
                    let p = array<vec2f, 3>(vec2f(-1,-1), vec2f(3,-1), vec2f(-1,3));
                    return vec4f(p[i], 0, 1);
                }
                @fragment fn fs(@builtin(position) p: vec4f) -> @location(0) vec4f {
                    let sample = textureLoad(source, vec2i(p.xy), 0);
                    let alpha = select(1.0, clamp(sample.a, 0.0, 1.0), display.z > 0.5);
                    let unassociated = select(vec3f(0), sample.rgb / max(alpha, 0.000001), alpha > 0.0);
                    var c = unassociated * display.x;
                    // The canvas tag is encoded sRGB, not native extended-linear scRGB.
                    // Only the explicitly selected SDR preview clips. HDR preserves sign/range.
                    if (display.y == 0.0) { c = clamp(c, vec3f(0), vec3f(1)); }
                    let a = abs(c);
                    let encoded = sign(c) * select(1.055 * pow(a, vec3f(1.0/2.4)) - 0.055,
                        12.92*a, a <= vec3f(0.0031308));
                    return vec4f(encoded * alpha, alpha);
                }
                """, "extended sRGB canvas encoding")));
            hdrPipeline = Own(device.CreateRenderPipeline(new(shader, "vs", shader, "fs", GraphicsTextureFormat.Rgba16Float, layout: layout)));
            sdrPipeline = Own(device.CreateRenderPipeline(new(shader, "vs", shader, "fs", GraphicsTextureFormat.Bgra8Unorm, layout: layout)));
            // Four 1-pixel patches from each texture. Readback cost does not grow with display resolution.
            readback = Own(device.CreateBuffer(new(8 * 256, GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead)));
        }
        catch { DisposeOwned(); throw; }
    }

    internal static async Task<RenderSession> OpenAsync(string selector = "#mu3d-canvas", bool transparent = false)
    {
        BrowserGpu browser = await BrowserGpu.OpenAsync();
        try
        {
            WgpuGraphicsDevice device = browser.CreateDevice();
            SharedDevice shared = new(browser, device);
            try { return new(shared, selector, transparent); }
            catch { shared.Release(); throw; }
        }
        catch { browser.Dispose(); throw; }
    }

    // Each Canvas owns its surface/attachments/handler while borrowing one shared GPU device.
    internal RenderSession CreateSharedCanvas(string selector, bool transparent = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        SharedDevice retained = sharedDevice.Retain();
        try { return new(retained, selector, transparent); }
        catch { retained.Release(); throw; }
    }

    private T Own<T>(T value) where T : IDisposable { owned.Add(value); return value; }

    internal async Task<ViewerFrameReport> DrawViewerAsync(int width, int height, float exposure,
        bool hdr, bool refreshSurface, ViewerInput input, ViewerBenchmark? measurement = null)
    {
        viewer ??= new ViewerScene(ValidationScene, camera);
        viewer.SetViewport((uint)width, (uint)height);
        viewer.Apply(input);
        bool statisticsClockBreak = refreshSurface || width != this.width || height != this.height ||
            configuredHdr != hdr;
        measurement?.Mark(ViewerBenchmarkStage.SceneUpdate);
        RenderFrameReport result = await DrawAsync(width, height, exposure, hdr, refreshSurface, validatePixels: false);
        measurement?.Mark(ViewerBenchmarkStage.RenderSubmit);
        measurement?.SetRendererTimings(Renderer.LastFrameTimings);
        viewer.RecordSubmittedFrame(
            statisticsClockBreak ? TimeSpan.Zero : TimeSpan.FromSeconds(input.FrameIntervalSeconds),
            Renderer.LastFrameTimings,
            resourceCounts: new RenderResourceCounts(meshCount: Renderer.CachedMeshCount));
        double logicalWidth = input.LogicalWidth > 0 ? input.LogicalWidth : width / input.PixelRatio;
        double logicalHeight = input.LogicalHeight > 0 ? input.LogicalHeight : height / input.PixelRatio;
        ViewerFrameReport report = viewer.Report(result.Frame,
            viewer.CaptureAnchors(result.Frame, logicalWidth, logicalHeight),
            viewer.CaptureNodeAnchors(result.Frame, logicalWidth, logicalHeight));
        measurement?.Mark(ViewerBenchmarkStage.AnchorsReport);
        return report;
    }

    internal bool BeginViewerDrag(float x, float y) => viewer?.BeginDrag(x, y) ?? false;
    internal void UpdateViewerDrag(float x, float y) => viewer?.UpdateDrag(x, y);
    internal void EndViewerDrag(bool cancel) => viewer?.EndDrag(cancel);
    internal void ConfigureViewerGizmo(int mode, bool local, bool enabled, bool resetModel) =>
        EnsureViewer().ConfigureGizmo(mode, local, enabled, resetModel);

    internal ViewerSelectionCandidates HitTestSelection(double x, double y, uint mask) => EnsureViewer().HitTestSelection(x, y, mask);
    internal ViewerSelectionReport SelectNode(string token) => EnsureViewer().SelectNode(token);
    internal void ConfigureHighlight(bool enabled) => EnsureViewer().ConfigureHighlight(enabled);

    private ViewerScene EnsureViewer() => viewer ??= new ViewerScene(ValidationScene, camera);
    internal ViewerNodeCatalog NodeCatalog => EnsureViewer().NodeCatalog;
    internal ViewerNodeAnchorRegistrationReport ConfigureNodeAnchor(string id, string target, double x, double y, double z) =>
        EnsureViewer().ConfigureNodeAnchor(id, target, x, y, z);
    internal long RemoveNodeAnchor(string id) => EnsureViewer().RemoveNodeAnchor(id);
    internal ViewerStatisticsReport StatisticsReport => EnsureViewer().LatestStatisticsReport;
    internal void ConfigureStatistics(ViewerStatisticsOptions options) => EnsureViewer().ConfigureStatistics(options);
    internal ViewerStatisticsReport ResetStatistics() => EnsureViewer().ResetStatistics();
    internal ViewerStatisticsReport PublishStatistics() => EnsureViewer().PublishStatisticsSnapshot();

    internal async Task<RenderFrameReport> DrawAsync(int nextWidth, int nextHeight, float exposure, bool hdr, bool refreshSurface,
        bool validatePixels = true, Action<GraphicsDevice, GraphicsTexture>? customDraw = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (nextWidth < 1 || nextHeight < 1 || nextWidth > MaximumDimension || nextHeight > MaximumDimension)
            throw new ArgumentOutOfRangeException(nameof(nextWidth), $"Native pixel size must be within GPU limit {MaximumDimension}.");
        if (!float.IsFinite(exposure) || exposure is < -4 or > 2) throw new ArgumentOutOfRangeException(nameof(exposure));
        bool resized = nextWidth != width || nextHeight != height;
        if (resized) Resize(nextWidth, nextHeight);
        surface.SetOutput(hdr);
        handler.RenderAndPresent((uint)width, (uint)height, canvas =>
        {
            camera.AspectRatio = (float)width / height;
            if (customDraw is null)
                Renderer.Render(ValidationScene, camera, color!, depth!, new(0.015f, 0.025f, 0.045f, 1, StandardColorSpaces.LinearSrgb));
            else customDraw(device, color!);
            if (!validatePixels && customDraw is null && viewer is not null)
            {
                RenderPassContext context = new(ValidationScene, camera, color!, depth!, StandardColorSpaces.LinearSrgb,
                    colorTargetInitialized: true, depthTargetInitialized: true);
                viewer.RenderSelectionHighlight(context);
                viewer.RenderGizmo(context);
            }
            WriteDisplayParameters(exposure, hdr);
            GraphicsOrigin3D[] centers = validatePixels ? PatchCenters() : [];
            using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("HDR canvas frame and reference pixels");
            using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(canvas))))
            {
                pass.SetPipeline(hdr ? hdrPipeline : sdrPipeline);
                pass.SetBindGroup(0, group!);
                pass.Draw(3);
                pass.End();
            }
            for (int i = 0; i < centers.Length; i++)
            {
                encoder.CopyTextureToBuffer(color!, 0, centers[i], new(1, 1), readback, (ulong)i * 256, 256, 1);
                encoder.CopyTextureToBuffer(canvas, 0, centers[i], new(1, 1), readback, (ulong)(i + 4) * 256, 256, 1);
            }
            using GraphicsCommandBuffer commands = encoder.Finish();
            device.Queue.Submit(commands);
        }, refreshSurface: resized || configuredHdr != hdr || refreshSurface);
        configuredHdr = hdr;
        if (!validatePixels)
        {
            // The host's requestAnimationFrame owns pacing; animation never waits on readback.
            BrowserGpu.ThrowIfErrors("viewer submission");
            return new(width, height, hdr, exposure, [], [], ++frame);
        }
        byte[] pixels = await readback.ReadAsync(0, 8 * 256).WaitAsync(TimeSpan.FromSeconds(20));
        BrowserGpu.ThrowIfErrors("scene and canvas pixel readback");
        (float[] scenePatches, float[] canvasPatches) = VerifyPixels(pixels, exposure, hdr);
        await BrowserGpu.PresentAsync();
        BrowserGpu.ThrowIfErrors("canvas presentation");
        return new(width, height, hdr, exposure, scenePatches, canvasPatches, ++frame);
    }

    private void WriteDisplayParameters(float exposure, bool hdr)
    {
        ReadOnlySpan<float> values = [MathF.Pow(2, exposure), hdr ? 1 : 0,
            surface.AlphaMode == SurfaceAlphaMode.Premultiplied ? 1 : 0, 0];
        device.Queue.WriteBuffer(displayParameters, 0, MemoryMarshal.AsBytes(values));
    }

    private void Resize(int nextWidth, int nextHeight)
    {
        // A preceding frame finishes its readback before this method can replace its attachments.
        DisposeTargets();
        width = height = 0;
        try
        {
            color = device.CreateTexture(new(new((uint)nextWidth, (uint)nextHeight), GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.CopySource));
            depth = device.CreateTexture(new(new((uint)nextWidth, (uint)nextHeight), GraphicsTextureFormat.Depth32Float,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding));
            source = device.CreateTextureView(new(color));
            group = device.CreateBindGroup(new(bindings, [new(0, source), new(1, displayParameters, 0, 16)]));
            width = nextWidth; height = nextHeight;
        }
        catch { DisposeTargets(); throw; }
    }

    private GraphicsOrigin3D[] PatchCenters()
    {
        GraphicsOrigin3D[] centers = new GraphicsOrigin3D[4];
        for (int i = 0; i < centers.Length; i++)
        {
            Vector4 clip = Vector4.Transform(new Vector4((i - 1.5f) * 1.05f, -1.1f, 0.1f, 1), camera.ViewProjectionMatrix);
            centers[i] = new((uint)((clip.X / clip.W * 0.5f + 0.5f) * width), (uint)((0.5f - clip.Y / clip.W * 0.5f) * height));
        }
        return centers;
    }

    private static (float[], float[]) VerifyPixels(byte[] bytes, float exposure, bool hdr)
    {
        float[] expected = [0.25f, 1, 2, 4], scenePatches = new float[4], canvasPatches = new float[4];
        for (int i = 0; i < expected.Length; i++)
        {
            ReadOnlySpan<Half> scenePixel = MemoryMarshal.Cast<byte, Half>(bytes.AsSpan(i * 256, 8));
            float linear = expected[i] * MathF.Pow(2, exposure);
            if (!hdr) linear = Math.Clamp(linear, 0, 1);
            float encoded = linear <= 0.0031308f ? linear * 12.92f : 1.055f * MathF.Pow(linear, 1 / 2.4f) - 0.055f;
            for (int channel = 0; channel < 4; channel++)
            {
                float sourceValue = (float)scenePixel[channel];
                if (!float.IsFinite(sourceValue) || MathF.Abs(sourceValue - (channel == 3 ? 1 : expected[i])) > 0.002f)
                    throw new InvalidOperationException($"Source patch {i}/{channel} changed: {sourceValue}.");
                float outputValue = hdr
                    ? (float)MemoryMarshal.Cast<byte, Half>(bytes.AsSpan((i + 4) * 256, 8))[channel]
                    : bytes[(i + 4) * 256 + channel] / 255f;
                if (!float.IsFinite(outputValue) || MathF.Abs(outputValue - (channel == 3 ? 1 : encoded)) > (hdr ? 0.003f : 0.006f))
                    throw new InvalidOperationException($"Canvas patch {i}/{channel}: {outputValue} vs expected {encoded}.");
                if (channel == 0) { scenePatches[i] = sourceValue; canvasPatches[i] = outputValue; }
            }
        }
        return (scenePatches, canvasPatches);
    }

    private void DisposeTargets()
    {
        group?.Dispose(); group = null;
        source?.Dispose(); source = null;
        depth?.Dispose(); depth = null;
        color?.Dispose(); color = null;
    }
    private void DisposeOwned() { for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose(); owned.Clear(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        viewer?.Dispose();
        DisposeTargets(); DisposeOwned(); sharedDevice.Release();
    }
}

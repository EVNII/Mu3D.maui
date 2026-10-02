using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

// Example-owned diagnostic: the host still owns presentation, resize and frame scheduling.
internal sealed class OpenPbrFurnaceRenderer : IRenderPass, IDisposable
{
    internal OpenPbrRenderPass Transport { get; } = new(StandardColorSpaces.AcesCg)
    {
        Mode = OpenPbrRenderMode.Reference, ReferenceMaxBounces = 32, HybridMaxBounces = 32,
        InteractiveMaxBounces = 32, InteractiveResolutionScale = 1, RasterEnvironmentSamples = 64, BackgroundAlpha = 1,
    };
    internal RenderPassPipeline Pipeline { get; }
    internal float EnvironmentLevel { get; set; } = 1;
    internal float ExpectedRatio { get; set; } = 1;
    internal int DisplayMode { get; set; }
    public RenderPassDescriptor Descriptor { get; } = new("White furnace", StandardColorSpaces.LinearSrgb,
        new RenderPassColorAttachmentPolicy(GraphicsLoadOperation.Clear, new(0, 0, 0, 1, StandardColorSpaces.LinearSrgb)));
    private readonly List<IDisposable> resources = [];
    private readonly object gate = new();
    private GraphicsDevice? device;
    private GraphicsTexture? source;
    private GraphicsBuffer? parameters;
    private GraphicsBindGroupLayout? bindings;
    private GraphicsRenderPipeline? display;
    private GraphicsTextureFormat format;
    private TaskCompletionSource<FurnaceMeasurement>? measurement;
    private bool measuring, disposed;
    internal OpenPbrFurnaceRenderer() => Pipeline = new([this]);

    internal Task<FurnaceMeasurement> MeasureNextFrameAsync()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (measurement is not null) throw new InvalidOperationException("已有测量进行中。");
            measurement = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return measurement.Task;
        }
    }

    public void Execute(RenderPassContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        try { Draw(context); }
        catch (Exception error) { FailMeasurement(error); throw; }
    }

    private void Draw(RenderPassContext context)
    {
        if (context.ColorTarget.Descriptor.Format is not (GraphicsTextureFormat.Rgba16Float or GraphicsTextureFormat.Rgba32Float))
            throw new NotSupportedException("熔炉显示需要 FP16/FP32 HDR 表面。");
        if (device != context.Device || format != context.ColorTarget.Descriptor.Format) Initialize(context);
        var size = context.OutputExtent;
        // Bounded scene-linear source preserves aspect ratio; presentation only scales its image.
        float scale = MathF.Min(1, 384f / Math.Max(size.Width, size.Height));
        var extent = new GraphicsExtent3D(Math.Max(1, (uint)(size.Width * scale)), Math.Max(1, (uint)(size.Height * scale)));
        if (source?.Descriptor.Size != extent)
        {
            source?.Dispose(); source = null;
            source = device!.CreateTexture(new(extent, GraphicsTextureFormat.Rgba32Float,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.CopySource));
            Transport.ResetAccumulation();
        }
        Transport.EnvironmentRadiance = new(EnvironmentLevel, EnvironmentLevel, EnvironmentLevel, 1, StandardColorSpaces.AcesCg);
        Transport.Execute(new(context.Scene, context.Camera, source, null, StandardColorSpaces.AcesCg));
        Vector4[] data = [new(EnvironmentLevel, ExpectedRatio, DisplayMode, 0), Primary(1, 0, 0), Primary(0, 1, 0), Primary(0, 0, 1)];
        device!.Queue.WriteBuffer(parameters!, 0, MemoryMarshal.AsBytes(data.AsSpan()));
        using var view = device.CreateTextureView(new(source));
        using var group = device.CreateBindGroup(new(bindings!, [new(0, view), new(1, parameters!, 0, 64)]));
        using (var encoder = device.CreateCommandEncoder("White furnace diagnostic view"))
        {
            using (var pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(context.ColorTarget))))
            { pass.SetPipeline(display!); pass.SetBindGroup(0, group); pass.Draw(3); }
            using var commands = encoder.Finish(); device.Queue.Submit(commands);
        }
        TaskCompletionSource<FurnaceMeasurement>? pending;
        lock (gate)
        {
            pending = measuring ? null : measurement;
            if (pending is not null) measuring = true;
        }
        if (pending is null) return;
        uint pitch = (extent.Width * 16 + 255) & ~255u;
        var readback = device.CreateBuffer(new((ulong)pitch * extent.Height,
            GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
        try
        {
            using var encoder = device.CreateCommandEncoder("Explicit furnace measurement");
            encoder.CopyTextureToBuffer(source, 0, default, extent, readback, 0, pitch, extent.Height);
            using var commands = encoder.Finish(); device.Queue.Submit(commands);
        }
        catch { readback.Dispose(); throw; }
        Matrix4x4.Invert(context.Camera.ViewProjectionMatrix, out Matrix4x4 inverse);
        Vector3 origin = Vector3.Transform(Vector3.Zero, context.Camera.WorldMatrix);
        _ = CompleteMeasurementAsync(pending, readback, extent, pitch, inverse, origin,
            EnvironmentLevel, ExpectedRatio, Transport.Mode, Transport.AccumulatedSamples);
    }

    private async Task CompleteMeasurementAsync(TaskCompletionSource<FurnaceMeasurement> pending, GraphicsBuffer buffer,
        GraphicsExtent3D size, uint pitch, Matrix4x4 inverse, Vector3 origin, float level, float expected,
        OpenPbrRenderMode mode, long samples)
    {
        try
        {
            using (buffer)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                byte[] bytes = await buffer.ReadAsync(0, checked((int)(pitch * size.Height)), timeout.Token).ConfigureAwait(false);
                FurnaceMeasurement result = FurnaceMeasurement.Analyze(bytes, size, pitch, inverse, origin, level, expected, mode, samples);
                lock (gate) { if (measurement == pending) { measurement = null; measuring = false; } }
                pending.TrySetResult(result);
            }
        }
        catch (Exception error)
        {
            lock (gate) { if (measurement == pending) { measurement = null; measuring = false; } }
            pending.TrySetException(error is OperationCanceledException ? new TimeoutException("GPU 回读等待超时。", error) : error);
        }
    }

    private void Initialize(RenderPassContext context)
    {
        ReleaseGpu(); device = context.Device; format = context.ColorTarget.Descriptor.Format;
        try
        {
            parameters = Own(device.CreateBuffer(new(64, GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination)));
            bindings = Own(device.CreateBindGroupLayout(new([
                new(0, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD),
                new(1, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 64)])));
            var layout = Own(device.CreatePipelineLayout(new([bindings])));
            var shader = Own(device.CreateShaderModule(new(Shader, "Furnace radiance / relative error")));
            display = Own(device.CreateRenderPipeline(new(shader, "vs_main", shader, "fs_main", format, layout: layout)));
        }
        catch { ReleaseGpu(); throw; }
    }
    private T Own<T>(T item) where T : IDisposable { resources.Add(item); return item; }
    private void ReleaseGpu()
    {
        source?.Dispose(); source = null;
        for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
        resources.Clear(); device = null;
    }
    private void FailMeasurement(Exception error)
    {
        lock (gate) { measurement?.TrySetException(error); measurement = null; measuring = false; }
    }
    public void Dispose()
    {
        lock (gate) { disposed = true; measurement?.TrySetCanceled(); measurement = null; }
        Transport.Dispose(); ReleaseGpu();
    }
    private static Vector4 Primary(float r, float g, float b)
    {
        var c = StandardLinearRgbConverter.Convert(new(r, g, b, 1, StandardColorSpaces.AcesCg), StandardColorSpaces.LinearSrgb);
        return new(c.Red, c.Green, c.Blue, 0);
    }
    private const string Shader = """
        @group(0) @binding(0) var source: texture_2d<f32>;
        @group(0) @binding(1) var<uniform> p: array<vec4<f32>,4>;
        struct Vertex { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32> }
        @vertex fn vs_main(@builtin(vertex_index) i: u32) -> Vertex {
            let v = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
            return Vertex(vec4<f32>(v*2.0-1.0,0,1), vec2<f32>(v.x,1.0-v.y));
        }
        @fragment fn fs_main(v: Vertex) -> @location(0) vec4<f32> {
            let size = vec2<i32>(textureDimensions(source));
            let c = textureLoad(source, clamp(vec2<i32>(v.uv*vec2<f32>(size)),vec2<i32>(0),size-1),0).rgb;
            if (p[0].z < 0.5) { return vec4<f32>(p[1].rgb*c.r + p[2].rgb*c.g + p[3].rgb*c.b,1); }
            let ratio = c / p[0].x;
            if (p[0].z < 1.5) { return vec4<f32>(ratio,1); }
            let error = ratio - vec3<f32>(p[0].y);
            let gain = max(0.0,max(error.r,max(error.g,error.b)));
            let loss = max(0.0,-min(error.r,min(error.g,error.b)));
            return vec4<f32>(clamp(vec3<f32>(0.18) + 10.0*(gain*vec3<f32>(1,0.1,0) + loss*vec3<f32>(0,0.3,1)),vec3<f32>(0),vec3<f32>(1)),1);
        }
        """;
}

internal sealed record FurnaceMeasurement(int Pixels, Vector3 Mean, float Minimum, float Maximum, double Rms,
    OpenPbrRenderMode Mode, long Samples)
{
    // This Gallery scene has exactly one unit sphere at the origin. Shrink its analytical mask to
    // avoid counting background / antialiased silhouette as successful furnace measurements.
    internal static FurnaceMeasurement Analyze(byte[] bytes, GraphicsExtent3D size, uint pitch, Matrix4x4 inverse,
        Vector3 origin, float level, float expected, OpenPbrRenderMode mode, long samples)
    {
        int count = 0; double r = 0, g = 0, b = 0, squared = 0;
        float minimum = float.PositiveInfinity, maximum = float.NegativeInfinity;
        for (int y = 0; y < size.Height; y++)
        for (int x = 0; x < size.Width; x++)
        {
            var near = Vector4.Transform(new Vector4(2f * (x + .5f) / size.Width - 1, 1 - 2f * (y + .5f) / size.Height, 0, 1), inverse);
            var direction = Vector3.Normalize(new Vector3(near.X, near.Y, near.Z) / near.W - origin);
            float projected = Vector3.Dot(origin, direction);
            if (projected >= 0 || projected * projected - (origin.LengthSquared() - .9f * .9f) <= 0) continue;
            int offset = checked(y * (int)pitch + x * 16);
            Vector3 ratio = new(BitConverter.ToSingle(bytes, offset) / level, BitConverter.ToSingle(bytes, offset + 4) / level,
                BitConverter.ToSingle(bytes, offset + 8) / level);
            for (int c = 0; c < 3; c++)
            {
                if (!float.IsFinite(ratio[c])) throw new InvalidOperationException("测量包含非有限像素。");
                minimum = Math.Min(minimum, ratio[c]); maximum = Math.Max(maximum, ratio[c]);
                squared += Math.Pow(ratio[c] - expected, 2);
            }
            r += ratio.X; g += ratio.Y; b += ratio.Z; count++;
        }
        if (count == 0) throw new InvalidOperationException("窗口中没有可测量的球体内部像素。");
        return new(count, new((float)(r / count), (float)(g / count), (float)(b / count)), minimum, maximum,
            Math.Sqrt(squared / (3 * count)), mode, samples);
    }
}

internal static class FurnaceMaterials
{
    internal static OpenPbrSurface Create(int preset, float roughness, float anisotropy)
    {
        LinearRgba white = new(1, 1, 1, 1, StandardColorSpaces.AcesCg);
        OpenPbrSurface surface = new() { BaseColor = white, SpecularColor = white, CoatColor = white, FuzzColor = white,
            SubsurfaceColor = white, TransmissionColor = white, SpecularRoughness = roughness, CoatRoughness = roughness,
            FuzzRoughness = roughness, SpecularRoughnessAnisotropy = anisotropy, CoatRoughnessAnisotropy = anisotropy };
        switch (preset)
        {
            case 0: surface.SpecularWeight = 0; surface.SpecularIor = 1; surface.BaseDiffuseRoughness = roughness; break;
            case 1: surface.BaseMetalness = 1; break;
            case 2: surface.CoatWeight = 1; break;
            case 3: surface.FuzzWeight = 1; break;
            case 4: surface.TransmissionWeight = 1; surface.TransmissionDepth = 0; break;
            case 5: surface.SubsurfaceWeight = 1; surface.SubsurfaceRadius = .5f; break;
            case 6: surface.BaseColor = new(.5f, .5f, .5f, 1, StandardColorSpaces.AcesCg);
                surface.SpecularWeight = 0; surface.SpecularIor = 1; break;
            default: throw new ArgumentOutOfRangeException(nameof(preset));
        }
        return surface;
    }
}

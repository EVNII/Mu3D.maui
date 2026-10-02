using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;

namespace Mu3D.GalleryApp.Examples;

// The original native low-level GPU example, shared at its Graphics-only draw boundary.
internal sealed class ColorLutExample : IDisposable
{
    private GraphicsDevice? resourceDevice;
    private GraphicsTexture? source;
    private GraphicsShaderModule? patternShader;
    private GraphicsRenderPipeline? patternPipeline;
    private LinearRgbLutGpuTransform? identity;
    private LinearRgbLutGpuTransform? adjusted;
    private float exposure;
    internal bool Enabled { get; set; } = true;
    internal float ExposureStops
    {
        get => exposure;
        set { if (exposure == value) return; exposure = value; adjusted?.Dispose(); adjusted = null; }
    }
    internal string Draw(GraphicsDevice device, GraphicsTexture target)
    {
        if (target.Descriptor.Format != GraphicsTextureFormat.Rgba16Float)
            throw new NotSupportedException("Color LUT requires an FP16 extended-linear HDR intermediate.");
        var size = target.Descriptor.Size;
        EnsureResources(device, size.Width, size.Height); EnsureAdjustedTransform();
        using (var encoder = device.CreateCommandEncoder("HDR LUT test-pattern encoder"))
        {
            using (var pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(source!), "HDR LUT source pattern")))
            { pass.SetPipeline(patternPipeline!); pass.Draw(3); }
            using var commands = encoder.Finish(); device.Queue.Submit(commands);
        }
        (Enabled ? adjusted! : identity!).Apply(source!, StandardColorSpaces.LinearSrgb, target, StandardColorSpaces.LinearSrgb);
        return $"{size.Width} × {size.Height} · FP32 17³ table → FP16 extended-linear sRGB\n" +
            (Enabled ? "Color mix and exposure enabled." : "Identity table selected.") +
            " Input domain −1 to 4; Clamp policy explicitly selected.";
    }
    private void EnsureResources(GraphicsDevice device, uint width, uint height)
    {
        if (ReferenceEquals(resourceDevice, device) && source?.Descriptor.Size == new GraphicsExtent3D(width, height))
        {
            return;
        }
        Dispose();
        // Bound this sample's one additional FP16 image; no CPU pixel copy or size polling is used.
        if ((ulong)width * height * 8 > 128UL * 1024 * 1024)
        {
            throw new InvalidOperationException("The example's HDR source texture exceeds its 128 MiB budget.");
        }
        resourceDevice = device;
        try
        {
            source = device.CreateTexture(new GraphicsTextureDescriptor(new GraphicsExtent3D(width, height),
                GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.RenderAttachment,
                label: "Gallery linear-sRGB HDR LUT input"));
            patternShader = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(PatternShader, "Gallery HDR LUT pattern"));
            patternPipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                patternShader, "vs_main", patternShader, "fs_main", GraphicsTextureFormat.Rgba16Float,
                label: "Gallery HDR LUT source pattern"));
            identity = new LinearRgbLutGpuTransform(device, BakeTable(0f, colorMix: false), PixelPrecision.Float16);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void EnsureAdjustedTransform()
    {
        adjusted ??= new LinearRgbLutGpuTransform(resourceDevice!,
            BakeTable(ExposureStops, colorMix: true), PixelPrecision.Float16);
    }

    private static LinearRgbLut3D BakeTable(float exposureStops, bool colorMix)
    {
        float exposure = MathF.Pow(2f, exposureStops);
        return LinearRgbLut3D.Bake(17, new Vector3(-1f), new Vector3(4f),
            StandardColorSpaces.LinearSrgb, StandardColorSpaces.LinearSrgb,
            color => new LinearRgba(
                (colorMix ? color.Red * 1.05f + color.Green * 0.05f : color.Red) * exposure,
                (colorMix ? color.Red * 0.08f + color.Green * 0.95f : color.Green) * exposure,
                (colorMix ? color.Blue * 0.9f + color.Green * 0.03f : color.Blue) * exposure,
                color.Alpha, StandardColorSpaces.LinearSrgb),
            ColorLutRangePolicy.Clamp);
    }

    public void Dispose()
    {
        adjusted?.Dispose(); adjusted = null;
        identity?.Dispose(); identity = null;
        patternPipeline?.Dispose(); patternPipeline = null;
        patternShader?.Dispose(); patternShader = null;
        source?.Dispose(); source = null;
        resourceDevice = null;
    }

    private const string PatternShader = """
        struct VertexOutput {
            @builtin(position) position: vec4f,
            @location(0) uv: vec2f,
        }
        @vertex fn vs_main(@builtin(vertex_index) index: u32) -> VertexOutput {
            var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
            let p = positions[index];
            var output: VertexOutput;
            output.position = vec4f(p, 0.0, 1.0);
            output.uv = vec2f((p.x + 1.0) * 0.5, (1.0 - p.y) * 0.5);
            return output;
        }
        @fragment fn fs_main(input: VertexOutput) -> @location(0) vec4f {
            let value = input.uv.x * 4.0;
            var rgb = vec3f(value);
            if (input.uv.y > 0.5 && input.uv.y <= 0.6667) { rgb = vec3f(value, 0.0, 0.0); }
            if (input.uv.y > 0.6667 && input.uv.y <= 0.8333) { rgb = vec3f(0.0, value, 0.0); }
            if (input.uv.y > 0.8333) { rgb = vec3f(0.0, 0.0, value); }
            return vec4f(rgb, 1.0);
        }
        """;
}

using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;

namespace Mu3D.GalleryApp.Pages;

// Same scene-linear Rec.2020 stimulus for raw output and both views; no CPU image/SDR intermediate.
internal sealed class ColorComparisonRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly GraphicsBuffer parameters;
    private readonly GraphicsBindGroupLayout bindingsLayout;
    private readonly GraphicsBindGroup bindings;
    private readonly GraphicsPipelineLayout layout;
    private readonly GraphicsShaderModule shader;
    private readonly GraphicsRenderPipeline pipeline;
    private GraphicsTexture? source;
    private ColorViewGpuTransform? view;
    private GraphicsRenderPipeline? rawPipeline;
    private GraphicsTextureFormat rawFormat;
    private readonly Vector4[] uniforms = [default, ConvertPrimary(1, 0, 0), ConvertPrimary(0, 1, 0), ConvertPrimary(0, 0, 1)];

    internal ColorComparisonRenderer(GraphicsDevice device)
    {
        this.device = device;
        var created = new List<IDisposable>();
        try
        {
            parameters = Own(device.CreateBuffer(new(64, GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination)));
            bindingsLayout = Own(device.CreateBindGroupLayout(new([
                new GraphicsBindGroupLayoutEntry(0, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 64)])));
            bindings = Own(device.CreateBindGroup(new(bindingsLayout, [new GraphicsBindGroupEntry(0, parameters, 0, 64)])));
            layout = Own(device.CreatePipelineLayout(new([bindingsLayout])));
            shader = Own(device.CreateShaderModule(new(Shader, "HDR saturated-color test")));
            pipeline = Own(device.CreateRenderPipeline(new(shader, "vs_main", shader, "fs_main",
                GraphicsTextureFormat.Rgba32Float, layout: layout)));
        }
        catch { for (int i = created.Count - 1; i >= 0; i--) created[i].Dispose(); throw; }
        T Own<T>(T resource) where T : IDisposable { created.Add(resource); return resource; }
    }

    internal void Draw(GraphicsTexture target, ColorViewPreset preset, float exposureStops,
        float saturation, bool rainbow, ColorEncoding outputEncoding)
    {
        var size = target.Descriptor.Size;
        if ((ulong)size.Width * size.Height * 16 > 128UL * 1024 * 1024)
            throw new InvalidOperationException("The test source exceeds its 128 MiB budget.");
        if (source?.Descriptor.Size != size)
        {
            source?.Dispose();
            source = device.CreateTexture(new(size, GraphicsTextureFormat.Rgba32Float,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding, label: "FP32 Rec.2020 test source"));
        }
        var transform = new ColorViewTransform(preset, StandardColorSpaces.LinearRec2020, exposureStops, 100);
        if (view is null || view.Transform.Preset != preset || view.OutputFormat != target.Descriptor.Format || view.OutputEncoding != outputEncoding)
        {
            view?.Dispose(); view = null;
            view = new(device, transform, target.Descriptor.Format, outputEncoding);
        }
        else view.UpdateTransform(transform);
        WriteParameters(saturation, rainbow, 0, 0);
        using (var encoder = device.CreateCommandEncoder("HDR test source"))
        {
            using (var pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(source))))
            {
                pass.SetPipeline(pipeline); pass.SetBindGroup(0, bindings); pass.Draw(3);
            }
            using var command = encoder.Finish(); device.Queue.Submit(command);
        }
        view.Apply(source, StandardColorSpaces.LinearRec2020, target, StandardColorSpaces.LinearSrgb);
    }

    internal void DrawStandard(GraphicsTexture target, float exposureStops, float saturation,
        bool rainbow, bool hdr, ColorEncoding outputEncoding)
    {
        var format = target.Descriptor.Format;
        bool floating = format == GraphicsTextureFormat.Rgba16Float;
        bool softwareSrgb = format is GraphicsTextureFormat.Rgba8Unorm or GraphicsTextureFormat.Bgra8Unorm;
        bool hardwareSrgb = format is GraphicsTextureFormat.Rgba8UnormSrgb or GraphicsTextureFormat.Bgra8UnormSrgb;
        if (!(floating && outputEncoding == ColorEncoding.ExtendedSrgbLinear ||
            !hdr && (softwareSrgb || hardwareSrgb) && outputEncoding == ColorEncoding.Srgb))
            throw new NotSupportedException("Raw HDR requires FP16 extended-linear output; SDR clipping must be selected explicitly.");
        if (rawPipeline is null || rawFormat != format)
        {
            rawPipeline?.Dispose(); rawPipeline = null;
            rawPipeline = device.CreateRenderPipeline(new(shader, "vs_main", shader, "fs_raw", format, layout: layout));
            rawFormat = format;
        }
        // Flags: explicit SDR hard clip, then software sRGB encoding only for non-sRGB UNORM targets.
        WriteParameters(saturation, rainbow, exposureStops, (hdr ? 0 : 1) + (softwareSrgb ? 2 : 0));
        using var encoder = device.CreateCommandEncoder("Raw color comparison (no display view)");
        using (var pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(target))))
        {
            pass.SetPipeline(rawPipeline); pass.SetBindGroup(0, bindings); pass.Draw(3);
        }
        using var command = encoder.Finish(); device.Queue.Submit(command);
    }

    private void WriteParameters(float saturation, bool rainbow, float exposureStops, int flags)
    {
        uniforms[0] = new(saturation, rainbow ? 1 : 0, MathF.Pow(2, exposureStops), flags);
        device.Queue.WriteBuffer(parameters, 0, MemoryMarshal.AsBytes(uniforms.AsSpan()));
    }

    private static Vector4 ConvertPrimary(float r, float g, float b)
    {
        var c = StandardLinearRgbConverter.Convert(new(r, g, b, 1, StandardColorSpaces.LinearRec2020), StandardColorSpaces.LinearSrgb);
        return new(c.Red, c.Green, c.Blue, 0);
    }

    public void Dispose()
    {
        rawPipeline?.Dispose(); view?.Dispose(); source?.Dispose(); pipeline.Dispose(); shader.Dispose();
        layout.Dispose(); bindings.Dispose(); bindingsLayout.Dispose(); parameters.Dispose();
    }

    private const string Shader = """
        struct Parameters { settings: vec4f, red: vec4f, green: vec4f, blue: vec4f, };
        @group(0) @binding(0) var<uniform> parameters: Parameters;
        struct VertexOutput { @builtin(position) position: vec4f, @location(0) uv: vec2f, };
        @vertex fn vs_main(@builtin(vertex_index) index: u32) -> VertexOutput {
            var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
            let p = positions[index];
            var result: VertexOutput;
            result.position = vec4f(p, 0.0, 1.0);
            result.uv = vec2f((p.x + 1.0) * 0.5, (1.0 - p.y) * 0.5);
            return result;
        }
        fn stimulus(uv: vec2f) -> vec3f {
            let settings = parameters.settings;
            let colors = array(vec3f(1,0,0), vec3f(0,1,0), vec3f(0,0,1),
                vec3f(0,1,1), vec3f(1,0,1), vec3f(1,1,0), vec3f(1));
            var primary = colors[min(u32(max(uv.y,0.0)*7.0),6u)];
            if (settings.y > 0.5) {
                let h = clamp(uv.y,0.0,1.0)*6.0;
                primary = clamp(abs((vec3f(h)+vec3f(0,4,2)) % vec3f(6)-vec3f(3))-vec3f(1),vec3f(0),vec3f(1));
            }
            let peak = 0.18 * exp2(-6.0 + 18.0 * uv.x);
            return mix(vec3f(1), primary, settings.x) * peak;
        }
        @fragment fn fs_main(input: VertexOutput) -> @location(0) vec4f {
            return vec4f(stimulus(input.uv), 1);
        }
        @fragment fn fs_raw(input: VertexOutput) -> @location(0) vec4f {
            let source = stimulus(input.uv) * parameters.settings.z;
            var rgb = parameters.red.rgb * source.r + parameters.green.rgb * source.g + parameters.blue.rgb * source.b;
            let flags = u32(parameters.settings.w);
            if ((flags & 1u) != 0u) { rgb = clamp(rgb, vec3f(0), vec3f(1)); }
            if ((flags & 2u) != 0u) {
                rgb = select(12.92 * rgb, 1.055 * pow(rgb, vec3f(1.0/2.4)) - 0.055, rgb > vec3f(0.0031308));
            }
            // Only the extreme +6 EV end can exceed FP16 storage. Keep it finite, not tone mapped.
            return vec4f(clamp(rgb, vec3f(-65504), vec3f(65504)), 1);
        }
        """;
}

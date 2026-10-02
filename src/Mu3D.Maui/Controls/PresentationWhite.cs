using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;

namespace Mu3D.Maui.Controls;

// Platform hosts supply a native unit only when its nit mapping is known. Apple EDR and
// Android retain compositor-managed relative white; unknown does not mean 80 nits.
internal static class PresentationWhite
{
    internal static float Resolve(OutputSettings settings, ColorEncoding encoding,
        float? nativeUnitNits, float? systemWhiteNits, bool windowsMatchSdrWhite = true)
    {
        if (settings.WhiteMode == OutputWhiteMode.PlatformNative) return 1;
        bool extended = encoding == ColorEncoding.ExtendedSrgbLinear;
        if (settings.WhiteMode == OutputWhiteMode.FixedAbsolute)
        {
            if (!extended || nativeUnitNits is not > 0)
                throw new NotSupportedException("Fixed absolute white requires an HDR surface with a known native nit mapping. Use System white on compositor-managed platforms.");
            return settings.ReferenceWhiteNits / nativeUnitNits.Value;
        }
        return windowsMatchSdrWhite && extended && nativeUnitNits is > 0 && systemWhiteNits is > 0 &&
            float.IsFinite(systemWhiteNits.Value)
            ? systemWhiteNits.Value / nativeUnitNits.Value : 1;
    }
}

// Runs once after the caller's full display pipeline. RGB scaling commutes with alpha
// association, so preserve alpha exactly without an extra unpremultiply/premultiply cycle.
internal sealed class PresentationWhitePass : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly List<IDisposable> resources = [];
    private readonly GraphicsBuffer parameters;
    private readonly GraphicsBindGroupLayout layout;
    private readonly GraphicsRenderPipeline pipeline;
    private GraphicsTexture? input;

    internal PresentationWhitePass(GraphicsDevice device, GraphicsTextureFormat format)
    {
        this.device = device;
        Format = format;
        try
        {
            parameters = Own(device.CreateBuffer(new GraphicsBufferDescriptor(16,
                GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination, "Presentation white")));
            layout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor([
                new GraphicsBindGroupLayoutEntry(0, GraphicsShaderStage.Fragment,
                    GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD),
                new GraphicsBindGroupLayoutEntry(1, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 16)])));
            var pipelineLayout = Own(device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor([layout])));
            var shader = Own(device.CreateShaderModule(new GraphicsShaderModuleDescriptor(Shader, "Presentation white")));
            pipeline = Own(device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                shader, "vertex", shader, "fragment", format, layout: pipelineLayout)));
        }
        catch { Dispose(); throw; }
    }

    internal GraphicsTextureFormat Format { get; }
    internal GraphicsTexture GetInput(GraphicsTexture target)
    {
        if (input is not null && input.Descriptor.Size == target.Descriptor.Size) return input;
        var replacement = device.CreateTexture(new GraphicsTextureDescriptor(target.Descriptor.Size, Format,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.CopySource |
            GraphicsTextureUsage.CopyDestination, label: "Unscaled presentation"));
        input?.Dispose();
        return input = replacement;
    }

    internal void Apply(GraphicsTexture target, float scale)
    {
        Vector4[] values = [new(scale, 0, 0, 0)];
        device.Queue.WriteBuffer(parameters, 0, MemoryMarshal.AsBytes(values.AsSpan()));
        using var view = device.CreateTextureView(new GraphicsTextureViewDescriptor(input!));
        using var bindings = device.CreateBindGroup(new GraphicsBindGroupDescriptor(layout,
            [new GraphicsBindGroupEntry(0, view), new GraphicsBindGroupEntry(1, parameters, 0, 16)]));
        using var encoder = device.CreateCommandEncoder("Presentation white");
        using (var pass = encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(new GraphicsRenderPassColorAttachment(target))))
        {
            pass.SetPipeline(pipeline);
            pass.SetBindGroup(0, bindings);
            pass.Draw(3);
        }
        using var commands = encoder.Finish();
        device.Queue.Submit(commands);
    }

    public void Dispose()
    {
        input?.Dispose(); input = null;
        for (int i = resources.Count - 1; i >= 0; i--) resources[i].Dispose();
        resources.Clear();
    }

    private T Own<T>(T value) where T : IDisposable { resources.Add(value); return value; }

    private const string Shader = """
        @group(0) @binding(0) var source: texture_2d<f32>;
        @group(0) @binding(1) var<uniform> scale: vec4<f32>;
        @vertex fn vertex(@builtin(vertex_index) i: u32) -> @builtin(position) vec4<f32> {
            var p = array(vec2<f32>(-1,-1), vec2<f32>(3,-1), vec2<f32>(-1,3));
            return vec4<f32>(p[i],0,1);
        }
        @fragment fn fragment(@builtin(position) p: vec4<f32>) -> @location(0) vec4<f32> {
            let color = textureLoad(source, vec2<i32>(p.xy), 0);
            return vec4<f32>(color.rgb * scale.x, color.a);
        }
        """;
}

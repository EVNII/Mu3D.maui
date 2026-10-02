using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;

namespace Mu3D.GalleryApp.Pages;

// Shared FP32 data for the native HDR view and both print previews.
internal static class CmykPrintingPattern
{
    internal const int Columns = 64, Rows = 32;
    internal static readonly float[] GrayLevels = [0, .018f, .18f, .5f, 1, 2, 4, 8];

    internal static LinearRgba[] Create()
    {
        var pixels = new LinearRgba[Columns * Rows];
        for (int y = 0; y < Rows; y++) for (int x = 0; x < Columns; x++)
        {
            Vector3 rgb;
            if (y >= 24) rgb = new(GrayLevels[x / 8]);
            else
            {
                // Linear primary mixtures, not encoded HSV samples reinterpreted as linear.
                float hue = 6f * x / (Columns - 1), f = hue - MathF.Floor(hue);
                Vector3 primary = ((int)hue % 6) switch
                {
                    0 => new(1, f, 0), 1 => new(1-f, 1, 0), 2 => new(0, 1, f),
                    3 => new(0, 1-f, 1), 4 => new(f, 0, 1), _ => new(1, 0, 1-f),
                };
                // Twelve stops centered on 0.18: shadows and midtones occupy most of the chart.
                rgb = primary * (.18f * MathF.Pow(2, -6 + 12f * y / 23));
            }
            pixels[y * Columns + x] = new(rgb.X, rgb.Y, rgb.Z, 1, StandardColorSpaces.LinearAdobeRgb);
        }
        return pixels;
    }
}

// Copyable low-level drawing example. Surface ownership remains with Mu3DView.
internal sealed class CmykPrintingHdrRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly GraphicsBuffer buffer;
    private readonly GraphicsBindGroupLayout bindingsLayout;
    private readonly GraphicsBindGroup bindings;
    private readonly GraphicsPipelineLayout layout;
    private readonly GraphicsShaderModule shader;
    private readonly GraphicsRenderPipeline pipeline;
    private float lastExposure = float.NaN;

    internal CmykPrintingHdrRenderer(GraphicsDevice device)
    {
        this.device = device;
        var created = new List<IDisposable>();
        try
        {
            ulong size = CmykPrintingPattern.Columns * CmykPrintingPattern.Rows * 16;
            buffer = Own(device.CreateBuffer(new(size, GraphicsBufferUsage.Storage | GraphicsBufferUsage.CopyDestination)));
            bindingsLayout = Own(device.CreateBindGroupLayout(new([
                new GraphicsBindGroupLayoutEntry(0, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, size)])));
            bindings = Own(device.CreateBindGroup(new(bindingsLayout, [new GraphicsBindGroupEntry(0, buffer, 0, size)])));
            layout = Own(device.CreatePipelineLayout(new([bindingsLayout])));
            shader = Own(device.CreateShaderModule(new(Shader, "Print HDR source diagnostic")));
            pipeline = Own(device.CreateRenderPipeline(new(shader, "vs_main", shader, "fs_main",
                GraphicsTextureFormat.Rgba16Float, layout: layout)));
        }
        catch { for (int i = created.Count - 1; i >= 0; i--) created[i].Dispose(); throw; }
        T Own<T>(T resource) where T : IDisposable { created.Add(resource); return resource; }
    }

    internal void Draw(GraphicsTexture target, LinearRgba[] source, float exposureStops)
    {
        if (target.Descriptor.Format != GraphicsTextureFormat.Rgba16Float)
            throw new ArgumentException("The diagnostic requires FP16 HDR output.", nameof(target));
        if (source.Length != CmykPrintingPattern.Columns * CmykPrintingPattern.Rows)
            throw new ArgumentException("Unexpected chart size.", nameof(source));
        if (lastExposure != exposureStops)
        {
            float exposure = MathF.Pow(2, exposureStops);
            var pixels = source.Select(c =>
            {
                var rgb = StandardLinearRgbConverter.Convert(c, StandardColorSpaces.LinearSrgb);
                return new Vector4(rgb.Red * exposure, rgb.Green * exposure, rgb.Blue * exposure, 1);
            }).ToArray();
            device.Queue.WriteBuffer(buffer, 0, MemoryMarshal.AsBytes(pixels.AsSpan()));
            lastExposure = exposureStops;
        }
        using var encoder = device.CreateCommandEncoder("Unmapped HDR color band");
        using (var pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(target))))
        {
            pass.SetPipeline(pipeline); pass.SetBindGroup(0, bindings); pass.Draw(3);
        }
        using var command = encoder.Finish(); device.Queue.Submit(command);
    }

    public void Dispose()
    {
        pipeline.Dispose(); shader.Dispose(); layout.Dispose(); bindings.Dispose(); bindingsLayout.Dispose(); buffer.Dispose();
    }

    private const string Shader = """
        @group(0) @binding(0) var<storage, read> pixels: array<vec4f>;
        struct VertexOutput { @builtin(position) position: vec4f, @location(0) uv: vec2f, };
        @vertex fn vs_main(@builtin(vertex_index) index: u32) -> VertexOutput {
            var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
            let p = positions[index];
            var result: VertexOutput;
            result.position = vec4f(p, 0.0, 1.0);
            result.uv = vec2f((p.x + 1.0) * 0.5, (1.0 - p.y) * 0.5);
            return result;
        }
        @fragment fn fs_main(input: VertexOutput) -> @location(0) vec4f {
            let x = min(u32(max(input.uv.x, 0.0) * 64.0), 63u);
            let y = min(u32(max(input.uv.y, 0.0) * 32.0), 31u);
            return pixels[y * 64u + x];
        }
        """;
}

using Mu3D.Graphics;

namespace Mu3D.Native.Wgpu;

// One diagnostic stimulus for native and Canvas hosts. Encoding belongs to the output boundary.
internal static class WgpuReferencePatternRenderer
{
    internal static void Render(GraphicsDevice device, GraphicsTexture target)
    {
        const string shaderCode = """
            struct VertexOutput {
                @builtin(position) position: vec4f,
                @location(0) uv: vec2f,
            }
            @vertex
            fn vs_main(@builtin(vertex_index) vertex_index: u32) -> VertexOutput {
                let positions = array<vec2f, 3>(
                    vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0),
                );
                let position = positions[vertex_index];
                var output: VertexOutput;
                output.position = vec4f(position, 0.0, 1.0);
                output.uv = vec2f(position.x * 0.5 + 0.5, 0.5 - position.y * 0.5);
                return output;
            }
            @fragment
            fn fs_main(input: VertexOutput) -> @location(0) vec4f {
                let x = clamp(input.uv.x, 0.0, 0.999999);
                var value: f32;
                if (input.uv.y < 0.5) {
                    value = 4.0 * x;
                } else {
                    let references = array<f32, 6>(0.0, 0.18, 0.5, 1.0, 2.0, 4.0);
                    value = references[u32(floor(x * 6.0))];
                }
                return vec4f(value, value, value, 1.0);
            }
            """;
        using GraphicsShaderModule shader = device.CreateShaderModule(new(shaderCode, "reference-pattern shader"));
        using GraphicsRenderPipeline pipeline = device.CreateRenderPipeline(new(
            shader, "vs_main", shader, "fs_main", target.Descriptor.Format, label: "reference-pattern pipeline"));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("reference-pattern encoder");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new(
            new GraphicsRenderPassColorAttachment(target, clearColor: new(0, 0, 0, 1)), "reference-pattern pass")))
        {
            pass.SetPipeline(pipeline);
            pass.Draw(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish("reference-pattern commands");
        device.Queue.Submit(commands);
    }
}

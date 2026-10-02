using Mu3D.Graphics;

namespace Mu3D.Native.Wgpu;

// Shared command workload; native and browser hosts retain their own completion mechanisms.
internal static class WgpuOffscreenTriangleRenderer
{
    internal static void Render(GraphicsDevice device, GraphicsTexture target)
    {
        const string shaderCode = """
            @vertex fn vs_main(@builtin(vertex_index) vertex_index: u32) -> @builtin(position) vec4f {
                var positions = array(vec2f(-0.7, -0.7), vec2f(0.7, -0.7), vec2f(0.0, 0.7));
                return vec4f(positions[vertex_index], 0.0, 1.0);
            }
            @fragment fn fs_main() -> @location(0) vec4f {
                return vec4f(0.05, 0.15, 2.0, 1.0);
            }
            """;
        using GraphicsShaderModule shader = device.CreateShaderModule(new(shaderCode, "M2 offscreen triangle shader"));
        using GraphicsRenderPipeline pipeline = device.CreateRenderPipeline(new(
            shader, "vs_main", shader, "fs_main", target.Descriptor.Format, label: "M2 offscreen triangle pipeline"));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("M2 offscreen triangle encoder");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new(
            new GraphicsRenderPassColorAttachment(target, clearColor: new(0, 0, 0, 1)), "M2 offscreen triangle pass")))
        {
            pass.SetPipeline(pipeline);
            pass.Draw(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish("M2 offscreen triangle commands");
        device.Queue.Submit(commands);
    }
}

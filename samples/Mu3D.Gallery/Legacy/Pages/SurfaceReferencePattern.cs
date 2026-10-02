using Mu3D.Graphics;
namespace Mu3D.Samples;

// Backend-independent reference drawing; the control owns acquisition, white adaptation and presentation.
internal sealed class SurfaceReferencePattern : IDisposable
{
    private readonly GraphicsShaderModule shader;
    private readonly GraphicsRenderPipeline pipeline;
    internal SurfaceReferencePattern(GraphicsDevice device, GraphicsTextureFormat format)
    {
        Format = format;
        shader = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(Shader, "Reference pattern"));
        try { pipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
            shader, "vs_main", shader, "fs_main", format)); }
        catch { shader.Dispose(); throw; }
    }
    internal GraphicsTextureFormat Format { get; }
    internal void Draw(GraphicsDevice device, GraphicsTexture target)
    {
        using var encoder = device.CreateCommandEncoder("Reference pattern");
        using (var pass = encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(new GraphicsRenderPassColorAttachment(target))))
        {
            pass.SetPipeline(pipeline);
            pass.Draw(3);
        }
        using var commands = encoder.Finish();
        device.Queue.Submit(commands);
    }
    public void Dispose() { pipeline.Dispose(); shader.Dispose(); }
        const string Shader = """
            struct VertexOutput {
                @builtin(position) position: vec4f,
                @location(0) uv: vec2f,
            }

            @vertex
            fn vs_main(@builtin(vertex_index) vertex_index: u32) -> VertexOutput {
                let positions = array<vec2f, 3>(
                    vec2f(-1.0, -1.0),
                    vec2f(3.0, -1.0),
                    vec2f(-1.0, 3.0),
                );
                let position = positions[vertex_index];
                var output: VertexOutput;
                output.position = vec4f(position, 0.0, 1.0);
                output.uv = vec2f(
                    position.x * 0.5 + 0.5,
                    0.5 - position.y * 0.5,
                );
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
}

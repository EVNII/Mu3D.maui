using Mu3D.Graphics;

namespace Mu3D.GalleryApp.Examples;

/// <summary>Shares the custom-draw triangle between native and browser Gallery hosts.</summary>
internal sealed class HdrTriangleExample : IDisposable
{
    private const string TriangleShader = """
        @vertex fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4f {
            let positions = array(vec2f(-0.72, -0.65), vec2f(0.72, -0.65), vec2f(0.0, 0.72));
            return vec4f(positions[index], 0.0, 1.0);
        }

        @fragment fn fs_main() -> @location(0) vec4f {
            return vec4f(0.05, 0.15, 2.0, 1.0);
        }
        """;

    private GraphicsDevice? pipelineDevice;
    private GraphicsTextureFormat pipelineFormat;
    private GraphicsShaderModule? shader;
    private GraphicsRenderPipeline? pipeline;

    /// <summary>Draws into the borrowed target, recreating owned resources for a new device or format.</summary>
    internal void Render(GraphicsDevice device, GraphicsTexture target)
    {
        EnsurePipeline(device, target.Descriptor.Format);
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("triangle frame");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
            new GraphicsRenderPassDescriptor(
                new GraphicsRenderPassColorAttachment(
                    target,
                    clearColor: new GraphicsClearColor(0, 0, 0, 1)),
                "triangle pass")))
        {
            pass.SetPipeline(pipeline!);
            pass.Draw(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish("triangle commands");
        device.Queue.Submit(commands);
    }

    /// <summary>Releases only owned GPU resources; a subsequent draw may prepare them again.</summary>
    public void Dispose()
    {
        pipeline?.Dispose();
        pipeline = null;
        shader?.Dispose();
        shader = null;
        pipelineDevice = null;
    }

    private void EnsurePipeline(GraphicsDevice device, GraphicsTextureFormat format)
    {
        if (ReferenceEquals(pipelineDevice, device) && pipelineFormat == format && pipeline is not null)
        {
            return;
        }

        Dispose();
        pipelineDevice = device;
        pipelineFormat = format;
        shader = device.CreateShaderModule(
            new GraphicsShaderModuleDescriptor(TriangleShader, "triangle shader"));
        pipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
            shader,
            "vs_main",
            shader,
            "fs_main",
            format,
            label: "triangle pipeline"));
    }
}

using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

// Backend-neutral, opaque, two-sided depth rendering. Camera-frustum culling must not remove casters.
internal sealed class OpenPbrDirectionalShadow : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly List<IDisposable> owned = [];
    private readonly GraphicsBuffer parameters;
    private readonly GraphicsBindGroupLayout casterLayout;
    private readonly GraphicsSampler sampler;
    private readonly GraphicsRenderPipeline pipeline;
    private GraphicsBindGroup? casterGroup;
    private GraphicsTexture? depth;
    private GraphicsTextureView? depthView;
    private GraphicsBindGroup? sampleGroup;
    private bool active;

    internal GraphicsBindGroupLayout Layout { get; }
    internal GraphicsBindGroup SampleGroup => sampleGroup!;

    internal OpenPbrDirectionalShadow(GraphicsDevice device)
    {
        this.device = device;
        try
        {
            parameters = Own(device.CreateBuffer(new(96, GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination)));
            Layout = Own(device.CreateBindGroupLayout(new([
                new(0, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 96),
                new(1, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.Depth, GraphicsTextureViewDimension.TwoD),
                new(2, GraphicsShaderStage.Fragment, GraphicsSamplerBindingType.Comparison)])));
            sampler = Own(device.CreateSampler(new GraphicsSamplerDescriptor(compare: GraphicsCompareFunction.LessEqual)));
            casterLayout = Own(device.CreateBindGroupLayout(new([
                new(0, GraphicsShaderStage.Vertex, GraphicsBufferBindingType.Uniform, 96),
                new(1, GraphicsShaderStage.Vertex, GraphicsBufferBindingType.ReadOnlyStorage, 16)])));
            var layout = Own(device.CreatePipelineLayout(new([casterLayout])));
            var shader = Own(device.CreateShaderModule(new(CasterSource, "OpenPBR directional depth")));
            pipeline = Own(device.CreateRenderPipeline(new(shader, "main", new GraphicsDepthStencilState(GraphicsTextureFormat.Depth32Float),
                layout: layout, label: "OpenPBR directional shadow")));
        }
        catch { Dispose(); throw; }
    }

    internal void SetGeometry(GraphicsBuffer triangles)
    {
        var next = device.CreateBindGroup(new(casterLayout, [new(0, parameters, 0, 96), new(1, triangles, 0, triangles.Size)]));
        casterGroup?.Dispose(); casterGroup = next;
    }

    internal void Prepare(Vector4[] frame, uint resolution)
    {
        active = frame[4].X >= 0;
        uint size = active ? resolution : 1;
        if (depth is null || depth.Descriptor.Size.Width != size)
        {
            GraphicsTexture? nextDepth = null;
            GraphicsTextureView? nextView = null;
            GraphicsBindGroup? nextGroup = null;
            try
            {
                nextDepth = device.CreateTexture(new(new(size, size), GraphicsTextureFormat.Depth32Float,
                    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding, label: "OpenPBR directional shadow map"));
                nextView = device.CreateTextureView(new(nextDepth));
                nextGroup = device.CreateBindGroup(new(Layout, [new(0, parameters, 0, 96), new(1, nextView), new(2, sampler)]));
            }
            catch { nextGroup?.Dispose(); nextView?.Dispose(); nextDepth?.Dispose(); throw; }
            sampleGroup?.Dispose(); depthView?.Dispose(); depth?.Dispose();
            depth = nextDepth; depthView = nextView; sampleGroup = nextGroup;
        }
        device.Queue.WriteBuffer(parameters, 0, MemoryMarshal.AsBytes(frame.AsSpan()));
    }

    internal void Encode(GraphicsCommandEncoder encoder, uint vertices)
    {
        if (!active) return;
        using var pass = encoder.BeginRenderPass(new(new GraphicsRenderPassDepthAttachment(depth!), "OpenPBR directional shadow map"));
        if (vertices == 0) return;
        pass.SetPipeline(pipeline); pass.SetBindGroup(0, casterGroup!); pass.Draw(vertices);
    }

    internal static Vector4[] CreateFrame(OpenPbrSceneSnapshot scene, PunctualLight[] lights, OpenPbrRenderPass settings)
    {
        Vector4[] frame = new Vector4[6];
        frame[4].X = -1;
        if (!settings.DirectionalShadowsEnabled || settings.Mode is not (OpenPbrRenderMode.Raster or OpenPbrRenderMode.Fast)) return frame;
        int index = -1;
        for (int i = 0; i < lights.Length; i++)
            if (lights[i] is DirectionalLight { CastsShadows: true })
            {
                if (index >= 0) throw new NotSupportedException("OpenPBR Raster/Fast supports one shadow-casting directional light; disable CastsShadows on additional lights.");
                index = i;
            }
        if (index < 0 || scene.NodeCount == 0) return frame;
        var light = (DirectionalLight)lights[index];
        if (light.Intensity == 0 || light.ShadowOpacity == 0) return frame;
        Vector3 direction = light.WorldDirection;
        Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.UnitY)) > .99f ? Vector3.UnitX : Vector3.UnitY;
        Matrix4x4 view = Matrix4x4.CreateLookAt(Vector3.Zero, direction, up);
        Vector4 lo = scene.BvhNodes[0], hi = scene.BvhNodes[1];
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int corner = 0; corner < 8; corner++)
        {
            Vector3 p = Vector3.Transform(new((corner & 1) == 0 ? lo.X : hi.X,
                (corner & 2) == 0 ? lo.Y : hi.Y, (corner & 4) == 0 ? lo.Z : hi.Z), view);
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
        }
        Vector3 padding = Vector3.Max((max - min) * .05f, new Vector3(.001f));
        min -= padding; max += padding;
        Matrix4x4 matrix = view * Matrix4x4.CreateOrthographicOffCenter(min.X, max.X, min.Y, max.Y, -max.Z, -min.Z);
        OpenPbrSceneCompiler.RequireFinite(matrix);
        frame[0] = new(matrix.M11, matrix.M12, matrix.M13, matrix.M14);
        frame[1] = new(matrix.M21, matrix.M22, matrix.M23, matrix.M24);
        frame[2] = new(matrix.M31, matrix.M32, matrix.M33, matrix.M34);
        frame[3] = new(matrix.M41, matrix.M42, matrix.M43, matrix.M44);
        frame[4] = new(index, settings.DirectionalShadowDepthBias, settings.DirectionalShadowNormalBias, settings.DirectionalShadowPcfRadius);
        frame[5] = new(settings.DirectionalShadowMapSize, light.ShadowOpacity, 0, 0);
        return frame;
    }

    public void Dispose()
    {
        casterGroup?.Dispose(); sampleGroup?.Dispose(); depthView?.Dispose(); depth?.Dispose();
        for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose();
        owned.Clear();
    }
    private T Own<T>(T resource) where T : IDisposable { owned.Add(resource); return resource; }

    private const string CasterSource = """
        @group(0) @binding(0) var<uniform> shadow: array<vec4<f32>, 6>;
        @group(0) @binding(1) var<storage, read> triangles: array<vec4<f32>>;
        @vertex fn main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32> {
            let base = (index / 3u) * 12u;
            if (triangles[base + 2u].w == 0.0) { return vec4<f32>(2.0, 2.0, 2.0, 1.0); }
            let p = triangles[base + index % 3u].xyz;
            return shadow[0] * p.x + shadow[1] * p.y + shadow[2] * p.z + shadow[3];
        }
        """;
}

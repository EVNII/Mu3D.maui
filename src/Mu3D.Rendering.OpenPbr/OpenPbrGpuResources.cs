using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

internal sealed class OpenPbrGpuResources : IDisposable
{
    internal Dictionary<string, double> InitializationMilliseconds { get; } = [];
    private readonly List<IDisposable> owned = [];
    private readonly List<IDisposable> sceneOwned = [];
    private readonly List<IDisposable> extentOwned = [];
    private readonly GraphicsBuffer frameBuffer;
    private readonly GraphicsBuffer initialBuffer;
    private readonly OpenPbrDirectionalShadow shadow;
    private readonly GraphicsBindGroupLayout sceneLayout;
    private readonly GraphicsBindGroupLayout imageLayout;
    private readonly GraphicsBindGroupLayout outputLayout;
    private readonly GraphicsBindGroup lutGroup;
    private readonly GraphicsRenderPipeline? tracePipeline;
    private readonly GraphicsRenderPipeline outputPipeline;
    private readonly GraphicsRenderPipeline? visibilityPipeline;
    private readonly GraphicsRenderPipeline? fastPipeline;
    private readonly GraphicsBindGroupLayout? fastSceneLayout;
    private readonly GraphicsBindGroupLayout? fastImageLayout;
    private GraphicsTexture visibilityTexture = null!;
    private GraphicsTextureView visibilityView = null!;
    private GraphicsTexture? visibilityDepth;
    private readonly GraphicsTexture[] history = new GraphicsTexture[2];
    private readonly GraphicsTextureView[] historyViews = new GraphicsTextureView[2];
    private readonly GraphicsBuffer outputParameters;
    private GraphicsBindGroup? sceneGroup;
    private GraphicsBindGroup? fastSceneGroup;
    private GraphicsBindGroup? fastImageGroup;
    private GraphicsTextureView? environmentView;
    private int current;
    private uint vertexCount;

    internal GraphicsDevice Device { get; }
    internal uint Width { get; private set; }
    internal uint Height { get; private set; }
    internal GraphicsTextureFormat OutputFormat { get; }
    internal bool HasRasterVisibility { get; }
    internal bool Fast { get; }
    internal bool RasterOnly { get; }

    internal OpenPbrGpuResources(GraphicsDevice device, uint width, uint height,
        GraphicsTextureFormat outputFormat, StandardRgbColorSpaceReference outputSpace, bool raster, bool fast = false, bool rasterOnly = false)
    {
        Device = device; Width = width; Height = height; OutputFormat = outputFormat;
        HasRasterVisibility = raster;
        Fast = fast; RasterOnly = rasterOnly;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        void Mark(string phase) { InitializationMilliseconds[phase] = timer.Elapsed.TotalMilliseconds; timer.Restart(); }
        try
        {
            frameBuffer = Buffer(new byte[20 * 16], GraphicsBufferUsage.Uniform, owned);
            initialBuffer = Buffer(new byte[8 * 16], GraphicsBufferUsage.Storage, owned);
            shadow = Own(new OpenPbrDirectionalShadow(device));
            sceneLayout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
            [new(0, GraphicsShaderStage.Vertex | GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 320),
             new(1, GraphicsShaderStage.Vertex | GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16),
             new(2, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16),
             new(3, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16),
             new(4, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16),
             new(5, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 128),
             new(6, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16),
             new(7, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16)])));
            GraphicsBindGroupLayout lutLayout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                [new(0, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 72736 * 4)])));
            GraphicsBuffer lut = Buffer(Resource("Lut.bin"), GraphicsBufferUsage.Storage, owned);
            lutGroup = Own(device.CreateBindGroup(new GraphicsBindGroupDescriptor(lutLayout, [new(0, lut, 0, 72736 * 4)])));
            imageLayout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                [new(0, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD),
                 new(1, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD),
                 new(2, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD)])));
            if (!fast)
            {
                Mark("buffers-layouts-shadow");
                GraphicsPipelineLayout layout = Own(device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor([sceneLayout, lutLayout, imageLayout, shadow.Layout])));
                GraphicsShaderModule vertex = Own(device.CreateShaderModule(new GraphicsShaderModuleDescriptor(VertexSource)));
                GraphicsShaderModule fragment = Own(device.CreateShaderModule(new GraphicsShaderModuleDescriptor(
                    System.Text.Encoding.UTF8.GetString(Resource(rasterOnly ? "raster.wgsl" : "render.wgsl")), "OpenPBR RGB transport")));
                Mark("transport-module");
                tracePipeline = Own(device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(vertex, "vs_main", fragment,
                    "main", GraphicsTextureFormat.Rgba32Float, layout: layout, label: "OpenPBR transport")));
                Mark("transport-pipeline");
            }
            else
            {
                // ADR 0030: a slim scene layout (no BVH, media or interpreted graph buffers) keeps the
                // Fast fragment stage at four storage buffers plus the LUT, and the image group at
                // twelve sampled textures plus the separate shadow depth — inside portable 8/16 limits.
                fastSceneLayout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                [new(0, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 320),
                 new(1, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16),
                 new(2, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16),
                 new(3, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16),
                 new(4, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.ReadOnlyStorage, 16)])));
                var fastImageEntries = new List<GraphicsBindGroupLayoutEntry>
                {
                    new(0, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD),
                    new(1, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.Float, GraphicsTextureViewDimension.Cube),
                    new(2, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.Float, GraphicsTextureViewDimension.CubeArray),
                    new(3, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.Float, GraphicsTextureViewDimension.TwoD),
                    new(4, GraphicsShaderStage.Fragment, GraphicsSamplerBindingType.Filtering),
                };
                for (uint slot = 0; slot < OpenPbrFastBake.MaximumBakedTextures; slot++)
                    fastImageEntries.Add(new(5 + slot, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.Float, GraphicsTextureViewDimension.TwoD));
                fastImageEntries.Add(new(13, GraphicsShaderStage.Fragment, GraphicsSamplerBindingType.Filtering));
                fastImageLayout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(fastImageEntries)));
                GraphicsPipelineLayout fastLayout = Own(device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor(
                    [fastSceneLayout, lutLayout, fastImageLayout, shadow.Layout])));
                GraphicsShaderModule fastVertex = Own(device.CreateShaderModule(new GraphicsShaderModuleDescriptor(VertexSource)));
                GraphicsShaderModule fastFragment = Own(device.CreateShaderModule(new GraphicsShaderModuleDescriptor(
                    System.Text.Encoding.UTF8.GetString(Resource("fast.wgsl")), "OpenPBR Fast split-sum shading")));
                fastPipeline = Own(device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(fastVertex, "vs_main", fastFragment,
                    "main", GraphicsTextureFormat.Rgba32Float, layout: fastLayout, label: "OpenPBR Fast transport")));
            }
            if (raster)
            {
                GraphicsPipelineLayout visibilityLayout = Own(device.CreatePipelineLayout(new([sceneLayout])));
                GraphicsShaderModule visibilityShader = Own(device.CreateShaderModule(new(
                    System.Text.Encoding.UTF8.GetString(Resource("visibility.wgsl")), "OpenPBR raster visibility")));
                visibilityPipeline = Own(device.CreateRenderPipeline(new(visibilityShader, "vs_main", visibilityShader,
                    "fs_main", GraphicsTextureFormat.Rgba32Float, layout: visibilityLayout,
                    depthStencil: new(GraphicsTextureFormat.Depth32Float), label: "OpenPBR raster primary visibility")));
            }
            Resize(width, height);
            Vector4[] transform = new Vector4[3];
            Mark("visibility-history");
            for (int i = 0; i < 3; i++)
            {
                LinearRgba c = StandardLinearRgbConverter.Convert(new LinearRgba(i == 0 ? 1 : 0, i == 1 ? 1 : 0,
                    i == 2 ? 1 : 0, 1, StandardColorSpaces.AcesCg), outputSpace);
                transform[i] = new(c.Red, c.Green, c.Blue, 0);
            }
            outputParameters = Buffer(MemoryMarshal.AsBytes(transform.AsSpan()), GraphicsBufferUsage.Uniform, owned);
            outputLayout = Own(device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                [new(0, GraphicsShaderStage.Fragment, GraphicsTextureSampleType.UnfilterableFloat, GraphicsTextureViewDimension.TwoD),
                 new(1, GraphicsShaderStage.Fragment, GraphicsBufferBindingType.Uniform, 48)])));
            GraphicsPipelineLayout outputPipelineLayout = Own(device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor([outputLayout])));
            GraphicsShaderModule outputShader = Own(device.CreateShaderModule(new GraphicsShaderModuleDescriptor(OutputSource)));
            outputPipeline = Own(device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(outputShader, "vs_main", outputShader,
                "fs_main", outputFormat, layout: outputPipelineLayout, label: "OpenPBR linear HDR output")));
            Mark("output");
        }
        catch { Dispose(); throw; }
    }

    // Extent changes keep all expensive pipelines. New scene bindings are uploaded by the pass
    // after resizing because Fast's image group contains the primary visibility view.
    internal void Resize(uint width, uint height)
    {
        List<IDisposable> pending = [];
        try
        {
            var nextVisibility = Keep(Device.CreateTexture(new(new(HasRasterVisibility ? width : 1,
                HasRasterVisibility ? height : 1), GraphicsTextureFormat.Rgba32Float,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding)));
            var nextView = Keep(Device.CreateTextureView(new(nextVisibility)));
            var nextDepth = HasRasterVisibility ? Keep(Device.CreateTexture(new(new(width, height),
                GraphicsTextureFormat.Depth32Float, GraphicsTextureUsage.RenderAttachment))) : null;
            GraphicsTexture[] nextHistory = new GraphicsTexture[2];
            GraphicsTextureView[] nextHistoryViews = new GraphicsTextureView[2];
            for (int i = 0; i < 2; i++)
            {
                nextHistory[i] = Keep(Device.CreateTexture(new(new(width, height), GraphicsTextureFormat.Rgba32Float,
                    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding)));
                nextHistoryViews[i] = Keep(Device.CreateTextureView(new(nextHistory[i])));
            }
            DisposeList(extentOwned);
            extentOwned.AddRange(pending); pending.Clear();
            visibilityTexture = nextVisibility; visibilityView = nextView; visibilityDepth = nextDepth;
            nextHistory.CopyTo(history, 0); nextHistoryViews.CopyTo(historyViews, 0);
            Width = width; Height = height; current = 0;
        }
        catch { DisposeList(pending); throw; }
        T Keep<T>(T item) where T : IDisposable { pending.Add(item); return item; }
    }

    internal void SetScene(OpenPbrSceneSnapshot scene, Vector4[] materials, Vector4[] lights, EquirectangularHdrEnvironment? environment, OpenPbrGpuGraph graph, OpenPbrFastBake? bake = null)
    {
        List<IDisposable> pending = [];
        try
        {
        GraphicsBuffer triangles = Buffer(MemoryMarshal.AsBytes(scene.Triangles.AsSpan()), GraphicsBufferUsage.Storage, pending);
        GraphicsBuffer nodes = Buffer(MemoryMarshal.AsBytes(scene.BvhNodes.AsSpan()), GraphicsBufferUsage.Storage, pending);
        GraphicsBuffer materialBuffer = Buffer(MemoryMarshal.AsBytes(materials.AsSpan()), GraphicsBufferUsage.Storage, pending);
        GraphicsBuffer lightBuffer = Buffer(MemoryMarshal.AsBytes(lights.AsSpan()), GraphicsBufferUsage.Storage, pending);
        GraphicsBuffer graphBuffer = Buffer(MemoryMarshal.AsBytes(graph.Programs.AsSpan()), GraphicsBufferUsage.Storage, pending);
        GraphicsBuffer texelBuffer = Buffer(MemoryMarshal.AsBytes(graph.Pixels.AsSpan()), GraphicsBufferUsage.Storage, pending);
        GraphicsBindGroup nextSceneGroup = Device.CreateBindGroup(new GraphicsBindGroupDescriptor(sceneLayout,
            [Entry(0, frameBuffer), Entry(1, triangles), Entry(2, nodes), Entry(3, materialBuffer), Entry(4, lightBuffer), Entry(5, initialBuffer), Entry(6, graphBuffer), Entry(7, texelBuffer)]));
        pending.Add(nextSceneGroup);
        GraphicsBindGroup? nextFastSceneGroup = null;
        GraphicsBindGroup? nextFastImageGroup = null;
        GraphicsTextureView? nextEnvironmentView = null;
        if (Fast)
        {
            OpenPbrFastEnvironment fastEnvironment = OpenPbrFastEnvironment.Create(Device, environment);
            pending.Add(fastEnvironment);
            GraphicsBuffer bakeInfoBuffer = Buffer(
                MemoryMarshal.AsBytes((bake?.PackBakeInfo(scene.SourceMaterials.Length) ?? [Vector4.Zero]).AsSpan()),
                GraphicsBufferUsage.Storage, pending);
            nextFastSceneGroup = Device.CreateBindGroup(new GraphicsBindGroupDescriptor(fastSceneLayout!,
                [Entry(0, frameBuffer), Entry(1, triangles), Entry(2, materialBuffer), Entry(3, lightBuffer), Entry(4, bakeInfoBuffer)]));
            pending.Add(nextFastSceneGroup);
            GraphicsSampler bakeSampler = Device.CreateSampler(new GraphicsSamplerDescriptor(
                GraphicsAddressMode.Repeat, GraphicsAddressMode.Repeat, GraphicsAddressMode.Repeat,
                GraphicsFilterMode.Linear, GraphicsFilterMode.Linear, GraphicsFilterMode.Linear,
                label: "OpenPBR Fast baked texture sampler"));
            pending.Add(bakeSampler);
            var imageEntries = new GraphicsBindGroupEntry[14];
            imageEntries[0] = new(0, visibilityView);
            imageEntries[1] = new(1, fastEnvironment.DiffuseView);
            imageEntries[2] = new(2, fastEnvironment.SpecularView);
            imageEntries[3] = new(3, fastEnvironment.LutView);
            imageEntries[4] = new(4, fastEnvironment.Sampler);
            GraphicsTextureView? placeholder = null;
            for (uint slot = 0; slot < OpenPbrFastBake.MaximumBakedTextures; slot++)
                imageEntries[5 + slot] = new(5 + slot, slot < (bake?.Textures.Count ?? 0)
                    ? UploadBaked(bake!.Textures[(int)slot], pending)
                    : placeholder ??= PlaceholderView(pending));
            imageEntries[13] = new(13, bakeSampler);
            nextFastImageGroup = Device.CreateBindGroup(new GraphicsBindGroupDescriptor(fastImageLayout!, imageEntries));
            pending.Add(nextFastImageGroup);
        }
        else
        {
        uint width = environment?.Width ?? 1, height = environment?.Height ?? 1;
        Vector4[] pixels = environment is null ? [Vector4.Zero] : environment.Pixels.Select(p =>
            new Vector4(OpenPbrRenderPass.Radiance(new(p.X, p.Y, p.Z, 1, environment.ColorSpace)), 1)).ToArray();
        GraphicsTexture texture = Device.CreateTexture(new(new(width, height), GraphicsTextureFormat.Rgba32Float,
            GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding));
        pending.Add(texture);
        Device.Queue.WriteTexture(texture, 0, default, new(width, height), MemoryMarshal.AsBytes(pixels.AsSpan()), width * 16, height);
        nextEnvironmentView = Device.CreateTextureView(new(texture));
        pending.Add(nextEnvironmentView);
        }
        DisposeList(sceneOwned);
        sceneOwned.AddRange(pending); pending.Clear();
        sceneGroup = nextSceneGroup; environmentView = nextEnvironmentView;
        fastSceneGroup = nextFastSceneGroup; fastImageGroup = nextFastImageGroup;
        vertexCount = checked((uint)scene.TriangleCount * 3);
        shadow.SetGeometry(triangles);
        }
        catch { DisposeList(pending); throw; }
    }

    private GraphicsTextureView PlaceholderView(List<IDisposable> pending)
    {
        GraphicsTexture texture = Device.CreateTexture(new(new GraphicsExtent3D(1, 1), GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding, label: "OpenPBR Fast unused bake slot"));
        pending.Add(texture);
        Half[] zero = new Half[4];
        Device.Queue.WriteTexture(texture, 0, default, new GraphicsExtent3D(1, 1), MemoryMarshal.AsBytes(zero.AsSpan()), 8, 1);
        GraphicsTextureView view = Device.CreateTextureView(new GraphicsTextureViewDescriptor(texture));
        pending.Add(view);
        return view;
    }

    private GraphicsTextureView UploadBaked(OpenPbrFastBake.BakedTexture baked, List<IDisposable> pending)
    {
        uint width = (uint)baked.Width, height = (uint)baked.Height;
        uint mips = (uint)(BitOperations.Log2((uint)Math.Max(baked.Width, baked.Height)) + 1);
        GraphicsTexture texture = Device.CreateTexture(new GraphicsTextureDescriptor(new GraphicsExtent3D(width, height),
            GraphicsTextureFormat.Rgba16Float, GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
            mipLevelCount: mips, label: "OpenPBR Fast baked graph texture"));
        pending.Add(texture);
        Vector4[] level = baked.Texels; int levelWidth = baked.Width, levelHeight = baked.Height;
        for (uint mip = 0; mip < mips; mip++)
        {
            Half[] texels = new Half[level.Length * 4];
            for (int i = 0; i < level.Length; i++)
            {
                texels[i * 4] = OpenPbrFastEnvironment.CheckedHalf(level[i].X, "baked graph");
                texels[i * 4 + 1] = OpenPbrFastEnvironment.CheckedHalf(level[i].Y, "baked graph");
                texels[i * 4 + 2] = OpenPbrFastEnvironment.CheckedHalf(level[i].Z, "baked graph");
                texels[i * 4 + 3] = OpenPbrFastEnvironment.CheckedHalf(level[i].W, "baked graph");
            }
            Device.Queue.WriteTexture(texture, mip, default, new GraphicsExtent3D((uint)levelWidth, (uint)levelHeight),
                MemoryMarshal.AsBytes(texels.AsSpan()), (uint)(levelWidth * 8), (uint)levelHeight);
            if (mip + 1 < mips)
                level = OpenPbrFastBake.DownsampleBox(level, levelWidth, levelHeight, out levelWidth, out levelHeight);
        }
        GraphicsTextureView view = Device.CreateTextureView(new GraphicsTextureViewDescriptor(texture, mipLevelCount: mips));
        pending.Add(view);
        return view;
    }

    internal void Draw(Vector4[] frame, Vector4[] initialMedia, GraphicsTexture output, Vector4[] shadowFrame, uint shadowSize)
    {
        shadow.Prepare(shadowFrame, shadowSize);
        Device.Queue.WriteBuffer(frameBuffer, 0, MemoryMarshal.AsBytes(frame.AsSpan()));
        if (initialMedia.Length > 0) Device.Queue.WriteBuffer(initialBuffer, 0, MemoryMarshal.AsBytes(initialMedia.AsSpan()));
        using GraphicsBindGroup? images = Fast ? null : Device.CreateBindGroup(new(imageLayout,
            [new(0, historyViews[1 - current]), new(1, environmentView!), new(2, visibilityView)]));
        using GraphicsBindGroup outputGroup = Device.CreateBindGroup(new(outputLayout,
            [new(0, historyViews[current]), new(1, outputParameters, 0, 48)]));
        using GraphicsCommandEncoder encoder = Device.CreateCommandEncoder("OpenPBR frame");
        shadow.Encode(encoder, vertexCount);
        if (HasRasterVisibility && frame[15].Z == 0)
        {
            using GraphicsRenderPassEncoder visibility = encoder.BeginRenderPass(new(
                new GraphicsRenderPassColorAttachment(visibilityTexture), "OpenPBR primary visibility",
                new GraphicsRenderPassDepthAttachment(visibilityDepth!)));
            visibility.SetPipeline(visibilityPipeline!); visibility.SetBindGroup(0, sceneGroup!);
            if (vertexCount > 0) visibility.Draw(vertexCount);
        }
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(history[current]))))
        {
            if (Fast)
            {
                pass.SetPipeline(fastPipeline!);
                pass.SetBindGroup(0, fastSceneGroup!); pass.SetBindGroup(1, lutGroup); pass.SetBindGroup(2, fastImageGroup!);
            }
            else
            {
                pass.SetPipeline(tracePipeline!); pass.SetBindGroup(0, sceneGroup!); pass.SetBindGroup(1, lutGroup); pass.SetBindGroup(2, images!);
            }
            pass.SetBindGroup(3, shadow.SampleGroup);
            pass.Draw(3);
        }
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(output))))
        {
            pass.SetPipeline(outputPipeline); pass.SetBindGroup(0, outputGroup); pass.Draw(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish();
        Device.Queue.Submit(commands);
        current = 1 - current;
    }

    internal void Present(GraphicsTexture output)
    {
        using GraphicsBindGroup group = Device.CreateBindGroup(new(outputLayout,
            [new(0, historyViews[1 - current]), new(1, outputParameters, 0, 48)]));
        using GraphicsCommandEncoder encoder = Device.CreateCommandEncoder("OpenPBR retained estimate");
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(output))))
        {
            pass.SetPipeline(outputPipeline); pass.SetBindGroup(0, group); pass.Draw(3);
        }
        using GraphicsCommandBuffer commands = encoder.Finish();
        Device.Queue.Submit(commands);
    }

    public void Dispose() { DisposeList(sceneOwned); DisposeList(extentOwned); DisposeList(owned); }
    private static void DisposeList(List<IDisposable> list)
    {
        for (int i = list.Count - 1; i >= 0; i--) list[i].Dispose();
        list.Clear();
    }
    private T Own<T>(T item) where T : IDisposable { owned.Add(item); return item; }
    private GraphicsBuffer Buffer(ReadOnlySpan<byte> bytes, GraphicsBufferUsage usage, List<IDisposable> ownership)
    {
        ulong length = (ulong)Math.Max(16, bytes.Length);
        // GraphicsCapabilities does not yet expose binding limits; stay within the portable
        // WebGPU storage-binding guarantee even on devices supporting larger allocations.
        if ((usage & GraphicsBufferUsage.Storage) != 0 && length > 128UL * 1024 * 1024)
            throw new InvalidOperationException("OpenPBR storage buffers are limited to 128 MiB each.");
        GraphicsBuffer buffer = Device.CreateBuffer(new(length, usage | GraphicsBufferUsage.CopyDestination));
        ownership.Add(buffer);
        if (!bytes.IsEmpty) Device.Queue.WriteBuffer(buffer, 0, bytes);
        return buffer;
    }
    private static GraphicsBindGroupEntry Entry(uint index, GraphicsBuffer buffer) => new(index, buffer, 0, buffer.Size);
    private static byte[] Resource(string name)
    {
        using Stream stream = typeof(OpenPbrGpuResources).Assembly.GetManifestResourceStream($"Mu3D.Rendering.OpenPbr.Shaders.{name}")
            ?? throw new InvalidOperationException($"Missing generated OpenPBR resource {name}.");
        using MemoryStream data = new(); stream.CopyTo(data); return data.ToArray();
    }
    private const string VertexSource = """
        @vertex fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32> {
            let p = vec2<f32>(f32((index << 1u) & 2u), f32(index & 2u));
            return vec4<f32>(p * 2.0 - vec2<f32>(1.0), 0.0, 1.0);
        }
        """;
    private const string OutputSource = """
        struct Vertex { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32> }
        @vertex fn vs_main(@builtin(vertex_index) index: u32) -> Vertex {
            let p = vec2<f32>(f32((index << 1u) & 2u), f32(index & 2u));
            return Vertex(vec4<f32>(p * 2.0 - vec2<f32>(1.0), 0.0, 1.0), vec2<f32>(p.x, 1.0-p.y));
        }
        @group(0) @binding(0) var source: texture_2d<f32>;
        @group(0) @binding(1) var<uniform> matrix: array<vec4<f32>,3>;
        @fragment fn fs_main(input: Vertex) -> @location(0) vec4<f32> {
            let size = vec2<i32>(textureDimensions(source));
            let point = input.uv * vec2<f32>(size) - vec2<f32>(0.5);
            let p = vec2<i32>(floor(point)); let f = fract(point);
            let a = textureLoad(source, clamp(p, vec2<i32>(0), size-1), 0);
            let b = textureLoad(source, clamp(p+vec2<i32>(1,0), vec2<i32>(0), size-1), 0);
            let c = textureLoad(source, clamp(p+vec2<i32>(0,1), vec2<i32>(0), size-1), 0);
            let d = textureLoad(source, clamp(p+vec2<i32>(1,1), vec2<i32>(0), size-1), 0);
            let color = mix(mix(a,b,f.x),mix(c,d,f.x),f.y);
            return vec4<f32>(matrix[0].xyz*color.r + matrix[1].xyz*color.g + matrix[2].xyz*color.b, color.a);
        }
        """;
}

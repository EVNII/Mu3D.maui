using Mu3D.Graphics;
using Mu3D.Rendering;

sealed class RecordingGraphicsDevice : GraphicsDevice
{
    private readonly RecordingGraphicsQueue queue;

    internal RecordingGraphicsDevice()
        : base(new GraphicsCapabilities
        {
            SupportsFloat16Textures = true,
            SupportsShaderFloat16 = true,
            SupportsHdrSurface = true,
            SupportsBcTextureCompression = true,
            SupportsEtc2TextureCompression = true,
            SupportsAstcTextureCompression = true,
            SurfaceFormats = [PresentationFormat.Rgba16Float],
        }) => queue = new RecordingGraphicsQueue(this);

    public override GraphicsQueue Queue => queue;

    internal bool FailNextBufferWrite { get; set; }

    internal Action? OnBufferDisposing { get; set; }

    internal List<GraphicsRenderPipelineDescriptor> PipelineDescriptors { get; } = [];

    internal List<GraphicsSamplerDescriptor> SamplerDescriptors { get; } = [];

    internal List<string?> SubmittedPipelineLabels { get; } = [];

    internal List<RecordingGraphicsBuffer> Buffers { get; } = [];

    internal IReadOnlyList<RecordedTextureWrite> TextureWrites => queue.TextureWrites;

    protected override GraphicsBuffer CreateBufferCore(GraphicsBufferDescriptor descriptor)
    {
        RecordingGraphicsBuffer buffer = new(this, descriptor);
        Buffers.Add(buffer);
        return buffer;
    }

    protected override GraphicsTexture CreateTextureCore(GraphicsTextureDescriptor descriptor) =>
        new RecordingGraphicsTexture(this, descriptor);

    protected override GraphicsTextureView CreateTextureViewCore(GraphicsTextureViewDescriptor descriptor) =>
        new RecordingGraphicsTextureView(this, descriptor);

    protected override GraphicsSampler CreateSamplerCore(GraphicsSamplerDescriptor descriptor)
    {
        SamplerDescriptors.Add(descriptor);
        return new RecordingGraphicsSampler(this, descriptor);
    }

    protected override GraphicsBindGroupLayout CreateBindGroupLayoutCore(
        GraphicsBindGroupLayoutDescriptor descriptor) => new RecordingBindGroupLayout(this, descriptor);

    protected override GraphicsPipelineLayout CreatePipelineLayoutCore(
        GraphicsPipelineLayoutDescriptor descriptor) => new RecordingPipelineLayout(this, descriptor);

    protected override GraphicsBindGroup CreateBindGroupCore(GraphicsBindGroupDescriptor descriptor) =>
        new RecordingBindGroup(this, descriptor);

    protected override GraphicsShaderModule CreateShaderModuleCore(GraphicsShaderModuleDescriptor descriptor) =>
        new RecordingShaderModule(this, descriptor);

    protected override GraphicsRenderPipeline CreateRenderPipelineCore(
        GraphicsRenderPipelineDescriptor descriptor)
    {
        PipelineDescriptors.Add(descriptor);
        return new RecordingRenderPipeline(this, descriptor);
    }

    protected override GraphicsCommandEncoder CreateCommandEncoderCore(string? label) =>
        new RecordingCommandEncoder(this, label);

    protected override void DisposeCore()
    {
    }
}

sealed class RecordingGraphicsQueue(RecordingGraphicsDevice device) : GraphicsQueue(device)
{
    internal List<RecordedTextureWrite> TextureWrites { get; } = [];

    protected override void WriteBufferCore(
        GraphicsBuffer destination,
        ulong destinationOffset,
        ReadOnlySpan<byte> data)
    {
        if (device.FailNextBufferWrite)
        {
            device.FailNextBufferWrite = false;
            throw new InvalidOperationException("Injected buffer upload failure.");
        }
        RecordingGraphicsBuffer buffer = (RecordingGraphicsBuffer)destination;
        data.CopyTo(buffer.Data.AsSpan(checked((int)destinationOffset)));
        buffer.LastWriteLength = data.Length;
    }

    protected override void WriteTextureCore(
        GraphicsTexture destination,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D writeSize,
        ReadOnlySpan<byte> data,
        uint bytesPerRow,
        uint rowsPerImage)
    {
        TextureWrites.Add(new RecordedTextureWrite(
            destination,
            mipLevel,
            origin,
            writeSize,
            data.ToArray(),
            bytesPerRow,
            rowsPerImage));
    }

    protected override void SubmitCore(GraphicsCommandBuffer commandBuffer)
    {
        foreach (Action command in ((RecordingCommandBuffer)commandBuffer).Commands)
        {
            command();
        }
    }
}

readonly record struct RecordedTextureWrite(
    GraphicsTexture Destination,
    uint MipLevel,
    GraphicsOrigin3D Origin,
    GraphicsExtent3D Size,
    byte[] Data,
    uint BytesPerRow,
    uint RowsPerImage);

sealed class RecordingOrderedRenderPass(
    RenderPassDescriptor descriptor,
    Action<RenderPassContext> execute) : IRenderPass
{
    public RenderPassDescriptor Descriptor { get; } = descriptor;

    internal int ExecutionCount { get; private set; }

    public void Execute(RenderPassContext context)
    {
        ExecutionCount++;
        execute(context);
    }
}

sealed class RecordingGraphicsBuffer : GraphicsBuffer
{
    internal RecordingGraphicsBuffer(RecordingGraphicsDevice device, GraphicsBufferDescriptor descriptor)
        : base(device, descriptor) => Data = new byte[checked((int)descriptor.Size)];

    internal byte[] Data { get; }

    internal int LastWriteLength { get; set; }

    protected override Task<byte[]> ReadAsyncCore(
        ulong offset,
        int byteCount,
        CancellationToken cancellationToken) => Task.FromResult(
            Data.AsSpan(checked((int)offset), byteCount).ToArray());

    protected override void DisposeCore()
    {
        ((RecordingGraphicsDevice)Device).OnBufferDisposing?.Invoke();
    }
}

sealed class RecordingGraphicsTexture(
    RecordingGraphicsDevice device,
    GraphicsTextureDescriptor descriptor) : GraphicsTexture(device, descriptor)
{
    internal int IndexedDrawCount { get; set; }

    internal List<GraphicsCullMode> CullModes { get; } = [];

    internal GraphicsClearColor? LastClearColor { get; set; }

    internal GraphicsLoadOperation? LastColorLoadOperation { get; set; }

    internal GraphicsLoadOperation? LastDepthLoadOperation { get; set; }

    protected override void DisposeCore()
    {
    }
}

sealed class RecordingGraphicsTextureView(
    RecordingGraphicsDevice device,
    GraphicsTextureViewDescriptor descriptor) : GraphicsTextureView(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class RecordingGraphicsSampler(
    RecordingGraphicsDevice device,
    GraphicsSamplerDescriptor descriptor) : GraphicsSampler(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class RecordingBindGroupLayout(
    RecordingGraphicsDevice device,
    GraphicsBindGroupLayoutDescriptor descriptor) : GraphicsBindGroupLayout(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class RecordingPipelineLayout(
    RecordingGraphicsDevice device,
    GraphicsPipelineLayoutDescriptor descriptor) : GraphicsPipelineLayout(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class RecordingBindGroup(
    RecordingGraphicsDevice device,
    GraphicsBindGroupDescriptor descriptor) : GraphicsBindGroup(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class RecordingShaderModule(
    RecordingGraphicsDevice device,
    GraphicsShaderModuleDescriptor descriptor) : GraphicsShaderModule(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class RecordingRenderPipeline(
    RecordingGraphicsDevice device,
    GraphicsRenderPipelineDescriptor descriptor) : GraphicsRenderPipeline(device, descriptor)
{
    protected override void DisposeCore()
    {
    }
}

sealed class RecordingCommandEncoder(RecordingGraphicsDevice device, string? label)
    : GraphicsCommandEncoder(device, label)
{
    private readonly List<Action> commands = [];

    protected override void CopyBufferToBufferCore(
        GraphicsBuffer source,
        ulong sourceOffset,
        GraphicsBuffer destination,
        ulong destinationOffset,
        ulong size) => commands.Add(() =>
            ((RecordingGraphicsBuffer)source).Data.AsSpan(checked((int)sourceOffset), checked((int)size))
                .CopyTo(((RecordingGraphicsBuffer)destination).Data.AsSpan(checked((int)destinationOffset))));

    protected override GraphicsRenderPassEncoder BeginRenderPassCore(GraphicsRenderPassDescriptor descriptor) =>
        new RecordingRenderPass(this, descriptor, commands);

    protected override GraphicsCommandBuffer FinishCore(string? label) =>
        new RecordingCommandBuffer((RecordingGraphicsDevice)Device, label, [.. commands]);

    protected override void DisposeCore()
    {
    }
}

sealed class RecordingRenderPass : GraphicsRenderPassEncoder
{
    private readonly List<Action> commands;
    private readonly List<GraphicsCullMode> cullModes = [];
    private readonly List<string?> pipelineLabels = [];
    private RecordingRenderPipeline? pipeline;

    internal RecordingRenderPass(
        RecordingCommandEncoder encoder,
        GraphicsRenderPassDescriptor descriptor,
        List<Action> commands)
        : base(encoder, descriptor) => this.commands = commands;

    protected override void SetPipelineCore(GraphicsRenderPipeline value) =>
        pipeline = (RecordingRenderPipeline)value;

    protected override void SetVertexBufferCore(uint slot, GraphicsBuffer buffer, ulong offset, ulong size)
    {
    }

    protected override void SetIndexBufferCore(
        GraphicsBuffer buffer,
        GraphicsIndexFormat format,
        ulong offset,
        ulong size)
    {
    }

    protected override void SetBindGroupCore(uint groupIndex, GraphicsBindGroup bindGroup)
    {
    }

    protected override void SetViewportCore(
        float x,
        float y,
        float width,
        float height,
        float minimumDepth,
        float maximumDepth)
    {
    }

    protected override void SetScissorRectCore(uint x, uint y, uint width, uint height)
    {
    }

    protected override void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance)
    {
        pipelineLabels.Add(pipeline!.Label);
    }

    protected override void DrawIndexedCore(
        uint indexCount,
        uint instanceCount,
        uint firstIndex,
        int baseVertex,
        uint firstInstance)
    {
        cullModes.Add(pipeline!.Descriptor.CullMode);
        pipelineLabels.Add(pipeline.Label);
    }

    protected override void EndCore()
    {
        GraphicsCullMode[] submittedCullModes = [.. cullModes];
        string?[] submittedPipelineLabels = [.. pipelineLabels];
        GraphicsRenderPassColorAttachment? submittedColor = Descriptor.ColorAttachment;
        GraphicsRenderPassDepthAttachment? submittedDepth = Descriptor.DepthAttachment;
        commands.Add(() =>
        {
            ((RecordingGraphicsDevice)Device).SubmittedPipelineLabels.AddRange(submittedPipelineLabels);
            if (submittedColor is GraphicsRenderPassColorAttachment colorAttachment)
            {
                RecordingGraphicsTexture target =
                    (RecordingGraphicsTexture)colorAttachment.Texture;
                target.IndexedDrawCount += submittedCullModes.Length;
                target.CullModes.AddRange(submittedCullModes);
                target.LastClearColor = colorAttachment.ClearColor;
                target.LastColorLoadOperation = colorAttachment.LoadOperation;
            }
            if (submittedDepth is GraphicsRenderPassDepthAttachment depthAttachment)
            {
                ((RecordingGraphicsTexture)depthAttachment.Texture).LastDepthLoadOperation =
                    depthAttachment.LoadOperation;
            }
        });
    }

    protected override void DisposeCore()
    {
    }
}

sealed class RecordingCommandBuffer(
    RecordingGraphicsDevice device,
    string? label,
    IReadOnlyList<Action> commands) : GraphicsCommandBuffer(device, label)
{
    internal IReadOnlyList<Action> Commands { get; } = commands;

    protected override void DisposeCore()
    {
    }
}

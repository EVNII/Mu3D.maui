namespace Mu3D.Graphics;

/// <summary>Provides queue writes and command-buffer submission for one device.</summary>
public abstract class GraphicsQueue
{
    /// <summary>Initializes a queue owned by a device.</summary>
    protected GraphicsQueue(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        Device = device;
    }

    /// <summary>Gets the owning device.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Writes a four-byte-aligned byte range into a copy-destination buffer.</summary>
    public void WriteBuffer(GraphicsBuffer destination, ulong destinationOffset, ReadOnlySpan<byte> data)
    {
        Device.ThrowIfUnavailable();
        Device.ValidateOwner(destination, nameof(destination));
        if ((destination.Usage & GraphicsBufferUsage.CopyDestination) == 0)
        {
            throw new ArgumentException("The destination buffer does not allow copy writes.", nameof(destination));
        }
        if (destinationOffset > destination.Size || (ulong)data.Length > destination.Size - destinationOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationOffset), "The write exceeds the buffer bounds.");
        }
        if ((destinationOffset & 3) != 0 || (data.Length & 3) != 0)
        {
            throw new ArgumentException("The write offset and byte count must be multiples of four.", nameof(data));
        }

        WriteBufferCore(destination, destinationOffset, data);
    }

    /// <summary>
    /// Writes tightly or explicitly row-padded texels or compressed blocks into one mip/layer
    /// region. This queue-write path does not require the 256-byte row alignment used by
    /// buffer-to-texture copies. For compressed formats, width and height are physical block-aligned
    /// extents and row counts are measured in block rows; the final logical mip may therefore use
    /// one padded block.
    /// </summary>
    public void WriteTexture(
        GraphicsTexture destination,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D writeSize,
        ReadOnlySpan<byte> data,
        uint bytesPerRow,
        uint rowsPerImage)
    {
        Device.ThrowIfUnavailable();
        Device.ValidateOwner(destination, nameof(destination));
        GraphicsTextureDescriptor descriptor = destination.Descriptor;
        if ((descriptor.Usage & GraphicsTextureUsage.CopyDestination) == 0)
        {
            throw new ArgumentException("The destination texture does not allow copy writes.", nameof(destination));
        }
        if (descriptor.SampleCount != 1 || mipLevel >= descriptor.MipLevelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel), "The destination mip level is invalid.");
        }
        if (writeSize.Width == 0 || writeSize.Height == 0 || writeSize.DepthOrArrayLayers == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(writeSize), "The write extent must be non-zero.");
        }

        GraphicsTextureBlockLayout block = GraphicsDevice.GetTextureBlockInfo(descriptor.Format);
        if (block.BytesPerBlock == 0)
        {
            throw new ArgumentException(
                "The destination format does not support color texture writes.",
                nameof(destination));
        }
        uint mipWidth = GraphicsDevice.GetMipDimension(descriptor.Size.Width, mipLevel);
        uint mipHeight = GraphicsDevice.GetMipDimension(descriptor.Size.Height, mipLevel);
        uint physicalMipWidth = AlignToBlock(mipWidth, block.BlockWidth);
        uint physicalMipHeight = AlignToBlock(mipHeight, block.BlockHeight);
        if (origin.X > physicalMipWidth || writeSize.Width > physicalMipWidth - origin.X ||
            origin.Y > physicalMipHeight || writeSize.Height > physicalMipHeight - origin.Y ||
            origin.Z > descriptor.Size.DepthOrArrayLayers ||
            writeSize.DepthOrArrayLayers > descriptor.Size.DepthOrArrayLayers - origin.Z)
        {
            throw new ArgumentOutOfRangeException(nameof(writeSize), "The texture write exceeds its mip or layers.");
        }
        if (origin.X % block.BlockWidth != 0 || origin.Y % block.BlockHeight != 0 ||
            writeSize.Width % block.BlockWidth != 0 ||
            writeSize.Height % block.BlockHeight != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(writeSize),
                "A compressed texture write origin and physical extent must be block-aligned.");
        }
        uint blocksWide = checked((writeSize.Width + block.BlockWidth - 1) / block.BlockWidth);
        uint blockRows = checked((writeSize.Height + block.BlockHeight - 1) / block.BlockHeight);
        ulong minimumBytesPerRow = checked((ulong)blocksWide * block.BytesPerBlock);
        if (bytesPerRow < minimumBytesPerRow || bytesPerRow % block.BytesPerBlock != 0 ||
            rowsPerImage < blockRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytesPerRow),
                "The texture data layout is too small or not texel-block aligned.");
        }
        ulong precedingRows = checked(
            ((ulong)writeSize.DepthOrArrayLayers - 1) * rowsPerImage + blockRows - 1);
        ulong requiredBytes = checked(precedingRows * bytesPerRow + minimumBytesPerRow);
        if ((ulong)data.Length < requiredBytes)
        {
            throw new ArgumentException("The source span is smaller than the declared texture layout.", nameof(data));
        }

        WriteTextureCore(destination, mipLevel, origin, writeSize, data, bytesPerRow, rowsPerImage);
    }

    private static uint AlignToBlock(uint value, uint blockSize) => checked((uint)(
        (((ulong)value + blockSize - 1) / blockSize) * blockSize));

    /// <summary>Submits a finished command buffer exactly once.</summary>
    public void Submit(GraphicsCommandBuffer commandBuffer)
    {
        Device.ThrowIfUnavailable();
        ArgumentNullException.ThrowIfNull(commandBuffer);
        if (!ReferenceEquals(commandBuffer.Device, Device))
        {
            throw new ArgumentException("The command buffer belongs to a different graphics device.", nameof(commandBuffer));
        }
        commandBuffer.MarkSubmitted();
        SubmitCore(commandBuffer);
    }

    /// <summary>Writes bytes after common ownership, usage and bounds validation.</summary>
    protected abstract void WriteBufferCore(
        GraphicsBuffer destination,
        ulong destinationOffset,
        ReadOnlySpan<byte> data);

    /// <summary>Writes validated uncompressed texels to a backend texture.</summary>
    protected abstract void WriteTextureCore(
        GraphicsTexture destination,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D writeSize,
        ReadOnlySpan<byte> data,
        uint bytesPerRow,
        uint rowsPerImage);

    /// <summary>Submits a command buffer after common ownership and state validation.</summary>
    protected abstract void SubmitCore(GraphicsCommandBuffer commandBuffer);
}

/// <summary>Records copy and render commands for one command buffer.</summary>
public abstract class GraphicsCommandEncoder : GraphicsResource
{
    private bool finished;
    private GraphicsRenderPassEncoder? activeRenderPass;

    /// <summary>Initializes a backend command encoder.</summary>
    protected GraphicsCommandEncoder(GraphicsDevice device, string? label)
        : base(device, label)
    {
    }

    /// <summary>Copies a four-byte-aligned range between two buffers owned by this device.</summary>
    public void CopyBufferToBuffer(
        GraphicsBuffer source,
        ulong sourceOffset,
        GraphicsBuffer destination,
        ulong destinationOffset,
        ulong size)
    {
        ThrowIfRecording();
        ThrowIfRenderPassActive();
        Device.ValidateOwner(source, nameof(source));
        Device.ValidateOwner(destination, nameof(destination));
        ArgumentOutOfRangeException.ThrowIfZero(size);
        if ((source.Usage & GraphicsBufferUsage.CopySource) == 0)
        {
            throw new ArgumentException("The source buffer does not allow copies.", nameof(source));
        }
        if ((destination.Usage & GraphicsBufferUsage.CopyDestination) == 0)
        {
            throw new ArgumentException("The destination buffer does not allow copies.", nameof(destination));
        }
        if (sourceOffset > source.Size || size > source.Size - sourceOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceOffset), "The copy exceeds the source bounds.");
        }
        if (destinationOffset > destination.Size || size > destination.Size - destinationOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(destinationOffset), "The copy exceeds the destination bounds.");
        }
        if (((sourceOffset | destinationOffset | size) & 3) != 0)
        {
            throw new ArgumentException("Copy offsets and size must be multiples of four.", nameof(size));
        }

        CopyBufferToBufferCore(source, sourceOffset, destination, destinationOffset, size);
    }

    /// <summary>
    /// Copies one color-texture subresource region into a row-padded buffer. Buffer rows must be
    /// aligned to 256 bytes. For compressed formats, origins and physical copy extents are
    /// block-aligned and <paramref name="rowsPerImage"/> counts block rows.
    /// </summary>
    public void CopyTextureToBuffer(
        GraphicsTexture source,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D copySize,
        GraphicsBuffer destination,
        ulong destinationOffset,
        uint bytesPerRow,
        uint rowsPerImage)
    {
        ThrowIfRecording();
        ThrowIfRenderPassActive();
        Device.ValidateOwner(source, nameof(source));
        Device.ValidateOwner(destination, nameof(destination));
        GraphicsTextureDescriptor descriptor = source.Descriptor;
        if ((descriptor.Usage & GraphicsTextureUsage.CopySource) == 0)
        {
            throw new ArgumentException("The source texture does not allow copies.", nameof(source));
        }
        if ((destination.Usage & GraphicsBufferUsage.CopyDestination) == 0)
        {
            throw new ArgumentException(
                "The destination buffer does not allow copy writes.",
                nameof(destination));
        }
        if (descriptor.SampleCount != 1 || mipLevel >= descriptor.MipLevelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel), "The source mip level is invalid.");
        }
        if (descriptor.Format is GraphicsTextureFormat.Depth32Float or
            GraphicsTextureFormat.Depth32FloatStencil8)
        {
            throw new NotSupportedException(
                "The initial texture-to-buffer contract reads color textures only.");
        }
        if (copySize.Width == 0 || copySize.Height == 0 || copySize.DepthOrArrayLayers == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(copySize), "The copy extent must be non-zero.");
        }

        GraphicsTextureBlockLayout block = GraphicsDevice.GetTextureBlockInfo(descriptor.Format);
        uint mipWidth = GraphicsDevice.GetMipDimension(descriptor.Size.Width, mipLevel);
        uint mipHeight = GraphicsDevice.GetMipDimension(descriptor.Size.Height, mipLevel);
        uint physicalMipWidth = AlignToBlock(mipWidth, block.BlockWidth);
        uint physicalMipHeight = AlignToBlock(mipHeight, block.BlockHeight);
        if (origin.X > physicalMipWidth || copySize.Width > physicalMipWidth - origin.X ||
            origin.Y > physicalMipHeight || copySize.Height > physicalMipHeight - origin.Y ||
            origin.Z > descriptor.Size.DepthOrArrayLayers ||
            copySize.DepthOrArrayLayers > descriptor.Size.DepthOrArrayLayers - origin.Z)
        {
            throw new ArgumentOutOfRangeException(nameof(copySize), "The texture copy exceeds its mip or layers.");
        }
        if (origin.X % block.BlockWidth != 0 || origin.Y % block.BlockHeight != 0 ||
            copySize.Width % block.BlockWidth != 0 || copySize.Height % block.BlockHeight != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(copySize),
                "A texture-to-buffer copy origin and physical extent must be block-aligned.");
        }
        uint blocksWide = copySize.Width / block.BlockWidth;
        uint blockRows = copySize.Height / block.BlockHeight;
        ulong rowBytes = checked((ulong)blocksWide * block.BytesPerBlock);
        if ((destinationOffset & 3) != 0 || destinationOffset % block.BytesPerBlock != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOffset),
                "The destination offset must be aligned to four bytes and one texture block.");
        }
        if (bytesPerRow < rowBytes || bytesPerRow % 256 != 0 || rowsPerImage < blockRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytesPerRow),
                "Texture readback rows must fit the copy, use 256-byte alignment, and declare enough rows.");
        }
        ulong precedingRows = checked(
            ((ulong)copySize.DepthOrArrayLayers - 1) * rowsPerImage + blockRows - 1);
        ulong requiredBytes = checked(precedingRows * bytesPerRow + rowBytes);
        if (destinationOffset > destination.Size || requiredBytes > destination.Size - destinationOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationOffset),
                "The texture copy exceeds the destination buffer.");
        }

        CopyTextureToBufferCore(
            source,
            mipLevel,
            origin,
            copySize,
            destination,
            destinationOffset,
            bytesPerRow,
            rowsPerImage);
    }

    /// <summary>
    /// Copies one color-texture subresource region into another texture with the same format.
    /// Compressed-texture origins and physical copy extents must be block-aligned.
    /// </summary>
    public void CopyTextureToTexture(
        GraphicsTexture source,
        uint sourceMipLevel,
        GraphicsOrigin3D sourceOrigin,
        GraphicsTexture destination,
        uint destinationMipLevel,
        GraphicsOrigin3D destinationOrigin,
        GraphicsExtent3D copySize)
    {
        ThrowIfRecording();
        ThrowIfRenderPassActive();
        Device.ValidateOwner(source, nameof(source));
        Device.ValidateOwner(destination, nameof(destination));
        if (ReferenceEquals(source, destination))
        {
            throw new ArgumentException(
                "The initial texture-copy contract requires different source and destination textures.",
                nameof(destination));
        }

        GraphicsTextureDescriptor sourceDescriptor = source.Descriptor;
        GraphicsTextureDescriptor destinationDescriptor = destination.Descriptor;
        if ((sourceDescriptor.Usage & GraphicsTextureUsage.CopySource) == 0)
        {
            throw new ArgumentException("The source texture does not allow copies.", nameof(source));
        }
        if ((destinationDescriptor.Usage & GraphicsTextureUsage.CopyDestination) == 0)
        {
            throw new ArgumentException(
                "The destination texture does not allow copy writes.",
                nameof(destination));
        }
        if (sourceDescriptor.SampleCount != 1 || sourceMipLevel >= sourceDescriptor.MipLevelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceMipLevel),
                "The source mip level is invalid.");
        }
        if (destinationDescriptor.SampleCount != 1 ||
            destinationMipLevel >= destinationDescriptor.MipLevelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(destinationMipLevel),
                "The destination mip level is invalid.");
        }
        if (sourceDescriptor.Format != destinationDescriptor.Format)
        {
            throw new ArgumentException(
                "Texture-to-texture copies require identical source and destination formats.",
                nameof(destination));
        }
        if (sourceDescriptor.Format is GraphicsTextureFormat.Depth32Float or
            GraphicsTextureFormat.Depth32FloatStencil8)
        {
            throw new NotSupportedException(
                "The initial texture-to-texture contract copies color textures only.");
        }
        if (copySize.Width == 0 || copySize.Height == 0 || copySize.DepthOrArrayLayers == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(copySize), "The copy extent must be non-zero.");
        }

        GraphicsTextureBlockLayout block = GraphicsDevice.GetTextureBlockInfo(sourceDescriptor.Format);
        ValidateTextureCopyRegion(
            sourceDescriptor,
            sourceMipLevel,
            sourceOrigin,
            copySize,
            block,
            "source");
        ValidateTextureCopyRegion(
            destinationDescriptor,
            destinationMipLevel,
            destinationOrigin,
            copySize,
            block,
            "destination");

        CopyTextureToTextureCore(
            source,
            sourceMipLevel,
            sourceOrigin,
            destination,
            destinationMipLevel,
            destinationOrigin,
            copySize);
    }

    /// <summary>Begins a render pass targeting a color attachment, a depth attachment, or both.</summary>
    public GraphicsRenderPassEncoder BeginRenderPass(GraphicsRenderPassDescriptor descriptor)
    {
        ThrowIfRecording();
        if (descriptor.ColorAttachment is null && descriptor.DepthAttachment is null)
        {
            throw new ArgumentException("At least one render attachment is required.", nameof(descriptor));
        }
        if (activeRenderPass is not null)
        {
            throw new InvalidOperationException("A render pass is already active on this command encoder.");
        }
        GraphicsTexture? texture = null;
        if (descriptor.ColorAttachment is GraphicsRenderPassColorAttachment attachment)
        {
            texture = attachment.Texture;
            Device.ValidateOwner(texture, nameof(descriptor));
            if (attachment.View is not null)
            {
                Device.ValidateOwner(attachment.View, nameof(descriptor));
            }
            if ((texture.Descriptor.Usage & GraphicsTextureUsage.RenderAttachment) == 0)
            {
                throw new ArgumentException("The color texture does not allow render attachment use.", nameof(descriptor));
            }
            if (texture.Descriptor.SampleCount != 1 || texture.Descriptor.Size.DepthOrArrayLayers != 1)
            {
                throw new ArgumentException(
                    "The initial render-pass contract requires single-sampled, non-array attachments.",
                    nameof(descriptor));
            }
            if (texture.Descriptor.Format is GraphicsTextureFormat.Depth32Float or GraphicsTextureFormat.Depth32FloatStencil8)
            {
                throw new ArgumentException("A depth format cannot be used as the color attachment.", nameof(descriptor));
            }
            if (!Enum.IsDefined(attachment.LoadOperation) || !Enum.IsDefined(attachment.StoreOperation))
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "The render pass contains an unknown operation.");
            }
            GraphicsClearColor clear = attachment.ClearColor;
            if (!float.IsFinite(clear.Red) || !float.IsFinite(clear.Green) ||
                !float.IsFinite(clear.Blue) || !float.IsFinite(clear.Alpha))
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "Clear components must be finite.");
            }
        }
        if (descriptor.DepthAttachment is GraphicsRenderPassDepthAttachment depthAttachment)
        {
            GraphicsTexture depthTexture = depthAttachment.Texture;
            Device.ValidateOwner(depthTexture, nameof(descriptor));
            if ((depthTexture.Descriptor.Usage & GraphicsTextureUsage.RenderAttachment) == 0)
            {
                throw new ArgumentException("The depth texture does not allow render attachment use.", nameof(descriptor));
            }
            if (depthTexture.Descriptor.Format != GraphicsTextureFormat.Depth32Float)
            {
                throw new ArgumentException(
                    "The initial depth-attachment contract supports Depth32Float; stencil state is reserved.",
                    nameof(descriptor));
            }
            if (depthTexture.Descriptor.SampleCount != 1 ||
                depthTexture.Descriptor.Size.DepthOrArrayLayers != 1)
            {
                throw new ArgumentException(
                    "The initial render-pass contract requires single-sampled, non-array attachments.",
                    nameof(descriptor));
            }
            if (texture is not null &&
                (depthTexture.Descriptor.Size != texture.Descriptor.Size ||
                 depthTexture.Descriptor.SampleCount != texture.Descriptor.SampleCount))
            {
                throw new ArgumentException("Color and depth attachments must have matching dimensions and samples.", nameof(descriptor));
            }
            if (!Enum.IsDefined(depthAttachment.LoadOperation) ||
                !Enum.IsDefined(depthAttachment.StoreOperation) ||
                !float.IsFinite(depthAttachment.ClearValue) ||
                depthAttachment.ClearValue is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(descriptor), "The depth attachment state is invalid.");
            }
        }

        GraphicsRenderPassEncoder result = BeginRenderPassCore(descriptor) ??
            throw new InvalidOperationException("The graphics backend returned a null render-pass encoder.");
        if (!ReferenceEquals(result.CommandEncoder, this))
        {
            throw new InvalidOperationException("The graphics backend returned a render pass for a different encoder.");
        }
        activeRenderPass = result;
        return result;
    }

    /// <summary>Finishes recording and returns a command buffer.</summary>
    public GraphicsCommandBuffer Finish(string? label = null)
    {
        ThrowIfRecording();
        if (activeRenderPass is not null)
        {
            throw new InvalidOperationException("The active render pass must be ended before finishing the encoder.");
        }
        finished = true;
        GraphicsCommandBuffer result = FinishCore(label) ??
            throw new InvalidOperationException("The graphics backend returned a null command buffer.");
        Device.ValidateOwner(result, "result");
        return result;
    }

    /// <summary>Records a validated buffer copy.</summary>
    protected abstract void CopyBufferToBufferCore(
        GraphicsBuffer source,
        ulong sourceOffset,
        GraphicsBuffer destination,
        ulong destinationOffset,
        ulong size);

    /// <summary>Records one validated texture-to-buffer copy through the backend.</summary>
    protected virtual void CopyTextureToBufferCore(
        GraphicsTexture source,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D copySize,
        GraphicsBuffer destination,
        ulong destinationOffset,
        uint bytesPerRow,
        uint rowsPerImage) => throw new NotSupportedException(
            "This graphics backend does not implement texture-to-buffer copies.");

    /// <summary>Records one validated texture-to-texture copy through the backend.</summary>
    protected virtual void CopyTextureToTextureCore(
        GraphicsTexture source,
        uint sourceMipLevel,
        GraphicsOrigin3D sourceOrigin,
        GraphicsTexture destination,
        uint destinationMipLevel,
        GraphicsOrigin3D destinationOrigin,
        GraphicsExtent3D copySize) => throw new NotSupportedException(
            "This graphics backend does not implement texture-to-texture copies.");

    /// <summary>Begins the backend render pass after common attachment validation.</summary>
    protected abstract GraphicsRenderPassEncoder BeginRenderPassCore(GraphicsRenderPassDescriptor descriptor);

    /// <summary>Finishes the backend encoder.</summary>
    protected abstract GraphicsCommandBuffer FinishCore(string? label);

    internal void NotifyRenderPassEnded(GraphicsRenderPassEncoder renderPass)
    {
        if (!ReferenceEquals(activeRenderPass, renderPass))
        {
            throw new InvalidOperationException("The render pass is not active on this command encoder.");
        }
        activeRenderPass = null;
    }

    private void ThrowIfRecording()
    {
        Device.ThrowIfUnavailable();
        ThrowIfDisposed();
        if (finished)
        {
            throw new InvalidOperationException("The command encoder has already been finished.");
        }
    }

    private void ThrowIfRenderPassActive()
    {
        if (activeRenderPass is not null)
        {
            throw new InvalidOperationException("Commands cannot be recorded outside the active render pass.");
        }
    }

    private static void ValidateTextureCopyRegion(
        GraphicsTextureDescriptor descriptor,
        uint mipLevel,
        GraphicsOrigin3D origin,
        GraphicsExtent3D copySize,
        GraphicsTextureBlockLayout block,
        string role)
    {
        uint mipWidth = GraphicsDevice.GetMipDimension(descriptor.Size.Width, mipLevel);
        uint mipHeight = GraphicsDevice.GetMipDimension(descriptor.Size.Height, mipLevel);
        uint physicalMipWidth = AlignToBlock(mipWidth, block.BlockWidth);
        uint physicalMipHeight = AlignToBlock(mipHeight, block.BlockHeight);
        if (origin.X > physicalMipWidth || copySize.Width > physicalMipWidth - origin.X ||
            origin.Y > physicalMipHeight || copySize.Height > physicalMipHeight - origin.Y ||
            origin.Z > descriptor.Size.DepthOrArrayLayers ||
            copySize.DepthOrArrayLayers > descriptor.Size.DepthOrArrayLayers - origin.Z)
        {
            throw new ArgumentOutOfRangeException(
                nameof(copySize),
                $"The texture copy exceeds the {role} mip or layers.");
        }
        if (origin.X % block.BlockWidth != 0 || origin.Y % block.BlockHeight != 0 ||
            copySize.Width % block.BlockWidth != 0 || copySize.Height % block.BlockHeight != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(copySize),
                "A texture-to-texture copy origin and physical extent must be block-aligned.");
        }
    }

    private static uint AlignToBlock(uint value, uint blockSize) => checked((uint)(
        (((ulong)value + blockSize - 1) / blockSize) * blockSize));
}

/// <summary>Records draw commands inside one render pass.</summary>
public abstract class GraphicsRenderPassEncoder : IDisposable
{
    private bool ended;
    private bool disposed;
    private GraphicsRenderPipeline? pipeline;
    private readonly HashSet<uint> vertexBufferSlots = [];
    private readonly Dictionary<uint, GraphicsBindGroup> bindGroups = [];
    private bool hasIndexBuffer;

    /// <summary>Initializes a render pass associated with a command encoder.</summary>
    protected GraphicsRenderPassEncoder(
        GraphicsCommandEncoder commandEncoder,
        GraphicsRenderPassDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(commandEncoder);
        commandEncoder.Device.ThrowIfUnavailable();
        CommandEncoder = commandEncoder;
        Descriptor = descriptor;
        Label = descriptor.Label;
    }

    /// <summary>Gets the command encoder that owns this render pass.</summary>
    public GraphicsCommandEncoder CommandEncoder { get; }

    /// <summary>Gets the owning graphics device.</summary>
    public GraphicsDevice Device => CommandEncoder.Device;

    /// <summary>Gets the immutable render-pass descriptor.</summary>
    public GraphicsRenderPassDescriptor Descriptor { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }

    /// <summary>Gets whether the pass has ended.</summary>
    public bool IsEnded => ended;

    /// <summary>Selects the render pipeline used by subsequent draws.</summary>
    public void SetPipeline(GraphicsRenderPipeline value)
    {
        ThrowIfRecording();
        Device.ValidateOwner(value, nameof(value));
        GraphicsTextureFormat? colorFormat = Descriptor.ColorAttachment?.Texture.Descriptor.Format;
        if (value.Descriptor.ColorFormat != colorFormat)
        {
            throw new ArgumentException("The pipeline color format does not match the render attachment.", nameof(value));
        }
        GraphicsTextureFormat? depthFormat = Descriptor.DepthAttachment?.Texture.Descriptor.Format;
        if (value.Descriptor.DepthStencil?.Format != depthFormat)
        {
            throw new ArgumentException("The pipeline depth format does not match the render attachment.", nameof(value));
        }
        SetPipelineCore(value);
        pipeline = value;
    }

    /// <summary>Binds a validated byte range to one vertex-buffer slot.</summary>
    public void SetVertexBuffer(uint slot, GraphicsBuffer buffer, ulong offset = 0, ulong? size = null)
    {
        ThrowIfRecording();
        Device.ValidateOwner(buffer, nameof(buffer));
        if ((buffer.Usage & GraphicsBufferUsage.Vertex) == 0)
        {
            throw new ArgumentException("The buffer does not allow vertex use.", nameof(buffer));
        }
        ulong resolvedSize = ValidateBufferBinding(buffer, offset, size, 4);
        SetVertexBufferCore(slot, buffer, offset, resolvedSize);
        vertexBufferSlots.Add(slot);
    }

    /// <summary>Binds a validated byte range as the index buffer.</summary>
    public void SetIndexBuffer(
        GraphicsBuffer buffer,
        GraphicsIndexFormat format,
        ulong offset = 0,
        ulong? size = null)
    {
        ThrowIfRecording();
        Device.ValidateOwner(buffer, nameof(buffer));
        if ((buffer.Usage & GraphicsBufferUsage.Index) == 0)
        {
            throw new ArgumentException("The buffer does not allow index use.", nameof(buffer));
        }
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }
        ulong alignment = format == GraphicsIndexFormat.Uint16 ? 2UL : 4UL;
        ulong resolvedSize = ValidateBufferBinding(buffer, offset, size, alignment);
        SetIndexBufferCore(buffer, format, offset, resolvedSize);
        hasIndexBuffer = true;
    }

    /// <summary>Binds immutable shader resources to one WGSL <c>@group</c> slot.</summary>
    public void SetBindGroup(uint groupIndex, GraphicsBindGroup bindGroup)
    {
        ThrowIfRecording();
        Device.ValidateOwner(bindGroup, nameof(bindGroup));
        if (pipeline is not null)
        {
            IReadOnlyList<GraphicsBindGroupLayout>? expectedLayouts =
                pipeline.Descriptor.Layout?.Descriptor.BindGroupLayouts;
            if (expectedLayouts is null || groupIndex >= (uint)expectedLayouts.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(groupIndex),
                    "The selected pipeline does not declare this bind-group slot.");
            }
            if (!ReferenceEquals(bindGroup.Descriptor.Layout, expectedLayouts[checked((int)groupIndex)]))
            {
                throw new ArgumentException(
                    "The bind group is incompatible with the selected pipeline layout.",
                    nameof(bindGroup));
            }
        }
        SetBindGroupCore(groupIndex, bindGroup);
        bindGroups[groupIndex] = bindGroup;
    }

    /// <summary>Sets the floating-point viewport used by subsequent draw commands.</summary>
    /// <param name="x">The viewport's left edge in attachment pixels.</param>
    /// <param name="y">The viewport's top edge in attachment pixels.</param>
    /// <param name="width">The positive viewport width in attachment pixels.</param>
    /// <param name="height">The positive viewport height in attachment pixels.</param>
    /// <param name="minimumDepth">The minimum normalized depth value.</param>
    /// <param name="maximumDepth">The maximum normalized depth value.</param>
    public void SetViewport(
        float x,
        float y,
        float width,
        float height,
        float minimumDepth = 0f,
        float maximumDepth = 1f)
    {
        ThrowIfRecording();
        GraphicsExtent3D extent = GetAttachmentExtent();
        if (!float.IsFinite(x) || !float.IsFinite(y) ||
            !float.IsFinite(width) || !float.IsFinite(height) ||
            x < 0f || y < 0f || width <= 0f || height <= 0f ||
            x + width > extent.Width || y + height > extent.Height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The viewport must be finite, positive, and contained by the render attachment.");
        }
        if (!float.IsFinite(minimumDepth) || !float.IsFinite(maximumDepth) ||
            minimumDepth < 0f || maximumDepth > 1f || minimumDepth > maximumDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumDepth),
                "Viewport depth bounds must be finite, ordered, and between zero and one.");
        }

        SetViewportCore(x, y, width, height, minimumDepth, maximumDepth);
    }

    /// <summary>Sets the integer scissor rectangle used by subsequent draw commands.</summary>
    /// <param name="x">The scissor rectangle's left edge in attachment pixels.</param>
    /// <param name="y">The scissor rectangle's top edge in attachment pixels.</param>
    /// <param name="width">The non-zero scissor width.</param>
    /// <param name="height">The non-zero scissor height.</param>
    public void SetScissorRect(uint x, uint y, uint width, uint height)
    {
        ThrowIfRecording();
        GraphicsExtent3D extent = GetAttachmentExtent();
        if (width == 0 || height == 0 ||
            x > extent.Width || width > extent.Width - x ||
            y > extent.Height || height > extent.Height - y)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                "The scissor rectangle must be non-zero and contained by the render attachment.");
        }

        SetScissorRectCore(x, y, width, height);
    }

    /// <summary>Draws non-indexed vertices using the currently selected pipeline.</summary>
    public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
    {
        ThrowIfRecording();
        ArgumentOutOfRangeException.ThrowIfZero(vertexCount);
        ArgumentOutOfRangeException.ThrowIfZero(instanceCount);
        if (pipeline is null)
        {
            throw new InvalidOperationException("A render pipeline must be selected before drawing.");
        }
        ValidateRequiredBindings();
        ValidateRequiredVertexBuffers();
        DrawCore(vertexCount, instanceCount, firstVertex, firstInstance);
    }

    /// <summary>Draws indexed vertices using the selected pipeline and bound buffers.</summary>
    public void DrawIndexed(
        uint indexCount,
        uint instanceCount = 1,
        uint firstIndex = 0,
        int baseVertex = 0,
        uint firstInstance = 0)
    {
        ThrowIfRecording();
        ArgumentOutOfRangeException.ThrowIfZero(indexCount);
        ArgumentOutOfRangeException.ThrowIfZero(instanceCount);
        if (pipeline is null)
        {
            throw new InvalidOperationException("A render pipeline must be selected before drawing.");
        }
        if (!hasIndexBuffer)
        {
            throw new InvalidOperationException("An index buffer must be selected before indexed drawing.");
        }
        ValidateRequiredBindings();
        ValidateRequiredVertexBuffers();
        DrawIndexedCore(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    /// <summary>Ends this render pass so its parent command encoder can be finished.</summary>
    public void End()
    {
        ThrowIfRecording();
        ended = true;
        try
        {
            EndCore();
        }
        finally
        {
            CommandEncoder.NotifyRenderPassEnded(this);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            if (!ended)
            {
                End();
            }
        }
        finally
        {
            disposed = true;
            DisposeCore();
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>Selects the backend render pipeline after common validation.</summary>
    protected abstract void SetPipelineCore(GraphicsRenderPipeline value);

    /// <summary>Binds a backend vertex-buffer range after common validation.</summary>
    protected abstract void SetVertexBufferCore(uint slot, GraphicsBuffer buffer, ulong offset, ulong size);

    /// <summary>Binds a backend index-buffer range after common validation.</summary>
    protected abstract void SetIndexBufferCore(
        GraphicsBuffer buffer,
        GraphicsIndexFormat format,
        ulong offset,
        ulong size);

    /// <summary>Binds a backend bind group after common validation.</summary>
    protected abstract void SetBindGroupCore(uint groupIndex, GraphicsBindGroup bindGroup);

    /// <summary>Records a validated backend viewport change.</summary>
    protected abstract void SetViewportCore(
        float x,
        float y,
        float width,
        float height,
        float minimumDepth,
        float maximumDepth);

    /// <summary>Records a validated backend scissor-rectangle change.</summary>
    protected abstract void SetScissorRectCore(uint x, uint y, uint width, uint height);

    /// <summary>Records a backend draw after common validation.</summary>
    protected abstract void DrawCore(uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    /// <summary>Records a backend indexed draw after common validation.</summary>
    protected abstract void DrawIndexedCore(
        uint indexCount,
        uint instanceCount,
        uint firstIndex,
        int baseVertex,
        uint firstInstance);

    /// <summary>Ends the backend render pass.</summary>
    protected abstract void EndCore();

    private GraphicsExtent3D GetAttachmentExtent() =>
        (Descriptor.ColorAttachment?.Texture ?? Descriptor.DepthAttachment!.Value.Texture).Descriptor.Size;

    /// <summary>Releases backend pass state. Implementations must tolerate one call.</summary>
    protected abstract void DisposeCore();

    private void ThrowIfRecording()
    {
        Device.ThrowIfUnavailable();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (ended)
        {
            throw new InvalidOperationException("The render pass has already ended.");
        }
    }

    private static ulong ValidateBufferBinding(
        GraphicsBuffer buffer,
        ulong offset,
        ulong? size,
        ulong alignment)
    {
        if (offset > buffer.Size || offset % alignment != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "The binding offset is outside or misaligned.");
        }
        ulong resolvedSize = size ?? (buffer.Size - offset);
        if (resolvedSize == 0 || resolvedSize > buffer.Size - offset || resolvedSize % alignment != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "The binding size is outside or misaligned.");
        }
        return resolvedSize;
    }

    private void ValidateRequiredVertexBuffers()
    {
        for (uint slot = 0; slot < pipeline!.Descriptor.VertexBuffers.Count; slot++)
        {
            if (!vertexBufferSlots.Contains(slot))
            {
                throw new InvalidOperationException($"Vertex-buffer slot {slot} must be bound before drawing.");
            }
        }
    }

    private void ValidateRequiredBindings()
    {
        GraphicsPipelineLayout? layout = pipeline!.Descriptor.Layout;
        if (layout is null)
        {
            return;
        }
        for (uint groupIndex = 0; groupIndex < layout.Descriptor.BindGroupLayouts.Count; groupIndex++)
        {
            if (!bindGroups.TryGetValue(groupIndex, out GraphicsBindGroup? bindGroup))
            {
                throw new InvalidOperationException($"Bind-group slot {groupIndex} must be bound before drawing.");
            }
            if (!ReferenceEquals(
                bindGroup.Descriptor.Layout,
                layout.Descriptor.BindGroupLayouts[checked((int)groupIndex)]))
            {
                throw new InvalidOperationException($"Bind-group slot {groupIndex} uses an incompatible layout.");
            }
        }
    }
}

/// <summary>Represents a finished, single-submission command buffer.</summary>
public abstract class GraphicsCommandBuffer : GraphicsResource
{
    private bool submitted;

    /// <summary>Initializes a backend command buffer.</summary>
    protected GraphicsCommandBuffer(GraphicsDevice device, string? label)
        : base(device, label)
    {
    }

    /// <summary>Gets whether this command buffer has been submitted.</summary>
    public bool IsSubmitted => submitted;

    internal void MarkSubmitted()
    {
        ThrowIfDisposed();
        if (submitted)
        {
            throw new InvalidOperationException("A command buffer can only be submitted once.");
        }
        submitted = true;
    }
}

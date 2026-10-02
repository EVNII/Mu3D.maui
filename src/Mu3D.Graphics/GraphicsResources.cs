namespace Mu3D.Graphics;

/// <summary>Base class for a deterministically owned graphics resource.</summary>
public abstract class GraphicsResource : IDisposable
{
    private bool disposed;

    /// <summary>Initializes a resource owned by a device.</summary>
    protected GraphicsResource(GraphicsDevice device, string? label)
    {
        ArgumentNullException.ThrowIfNull(device);
        device.ThrowIfUnavailable();
        Device = device;
        Label = label;
    }

    /// <summary>Gets the device that owns this resource.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }

    /// <summary>Gets whether this resource has been disposed.</summary>
    public bool IsDisposed => disposed;

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the backend resource. Implementations must tolerate one call.</summary>
    protected abstract void DisposeCore();

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}

/// <summary>Represents an owned GPU buffer.</summary>
public abstract class GraphicsBuffer : GraphicsResource
{
    /// <summary>Initializes a backend buffer.</summary>
    protected GraphicsBuffer(GraphicsDevice device, GraphicsBufferDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        Size = descriptor.Size;
        Usage = descriptor.Usage;
    }

    /// <summary>Gets the allocation size in bytes.</summary>
    public ulong Size { get; }

    /// <summary>Gets the allowed buffer uses.</summary>
    public GraphicsBufferUsage Usage { get; }

    /// <summary>
    /// Asynchronously copies a mapped read range into caller-owned managed memory. The buffer must
    /// have been created with <see cref="GraphicsBufferUsage.MapRead"/>; the byte offset must be a
    /// multiple of eight and the byte count a multiple of four.
    /// </summary>
    /// <param name="offset">Byte offset of the mapped range.</param>
    /// <param name="byteCount">Positive number of bytes to copy.</param>
    /// <param name="cancellationToken">Cancels the managed wait without abandoning native cleanup.</param>
    /// <returns>A new byte array containing the requested range.</returns>
    public Task<byte[]> ReadAsync(
        ulong offset,
        int byteCount,
        CancellationToken cancellationToken = default)
    {
        Device.ThrowIfUnavailable();
        ThrowIfDisposed();
        if ((Usage & GraphicsBufferUsage.MapRead) == 0)
        {
            throw new InvalidOperationException("The buffer was not created for mapped reads.");
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteCount);
        if ((offset & 7) != 0 || (byteCount & 3) != 0)
        {
            throw new ArgumentException(
                "A mapped read offset must be a multiple of eight and its byte count a multiple of four.",
                nameof(offset));
        }
        if (offset > Size || (ulong)byteCount > Size - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(byteCount), "The mapped read exceeds the buffer.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return ReadAsyncCore(offset, byteCount, cancellationToken);
    }

    /// <summary>Reads one validated mapped range through the backend.</summary>
    protected virtual Task<byte[]> ReadAsyncCore(
        ulong offset,
        int byteCount,
        CancellationToken cancellationToken) =>
        Task.FromException<byte[]>(new NotSupportedException(
            "This graphics backend does not implement mapped buffer reads."));
}

/// <summary>Represents an owned two-dimensional GPU texture.</summary>
public abstract class GraphicsTexture : GraphicsResource
{
    /// <summary>Initializes a backend texture.</summary>
    protected GraphicsTexture(GraphicsDevice device, GraphicsTextureDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        Descriptor = descriptor;
    }

    /// <summary>Gets the immutable allocation descriptor.</summary>
    public GraphicsTextureDescriptor Descriptor { get; }
}

/// <summary>Represents an owned mip/layer/dimension view of a texture.</summary>
public abstract class GraphicsTextureView : GraphicsResource
{
    /// <summary>Initializes a backend texture view.</summary>
    protected GraphicsTextureView(GraphicsDevice device, GraphicsTextureViewDescriptor descriptor)
        : base(device, descriptor.Label) => Descriptor = descriptor;

    /// <summary>Gets the immutable view descriptor.</summary>
    public GraphicsTextureViewDescriptor Descriptor { get; }
}

/// <summary>Represents an immutable texture sampler.</summary>
public abstract class GraphicsSampler : GraphicsResource
{
    /// <summary>Initializes a backend sampler.</summary>
    protected GraphicsSampler(GraphicsDevice device, GraphicsSamplerDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        Descriptor = descriptor;
    }

    /// <summary>Gets the immutable sampler descriptor.</summary>
    public GraphicsSamplerDescriptor Descriptor { get; }
}

/// <summary>Represents an immutable bind-group schema.</summary>
public abstract class GraphicsBindGroupLayout : GraphicsResource
{
    /// <summary>Initializes a backend bind-group layout.</summary>
    protected GraphicsBindGroupLayout(GraphicsDevice device, GraphicsBindGroupLayoutDescriptor descriptor)
        : base(device, descriptor.Label) => Descriptor = descriptor;

    /// <summary>Gets the immutable layout descriptor.</summary>
    public GraphicsBindGroupLayoutDescriptor Descriptor { get; }
}

/// <summary>Represents the ordered bind-group schemas used by a pipeline.</summary>
public abstract class GraphicsPipelineLayout : GraphicsResource
{
    /// <summary>Initializes a backend pipeline layout.</summary>
    protected GraphicsPipelineLayout(GraphicsDevice device, GraphicsPipelineLayoutDescriptor descriptor)
        : base(device, descriptor.Label) => Descriptor = descriptor;

    /// <summary>Gets the immutable pipeline-layout descriptor.</summary>
    public GraphicsPipelineLayoutDescriptor Descriptor { get; }
}

/// <summary>Represents immutable resources bound against one bind-group layout.</summary>
public abstract class GraphicsBindGroup : GraphicsResource
{
    /// <summary>Initializes a backend bind group.</summary>
    protected GraphicsBindGroup(GraphicsDevice device, GraphicsBindGroupDescriptor descriptor)
        : base(device, descriptor.Label) => Descriptor = descriptor;

    /// <summary>Gets the immutable bind-group descriptor.</summary>
    public GraphicsBindGroupDescriptor Descriptor { get; }
}

/// <summary>Represents a compiled or backend-validated WGSL shader module.</summary>
public abstract class GraphicsShaderModule : GraphicsResource
{
    /// <summary>Initializes a backend shader module.</summary>
    protected GraphicsShaderModule(GraphicsDevice device, GraphicsShaderModuleDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        Descriptor = descriptor;
    }

    /// <summary>Gets the immutable shader-module descriptor.</summary>
    public GraphicsShaderModuleDescriptor Descriptor { get; }
}

/// <summary>Represents an immutable render pipeline.</summary>
public abstract class GraphicsRenderPipeline : GraphicsResource
{
    /// <summary>Initializes a backend render pipeline.</summary>
    protected GraphicsRenderPipeline(GraphicsDevice device, GraphicsRenderPipelineDescriptor descriptor)
        : base(device, descriptor.Label)
    {
        Descriptor = descriptor;
    }

    /// <summary>Gets the immutable render-pipeline descriptor.</summary>
    public GraphicsRenderPipelineDescriptor Descriptor { get; }
}

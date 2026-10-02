namespace Mu3D.Toolkit.Diagnostics;

/// <summary>Stores an immutable snapshot of optional known render-resource counts.</summary>
/// <remarks>
/// Null means unavailable, while zero means the host measured that no such resources exist. Byte
/// counts are estimates supplied by the host and do not imply backend-reported total GPU memory.
/// </remarks>
public readonly record struct RenderResourceCounts
{
    /// <summary>Initializes a validated resource-count snapshot.</summary>
    /// <param name="meshCount">Optional known mesh count.</param>
    /// <param name="vertexCount">Optional known vertex count.</param>
    /// <param name="materialCount">Optional known material count.</param>
    /// <param name="textureCount">Optional known texture count.</param>
    /// <param name="bufferCount">Optional known graphics-buffer count.</param>
    /// <param name="pipelineCount">Optional known render-pipeline count.</param>
    /// <param name="estimatedCpuBytes">Optional estimated CPU resource payload in bytes.</param>
    /// <param name="estimatedGpuBytes">Optional estimated GPU resource payload in bytes.</param>
    public RenderResourceCounts(
        long? meshCount = null,
        long? vertexCount = null,
        long? materialCount = null,
        long? textureCount = null,
        long? bufferCount = null,
        long? pipelineCount = null,
        long? estimatedCpuBytes = null,
        long? estimatedGpuBytes = null)
    {
        ThrowIfNegative(meshCount, nameof(meshCount));
        ThrowIfNegative(vertexCount, nameof(vertexCount));
        ThrowIfNegative(materialCount, nameof(materialCount));
        ThrowIfNegative(textureCount, nameof(textureCount));
        ThrowIfNegative(bufferCount, nameof(bufferCount));
        ThrowIfNegative(pipelineCount, nameof(pipelineCount));
        ThrowIfNegative(estimatedCpuBytes, nameof(estimatedCpuBytes));
        ThrowIfNegative(estimatedGpuBytes, nameof(estimatedGpuBytes));
        MeshCount = meshCount;
        VertexCount = vertexCount;
        MaterialCount = materialCount;
        TextureCount = textureCount;
        BufferCount = bufferCount;
        PipelineCount = pipelineCount;
        EstimatedCpuBytes = estimatedCpuBytes;
        EstimatedGpuBytes = estimatedGpuBytes;
    }

    /// <summary>Gets the optional known mesh count.</summary>
    public long? MeshCount { get; }

    /// <summary>Gets the optional known vertex count.</summary>
    public long? VertexCount { get; }

    /// <summary>Gets the optional known material count.</summary>
    public long? MaterialCount { get; }

    /// <summary>Gets the optional known texture count.</summary>
    public long? TextureCount { get; }

    /// <summary>Gets the optional known buffer count.</summary>
    public long? BufferCount { get; }

    /// <summary>Gets the optional known pipeline count.</summary>
    public long? PipelineCount { get; }

    /// <summary>Gets the optional estimated CPU resource payload in bytes.</summary>
    public long? EstimatedCpuBytes { get; }

    /// <summary>Gets the optional estimated GPU resource payload in bytes.</summary>
    public long? EstimatedGpuBytes { get; }

    private static void ThrowIfNegative(long? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The count must be non-negative.");
        }
    }
}

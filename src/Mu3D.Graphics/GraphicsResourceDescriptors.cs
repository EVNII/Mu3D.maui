namespace Mu3D.Graphics;

internal enum GraphicsTextureCompressionFamily
{
    None,
    Bc,
    Etc2,
    Astc,
}

/// <summary>Specifies allowed uses for a GPU buffer.</summary>
[Flags]
public enum GraphicsBufferUsage
{
    /// <summary>No use has been specified.</summary>
    None = 0,

    /// <summary>The buffer can be the source of a copy operation.</summary>
    CopySource = 1 << 0,

    /// <summary>The buffer can be the destination of a copy operation or queue write.</summary>
    CopyDestination = 1 << 1,

    /// <summary>The buffer contains vertex data.</summary>
    Vertex = 1 << 2,

    /// <summary>The buffer contains index data.</summary>
    Index = 1 << 3,

    /// <summary>The buffer contains uniform data.</summary>
    Uniform = 1 << 4,

    /// <summary>The buffer is accessible as shader storage.</summary>
    Storage = 1 << 5,

    /// <summary>The buffer contains indirect command arguments.</summary>
    Indirect = 1 << 6,

    /// <summary>The buffer can receive data for asynchronous CPU readback.</summary>
    MapRead = 1 << 7,

    /// <summary>The buffer can be mapped for CPU writes.</summary>
    MapWrite = 1 << 8,
}

/// <summary>Describes a GPU buffer allocation.</summary>
public readonly record struct GraphicsBufferDescriptor
{
    /// <summary>Initializes a buffer descriptor.</summary>
    /// <param name="size">Buffer size in bytes.</param>
    /// <param name="usage">Allowed buffer uses.</param>
    /// <param name="label">Optional diagnostic label.</param>
    public GraphicsBufferDescriptor(ulong size, GraphicsBufferUsage usage, string? label = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(size);
        if (usage == GraphicsBufferUsage.None)
        {
            throw new ArgumentOutOfRangeException(nameof(usage), "At least one buffer use is required.");
        }

        Size = size;
        Usage = usage;
        Label = label;
    }

    /// <summary>Gets the buffer size in bytes.</summary>
    public ulong Size { get; }

    /// <summary>Gets the allowed buffer uses.</summary>
    public GraphicsBufferUsage Usage { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

/// <summary>Identifies a backend-independent texture format.</summary>
public enum GraphicsTextureFormat
{
    /// <summary>Eight-bit normalized BGRA.</summary>
    Bgra8Unorm,

    /// <summary>Eight-bit normalized BGRA with hardware sRGB encoding.</summary>
    Bgra8UnormSrgb,

    /// <summary>Eight-bit normalized RGBA.</summary>
    Rgba8Unorm,

    /// <summary>Eight-bit normalized RGBA with hardware sRGB encoding.</summary>
    Rgba8UnormSrgb,

    /// <summary>Four IEEE 754 binary16 channels.</summary>
    Rgba16Float,

    /// <summary>Four IEEE 754 binary32 channels.</summary>
    Rgba32Float,

    /// <summary>Ten-bit normalized RGB with two-bit normalized alpha.</summary>
    Rgb10A2Unorm,

    /// <summary>BC1 RGB/RGBA in 4x4 blocks with linear normalized sampling.</summary>
    Bc1RgbaUnorm,

    /// <summary>BC1 RGB/RGBA in 4x4 blocks with hardware sRGB decoding.</summary>
    Bc1RgbaUnormSrgb,

    /// <summary>BC3 RGBA in 4x4 blocks with linear normalized sampling.</summary>
    Bc3RgbaUnorm,

    /// <summary>BC3 RGBA in 4x4 blocks with hardware sRGB decoding.</summary>
    Bc3RgbaUnormSrgb,

    /// <summary>BC7 RGBA in 4x4 blocks with linear normalized sampling.</summary>
    Bc7RgbaUnorm,

    /// <summary>BC7 RGBA in 4x4 blocks with hardware sRGB decoding.</summary>
    Bc7RgbaUnormSrgb,

    /// <summary>ETC2 RGB in 4x4 blocks with linear normalized sampling.</summary>
    Etc2Rgb8Unorm,

    /// <summary>ETC2 RGB in 4x4 blocks with hardware sRGB decoding.</summary>
    Etc2Rgb8UnormSrgb,

    /// <summary>ETC2 RGBA in 4x4 blocks with linear normalized sampling.</summary>
    Etc2Rgba8Unorm,

    /// <summary>ETC2 RGBA in 4x4 blocks with hardware sRGB decoding.</summary>
    Etc2Rgba8UnormSrgb,

    /// <summary>ASTC LDR RGBA in 4x4 blocks with linear normalized sampling.</summary>
    Astc4x4Unorm,

    /// <summary>ASTC LDR RGBA in 4x4 blocks with hardware sRGB decoding.</summary>
    Astc4x4UnormSrgb,

    /// <summary>ASTC LDR RGBA in 6x6 blocks with linear normalized sampling.</summary>
    Astc6x6Unorm,

    /// <summary>ASTC LDR RGBA in 6x6 blocks with hardware sRGB decoding.</summary>
    Astc6x6UnormSrgb,

    /// <summary>ASTC LDR RGBA in 8x8 blocks with linear normalized sampling.</summary>
    Astc8x8Unorm,

    /// <summary>ASTC LDR RGBA in 8x8 blocks with hardware sRGB decoding.</summary>
    Astc8x8UnormSrgb,

    /// <summary>32-bit floating-point depth.</summary>
    Depth32Float,

    /// <summary>32-bit floating-point depth with an 8-bit stencil component.</summary>
    Depth32FloatStencil8,
}

/// <summary>Describes the storage block used by one backend-independent texture format.</summary>
public readonly struct GraphicsTextureBlockLayout
{
    internal GraphicsTextureBlockLayout(
        uint blockWidth,
        uint blockHeight,
        uint bytesPerBlock,
        GraphicsTextureCompressionFamily compressionFamily = GraphicsTextureCompressionFamily.None)
    {
        BlockWidth = blockWidth;
        BlockHeight = blockHeight;
        BytesPerBlock = bytesPerBlock;
        CompressionFamily = compressionFamily;
    }

    /// <summary>Gets the number of horizontal texels represented by one storage block.</summary>
    public uint BlockWidth { get; }

    /// <summary>Gets the number of vertical texels represented by one storage block.</summary>
    public uint BlockHeight { get; }

    /// <summary>Gets the number of bytes occupied by one storage block.</summary>
    public uint BytesPerBlock { get; }

    /// <summary>Gets whether this layout represents a block-compressed texture format.</summary>
    public bool IsCompressed => CompressionFamily != GraphicsTextureCompressionFamily.None;

    internal GraphicsTextureCompressionFamily CompressionFamily { get; }
}

/// <summary>Provides backend-independent storage metadata for texture formats.</summary>
public static class GraphicsTextureFormatInfo
{
    /// <summary>
    /// Gets the texel-block layout for a color texture format. Depth/stencil formats do not support
    /// queue color writes and therefore throw.
    /// </summary>
    /// <param name="format">The texture format to inspect.</param>
    /// <returns>The format's immutable block dimensions and byte size.</returns>
    public static GraphicsTextureBlockLayout GetBlockLayout(GraphicsTextureFormat format) =>
        format switch
        {
            GraphicsTextureFormat.Bgra8Unorm or
            GraphicsTextureFormat.Bgra8UnormSrgb or
            GraphicsTextureFormat.Rgba8Unorm or
            GraphicsTextureFormat.Rgba8UnormSrgb or
            GraphicsTextureFormat.Rgb10A2Unorm => new(1, 1, 4),
            GraphicsTextureFormat.Rgba16Float => new(1, 1, 8),
            GraphicsTextureFormat.Rgba32Float => new(1, 1, 16),
            GraphicsTextureFormat.Bc1RgbaUnorm or
            GraphicsTextureFormat.Bc1RgbaUnormSrgb =>
                new(4, 4, 8, GraphicsTextureCompressionFamily.Bc),
            GraphicsTextureFormat.Bc3RgbaUnorm or
            GraphicsTextureFormat.Bc3RgbaUnormSrgb or
            GraphicsTextureFormat.Bc7RgbaUnorm or
            GraphicsTextureFormat.Bc7RgbaUnormSrgb =>
                new(4, 4, 16, GraphicsTextureCompressionFamily.Bc),
            GraphicsTextureFormat.Etc2Rgb8Unorm or
            GraphicsTextureFormat.Etc2Rgb8UnormSrgb =>
                new(4, 4, 8, GraphicsTextureCompressionFamily.Etc2),
            GraphicsTextureFormat.Etc2Rgba8Unorm or
            GraphicsTextureFormat.Etc2Rgba8UnormSrgb =>
                new(4, 4, 16, GraphicsTextureCompressionFamily.Etc2),
            GraphicsTextureFormat.Astc4x4Unorm or
            GraphicsTextureFormat.Astc4x4UnormSrgb =>
                new(4, 4, 16, GraphicsTextureCompressionFamily.Astc),
            GraphicsTextureFormat.Astc6x6Unorm or
            GraphicsTextureFormat.Astc6x6UnormSrgb =>
                new(6, 6, 16, GraphicsTextureCompressionFamily.Astc),
            GraphicsTextureFormat.Astc8x8Unorm or
            GraphicsTextureFormat.Astc8x8UnormSrgb =>
                new(8, 8, 16, GraphicsTextureCompressionFamily.Astc),
            _ => throw new ArgumentOutOfRangeException(
                nameof(format),
                format,
                "The texture format does not support color texture writes."),
        };
}

/// <summary>Specifies allowed uses for a GPU texture.</summary>
[Flags]
public enum GraphicsTextureUsage
{
    /// <summary>No use has been specified.</summary>
    None = 0,

    /// <summary>The texture can be the source of a copy operation.</summary>
    CopySource = 1 << 0,

    /// <summary>The texture can be the destination of a copy operation.</summary>
    CopyDestination = 1 << 1,

    /// <summary>Shaders can sample or load the texture.</summary>
    TextureBinding = 1 << 2,

    /// <summary>Shaders can access the texture as storage.</summary>
    StorageBinding = 1 << 3,

    /// <summary>The texture can be attached to a render pass.</summary>
    RenderAttachment = 1 << 4,
}

/// <summary>Describes the texel extent of a texture.</summary>
public readonly record struct GraphicsExtent3D
{
    /// <summary>Initializes a non-zero texture extent.</summary>
    public GraphicsExtent3D(uint width, uint height = 1, uint depthOrArrayLayers = 1)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentOutOfRangeException.ThrowIfZero(depthOrArrayLayers);
        Width = width;
        Height = height;
        DepthOrArrayLayers = depthOrArrayLayers;
    }

    /// <summary>Gets the width in texels.</summary>
    public uint Width { get; }

    /// <summary>Gets the height in texels.</summary>
    public uint Height { get; }

    /// <summary>Gets the depth or array-layer count.</summary>
    public uint DepthOrArrayLayers { get; }
}

/// <summary>Identifies a texel or array-layer origin inside a texture subresource.</summary>
public readonly record struct GraphicsOrigin3D
{
    /// <summary>Initializes a texture origin.</summary>
    public GraphicsOrigin3D(uint x = 0, uint y = 0, uint z = 0)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>Gets the horizontal texel coordinate.</summary>
    public uint X { get; }

    /// <summary>Gets the vertical texel coordinate.</summary>
    public uint Y { get; }

    /// <summary>Gets the depth coordinate or array-layer index.</summary>
    public uint Z { get; }
}

/// <summary>Describes a two-dimensional GPU texture allocation.</summary>
public readonly record struct GraphicsTextureDescriptor
{
    /// <summary>Initializes a texture descriptor.</summary>
    public GraphicsTextureDescriptor(
        GraphicsExtent3D size,
        GraphicsTextureFormat format,
        GraphicsTextureUsage usage,
        uint mipLevelCount = 1,
        uint sampleCount = 1,
        string? label = null)
    {
        if (size.Width == 0 || size.Height == 0 || size.DepthOrArrayLayers == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "Texture extent must be non-zero.");
        }
        if (usage == GraphicsTextureUsage.None)
        {
            throw new ArgumentOutOfRangeException(nameof(usage), "At least one texture use is required.");
        }
        ArgumentOutOfRangeException.ThrowIfZero(mipLevelCount);
        ArgumentOutOfRangeException.ThrowIfZero(sampleCount);

        Size = size;
        Format = format;
        Usage = usage;
        MipLevelCount = mipLevelCount;
        SampleCount = sampleCount;
        Label = label;
    }

    /// <summary>Gets the texture extent.</summary>
    public GraphicsExtent3D Size { get; }

    /// <summary>Gets the texel format.</summary>
    public GraphicsTextureFormat Format { get; }

    /// <summary>Gets the allowed texture uses.</summary>
    public GraphicsTextureUsage Usage { get; }

    /// <summary>Gets the mip-level count.</summary>
    public uint MipLevelCount { get; }

    /// <summary>Gets the sample count.</summary>
    public uint SampleCount { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

/// <summary>Identifies how a two-dimensional texture's array layers are exposed to shaders.</summary>
public enum GraphicsTextureViewDimension
{
    /// <summary>One two-dimensional layer.</summary>
    TwoD,

    /// <summary>One or more two-dimensional array layers.</summary>
    TwoDArray,

    /// <summary>Exactly six square layers interpreted as cube faces.</summary>
    Cube,

    /// <summary>A positive multiple of six square layers interpreted as cube faces.</summary>
    CubeArray,
}

/// <summary>Describes an explicitly selected mip/layer view of an existing texture.</summary>
public readonly record struct GraphicsTextureViewDescriptor
{
    /// <summary>Initializes a texture view descriptor.</summary>
    public GraphicsTextureViewDescriptor(
        GraphicsTexture texture,
        GraphicsTextureViewDimension dimension = GraphicsTextureViewDimension.TwoD,
        uint baseMipLevel = 0,
        uint mipLevelCount = 1,
        uint baseArrayLayer = 0,
        uint arrayLayerCount = 1,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentOutOfRangeException.ThrowIfZero(mipLevelCount);
        ArgumentOutOfRangeException.ThrowIfZero(arrayLayerCount);
        Texture = texture;
        Dimension = dimension;
        BaseMipLevel = baseMipLevel;
        MipLevelCount = mipLevelCount;
        BaseArrayLayer = baseArrayLayer;
        ArrayLayerCount = arrayLayerCount;
        Label = label;
    }

    /// <summary>Gets the viewed texture.</summary>
    public GraphicsTexture Texture { get; }

    /// <summary>Gets the shader-visible view dimension.</summary>
    public GraphicsTextureViewDimension Dimension { get; }

    /// <summary>Gets the first visible mip level.</summary>
    public uint BaseMipLevel { get; }

    /// <summary>Gets the number of visible mip levels.</summary>
    public uint MipLevelCount { get; }

    /// <summary>Gets the first visible array layer.</summary>
    public uint BaseArrayLayer { get; }

    /// <summary>Gets the number of visible array layers.</summary>
    public uint ArrayLayerCount { get; }

    /// <summary>Gets the optional diagnostic label.</summary>
    public string? Label { get; }
}

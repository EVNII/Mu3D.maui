using System.Collections.ObjectModel;
using Mu3D.Color;
using Mu3D.Graphics;

namespace Mu3D.SceneGraph;

/// <summary>Identifies whether compressed material texels represent color or numerical data.</summary>
public enum CompressedMaterialTextureContent
{
    /// <summary>RGB represents explicitly tagged color; alpha remains unpremultiplied.</summary>
    Color,

    /// <summary>Channels represent non-color data and are never color transformed.</summary>
    Data,
}

/// <summary>Stores one immutable, tightly packed mip level of a compressed material texture.</summary>
public sealed class CompressedTextureMipLevel
{
    /// <summary>Initializes a mip level and copies its complete block payload.</summary>
    /// <param name="width">The non-zero mip width in texels.</param>
    /// <param name="height">The non-zero mip height in texels.</param>
    /// <param name="data">Tightly packed block rows with no padding.</param>
    public CompressedTextureMipLevel(uint width, uint height, ReadOnlySpan<byte> data)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        Width = width;
        Height = height;
        Data = data.ToArray();
    }

    /// <summary>Gets the mip width in texels.</summary>
    public uint Width { get; }

    /// <summary>Gets the mip height in texels.</summary>
    public uint Height { get; }

    /// <summary>Gets the copied, tightly packed block payload.</summary>
    public ReadOnlyMemory<byte> Data { get; }
}

/// <summary>
/// Stores an immutable backend-independent compressed two-dimensional material texture and its
/// contiguous mip prefix. Container metadata and transcoder handles are deliberately excluded.
/// </summary>
public sealed class CompressedMaterialTexture
{
    /// <summary>Initializes and validates a compressed texture mip chain.</summary>
    /// <param name="format">A BC, ETC2 or ASTC texture format.</param>
    /// <param name="content">Whether the channels contain color or non-color data.</param>
    /// <param name="mipLevels">A non-empty, largest-to-smallest contiguous mip prefix.</param>
    /// <param name="colorSpace">
    /// The explicit linear-light RGB gamut for color content, or null for data content.
    /// Hardware-sRGB formats require Linear sRGB because their transfer decoding is fixed.
    /// </param>
    /// <param name="name">An optional diagnostic name.</param>
    public CompressedMaterialTexture(
        GraphicsTextureFormat format,
        CompressedMaterialTextureContent content,
        IEnumerable<CompressedTextureMipLevel> mipLevels,
        ColorSpaceReference? colorSpace = null,
        string? name = null)
    {
        if (!Enum.IsDefined(content))
        {
            throw new ArgumentOutOfRangeException(nameof(content));
        }
        GraphicsTextureBlockLayout block = GraphicsTextureFormatInfo.GetBlockLayout(format);
        if (!block.IsCompressed)
        {
            throw new ArgumentException("A compressed material texture requires a compressed format.", nameof(format));
        }
        ArgumentNullException.ThrowIfNull(mipLevels);
        CompressedTextureMipLevel[] copiedLevels = [.. mipLevels];
        if (copiedLevels.Length == 0 || copiedLevels.Any(static level => level is null))
        {
            throw new ArgumentException("A compressed texture requires non-null mip levels.", nameof(mipLevels));
        }
        bool srgbFormat = IsSrgbFormat(format);
        if (content == CompressedMaterialTextureContent.Color)
        {
            ArgumentNullException.ThrowIfNull(colorSpace);
            if (srgbFormat && colorSpace != StandardColorSpaces.LinearSrgb)
            {
                throw new ArgumentException(
                    "A hardware-sRGB compressed format can represent only Linear sRGB color.",
                    nameof(colorSpace));
            }
        }
        else if (colorSpace is not null || srgbFormat)
        {
            throw new ArgumentException(
                "Non-color data requires a linear compressed format and no color-space tag.",
                nameof(colorSpace));
        }

        uint baseWidth = copiedLevels[0].Width;
        uint baseHeight = copiedLevels[0].Height;
        uint maximumMipLevels = GetMaximumMipLevelCount(baseWidth, baseHeight);
        if ((uint)copiedLevels.Length > maximumMipLevels)
        {
            throw new ArgumentException("The compressed mip chain contains too many levels.", nameof(mipLevels));
        }
        for (int index = 0; index < copiedLevels.Length; index++)
        {
            CompressedTextureMipLevel level = copiedLevels[index];
            uint expectedWidth = GetMipDimension(baseWidth, checked((uint)index));
            uint expectedHeight = GetMipDimension(baseHeight, checked((uint)index));
            if (level.Width != expectedWidth || level.Height != expectedHeight)
            {
                throw new ArgumentException(
                    "Compressed mip levels must form a contiguous largest-to-smallest chain.",
                    nameof(mipLevels));
            }
            ulong blocksWide = ((ulong)level.Width + block.BlockWidth - 1) / block.BlockWidth;
            ulong blockRows = ((ulong)level.Height + block.BlockHeight - 1) / block.BlockHeight;
            ulong expectedBytes = checked(blocksWide * blockRows * block.BytesPerBlock);
            if ((ulong)level.Data.Length != expectedBytes)
            {
                throw new ArgumentException(
                    $"Compressed mip {index} requires exactly {expectedBytes} bytes.",
                    nameof(mipLevels));
            }
        }

        Format = format;
        Content = content;
        MipLevels = new ReadOnlyCollection<CompressedTextureMipLevel>(copiedLevels);
        ColorSpace = colorSpace;
        Name = name;
    }

    /// <summary>Gets the GPU-ready compressed texture format.</summary>
    public GraphicsTextureFormat Format { get; }

    /// <summary>Gets whether the channels contain color or non-color data.</summary>
    public CompressedMaterialTextureContent Content { get; }

    /// <summary>Gets the contiguous largest-to-smallest mip prefix.</summary>
    public IReadOnlyList<CompressedTextureMipLevel> MipLevels { get; }

    /// <summary>Gets the explicit linear-light color gamut, or null for non-color data.</summary>
    public ColorSpaceReference? ColorSpace { get; }

    /// <summary>Gets the optional diagnostic name.</summary>
    public string? Name { get; }

    /// <summary>Gets the base-level width in texels.</summary>
    public uint Width => MipLevels[0].Width;

    /// <summary>Gets the base-level height in texels.</summary>
    public uint Height => MipLevels[0].Height;

    private static bool IsSrgbFormat(GraphicsTextureFormat format) => format is
        GraphicsTextureFormat.Bc1RgbaUnormSrgb or
        GraphicsTextureFormat.Bc3RgbaUnormSrgb or
        GraphicsTextureFormat.Bc7RgbaUnormSrgb or
        GraphicsTextureFormat.Etc2Rgb8UnormSrgb or
        GraphicsTextureFormat.Etc2Rgba8UnormSrgb or
        GraphicsTextureFormat.Astc4x4UnormSrgb or
        GraphicsTextureFormat.Astc6x6UnormSrgb or
        GraphicsTextureFormat.Astc8x8UnormSrgb;

    private static uint GetMaximumMipLevelCount(uint width, uint height)
    {
        uint largest = Math.Max(width, height);
        uint count = 0;
        do
        {
            count++;
            largest >>= 1;
        }
        while (largest != 0);
        return count;
    }

    private static uint GetMipDimension(uint baseDimension, uint mipLevel) =>
        mipLevel >= 32 ? 1 : Math.Max(1u, baseDimension >> checked((int)mipLevel));
}

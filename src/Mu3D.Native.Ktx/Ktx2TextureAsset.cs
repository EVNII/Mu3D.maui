using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Native.Ktx;

/// <summary>Specifies how a standalone KTX2 load chooses its material-texture representation.</summary>
public enum Ktx2TextureLoadPreference
{
    /// <summary>Always decodes the base mip into an FP32 application image.</summary>
    DecodedRgba,

    /// <summary>Prefers a GPU-compressed mip chain and decodes when capabilities provide no target.</summary>
    PreferGpuCompressed,
}

/// <summary>Identifies the representation stored by a loaded standalone KTX2 texture.</summary>
public enum Ktx2TextureRepresentation
{
    /// <summary>The asset contains a decoded, explicitly tagged linear color image.</summary>
    DecodedColor,

    /// <summary>The asset contains a decoded normalized non-color data image.</summary>
    DecodedData,

    /// <summary>The asset contains a GPU-ready compressed mip chain.</summary>
    GpuCompressed,
}

/// <summary>Configures one bounded standalone KTX2 load.</summary>
public sealed class Ktx2TextureLoadOptions
{
    /// <summary>Gets the maximum encoded source size. The default is 256 MiB.</summary>
    public int MaximumSourceByteCount { get; init; } =
        Ktx2TextureLoader.DefaultMaximumSourceByteCount;

    /// <summary>
    /// Gets the maximum decoded FP32 image or compressed mip payload size. The default is 256 MiB.
    /// The loader validates dimensions before invoking libktx and verifies the final result.
    /// </summary>
    public long MaximumOutputByteCount { get; init; } =
        Ktx2TextureLoader.DefaultMaximumOutputByteCount;

    /// <summary>Gets whether the texture represents color or normalized numerical data.</summary>
    public CompressedMaterialTextureContent Content { get; init; } =
        CompressedMaterialTextureContent.Color;

    /// <summary>Gets the requested decoded-versus-compressed representation policy.</summary>
    public Ktx2TextureLoadPreference Preference { get; init; } =
        Ktx2TextureLoadPreference.DecodedRgba;

    /// <summary>
    /// Gets explicit destination capabilities used by <see cref="Ktx2TextureLoadPreference.PreferGpuCompressed"/>.
    /// They are not required for decoded loading.
    /// </summary>
    public Mu3D.Graphics.GraphicsCapabilities? GraphicsCapabilities { get; init; }

    /// <summary>Gets an optional diagnostic name attached to the decoded or transcoded texture.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets whether to tolerate BT.709 primaries on linear non-color legacy data. Strict glTF KTX2
    /// DFD validation remains the default.
    /// </summary>
    public bool AllowBt709PrimariesForLinearData { get; init; }

    /// <summary>
    /// Gets whether the returned asset owns an independent copy of the exact encoded KTX2 source.
    /// The default releases temporary encoded bytes after decoding or transcoding.
    /// </summary>
    public bool RetainEncodedSource { get; init; }
}

/// <summary>
/// Contains one decoded or GPU-compressed standalone KTX2 texture and optional exact source bytes.
/// </summary>
public sealed class Ktx2TextureAsset
{
    internal Ktx2TextureAsset(
        CompressedMaterialTextureContent content,
        LinearRgbaImage? decodedColorImage,
        NormalizedRgbaDataImage? decodedDataImage,
        CompressedMaterialTexture? compressedTexture,
        ReadOnlyMemory<byte> encodedSource,
        bool retainEncodedSource)
    {
        Content = content;
        DecodedColorImage = decodedColorImage;
        DecodedDataImage = decodedDataImage;
        CompressedTexture = compressedTexture;
        RetainedEncodedSource = retainEncodedSource ? encodedSource.ToArray() : default;
        if (compressedTexture is not null)
        {
            Representation = Ktx2TextureRepresentation.GpuCompressed;
            Width = compressedTexture.Width;
            Height = compressedTexture.Height;
            OutputByteCount = compressedTexture.MipLevels.Sum(
                static level => (long)level.Data.Length);
        }
        else if (decodedColorImage is not null)
        {
            Representation = Ktx2TextureRepresentation.DecodedColor;
            Width = decodedColorImage.Width;
            Height = decodedColorImage.Height;
            OutputByteCount = checked((long)decodedColorImage.Pixels.Count * 16);
        }
        else if (decodedDataImage is not null)
        {
            Representation = Ktx2TextureRepresentation.DecodedData;
            Width = decodedDataImage.Width;
            Height = decodedDataImage.Height;
            OutputByteCount = checked((long)decodedDataImage.Texels.Count * 16);
        }
        else
        {
            throw new ArgumentException("A KTX2 texture asset requires one decoded or compressed result.");
        }
    }

    /// <summary>Gets whether the texture represents color or normalized numerical data.</summary>
    public CompressedMaterialTextureContent Content { get; }

    /// <summary>Gets the representation selected for this load.</summary>
    public Ktx2TextureRepresentation Representation { get; }

    /// <summary>Gets the decoded linear color image when <see cref="Representation"/> is decoded color.</summary>
    public LinearRgbaImage? DecodedColorImage { get; }

    /// <summary>Gets the decoded non-color image when <see cref="Representation"/> is decoded data.</summary>
    public NormalizedRgbaDataImage? DecodedDataImage { get; }

    /// <summary>Gets the compressed mip chain when <see cref="Representation"/> is GPU compressed.</summary>
    public CompressedMaterialTexture? CompressedTexture { get; }

    /// <summary>Gets the base width in texels.</summary>
    public uint Width { get; }

    /// <summary>Gets the base height in texels.</summary>
    public uint Height { get; }

    /// <summary>Gets the retained decoded FP32 or compressed mip payload size.</summary>
    public long OutputByteCount { get; }

    /// <summary>Gets whether this asset owns the exact encoded KTX2 source.</summary>
    public bool HasRetainedEncodedSource => !RetainedEncodedSource.IsEmpty;

    /// <summary>
    /// Gets an owned exact encoded source copy, or empty memory when source retention was disabled.
    /// </summary>
    public ReadOnlyMemory<byte> RetainedEncodedSource { get; }
}

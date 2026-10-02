using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Native.Ktx.Interop;
using Mu3D.SceneGraph;

namespace Mu3D.Native.Ktx;

/// <summary>
/// Decodes glTF-compatible KTX2 Basis Universal images to RGBA8 when no acceptable high-quality
/// GPU-compressed target exists. Non-KTX MIME types may be delegated to another application-owned
/// image decoder.
/// </summary>
public sealed unsafe class Ktx2ImageDecoder : IEncodedImageDecoder
{
    private const uint LoadImageData = 1;
    private readonly IEncodedImageDecoder? fallback;
    private readonly bool allowBt709PrimariesForLinearData;

    static Ktx2ImageDecoder() => KtxNativeLibraryResolver.Register();

    /// <summary>Initializes a KTX2 decoder with an optional decoder for all other MIME types.</summary>
    /// <param name="fallback">An optional JPEG, PNG or application-specific decoder.</param>
    public Ktx2ImageDecoder(IEncodedImageDecoder? fallback = null)
        : this(fallback, false)
    {
    }

    /// <summary>Initializes a KTX2 decoder with an explicit data-DFD compatibility policy.</summary>
    /// <param name="fallback">An optional JPEG, PNG or application-specific decoder.</param>
    /// <param name="allowBt709PrimariesForLinearData">
    /// True to tolerate BT.709 primaries on linear non-color data. The strict glTF requirement is
    /// unspecified primaries, so applications should enable this only for identified legacy assets.
    /// </param>
    public Ktx2ImageDecoder(
        IEncodedImageDecoder? fallback,
        bool allowBt709PrimariesForLinearData)
    {
        this.fallback = fallback;
        this.allowBt709PrimariesForLinearData = allowBt709PrimariesForLinearData;
    }

    /// <inheritdoc />
    public LinearRgbaImage DecodeColor(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        if (!IsKtx2(mimeType))
        {
            return RequireFallback(mimeType).DecodeColor(source, mimeType, name);
        }
        DecodedRgba8 decoded = DecodeRgba8(
            source,
            CompressedMaterialTextureContent.Color,
            allowBt709PrimariesForLinearData);
        Vector4[] texels = new Vector4[decoded.Pixels.Length / 4];
        for (int index = 0, component = 0; index < texels.Length; index++, component += 4)
        {
            texels[index] = new Vector4(
                DecodeSrgb(decoded.Pixels[component] / 255f),
                DecodeSrgb(decoded.Pixels[component + 1] / 255f),
                DecodeSrgb(decoded.Pixels[component + 2] / 255f),
                decoded.Pixels[component + 3] / 255f);
        }
        return LinearRgbaImage.FromOwnedPixels(
            decoded.Width,
            decoded.Height,
            texels,
            StandardColorSpaces.LinearSrgb,
            name);
    }

    /// <inheritdoc />
    public NormalizedRgbaDataImage DecodeData(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        if (!IsKtx2(mimeType))
        {
            return RequireFallback(mimeType).DecodeData(source, mimeType, name);
        }
        DecodedRgba8 decoded = DecodeRgba8(
            source,
            CompressedMaterialTextureContent.Data,
            allowBt709PrimariesForLinearData);
        Vector4[] texels = new Vector4[decoded.Pixels.Length / 4];
        for (int index = 0, component = 0; index < texels.Length; index++, component += 4)
        {
            texels[index] = new Vector4(
                decoded.Pixels[component] / 255f,
                decoded.Pixels[component + 1] / 255f,
                decoded.Pixels[component + 2] / 255f,
                decoded.Pixels[component + 3] / 255f);
        }
        return NormalizedRgbaDataImage.FromOwnedTexels(
            decoded.Width,
            decoded.Height,
            texels,
            name);
    }

    private static DecodedRgba8 DecodeRgba8(
        ReadOnlyMemory<byte> source,
        CompressedMaterialTextureContent content,
        bool allowBt709PrimariesForLinearData)
    {
        if (source.IsEmpty)
        {
            throw new ArgumentException("KTX2 source cannot be empty.", nameof(source));
        }
        nint texture = 0;
        try
        {
            fixed (byte* bytes = source.Span)
            {
                Ktx2TextureTranscoder.ThrowIfError(
                    KtxNative.ktxTexture2_CreateFromMemory(
                        bytes,
                        checked((nuint)source.Length),
                        LoadImageData,
                        &texture),
                    "parse KTX2");
            }
            KtxTexture2 header = Marshal.PtrToStructure<KtxTexture2>(texture);
            Ktx2TextureTranscoder.ValidateTexture(
                texture,
                header,
                content,
                allowBt709PrimariesForLinearData);
            if (KtxNative.ktxTexture2_NeedsTranscoding(texture) == 0)
            {
                throw new InvalidDataException(
                    "KHR_texture_basisu requires ETC1S/BasisLZ or UASTC content that needs transcoding.");
            }
            Ktx2TextureTranscoder.ThrowIfError(
                KtxNative.ktxTexture2_TranscodeBasis(
                    texture,
                    KtxTranscodeFormat.Rgba32,
                    0),
                "decode Basis Universal texture to RGBA8");
            nuint offset = 0;
            Ktx2TextureTranscoder.ThrowIfError(
                KtxNative.ktxTexture2_GetImageOffset(texture, 0, 0, 0, &offset),
                "locate decoded KTX2 base mip");
            nint data = KtxNative.ktxTexture_GetData(texture);
            nuint dataSize = KtxNative.ktxTexture_GetDataSize(texture);
            ulong byteCount = checked((ulong)header.BaseWidth * header.BaseHeight * 4);
            if (data == 0 || byteCount > int.MaxValue || (ulong)offset > (ulong)dataSize ||
                byteCount > (ulong)dataSize - (ulong)offset)
            {
                throw new InvalidDataException("Decoded KTX2 RGBA8 data exceeds its native buffer.");
            }
            byte[] pixels = new byte[checked((int)byteCount)];
            new ReadOnlySpan<byte>(
                (byte*)data + checked((nint)offset),
                pixels.Length).CopyTo(pixels);
            return new DecodedRgba8(header.BaseWidth, header.BaseHeight, pixels);
        }
        catch (DllNotFoundException exception)
        {
            throw new InvalidOperationException(
                "The pinned libktx native runtime is not available for this target.",
                exception);
        }
        finally
        {
            if (texture != 0)
            {
                KtxNative.ktxTexture2_Destroy(texture);
            }
        }
    }

    private static bool IsKtx2(string mimeType) =>
        string.Equals(mimeType, "image/ktx2", StringComparison.OrdinalIgnoreCase);

    private IEncodedImageDecoder RequireFallback(string mimeType) => fallback ??
        throw new NotSupportedException(
            $"MIME type '{mimeType}' requires a fallback IEncodedImageDecoder.");

    private static float DecodeSrgb(float encoded) => encoded <= 0.04045f
        ? encoded / 12.92f
        : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);

    private readonly record struct DecodedRgba8(uint Width, uint Height, byte[] Pixels);
}

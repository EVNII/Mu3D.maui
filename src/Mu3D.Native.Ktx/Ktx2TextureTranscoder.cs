using System.Runtime.InteropServices;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Ktx.Interop;
using Mu3D.SceneGraph;

namespace Mu3D.Native.Ktx;

/// <summary>
/// Transcodes glTF-compatible KTX2 Basis Universal textures through pinned Khronos libktx.
/// The adapter owns no GPU resource and copies the selected compressed mip chain into Mu3D's
/// backend-independent immutable material representation.
/// </summary>
public sealed unsafe class Ktx2TextureTranscoder : IEncodedTextureTranscoder
{
    private const uint LoadImageData = 1;
    private const int KtxTexture2Class = 2;
    private const int OrientationRight = 'r';
    private const int OrientationDown = 'd';
    private const int PrimariesUnspecified = 0;
    private const int PrimariesBt709 = 1;
    private const int TransferLinear = 1;
    private const int TransferSrgb = 2;
    private static readonly int HashListOffset = checked((int)Marshal.OffsetOf<KtxTexture2>(
        nameof(KtxTexture2.KvDataHead)));
    private readonly bool allowBt709PrimariesForLinearData;

    static Ktx2TextureTranscoder() => KtxNativeLibraryResolver.Register();

    /// <summary>Initializes a strict glTF KTX2 transcoder.</summary>
    public Ktx2TextureTranscoder()
    {
    }

    /// <summary>Initializes a glTF KTX2 transcoder with an explicit data-DFD compatibility policy.</summary>
    /// <param name="allowBt709PrimariesForLinearData">
    /// True to tolerate BT.709 primaries on linear non-color data. Khronos
    /// <c>KHR_texture_basisu</c> requires unspecified primaries, so this is intended only for known
    /// legacy assets; the transfer function must still be linear.
    /// </param>
    public Ktx2TextureTranscoder(bool allowBt709PrimariesForLinearData)
    {
        this.allowBt709PrimariesForLinearData = allowBt709PrimariesForLinearData;
    }

    /// <inheritdoc />
    public CompressedMaterialTexture? TryTranscode(
        ReadOnlyMemory<byte> source,
        string mimeType,
        CompressedMaterialTextureContent content,
        GraphicsCapabilities capabilities,
        string? name = null)
    {
        if (source.IsEmpty)
        {
            throw new ArgumentException("KTX2 source cannot be empty.", nameof(source));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!string.Equals(mimeType, "image/ktx2", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"KTX adapter cannot transcode MIME type '{mimeType}'.");
        }
        if (!Enum.IsDefined(content))
        {
            throw new ArgumentOutOfRangeException(nameof(content));
        }
        KtxTranscodeTarget? target = SelectTarget(content, capabilities);
        if (target is null)
        {
            return null;
        }

        nint texture = 0;
        try
        {
            fixed (byte* bytes = source.Span)
            {
                ThrowIfError(
                    KtxNative.ktxTexture2_CreateFromMemory(
                        bytes,
                        checked((nuint)source.Length),
                        LoadImageData,
                        &texture),
                    "parse KTX2");
            }
            KtxTexture2 header = Marshal.PtrToStructure<KtxTexture2>(texture);
            ValidateTexture(
                texture,
                header,
                content,
                allowBt709PrimariesForLinearData);
            if (KtxNative.ktxTexture2_NeedsTranscoding(texture) == 0)
            {
                throw new InvalidDataException(
                    "KHR_texture_basisu requires ETC1S/BasisLZ or UASTC content that needs transcoding.");
            }
            ThrowIfError(
                KtxNative.ktxTexture2_TranscodeBasis(texture, target.Value.NativeFormat, 0),
                "transcode Basis Universal texture");
            header = Marshal.PtrToStructure<KtxTexture2>(texture);
            if (header.IsCompressed == 0)
            {
                throw new InvalidDataException("libktx returned an uncompressed target unexpectedly.");
            }
            return CopyMipChain(texture, header, target.Value.GraphicsFormat, content, name);
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

    internal static KtxTranscodeTarget? SelectTarget(
        CompressedMaterialTextureContent content,
        GraphicsCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        bool color = content == CompressedMaterialTextureContent.Color;
        if (capabilities.SupportsAstcTextureCompression)
        {
            return new(
                KtxTranscodeFormat.Astc4x4Rgba,
                color
                    ? GraphicsTextureFormat.Astc4x4UnormSrgb
                    : GraphicsTextureFormat.Astc4x4Unorm);
        }
        if (capabilities.SupportsBcTextureCompression)
        {
            return new(
                KtxTranscodeFormat.Bc7Rgba,
                color
                    ? GraphicsTextureFormat.Bc7RgbaUnormSrgb
                    : GraphicsTextureFormat.Bc7RgbaUnorm);
        }
        if (color && capabilities.SupportsEtc2TextureCompression)
        {
            return new(KtxTranscodeFormat.Etc2Rgba, GraphicsTextureFormat.Etc2Rgba8UnormSrgb);
        }
        return null;
    }

    internal static void ValidateTexture(
        nint texture,
        KtxTexture2 header,
        CompressedMaterialTextureContent content,
        bool allowBt709PrimariesForLinearData = false)
    {
        if (header.ClassId != KtxTexture2Class || header.NumDimensions != 2 ||
            header.BaseDepth != 1 || header.NumLayers != 1 || header.NumFaces != 1 ||
            header.IsArray != 0 || header.IsCubemap != 0 || header.IsVideo != 0)
        {
            throw new InvalidDataException(
                "KHR_texture_basisu material images must be non-array, non-cubemap, non-video 2D textures.");
        }
        if (header.BaseWidth == 0 || header.BaseHeight == 0 || header.NumLevels == 0 ||
            header.BaseWidth % 4 != 0 || header.BaseHeight % 4 != 0)
        {
            throw new InvalidDataException(
                "KHR_texture_basisu dimensions must be non-zero multiples of four with at least one mip.");
        }
        if (header.Orientation.X != OrientationRight || header.Orientation.Y != OrientationDown)
        {
            throw new InvalidDataException("KHR_texture_basisu orientation must be 'rd' or omitted.");
        }
        if (KtxNative.ktxTexture2_GetPremultipliedAlpha(texture) != 0)
        {
            throw new InvalidDataException(
                "Premultiplied KTX2 alpha is incompatible with glTF material texture semantics.");
        }
        int transfer = KtxNative.ktxTexture2_GetTransferFunction_e(texture);
        int primaries = KtxNative.ktxTexture2_GetPrimaries_e(texture);
        if (content == CompressedMaterialTextureContent.Color)
        {
            if (transfer != TransferSrgb || primaries != PrimariesBt709)
            {
                throw new InvalidDataException(
                    "glTF KTX2 color textures require BT.709 primaries and the sRGB transfer function.");
            }
        }
        else if (transfer != TransferLinear ||
            (primaries != PrimariesUnspecified &&
                !(allowBt709PrimariesForLinearData && primaries == PrimariesBt709)))
        {
            throw new InvalidDataException(
                "glTF KTX2 data textures require unspecified primaries and a linear transfer " +
                $"function; the DFD reported primaries={primaries}, transfer={transfer}.");
        }
        ValidateSwizzle(texture);
    }

    private static void ValidateSwizzle(nint texture)
    {
        byte* key = stackalloc byte[]
        {
            (byte)'K', (byte)'T', (byte)'X', (byte)'s', (byte)'w', (byte)'i', (byte)'z',
            (byte)'z', (byte)'l', (byte)'e', 0,
        };
        uint length = 0;
        nint value = 0;
        KtxError error = KtxNative.ktxHashList_FindValue(
            nint.Add(texture, HashListOffset),
            key,
            &length,
            &value);
        if (error == KtxError.NotFound)
        {
            return;
        }
        ThrowIfError(error, "read KTXswizzle metadata");
        if (length is not (4 or 5) || value == 0)
        {
            throw new InvalidDataException("KHR_texture_basisu KTXswizzle must be 'rgba' or omitted.");
        }
        ReadOnlySpan<byte> bytes = new((void*)value, checked((int)length));
        if (!bytes[..4].SequenceEqual("rgba"u8) || (length == 5 && bytes[4] != 0))
        {
            throw new InvalidDataException("KHR_texture_basisu KTXswizzle must be 'rgba' or omitted.");
        }
    }

    private static CompressedMaterialTexture CopyMipChain(
        nint texture,
        KtxTexture2 header,
        GraphicsTextureFormat format,
        CompressedMaterialTextureContent content,
        string? name)
    {
        nint data = KtxNative.ktxTexture_GetData(texture);
        nuint dataSize = KtxNative.ktxTexture_GetDataSize(texture);
        if (data == 0 || dataSize == 0 || header.NumLevels > int.MaxValue)
        {
            throw new InvalidDataException("Transcoded KTX2 contains no addressable image data.");
        }
        GraphicsTextureBlockLayout block = GraphicsTextureFormatInfo.GetBlockLayout(format);
        CompressedTextureMipLevel[] levels = new CompressedTextureMipLevel[header.NumLevels];
        for (uint level = 0; level < header.NumLevels; level++)
        {
            uint width = Math.Max(1u, header.BaseWidth >> checked((int)Math.Min(level, 31)));
            uint height = Math.Max(1u, header.BaseHeight >> checked((int)Math.Min(level, 31)));
            ulong blocksWide = ((ulong)width + block.BlockWidth - 1) / block.BlockWidth;
            ulong blockRows = ((ulong)height + block.BlockHeight - 1) / block.BlockHeight;
            ulong byteCount = checked(blocksWide * blockRows * block.BytesPerBlock);
            if (byteCount > int.MaxValue)
            {
                throw new InvalidDataException("A transcoded KTX2 mip is too large for managed memory.");
            }
            nuint offset = 0;
            ThrowIfError(
                KtxNative.ktxTexture2_GetImageOffset(texture, level, 0, 0, &offset),
                $"locate KTX2 mip {level}");
            if ((ulong)offset > (ulong)dataSize || byteCount > (ulong)dataSize - (ulong)offset)
            {
                throw new InvalidDataException("A transcoded KTX2 mip exceeds its native data buffer.");
            }
            ReadOnlySpan<byte> payload = new(
                (byte*)data + checked((nint)offset),
                checked((int)byteCount));
            levels[level] = new CompressedTextureMipLevel(width, height, payload);
        }
        return new CompressedMaterialTexture(
            format,
            content,
            levels,
            content == CompressedMaterialTextureContent.Color
                ? StandardColorSpaces.LinearSrgb
                : null,
            name);
    }

    internal static void ThrowIfError(KtxError error, string operation)
    {
        if (error != KtxError.Success)
        {
            throw new InvalidDataException($"libktx failed to {operation}: {error} ({(int)error}).");
        }
    }
}

internal readonly record struct KtxTranscodeTarget(
    KtxTranscodeFormat NativeFormat,
    GraphicsTextureFormat GraphicsFormat);

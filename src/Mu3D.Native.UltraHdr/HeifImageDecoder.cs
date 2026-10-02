using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Native.UltraHdr.Interop;
using Mu3D.SceneGraph;

namespace Mu3D.Native.UltraHdr;

/// <summary>
/// Decodes HEIF, HEIC and AVIF color containers through the optional HEIF-enabled libultrahdr
/// feature set. A caller-owned decoder may handle every other MIME type.
/// </summary>
/// <remarks>
/// The normal package is JPEG-only. This decoder accepts its HEIF-family MIME types only when
/// <see cref="UltraHdrBackendInfo.IsHeifEnabled"/> is true. Normalized non-color HEIF data decoding
/// is not defined by the current asset contract.
/// </remarks>
public sealed class HeifImageDecoder : IEncodedImageDecoder
{
    private readonly IEncodedImageDecoder? fallback;

    static HeifImageDecoder() => UltraHdrNativeLibraryResolver.Register();

    /// <summary>Initializes a HEIF decoder with an optional decoder for other MIME types.</summary>
    /// <param name="fallback">
    /// An optional application-owned decoder, such as <see cref="JpegImageDecoder"/>.
    /// </param>
    public HeifImageDecoder(IEncodedImageDecoder? fallback = null) => this.fallback = fallback;

    /// <inheritdoc />
    public LinearRgbaImage DecodeColor(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (!IsHeifFamily(mimeType))
        {
            return RequireFallback(mimeType).DecodeColor(source, mimeType, name);
        }
        ValidateHandledInput(source, mimeType);
        return UltraHdrGainMapCodec.DecodeColor(source, name);
    }

    /// <inheritdoc />
    public NormalizedRgbaDataImage DecodeData(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (!IsHeifFamily(mimeType))
        {
            return RequireFallback(mimeType).DecodeData(source, mimeType, name);
        }
        ValidateHandledInput(source, mimeType);
        throw new NotSupportedException(
            "HEIF/HEIC/AVIF data-texture decoding is not defined by the current normalized-channel contract.");
    }

    private static void ValidateHandledInput(ReadOnlyMemory<byte> source, string mimeType)
    {
        if (source.IsEmpty)
        {
            throw new ArgumentException("Encoded HEIF input cannot be empty.", nameof(source));
        }
        if (!UltraHdrBackendInfo.IsHeifEnabled)
        {
            throw new NotSupportedException(
                $"The default Mu3D.Native.UltraHdr native feature set does not decode '{mimeType}'. " +
                "Use a package built with Mu3DUltraHdrEnableHeif=true.");
        }
    }

    private static bool IsHeifFamily(string mimeType) =>
        mimeType.Equals("image/heif", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("image/heic", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("image/avif", StringComparison.OrdinalIgnoreCase);

    private IEncodedImageDecoder RequireFallback(string mimeType) => fallback ??
        throw new NotSupportedException(
            $"HeifImageDecoder accepts HEIF/HEIC/AVIF; MIME type '{mimeType}' requires a fallback " +
            "IEncodedImageDecoder.");
}

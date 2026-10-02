using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Native.UltraHdr.Interop;
using Mu3D.SceneGraph;

namespace Mu3D.Native.UltraHdr;

/// <summary>
/// Decodes ordinary JPEG plus Ultra HDR and ISO 21496-1 gain-map JPEG through the repository-pinned
/// libjpeg-turbo and Google libultrahdr codecs. Color decoding returns the complete linear HDR
/// intent as FP32 CPU pixels; data decoding preserves normalized encoded RGB channels.
/// </summary>
/// <remarks>
/// Ultra HDR is treated as a JPEG capability rather than a container-independent decoder identity.
/// HEIF, HEIC and AVIF are handled separately by <see cref="HeifImageDecoder"/>. Applications can
/// replace this adapter through <see cref="IEncodedImageDecoder"/>.
/// </remarks>
public sealed unsafe class JpegImageDecoder : IEncodedImageDecoder
{
    private readonly IccRgbToLinearTransformCache iccTransformCache = new(capacity: 64);

    static JpegImageDecoder() => UltraHdrNativeLibraryResolver.Register();

    /// <summary>
    /// Reads and reassembles an embedded JPEG ICC APP2 profile without decoding pixels or applying
    /// the profile transform.
    /// </summary>
    /// <param name="source">The complete JPEG stream.</param>
    /// <param name="mimeType">The source MIME type, which must be <c>image/jpeg</c>.</param>
    /// <returns>The validated immutable ICC v2/v4 profile, or null when none is embedded.</returns>
    public IccProfile? ReadEmbeddedIccProfile(
        ReadOnlyMemory<byte> source,
        string mimeType)
    {
        ValidateJpegInput(source, mimeType);
        return JpegIccProfileCodec.Extract(source.Span);
    }

    /// <summary>
    /// Decodes an ordinary JPEG as document color into an explicit standard linear working space.
    /// Embedded RGB ICC v2/v4 profiles are transformed through the selected AToB rendering intent
    /// or matrix/TRC colorimetric path. An untagged JPEG uses the conventional encoded-sRGB
    /// assumption and reports that assumption explicitly.
    /// </summary>
    /// <remarks>
    /// Matrix/TRC, legacy lut8Type/lut16Type, ICC v4 lutAToBType and supported DToB MPE profiles
    /// are accepted, including the explicit ICC-absolute path. Black-point compensation is a
    /// two-profile link policy and is not silently applied during this single-profile decode.
    /// Ultra HDR gain-map JPEGs remain on <see cref="DecodeColor"/> because their HDR intent and
    /// container gamut metadata require the gain-map decode path.
    /// </remarks>
    /// <param name="source">The complete ordinary JPEG stream.</param>
    /// <param name="destination">The requested standard linear working space.</param>
    /// <param name="name">An optional diagnostic image name.</param>
    /// <param name="renderingIntent">The application-selected ICC rendering objective.</param>
    /// <returns>The transformed image plus its source-profile decision.</returns>
    public JpegDocumentDecodeResult DecodeDocumentColor(
        ReadOnlyMemory<byte> source,
        StandardRgbColorSpaceReference destination,
        string? name = null,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric)
    {
        ValidateJpegInput(source, "image/jpeg");
        ArgumentNullException.ThrowIfNull(destination);
        if (IsUltraHdr(source))
        {
            throw new NotSupportedException(
                "DecodeDocumentColor accepts ordinary JPEG; use DecodeColor for Ultra HDR gain-map JPEG.");
        }

        IccProfile? profile = JpegIccProfileCodec.Extract(source.Span);
        IccRgbToLinearTransform? transform = profile is null
            ? null
            : iccTransformCache.GetOrCreate(profile, renderingIntent);
        (uint width, uint height, byte[] rgba) = DecodeJpegRgba8(source);
        Vector4[] pixels = new Vector4[checked((int)(width * height))];
        const float scale = 1f / byte.MaxValue;
        for (int index = 0; index < pixels.Length; index++)
        {
            int component = index * 4;
            float red = rgba[component] * scale;
            float green = rgba[component + 1] * scale;
            float blue = rgba[component + 2] * scale;
            float alpha = rgba[component + 3] * scale;
            LinearRgba converted = transform is null
                ? StandardLinearRgbConverter.Convert(
                    new LinearRgba(
                        DecodeSrgb(red),
                        DecodeSrgb(green),
                        DecodeSrgb(blue),
                        alpha,
                        StandardColorSpaces.LinearSrgb),
                    destination)
                : transform.TransformEncodedRgb(red, green, blue, alpha, destination);
            pixels[index] = new Vector4(
                converted.Red,
                converted.Green,
                converted.Blue,
                converted.Alpha);
        }

        return new JpegDocumentDecodeResult(
            LinearRgbaImage.FromOwnedPixels(width, height, pixels, destination, name),
            profile,
            assumedSrgb: profile is null,
            appliedRenderingIntent: profile is null ? null : renderingIntent);
    }

    /// <inheritdoc />
    public LinearRgbaImage DecodeColor(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        ValidateJpegInput(source, mimeType);
        if (!IsUltraHdr(source))
        {
            (uint width, uint height, byte[] rgba) = DecodeJpegRgba8(source);
            Vector4[] pixels = new Vector4[checked((int)(width * height))];
            const float scale = 1f / byte.MaxValue;
            for (int index = 0; index < pixels.Length; index++)
            {
                int component = index * 4;
                pixels[index] = new Vector4(
                    DecodeSrgb(rgba[component] * scale),
                    DecodeSrgb(rgba[component + 1] * scale),
                    DecodeSrgb(rgba[component + 2] * scale),
                    rgba[component + 3] * scale);
            }
            return LinearRgbaImage.FromOwnedPixels(
                width,
                height,
                pixels,
                StandardColorSpaces.LinearSrgb,
                name);
        }
        return UltraHdrGainMapCodec.DecodeColor(source, name);
    }

    /// <inheritdoc />
    public NormalizedRgbaDataImage DecodeData(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null)
    {
        ValidateJpegInput(source, mimeType);
        (uint width, uint height, byte[] rgba) = DecodeJpegRgba8(source);
        Vector4[] texels = new Vector4[checked((int)(width * height))];
        const float scale = 1f / byte.MaxValue;
        for (int index = 0; index < texels.Length; index++)
        {
            int component = index * 4;
            texels[index] = new Vector4(
                rgba[component] * scale,
                rgba[component + 1] * scale,
                rgba[component + 2] * scale,
                rgba[component + 3] * scale);
        }
        return NormalizedRgbaDataImage.FromOwnedTexels(width, height, texels, name);
    }

    private static void ValidateJpegInput(ReadOnlyMemory<byte> source, string mimeType)
    {
        if (source.IsEmpty)
        {
            throw new ArgumentException("Encoded image input cannot be empty.", nameof(source));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(mimeType);
        if (!IsJpegMimeType(mimeType))
        {
            throw new NotSupportedException(
                $"JpegImageDecoder accepts image/jpeg, not '{mimeType}'.");
        }
    }

    private static bool IsJpegMimeType(string mimeType) =>
        mimeType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        mimeType.Equals("image/jpg", StringComparison.OrdinalIgnoreCase);

    private static bool IsUltraHdr(ReadOnlyMemory<byte> source)
    {
        using MemoryHandle handle = source.Pin();
        return Interop.UltraHdrNative.is_uhdr_image(handle.Pointer, source.Length) != 0;
    }

    private static (uint Width, uint Height, byte[] Rgba) DecodeJpegRgba8(
        ReadOnlyMemory<byte> source)
    {
        nint decoder = TurboJpegNative.tj3Init(TurboJpegInitialization.Decompress);
        if (decoder == 0)
        {
            throw new InvalidOperationException("libjpeg-turbo could not allocate a decoder.");
        }
        try
        {
            using MemoryHandle sourceHandle = source.Pin();
            byte* sourceBytes = (byte*)sourceHandle.Pointer;
            ThrowIfTurboJpegError(
                decoder,
                TurboJpegNative.tj3DecompressHeader(decoder, sourceBytes, (nuint)source.Length),
                "read JPEG header");
            int width = TurboJpegNative.tj3Get(decoder, TurboJpegParameter.JpegWidth);
            int height = TurboJpegNative.tj3Get(decoder, TurboJpegParameter.JpegHeight);
            if (width <= 0 || height <= 0)
            {
                throw new InvalidDataException("libjpeg-turbo returned invalid JPEG dimensions.");
            }
            byte[] rgba = new byte[checked(width * height * 4)];
            fixed (byte* destination = rgba)
            {
                ThrowIfTurboJpegError(
                    decoder,
                    TurboJpegNative.tj3Decompress8(
                        decoder,
                        sourceBytes,
                        (nuint)source.Length,
                        destination,
                        0,
                        TurboJpegPixelFormat.Rgba),
                    "decode JPEG pixels");
            }
            return ((uint)width, (uint)height, rgba);
        }
        finally
        {
            TurboJpegNative.tj3Destroy(decoder);
        }
    }

    private static void ThrowIfTurboJpegError(nint decoder, int result, string operation)
    {
        if (result == 0)
        {
            return;
        }
        string detail = Marshal.PtrToStringUTF8(TurboJpegNative.tj3GetErrorStr(decoder)) ??
            "no native detail";
        throw new InvalidDataException($"libjpeg-turbo failed to {operation}: {detail}");
    }

    private static float DecodeSrgb(float encoded) => encoded <= 0.04045f
        ? encoded / 12.92f
        : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);
}

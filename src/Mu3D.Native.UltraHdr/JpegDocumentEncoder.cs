using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Native.UltraHdr.Interop;

namespace Mu3D.Native.UltraHdr;

/// <summary>Specifies the chroma sampling used by an ordinary lossy JPEG document.</summary>
public enum JpegChromaSubsampling
{
    /// <summary>Preserves one chroma sample per pixel.</summary>
    S444,

    /// <summary>Shares chroma between each horizontal pair of pixels.</summary>
    S422,

    /// <summary>Shares chroma within each two-by-two pixel block.</summary>
    S420,
}

/// <summary>Controls how target-profile RGB outside the baseline JPEG domain is handled.</summary>
public enum JpegEncodingRangePolicy
{
    /// <summary>Rejects any target encoded RGB component outside zero through one.</summary>
    Reject,

    /// <summary>Explicitly clamps target encoded RGB components to zero through one.</summary>
    Clip,
}

/// <summary>Configures professional ordinary JPEG document export.</summary>
public sealed record JpegDocumentEncodeOptions
{
    /// <summary>Gets the lossy JPEG quality from 1 through 100.</summary>
    public int Quality { get; init; } = 95;

    /// <summary>Gets the chroma sampling policy. The professional default is 4:4:4.</summary>
    public JpegChromaSubsampling ChromaSubsampling { get; init; } = JpegChromaSubsampling.S444;

    /// <summary>Gets the ICC PCS-to-device rendering intent.</summary>
    public IccRenderingIntent RenderingIntent { get; init; } =
        IccRenderingIntent.MediaRelativeColorimetric;

    /// <summary>
    /// Gets the explicit policy for values that the selected target transform places outside the
    /// baseline eight-bit JPEG domain.
    /// </summary>
    public JpegEncodingRangePolicy RangePolicy { get; init; } = JpegEncodingRangePolicy.Reject;
}

/// <summary>
/// Encodes opaque linear-light document pixels into an ordinary eight-bit JPEG with an explicit
/// target ICC profile through the repository-pinned libjpeg-turbo backend.
/// </summary>
public sealed unsafe class JpegDocumentEncoder
{
    private readonly IccLinearToRgbTransformCache transformCache = new(capacity: 64);

    static JpegDocumentEncoder() => UltraHdrNativeLibraryResolver.Register();

    /// <summary>Exports to a standard RGB encoding and embeds its matching ICC v4 profile.</summary>
    /// <remarks>Uses the existing explicit range/alpha policy. Standard JPEG is eight-bit SDR;
    /// choosing Rec.2020 or ProPhoto does not enable HDR or implicit tone mapping.</remarks>
    public byte[] Encode(LinearRgbaImage source, StandardRgbEncoding destination,
        JpegDocumentEncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new JpegDocumentEncodeOptions();
        ValidateOptions(options);
        if (options.RenderingIntent != IccRenderingIntent.MediaRelativeColorimetric)
            throw new NotSupportedException("Standard RGB export uses colorimetric conversion; select an explicit ICC LUT profile for other intents.");
        IccProfile profile = StandardRgbProfiles.Get(destination);
        byte[] rgb = new byte[checked(source.Pixels.Count * 3)];
        for (int index = 0; index < source.Pixels.Count; index++)
        {
            var p = source.Pixels[index];
            if (p.W != 1f) throw new InvalidOperationException("Ordinary JPEG export requires explicitly composited opaque pixels.");
            var c = StandardRgbEncodingConverter.Encode(new(p.X, p.Y, p.Z, p.W, source.ColorSpace), destination);
            rgb[index * 3] = Quantize(c.Red, index, "red", options.RangePolicy);
            rgb[index * 3 + 1] = Quantize(c.Green, index, "green", options.RangePolicy);
            rgb[index * 3 + 2] = Quantize(c.Blue, index, "blue", options.RangePolicy);
        }
        return JpegIccProfileCodec.Insert(Compress(rgb, checked((int)source.Width), checked((int)source.Height), options), profile);
    }

    /// <summary>
    /// Converts every pixel through the selected target-profile transform, applies the explicit
    /// range policy, compresses the encoded RGB bytes, and embeds the complete target profile.
    /// </summary>
    /// <remarks>
    /// Ordinary JPEG has no alpha or HDR gain map. This method rejects transparency and defaults to
    /// rejecting negative or above-one target RGB; it never silently tone maps, gamut maps, clips,
    /// composites, or substitutes sRGB. Use <see cref="UltraHdrJpegEncoder"/> when HDR headroom must
    /// remain represented by a gain map.
    /// </remarks>
    /// <param name="source">The immutable explicitly tagged linear-light source image.</param>
    /// <param name="targetProfile">The RGB ICC profile to transform into and embed.</param>
    /// <param name="options">The optional encoding policy.</param>
    /// <returns>A complete ordinary JPEG stream containing the target ICC profile.</returns>
    public byte[] Encode(
        LinearRgbaImage source,
        IccProfile targetProfile,
        JpegDocumentEncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(targetProfile);
        options ??= new JpegDocumentEncodeOptions();
        ValidateOptions(options);
        IccLinearToRgbTransform transform = transformCache.GetOrCreate(
            targetProfile,
            options.RenderingIntent);
        int width = checked((int)source.Width);
        int height = checked((int)source.Height);
        byte[] rgb = new byte[checked(width * height * 3)];
        for (int index = 0; index < source.Pixels.Count; index++)
        {
            System.Numerics.Vector4 pixel = source.Pixels[index];
            if (pixel.W != 1f)
            {
                throw new InvalidOperationException(
                    "Ordinary JPEG export requires opaque pixels; composite explicitly before encoding.");
            }
            IccEncodedRgba encoded = transform.Transform(
                new LinearRgba(
                    pixel.X,
                    pixel.Y,
                    pixel.Z,
                    pixel.W,
                    source.ColorSpace));
            int component = index * 3;
            rgb[component] = Quantize(encoded.Red, index, "red", options.RangePolicy);
            rgb[component + 1] = Quantize(encoded.Green, index, "green", options.RangePolicy);
            rgb[component + 2] = Quantize(encoded.Blue, index, "blue", options.RangePolicy);
        }

        byte[] jpeg = Compress(rgb, width, height, options);
        return JpegIccProfileCodec.Insert(jpeg, targetProfile);
    }

    private static byte[] Compress(
        byte[] rgb,
        int width,
        int height,
        JpegDocumentEncodeOptions options)
    {
        nint compressor = TurboJpegNative.tj3Init(TurboJpegInitialization.Compress);
        if (compressor == 0)
        {
            throw new InvalidOperationException("libjpeg-turbo could not allocate a compressor.");
        }
        byte* jpegBuffer = null;
        try
        {
            ThrowIfTurboJpegError(
                compressor,
                TurboJpegNative.tj3Set(
                    compressor,
                    TurboJpegParameter.Quality,
                    options.Quality),
                "set JPEG quality");
            ThrowIfTurboJpegError(
                compressor,
                TurboJpegNative.tj3Set(
                    compressor,
                    TurboJpegParameter.Subsampling,
                    (int)ToNative(options.ChromaSubsampling)),
                "set JPEG chroma subsampling");
            nuint jpegSize = 0;
            fixed (byte* source = rgb)
            {
                ThrowIfTurboJpegError(
                    compressor,
                    TurboJpegNative.tj3Compress8(
                        compressor,
                        source,
                        width,
                        0,
                        height,
                        TurboJpegPixelFormat.Rgb,
                        &jpegBuffer,
                        &jpegSize),
                    "compress JPEG pixels");
            }
            if (jpegBuffer is null || jpegSize is 0 or > int.MaxValue)
            {
                throw new InvalidDataException("libjpeg-turbo returned an invalid JPEG buffer.");
            }
            return new ReadOnlySpan<byte>(jpegBuffer, checked((int)jpegSize)).ToArray();
        }
        finally
        {
            TurboJpegNative.tj3Free(jpegBuffer);
            TurboJpegNative.tj3Destroy(compressor);
        }
    }

    private static byte Quantize(
        float value,
        int pixelIndex,
        string channel,
        JpegEncodingRangePolicy policy)
    {
        if (value is < 0f or > 1f)
        {
            if (policy == JpegEncodingRangePolicy.Reject)
            {
                throw new InvalidOperationException(
                    $"Target-profile {channel} at pixel {pixelIndex} is {value:R}, outside [0, 1]. " +
                    "Select Clip explicitly, choose another output transform, or use HDR export.");
            }
            value = Math.Clamp(value, 0f, 1f);
        }
        return checked((byte)MathF.Round(value * byte.MaxValue));
    }

    private static TurboJpegSubsampling ToNative(JpegChromaSubsampling value) => value switch
    {
        JpegChromaSubsampling.S444 => TurboJpegSubsampling.S444,
        JpegChromaSubsampling.S422 => TurboJpegSubsampling.S422,
        JpegChromaSubsampling.S420 => TurboJpegSubsampling.S420,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown chroma sampling."),
    };

    private static void ValidateOptions(JpegDocumentEncodeOptions options)
    {
        if (options.Quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "JPEG quality must be in [1, 100].");
        }
        if (!Enum.IsDefined(options.ChromaSubsampling))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown chroma sampling.");
        }
        if (!Enum.IsDefined(options.RenderingIntent))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown ICC rendering intent.");
        }
        if (!Enum.IsDefined(options.RangePolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown JPEG range policy.");
        }
    }

    private static void ThrowIfTurboJpegError(nint compressor, int result, string operation)
    {
        if (result == 0)
        {
            return;
        }
        string detail = Marshal.PtrToStringUTF8(TurboJpegNative.tj3GetErrorStr(compressor)) ??
            "no native detail";
        throw new InvalidDataException($"libjpeg-turbo failed to {operation}: {detail}");
    }
}

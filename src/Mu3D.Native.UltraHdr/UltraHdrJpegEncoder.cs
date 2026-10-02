using System.Numerics;
using Mu3D.Color;
using Mu3D.Native.UltraHdr.Interop;

namespace Mu3D.Native.UltraHdr;

/// <summary>Controls ISO 21496-1/Ultra HDR gain-map JPEG encoding.</summary>
public sealed record UltraHdrJpegEncodeOptions
{
    /// <summary>Gets the JPEG quality for the SDR base image, from zero through 100.</summary>
    public int BaseQuality { get; init; } = 95;

    /// <summary>Gets the JPEG quality for the gain-map image, from zero through 100.</summary>
    public int GainMapQuality { get; init; } = 95;

    /// <summary>
    /// Gets the positive gain-map downscale factor. One retains full gain-map resolution; values
    /// through 128 trade spatial HDR precision for file size.
    /// </summary>
    public int GainMapScaleFactor { get; init; } = 1;

    /// <summary>Gets whether independent RGB gain-map channels are encoded.</summary>
    public bool UseMultiChannelGainMap { get; init; } = true;

    /// <summary>
    /// Gets an optional target display peak in nits, from 203 through 10,000. When null, Mu3D
    /// derives the value from the maximum encoded component using 203 nits as linear value 1.0.
    /// </summary>
    public float? TargetDisplayPeakBrightnessNits { get; init; }

    /// <summary>Gets whether libultrahdr uses its best-quality preset instead of realtime mode.</summary>
    public bool UseBestQualityPreset { get; init; } = true;

    /// <summary>
    /// Gets an optional ICC v2/v4 profile to preserve in JPEG APP2 chunks. The caller must ensure
    /// that it describes the encoder-selected output RGB space; Mu3D does not silently reinterpret
    /// or apply this profile.
    /// </summary>
    public IccProfile? EmbeddedIccProfile { get; init; }
}

/// <summary>
/// Encodes opaque, unpremultiplied linear-light images as JPEG with an SDR base rendition and an
/// Ultra HDR/ISO 21496-1 gain map through the repository-pinned libultrahdr codec.
/// </summary>
/// <remarks>
/// Linear value 1.0 maps to the gain-map reference white of 203 nits. The JPEG container cannot
/// preserve alpha, negative RGB components or colors outside its selected output gamut, so this
/// encoder fails closed instead of silently compositing or clipping them.
/// </remarks>
public sealed unsafe class UltraHdrJpegEncoder
{
    private const float ReferenceWhiteNits = 203f;
    private const float MaximumLinearComponent = 10000f / ReferenceWhiteNits;

    static UltraHdrJpegEncoder() => UltraHdrNativeLibraryResolver.Register();

    /// <summary>Encodes a complete linear HDR image as a gain-map JPEG.</summary>
    /// <param name="image">The immutable, explicitly tagged source image.</param>
    /// <param name="options">Optional gain-map and JPEG quality settings.</param>
    /// <returns>The complete encoded JPEG byte stream.</returns>
    /// <exception cref="NotSupportedException">
    /// The source color space cannot be converted through Mu3D's standard RGB converter.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The source contains transparency, negative/out-of-range converted RGB values, or the
    /// requested target peak is lower than the image content peak.
    /// </exception>
    public byte[] Encode(
        LinearRgbaImage image,
        UltraHdrJpegEncodeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        options ??= new UltraHdrJpegEncodeOptions();
        ValidateOptions(options);

        (StandardRgbColorSpaceReference outputSpace, UltraHdrColorGamut gamut) =
            ResolveOutputColorSpace(image.ColorSpace);
        Half[] encodedPixels = new Half[checked(image.Pixels.Count * 4)];
        float maximumComponent = 0f;
        for (int index = 0; index < image.Pixels.Count; index++)
        {
            Vector4 source = image.Pixels[index];
            if (source.W != 1f)
            {
                throw new InvalidOperationException(
                    "Gain-map JPEG export requires fully opaque pixels because JPEG has no alpha channel.");
            }
            LinearRgba converted = StandardLinearRgbConverter.Convert(
                new LinearRgba(source.X, source.Y, source.Z, source.W, image.ColorSpace),
                outputSpace);
            ValidateEncodedComponent(converted.Red, index, "red");
            ValidateEncodedComponent(converted.Green, index, "green");
            ValidateEncodedComponent(converted.Blue, index, "blue");
            int component = index * 4;
            encodedPixels[component] = (Half)converted.Red;
            encodedPixels[component + 1] = (Half)converted.Green;
            encodedPixels[component + 2] = (Half)converted.Blue;
            encodedPixels[component + 3] = (Half)1f;
            maximumComponent = MathF.Max(
                maximumComponent,
                MathF.Max(converted.Red, MathF.Max(converted.Green, converted.Blue)));
        }

        float contentPeakNits = Math.Clamp(
            maximumComponent * ReferenceWhiteNits,
            ReferenceWhiteNits,
            10000f);
        float targetPeakNits = options.TargetDisplayPeakBrightnessNits ?? contentPeakNits;
        if (targetPeakNits + 0.01f < contentPeakNits)
        {
            throw new InvalidOperationException(
                $"The target display peak {targetPeakNits} nits is below the encoded content peak " +
                $"{contentPeakNits} nits.");
        }

        nint encoder = UltraHdrNative.uhdr_create_encoder();
        if (encoder == 0)
        {
            throw new InvalidOperationException("libultrahdr could not allocate an encoder.");
        }
        try
        {
            fixed (Half* pixels = encodedPixels)
            {
                UltraHdrRawImage input = new()
                {
                    Format = UltraHdrImageFormat.RgbaHalfFloat,
                    ColorGamut = gamut,
                    ColorTransfer = UltraHdrColorTransfer.Linear,
                    ColorRange = (int)UltraHdrColorRange.Full,
                    Width = image.Width,
                    Height = image.Height,
                    Plane0 = pixels,
                    Stride0 = image.Width,
                };
                ThrowIfError(
                    UltraHdrNative.uhdr_enc_set_raw_image(
                        encoder,
                        &input,
                        UltraHdrImageLabel.Hdr),
                    "register the linear HDR intent");
                ConfigureEncoder(encoder, options, targetPeakNits);
                ThrowIfError(UltraHdrNative.uhdr_encode(encoder), "encode gain-map JPEG");
            }

            UltraHdrCompressedImage* output = UltraHdrNative.uhdr_get_encoded_stream(encoder);
            if (output is null || output->Data is null || output->DataSize == 0 ||
                output->DataSize > output->Capacity || output->DataSize > int.MaxValue)
            {
                throw new InvalidDataException("libultrahdr returned an invalid encoded stream.");
            }
            byte[] encoded = new ReadOnlySpan<byte>(
                output->Data,
                checked((int)output->DataSize)).ToArray();
            return options.EmbeddedIccProfile is null
                ? encoded
                : JpegIccProfileCodec.Insert(encoded, options.EmbeddedIccProfile);
        }
        finally
        {
            UltraHdrNative.uhdr_release_encoder(encoder);
        }
    }

    private static void ConfigureEncoder(
        nint encoder,
        UltraHdrJpegEncodeOptions options,
        float targetPeakNits)
    {
        ThrowIfError(
            UltraHdrNative.uhdr_enc_set_output_format(encoder, UltraHdrCodec.Jpeg),
            "select JPEG output");
        ThrowIfError(
            UltraHdrNative.uhdr_enc_set_quality(
                encoder,
                options.BaseQuality,
                UltraHdrImageLabel.Base),
            "set base-image quality");
        ThrowIfError(
            UltraHdrNative.uhdr_enc_set_quality(
                encoder,
                options.GainMapQuality,
                UltraHdrImageLabel.GainMap),
            "set gain-map quality");
        ThrowIfError(
            UltraHdrNative.uhdr_enc_set_gainmap_scale_factor(
                encoder,
                options.GainMapScaleFactor),
            "set gain-map scale factor");
        ThrowIfError(
            UltraHdrNative.uhdr_enc_set_using_multi_channel_gainmap(
                encoder,
                options.UseMultiChannelGainMap ? 1 : 0),
            "select gain-map channel count");
        ThrowIfError(
            UltraHdrNative.uhdr_enc_set_target_display_peak_brightness(
                encoder,
                targetPeakNits),
            "set target display peak");
        ThrowIfError(
            UltraHdrNative.uhdr_enc_set_preset(
                encoder,
                options.UseBestQualityPreset
                    ? UltraHdrEncoderPreset.BestQuality
                    : UltraHdrEncoderPreset.Realtime),
            "select encoder preset");
    }

    private static void ValidateOptions(UltraHdrJpegEncodeOptions options)
    {
        if (options.BaseQuality is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Base quality must be in [0, 100].");
        }
        if (options.GainMapQuality is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Gain-map quality must be in [0, 100].");
        }
        if (options.GainMapScaleFactor is < 1 or > 128)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Gain-map scale factor must be in [1, 128].");
        }
        if (options.TargetDisplayPeakBrightnessNits is float peak &&
            (!float.IsFinite(peak) || peak is < ReferenceWhiteNits or > 10000f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Target display peak must be in [203, 10000] nits.");
        }
    }

    private static void ValidateEncodedComponent(float value, int pixelIndex, string channel)
    {
        if (value < 0f || value > MaximumLinearComponent)
        {
            throw new InvalidOperationException(
                $"Pixel {pixelIndex} {channel} component {value} is outside the gain-map JPEG " +
                $"linear range [0, {MaximumLinearComponent}]. Apply an explicit gamut/tone mapping first.");
        }
    }

    private static (
        StandardRgbColorSpaceReference ColorSpace,
        UltraHdrColorGamut Gamut) ResolveOutputColorSpace(ColorSpaceReference source) => source switch
    {
        StandardRgbColorSpaceReference { Space: StandardRgbColorSpace.LinearSrgb } =>
            (StandardColorSpaces.LinearSrgb, UltraHdrColorGamut.Bt709),
        StandardRgbColorSpaceReference { Space: StandardRgbColorSpace.LinearDisplayP3 } =>
            (StandardColorSpaces.LinearDisplayP3, UltraHdrColorGamut.DisplayP3),
        StandardRgbColorSpaceReference { Space: StandardRgbColorSpace.LinearRec2020 } =>
            (StandardColorSpaces.LinearRec2020, UltraHdrColorGamut.Bt2100),
        StandardRgbColorSpaceReference =>
            (StandardColorSpaces.LinearRec2020, UltraHdrColorGamut.Bt2100),
        _ => throw new NotSupportedException(
            $"Gain-map JPEG export does not yet transform color space '{source.Name}'."),
    };

    private static void ThrowIfError(UltraHdrErrorInfo error, string operation) =>
        UltraHdrGainMapCodec.ThrowIfCodecError(error, operation);
}

using System.Numerics;

namespace Mu3D.Color;

/// <summary>Identifies a standard RGB encoding, including primaries, white point and transfer function.</summary>
/// <remarks>Rec.2020 here uses the continuous BT.2020 OETF, not PQ, HLG or a display EOTF.
/// ProPhoto uses the standard linear toe and 1.8 exponent. Extended values use sign reflection.</remarks>
public enum StandardRgbEncoding
{
    /// <summary>sRGB primaries, D65 and the sRGB transfer function.</summary>
    Srgb,
    /// <summary>Display P3 primaries, D65 and the sRGB transfer function; not cinema DCI-P3.</summary>
    DisplayP3,
    /// <summary>Adobe RGB (1998), D65 and exponent 563/256.</summary>
    AdobeRgb,
    /// <summary>ProPhoto RGB, D50 and the toe-plus-power transfer function.</summary>
    ProPhotoRgb,
    /// <summary>Rec.2020, D65 and the continuous BT.2020 transfer function.</summary>
    Rec2020,
}

/// <summary>Explicit handling of encoded output components outside [0,1]. Alpha is never transformed.</summary>
public enum RgbEncodingRangePolicy
{
    /// <summary>Retains extended negative and above-one components in floating-point output.</summary>
    Preserve,
    /// <summary>Rejects output outside [0,1] instead of silently changing its appearance.</summary>
    Reject,
    /// <summary>Clips components to [0,1]. This is not perceptual gamut mapping or tone mapping.</summary>
    Clip,
}

/// <summary>A finite, unpremultiplied RGB value with an explicit standard nonlinear encoding.</summary>
public readonly record struct StandardEncodedRgba
{
    /// <summary>Constructs tagged components, preserving extended RGB and requiring alpha in [0,1].</summary>
    public StandardEncodedRgba(float red, float green, float blue, float alpha, StandardRgbEncoding encoding)
    {
        _ = StandardRgbEncodingConverter.GetLinearSpace(encoding);
        if (!float.IsFinite(red) || !float.IsFinite(green) || !float.IsFinite(blue) ||
            !float.IsFinite(alpha) || alpha is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(red), "RGB must be finite and alpha must be in [0,1].");
        Red = red; Green = green; Blue = blue; Alpha = alpha; Encoding = encoding;
    }
    /// <summary>Gets encoded red.</summary>
    public float Red { get; }
    /// <summary>Gets encoded green.</summary>
    public float Green { get; }
    /// <summary>Gets encoded blue.</summary>
    public float Blue { get; }
    /// <summary>Gets unpremultiplied alpha.</summary>
    public float Alpha { get; }
    /// <summary>Gets the complete RGB encoding identity.</summary>
    public StandardRgbEncoding Encoding { get; }
}

/// <summary>Decodes standard RGB before rendering and encodes explicitly selected RGB outputs.</summary>
/// <remarks>CPU calculations are FP32. Values are never implicitly gamut mapped or tone mapped.
/// Encoded pixels must be straight alpha; unpremultiply before calling these methods.</remarks>
public static class StandardRgbEncodingConverter
{
    private const float RecAlpha = 1.09929682680944f;
    private const float RecBeta = 0.018053968510807f;

    /// <summary>Gets the linear-light space corresponding to an encoded RGB identity.</summary>
    public static StandardRgbColorSpaceReference GetLinearSpace(StandardRgbEncoding encoding) => encoding switch
    {
        StandardRgbEncoding.Srgb => StandardColorSpaces.LinearSrgb,
        StandardRgbEncoding.DisplayP3 => StandardColorSpaces.LinearDisplayP3,
        StandardRgbEncoding.AdobeRgb => StandardColorSpaces.LinearAdobeRgb,
        StandardRgbEncoding.ProPhotoRgb => StandardColorSpaces.LinearProPhotoRgb,
        StandardRgbEncoding.Rec2020 => StandardColorSpaces.LinearRec2020,
        _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
    };

    /// <summary>Decodes encoded components to their own linear space without gamut conversion.</summary>
    public static LinearRgba Decode(StandardEncodedRgba source) => new(
        DecodeChannel(source.Red, source.Encoding), DecodeChannel(source.Green, source.Encoding),
        DecodeChannel(source.Blue, source.Encoding), source.Alpha, GetLinearSpace(source.Encoding));

    /// <summary>Decodes and converts to an explicitly selected linear rendering/document space.</summary>
    public static LinearRgba Decode(StandardEncodedRgba source, StandardRgbColorSpaceReference destination) =>
        StandardLinearRgbConverter.Convert(Decode(source), destination);

    /// <summary>Converts linear color to the target primaries/white, then encodes it with explicit range policy.</summary>
    public static StandardEncodedRgba Encode(LinearRgba source, StandardRgbEncoding destination,
        RgbEncodingRangePolicy rangePolicy = RgbEncodingRangePolicy.Preserve)
    {
        if (!Enum.IsDefined(rangePolicy)) throw new ArgumentOutOfRangeException(nameof(rangePolicy));
        var linear = StandardLinearRgbConverter.Convert(source, GetLinearSpace(destination));
        return new(ApplyRange(EncodeChannel(linear.Red, destination), rangePolicy),
            ApplyRange(EncodeChannel(linear.Green, destination), rangePolicy),
            ApplyRange(EncodeChannel(linear.Blue, destination), rangePolicy), source.Alpha, destination);
    }

    /// <summary>Converts between encoded spaces, preserving alpha and extended components by default.</summary>
    public static StandardEncodedRgba Convert(StandardEncodedRgba source, StandardRgbEncoding destination,
        RgbEncodingRangePolicy rangePolicy = RgbEncodingRangePolicy.Preserve) => Encode(Decode(source), destination, rangePolicy);

    /// <summary>Decodes row-major straight-alpha pixels into a tagged FP32 image ready for texture upload.</summary>
    public static LinearRgbaImage DecodeImage(uint width, uint height, IEnumerable<Vector4> pixels,
        StandardRgbEncoding encoding, StandardRgbColorSpaceReference destination, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(destination);
        _ = GetLinearSpace(encoding);
        return new(width, height, pixels.Select(p =>
        {
            var c = Decode(new(p.X, p.Y, p.Z, p.W, encoding), destination);
            return new Vector4(c.Red, c.Green, c.Blue, c.Alpha);
        }), destination, name);
    }

    /// <summary>Returns straight-alpha encoded FP32 pixels. A file writer must also store the target encoding or ICC.</summary>
    public static Vector4[] EncodeImage(LinearRgbaImage source, StandardRgbEncoding destination,
        RgbEncodingRangePolicy rangePolicy = RgbEncodingRangePolicy.Preserve)
    {
        ArgumentNullException.ThrowIfNull(source);
        _ = GetLinearSpace(destination);
        if (!Enum.IsDefined(rangePolicy)) throw new ArgumentOutOfRangeException(nameof(rangePolicy));
        return source.Pixels.Select(p =>
        {
            var c = Encode(new(p.X, p.Y, p.Z, p.W, source.ColorSpace), destination, rangePolicy);
            return new Vector4(c.Red, c.Green, c.Blue, c.Alpha);
        }).ToArray();
    }

    private static float ApplyRange(float value, RgbEncodingRangePolicy policy) => policy switch
    {
        RgbEncodingRangePolicy.Clip => Math.Clamp(value, 0, 1),
        RgbEncodingRangePolicy.Reject when value is < 0 or > 1 =>
            throw new InvalidOperationException("Target RGB lies outside [0,1]; choose an explicit mapping or clipping policy."),
        _ => value,
    };

    private static float DecodeChannel(float value, StandardRgbEncoding encoding)
    {
        float v = MathF.Abs(value);
        float result = encoding switch
        {
            StandardRgbEncoding.Srgb or StandardRgbEncoding.DisplayP3 => v <= 0.04045f
                ? v / 12.92f : MathF.Pow((v + 0.055f) / 1.055f, 2.4f),
            StandardRgbEncoding.AdobeRgb => MathF.Pow(v, 563f / 256f),
            StandardRgbEncoding.ProPhotoRgb => v <= 1f / 32f ? v / 16f : MathF.Pow(v, 1.8f),
            StandardRgbEncoding.Rec2020 => v < RecBeta * 4.5f
                ? v / 4.5f : MathF.Pow((v + RecAlpha - 1) / RecAlpha, 1 / 0.45f),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
        };
        return MathF.CopySign(result, value);
    }

    private static float EncodeChannel(float value, StandardRgbEncoding encoding)
    {
        float v = MathF.Abs(value);
        float result = encoding switch
        {
            StandardRgbEncoding.Srgb or StandardRgbEncoding.DisplayP3 => v <= 0.0031308f
                ? v * 12.92f : 1.055f * MathF.Pow(v, 1 / 2.4f) - 0.055f,
            StandardRgbEncoding.AdobeRgb => MathF.Pow(v, 256f / 563f),
            StandardRgbEncoding.ProPhotoRgb => v <= 1f / 512f ? v * 16f : MathF.Pow(v, 1 / 1.8f),
            StandardRgbEncoding.Rec2020 => v < RecBeta ? v * 4.5f : RecAlpha * MathF.Pow(v, 0.45f) - (RecAlpha - 1),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding)),
        };
        return MathF.CopySign(result, value);
    }
}

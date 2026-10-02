using System.Numerics;

namespace Mu3D.Color;

/// <summary>Evaluates nominal-range PQ and HLG transfer functions from ITU-R BT.2100-3.</summary>
/// <remarks>
/// These functions do not tone map or gamut map. PQ uses absolute component luminance in nits.
/// HLG display conversion uses Rec.2020 luminance, an explicit system gamma, and zero display black.
/// Negative and extended production signals are outside this nominal-range API and are rejected.
/// </remarks>
public static class HdrTransferFunctions
{
    private const double M1 = 2610.0 / 16384.0;
    private const double M2 = 2523.0 / 32.0;
    private const double C1 = 3424.0 / 4096.0;
    private const double C2 = 2413.0 / 128.0;
    private const double C3 = 2392.0 / 128.0;
    private const double HlgA = 0.17883277;
    private const double HlgB = 1 - 4 * HlgA;
    private static readonly double HlgC = 0.5 - HlgA * Math.Log(4 * HlgA);

    /// <summary>Encodes an absolute component luminance from zero through 10000 nits as PQ.</summary>
    /// <remarks>The mathematical encoding of zero is approximately 7.31e-7, as specified by ST 2084.</remarks>
    public static float EncodePqNits(float nits)
    {
        Range(nits, 0, 10000, nameof(nits));
        double power = Math.Pow(nits / 10000.0, M1);
        return (float)Math.Pow((C1 + C2 * power) / (1 + C3 * power), M2);
    }

    /// <summary>Decodes a nominal PQ component in [0, 1] to absolute luminance in nits.</summary>
    public static float DecodePqNits(float encoded)
    {
        Range(encoded, 0, 1, nameof(encoded));
        double power = Math.Pow(encoded, 1 / M2);
        return (float)(10000 * Math.Pow(Math.Max(power - C1, 0) / (C2 - C3 * power), 1 / M1));
    }

    /// <summary>Encodes nominal relative scene light in [0, 1] using the HLG OETF.</summary>
    /// <remarks>This is a scene transfer function; it does not apply the display OOTF.</remarks>
    public static float EncodeHlgScene(float sceneLight)
    {
        Range(sceneLight, 0, 1, nameof(sceneLight));
        return (float)(sceneLight <= 1.0 / 12
            ? Math.Sqrt(3.0 * sceneLight)
            : HlgA * Math.Log(12.0 * sceneLight - HlgB) + HlgC);
    }

    /// <summary>Decodes a nominal HLG component in [0, 1] to relative scene light.</summary>
    public static float DecodeHlgScene(float encoded)
    {
        Range(encoded, 0, 1, nameof(encoded));
        return (float)(encoded <= 0.5
            ? encoded * (double)encoded / 3
            : (Math.Exp((encoded - HlgC) / HlgA) + HlgB) / 12);
    }

    /// <summary>Encodes display-linear Rec.2020 RGB as HLG using the inverse luminance-coupled OOTF.</summary>
    /// <param name="linearRgb">Display-linear Rec.2020 RGB; one denotes reference white.</param>
    /// <param name="referenceWhiteNits">The positive luminance represented by linear RGB one.</param>
    /// <param name="peakNits">The positive display peak, no less than reference white.</param>
    /// <param name="systemGamma">The explicitly chosen positive HLG display system gamma.</param>
    /// <returns>Nominal HLG RGB. Colors requiring extended encoded components are rejected.</returns>
    public static Vector3 EncodeHlgFromLinearRec2020(
        Vector3 linearRgb, float referenceWhiteNits, float peakNits, float systemGamma)
    {
        ValidateHlg(referenceWhiteNits, peakNits, systemGamma);
        ValidateRgb(linearRgb, nameof(linearRgb));
        Vector3 display = linearRgb * (referenceWhiteNits / peakNits);
        float luminance = Luminance(display);
        if (luminance == 0) return Vector3.Zero;
        Vector3 scene = display * MathF.Pow(luminance, (1 - systemGamma) / systemGamma);
        return new(EncodeHlgScene(scene.X), EncodeHlgScene(scene.Y), EncodeHlgScene(scene.Z));
    }

    /// <summary>Decodes HLG RGB through the Rec.2020 luminance-coupled OOTF to display-linear RGB.</summary>
    /// <param name="encodedRgb">Nominal HLG RGB components in [0, 1].</param>
    /// <param name="referenceWhiteNits">The positive luminance represented by returned RGB one.</param>
    /// <param name="peakNits">The positive display peak, no less than reference white.</param>
    /// <param name="systemGamma">The explicitly chosen positive HLG display system gamma.</param>
    /// <returns>Rec.2020 display-linear RGB, relative to the specified reference white.</returns>
    public static Vector3 DecodeHlgToLinearRec2020(
        Vector3 encodedRgb, float referenceWhiteNits, float peakNits, float systemGamma)
    {
        ValidateHlg(referenceWhiteNits, peakNits, systemGamma);
        Vector3 scene = new(DecodeHlgScene(encodedRgb.X), DecodeHlgScene(encodedRgb.Y), DecodeHlgScene(encodedRgb.Z));
        float luminance = Luminance(scene);
        if (luminance == 0) return Vector3.Zero;
        Vector3 result = scene * (peakNits / referenceWhiteNits * MathF.Pow(luminance, systemGamma - 1));
        ValidateRgb(result, nameof(encodedRgb));
        return result;
    }

    private static float Luminance(Vector3 rgb) => 0.2627f * rgb.X + 0.6780f * rgb.Y + 0.0593f * rgb.Z;

    private static void ValidateHlg(float white, float peak, float gamma)
    {
        Positive(white, nameof(white));
        Positive(peak, nameof(peak));
        Positive(gamma, nameof(gamma));
        if (peak < white) throw new ArgumentOutOfRangeException(nameof(peak), "Peak must not be below reference white.");
    }

    private static void ValidateRgb(Vector3 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) ||
            value.X < 0 || value.Y < 0 || value.Z < 0)
            throw new ArgumentOutOfRangeException(name, "RGB components must be finite and nonnegative.");
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name, "A finite positive value is required.");
    }

    private static void Range(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name, $"A finite value in [{min}, {max}] is required; no clipping is performed.");
    }
}

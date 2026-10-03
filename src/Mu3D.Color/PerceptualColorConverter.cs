namespace Mu3D.Color;

/// <summary>Converts tagged standard linear RGB to and from CIE Lab/LCh and Oklab/Oklch using FP32 math.</summary>
/// <remarks>
/// These transforms preserve unpremultiplied alpha and extended color values; they do not clip,
/// gamut map, tone map or apply a display transfer function. Lab uses D50 xy = (0.3457, 0.3585),
/// consistently with <see cref="StandardLinearRgbConverter"/>. Oklab uses linear sRGB/D65 and
/// signed cube roots. Finite coordinates whose conversion exceeds FP32 range fail explicitly.
/// </remarks>
public static class PerceptualColorConverter
{
    private const float D50X = 0.3457f / 0.3585f;
    private const float D50Z = (1 - 0.3457f - 0.3585f) / 0.3585f;
    private const float Epsilon = 216f / 24389f;
    private const float Kappa = 24389f / 27f;
    private const float Delta = 6f / 29f;

    /// <summary>Converts a tagged standard linear RGB color to D50 CIE L*a*b*, adapting its white point.</summary>
    /// <param name="source">The unpremultiplied, tagged standard linear RGB color.</param>
    /// <returns>D50 Lab coordinates with unchanged alpha.</returns>
    public static CieLabColor ToLab(LinearRgba source)
    {
        var xyz = StandardLinearRgbConverter.ToXyzD50(source);
        float x = LabForward(xyz.X / D50X);
        float y = LabForward(xyz.Y);
        float z = LabForward(xyz.Z / D50Z);
        return new CieLabColor(116 * y - 16, 500 * (x - y), 200 * (y - z), source.Alpha);
    }

    /// <summary>Converts D50 CIE L*a*b* to an explicitly chosen standard linear RGB space, adapting its white point.</summary>
    /// <param name="source">The unpremultiplied D50 Lab color.</param>
    /// <param name="destination">The destination standard linear RGB identity.</param>
    /// <returns>A tagged linear RGB color with unchanged alpha and no gamut clipping.</returns>
    public static LinearRgba FromLab(CieLabColor source, StandardRgbColorSpaceReference destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        float y = (source.Lightness + 16) / 116;
        float x = y + source.A / 500;
        float z = y - source.B / 200;
        return StandardLinearRgbConverter.FromXyzD50(
            LabInverse(x) * D50X,
            source.Lightness > 8 ? Cube(y) : source.Lightness / Kappa,
            LabInverse(z) * D50Z,
            source.Alpha,
            destination);
    }

    /// <summary>Converts Cartesian D50 Lab to cylindrical D50 LCh; a neutral color receives hue zero.</summary>
    /// <param name="source">The unpremultiplied D50 Lab color.</param>
    /// <returns>D50 LCh coordinates with unchanged lightness and alpha.</returns>
    public static CieLchColor ToLch(CieLabColor source) => new(
        source.Lightness, Chroma(source.A, source.B), Hue(source.A, source.B), source.Alpha);

    /// <summary>Converts cylindrical D50 LCh to Cartesian D50 Lab.</summary>
    /// <param name="source">The unpremultiplied D50 LCh color.</param>
    /// <returns>D50 Lab coordinates with unchanged lightness and alpha.</returns>
    public static CieLabColor FromLch(CieLchColor source)
    {
        float angle = source.HueDegrees * (MathF.PI / 180);
        return new CieLabColor(source.Lightness,
            source.Chroma * MathF.Cos(angle), source.Chroma * MathF.Sin(angle), source.Alpha);
    }

    /// <summary>Converts a tagged standard linear RGB color to D65 Oklab, adapting its white point.</summary>
    /// <param name="source">The unpremultiplied, tagged standard linear RGB color.</param>
    /// <returns>Oklab coordinates with unchanged alpha and signed extended values.</returns>
    public static OklabColor ToOklab(LinearRgba source)
    {
        var rgb = StandardLinearRgbConverter.Convert(source, StandardColorSpaces.LinearSrgb);
        // Bjorn Ottosson's 2021-01-25 linear sRGB matrices; cbrt retains negative LMS values.
        float l = MathF.Cbrt(0.4122214708f * rgb.Red + 0.5363325363f * rgb.Green + 0.0514459929f * rgb.Blue);
        float m = MathF.Cbrt(0.2119034982f * rgb.Red + 0.6806995451f * rgb.Green + 0.1073969566f * rgb.Blue);
        float s = MathF.Cbrt(0.0883024619f * rgb.Red + 0.2817188376f * rgb.Green + 0.6299787005f * rgb.Blue);
        return new OklabColor(
            0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
            1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
            0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s,
            source.Alpha);
    }

    /// <summary>Converts D65 Oklab to an explicitly chosen standard linear RGB space, adapting its white point.</summary>
    /// <param name="source">The unpremultiplied Oklab color.</param>
    /// <param name="destination">The destination standard linear RGB identity.</param>
    /// <returns>A tagged linear RGB color with unchanged alpha and no gamut clipping.</returns>
    public static LinearRgba FromOklab(OklabColor source, StandardRgbColorSpaceReference destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        float l = Cube(source.Lightness + 0.3963377774f * source.A + 0.2158037573f * source.B);
        float m = Cube(source.Lightness - 0.1055613458f * source.A - 0.0638541728f * source.B);
        float s = Cube(source.Lightness - 0.0894841775f * source.A - 1.2914855480f * source.B);
        var rgb = new LinearRgba(
            4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
            -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
            -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s,
            source.Alpha, StandardColorSpaces.LinearSrgb);
        return StandardLinearRgbConverter.Convert(rgb, destination);
    }

    /// <summary>Converts Cartesian Oklab to cylindrical Oklch; a neutral color receives hue zero.</summary>
    /// <param name="source">The unpremultiplied Oklab color.</param>
    /// <returns>Oklch coordinates with unchanged lightness and alpha.</returns>
    public static OklchColor ToOklch(OklabColor source) => new(
        source.Lightness, Chroma(source.A, source.B), Hue(source.A, source.B), source.Alpha);

    /// <summary>Converts cylindrical Oklch to Cartesian Oklab.</summary>
    /// <param name="source">The unpremultiplied Oklch color.</param>
    /// <returns>Oklab coordinates with unchanged lightness and alpha.</returns>
    public static OklabColor FromOklch(OklchColor source)
    {
        float angle = source.HueDegrees * (MathF.PI / 180);
        return new OklabColor(source.Lightness,
            source.Chroma * MathF.Cos(angle), source.Chroma * MathF.Sin(angle), source.Alpha);
    }

    // CIE's rational epsilon/kappa definitions, using a linear branch for negative XYZ.
    private static float LabForward(float value) => value > Epsilon
        ? MathF.Cbrt(value) : value * (Kappa / 116) + 16f / 116;

    private static float LabInverse(float value) => value > Delta
        ? Cube(value) : (value - 16f / 116) / (Kappa / 116);

    private static float Cube(float value) => value * value * value;

    private static float Chroma(float a, float b)
    {
        // Scale before squaring so large finite coordinates do not overflow unnecessarily.
        float scale = MathF.Max(MathF.Abs(a), MathF.Abs(b));
        if (scale == 0) return 0;
        float x = a / scale;
        float y = b / scale;
        return scale * MathF.Sqrt(x * x + y * y);
    }

    private static float Hue(float a, float b) => a == 0 && b == 0
        ? 0 : MathF.Atan2(b, a) * (180 / MathF.PI);
}

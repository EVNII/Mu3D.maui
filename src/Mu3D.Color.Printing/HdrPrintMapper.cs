namespace Mu3D.Color.Printing;

/// <summary>Explicit scene-linear HDR to bounded linear Adobe RGB printing transform.</summary>
/// <remarks>
/// This Mu3D mapping is not an AgX or ACES view. After exposure and conversion directly to
/// Adobe RGB, luminance Y is compressed to Y/(1+Y). Chroma is contracted toward that neutral
/// luminance until every channel fits [0,1], preserving target linear luminance.
/// Nonpositive luminance maps to black. No intermediate sRGB gamut restriction is applied.
/// Output remains linear; use Adobe RGB encoding and an appropriate profile for file export.
/// Alpha is preserved, so composite explicitly before CMYK separation. PQ/HLG decoding is upstream.
/// </remarks>
public static class HdrPrintMapper
{
    /// <summary>Maps a tagged, unpremultiplied scene-linear color to print-relative linear Adobe RGB.</summary>
    /// <param name="source">Source in a supported standard linear RGB space.</param>
    /// <param name="exposureStops">Exposure adjustment in stops, from -20 to 20.</param>
    /// <returns>Bounded linear Adobe RGB with unchanged alpha.</returns>
    public static LinearRgba ToAdobeRgb(LinearRgba source, float exposureStops = 0)
    {
        if (!float.IsFinite(exposureStops) || exposureStops is < -20 or > 20)
            throw new ArgumentOutOfRangeException(nameof(exposureStops));
        var rgb = StandardLinearRgbConverter.Convert(source, StandardColorSpaces.LinearAdobeRgb);
        double exposure = Math.Pow(2, exposureStops);
        double r = rgb.Red * exposure, g = rgb.Green * exposure, b = rgb.Blue * exposure;
        // Adobe RGB (1998), D65 luminance coefficients normalized to a unit white.
        double y = .297344975 * r + .627363566 * g + .075291459 * b;
        if (y <= 0) return new(0, 0, 0, source.Alpha, StandardColorSpaces.LinearAdobeRgb);
        double neutral = y / (1 + y);
        r /= 1 + y; g /= 1 + y; b /= 1 + y;
        double chroma = 1;
        foreach (double channel in new[] { r, g, b })
        {
            double delta = channel - neutral;
            if (channel > 1) chroma = Math.Min(chroma, (1 - neutral) / delta);
            else if (channel < 0) chroma = Math.Min(chroma, -neutral / delta);
        }
        float Fit(double channel) => (float)Math.Clamp(neutral + chroma * (channel - neutral), 0, 1);
        return new(Fit(r), Fit(g), Fit(b), source.Alpha, StandardColorSpaces.LinearAdobeRgb);
    }
}

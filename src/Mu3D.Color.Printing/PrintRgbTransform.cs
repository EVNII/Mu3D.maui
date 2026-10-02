namespace Mu3D.Color.Printing;

/// <summary>Selects explicit scene-to-print rendering independently of the destination RGB space.</summary>
public enum PrintToneMapping
{
    /// <summary>Rational luminance compression followed by linear-luminance chroma contraction.</summary>
    SimpleLuminance,
    /// <summary>Official Blender 5.0 SDR Rec.2020 AgX followed by Mu3D target-gamut contraction.</summary>
    /// <remarks>This combined print transform is not a native Blender Adobe RGB or ProPhoto view.</remarks>
    AgXRec2020,
}

/// <summary>Explicit HDR-to-bounded-RGB rendering for subsequent ICC separation or profiled RGB export.</summary>
/// <remarks>
/// Source HDR is unchanged. AgX retains extended linear sRGB as a transport representation,
/// without sRGB clipping, then converts directly to the selected target. Target-gamut contraction
/// preserves D50 PCS luminance and is not perceptually uniform hue preservation.
/// ProPhoto output retains the AgX Rec.2020 rendering gamut; it cannot recover colors already
/// compressed by that view. Alpha is preserved: composite explicitly before CMYK separation.
/// </remarks>
public sealed class PrintRgbTransform : ILinearRgbTransform
{
    private readonly ColorViewTransform? agx;
    private readonly float exposure;

    /// <summary>Creates a reusable print renderer; mapping and target are independent explicit choices.</summary>
    /// <param name="sourceSpace">Standard scene-linear input space.</param>
    /// <param name="destinationSpace">Standard linear output space, for example Adobe RGB or ProPhoto.</param>
    /// <param name="mapping">Scene rendering algorithm.</param>
    /// <param name="exposureStops">Exposure before rendering, in [-20,20] stops.</param>
    public PrintRgbTransform(StandardRgbColorSpaceReference sourceSpace,
        StandardRgbColorSpaceReference destinationSpace,
        PrintToneMapping mapping = PrintToneMapping.AgXRec2020, float exposureStops = 0)
    {
        ArgumentNullException.ThrowIfNull(sourceSpace);
        ArgumentNullException.ThrowIfNull(destinationSpace);
        if (!Enum.IsDefined(mapping)) throw new ArgumentOutOfRangeException(nameof(mapping));
        if (!float.IsFinite(exposureStops) || exposureStops is < -20 or > 20)
            throw new ArgumentOutOfRangeException(nameof(exposureStops));
        SourceSpace = sourceSpace; DestinationSpace = destinationSpace;
        Mapping = mapping; ExposureStops = exposureStops;
        exposure = MathF.Pow(2, exposureStops);
        if (mapping == PrintToneMapping.AgXRec2020)
            agx = new(ColorViewPreset.AgXSdrRec2020, sourceSpace, exposureStops);
    }

    /// <summary>Gets the exact scene-linear input identity.</summary>
    public ColorSpaceReference SourceSpace { get; }
    /// <summary>Gets the bounded linear output identity; encoding and profile embedding follow downstream.</summary>
    public ColorSpaceReference DestinationSpace { get; }
    /// <summary>Gets the selected scene rendering algorithm.</summary>
    public PrintToneMapping Mapping { get; }
    /// <summary>Gets the exposure applied once before rendering.</summary>
    public float ExposureStops { get; }

    /// <summary>Renders one unpremultiplied source color; does not perform ICC separation or file encoding.</summary>
    public LinearRgba Transform(LinearRgba source)
    {
        if (source.ColorSpace != SourceSpace) throw new ArgumentException("Source tag must match the transform.", nameof(source));
        // Preserve the existing Adobe RGB simple mapper's numerical behavior.
        if (Mapping == PrintToneMapping.SimpleLuminance && DestinationSpace == StandardColorSpaces.LinearAdobeRgb)
            return HdrPrintMapper.ToAdobeRgb(source, ExposureStops);
        LinearRgba rendered;
        if (agx is not null) rendered = agx.Transform(source);
        else
        {
            float r = source.Red * exposure, g = source.Green * exposure, b = source.Blue * exposure;
            if (!float.IsFinite(r) || !float.IsFinite(g) || !float.IsFinite(b) ||
                Math.Max(Math.Abs(r), Math.Max(Math.Abs(g), Math.Abs(b))) > ColorViewTransform.MaximumExposedComponentMagnitude)
                throw new ArgumentOutOfRangeException(nameof(source), "Exposed components must have magnitude at most 1e30.");
            rendered = new(r, g, b, source.Alpha, source.ColorSpace);
        }
        var rgb = StandardLinearRgbConverter.Convert(rendered, (StandardRgbColorSpaceReference)DestinationSpace);
        float y = StandardLinearRgbConverter.ToXyzD50(rgb).Y;
        if (y <= 0) return new(0, 0, 0, source.Alpha, DestinationSpace);
        float scale = agx is null ? 1 / (1 + y) : 1;
        float neutral = Math.Clamp(y * scale, 0, 1);
        if (neutral >= 1) return new(1, 1, 1, source.Alpha, DestinationSpace);
        float red = rgb.Red * scale, green = rgb.Green * scale, blue = rgb.Blue * scale;
        float chroma = Math.Min(Limit(red), Math.Min(Limit(green), Limit(blue)));
        float Limit(float c) => c > 1 ? (1 - neutral) / (c - neutral) : c < 0 ? -neutral / (c - neutral) : 1;
        float Fit(float c) => Math.Clamp(neutral + chroma * (c - neutral), 0, 1);
        return new(Fit(red), Fit(green), Fit(blue), source.Alpha, DestinationSpace);
    }
}

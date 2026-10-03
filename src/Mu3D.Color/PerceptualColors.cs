namespace Mu3D.Color;

/// <summary>Stores finite, unpremultiplied CIE L*a*b* coordinates relative to the D50 white point.</summary>
/// <remarks>
/// D50 uses xy = (0.3457, 0.3585) with reference Y = 1. Lightness normally spans zero to 100;
/// extended lightness and opponent coordinates are retained without clipping. This is a relative
/// color representation, not an absolute HDR luminance or viewing-conditions model.
/// </remarks>
public readonly record struct CieLabColor
{
    /// <summary>Initializes a D50 CIE L*a*b* color without clipping its coordinates.</summary>
    /// <param name="lightness">The finite L* coordinate, normally zero to 100.</param>
    /// <param name="a">The finite green-to-red opponent coordinate.</param>
    /// <param name="b">The finite blue-to-yellow opponent coordinate.</param>
    /// <param name="alpha">Unpremultiplied alpha in the inclusive zero-to-one range.</param>
    public CieLabColor(float lightness, float a, float b, float alpha = 1)
    {
        PerceptualColorCoordinates.ValidateFinite(lightness, nameof(lightness));
        PerceptualColorCoordinates.ValidateFinite(a, nameof(a));
        PerceptualColorCoordinates.ValidateFinite(b, nameof(b));
        PerceptualColorCoordinates.ValidateAlpha(alpha);
        Lightness = lightness;
        A = a;
        B = b;
        Alpha = alpha;
    }

    /// <summary>Gets L*, retaining values outside its ordinary zero-to-100 range.</summary>
    public float Lightness { get; }

    /// <summary>Gets the green-to-red opponent coordinate.</summary>
    public float A { get; }

    /// <summary>Gets the blue-to-yellow opponent coordinate.</summary>
    public float B { get; }

    /// <summary>Gets unpremultiplied alpha; color conversion does not transform it.</summary>
    public float Alpha { get; }
}

/// <summary>Stores the finite cylindrical L*C*h form of D50 CIE L*a*b*.</summary>
/// <remarks>Lightness normally spans zero to 100; extended values are retained. The default value is transparent black.</remarks>
public readonly record struct CieLchColor
{
    /// <summary>Initializes D50 L*C*h coordinates, normalizing hue to the zero-inclusive, 360-exclusive range.</summary>
    /// <param name="lightness">The finite L* coordinate, normally zero to 100.</param>
    /// <param name="chroma">The finite, nonnegative C* coordinate.</param>
    /// <param name="hueDegrees">The finite hue angle in degrees; zero chroma makes this angle immaterial.</param>
    /// <param name="alpha">Unpremultiplied alpha in the inclusive zero-to-one range.</param>
    public CieLchColor(float lightness, float chroma, float hueDegrees, float alpha = 1)
    {
        PerceptualColorCoordinates.ValidateFinite(lightness, nameof(lightness));
        PerceptualColorCoordinates.ValidateChroma(chroma);
        PerceptualColorCoordinates.ValidateAlpha(alpha);
        Lightness = lightness;
        Chroma = chroma;
        HueDegrees = PerceptualColorCoordinates.NormalizeHue(hueDegrees);
        Alpha = alpha;
    }

    /// <summary>Gets L*, retaining values outside its ordinary zero-to-100 range.</summary>
    public float Lightness { get; }

    /// <summary>Gets nonnegative chroma in CIE L*a*b* coordinate units.</summary>
    public float Chroma { get; }

    /// <summary>Gets hue in the zero-inclusive, 360-exclusive degree range.</summary>
    public float HueDegrees { get; }

    /// <summary>Gets unpremultiplied alpha; color conversion does not transform it.</summary>
    public float Alpha { get; }
}

/// <summary>Stores finite, unpremultiplied Oklab coordinates relative to the D65 white point.</summary>
/// <remarks>
/// Lightness normally spans zero to one. Extended coordinates are retained without clipping;
/// Oklab is a relative perceptual representation, not an absolute HDR display appearance model.
/// </remarks>
public readonly record struct OklabColor
{
    /// <summary>Initializes a D65 Oklab color without clipping its coordinates.</summary>
    /// <param name="lightness">The finite lightness coordinate, normally zero to one.</param>
    /// <param name="a">The finite green-to-red opponent coordinate.</param>
    /// <param name="b">The finite blue-to-yellow opponent coordinate.</param>
    /// <param name="alpha">Unpremultiplied alpha in the inclusive zero-to-one range.</param>
    public OklabColor(float lightness, float a, float b, float alpha = 1)
    {
        PerceptualColorCoordinates.ValidateFinite(lightness, nameof(lightness));
        PerceptualColorCoordinates.ValidateFinite(a, nameof(a));
        PerceptualColorCoordinates.ValidateFinite(b, nameof(b));
        PerceptualColorCoordinates.ValidateAlpha(alpha);
        Lightness = lightness;
        A = a;
        B = b;
        Alpha = alpha;
    }

    /// <summary>Gets lightness, retaining values outside its ordinary zero-to-one range.</summary>
    public float Lightness { get; }

    /// <summary>Gets the green-to-red opponent coordinate.</summary>
    public float A { get; }

    /// <summary>Gets the blue-to-yellow opponent coordinate.</summary>
    public float B { get; }

    /// <summary>Gets unpremultiplied alpha; color conversion does not transform it.</summary>
    public float Alpha { get; }
}

/// <summary>Stores the finite cylindrical lightness, chroma and hue form of D65 Oklab.</summary>
/// <remarks>Lightness normally spans zero to one; extended values are retained. The default value is transparent black.</remarks>
public readonly record struct OklchColor
{
    /// <summary>Initializes Oklch coordinates, normalizing hue to the zero-inclusive, 360-exclusive range.</summary>
    /// <param name="lightness">The finite lightness coordinate, normally zero to one.</param>
    /// <param name="chroma">The finite, nonnegative chroma coordinate.</param>
    /// <param name="hueDegrees">The finite hue angle in degrees; zero chroma makes this angle immaterial.</param>
    /// <param name="alpha">Unpremultiplied alpha in the inclusive zero-to-one range.</param>
    public OklchColor(float lightness, float chroma, float hueDegrees, float alpha = 1)
    {
        PerceptualColorCoordinates.ValidateFinite(lightness, nameof(lightness));
        PerceptualColorCoordinates.ValidateChroma(chroma);
        PerceptualColorCoordinates.ValidateAlpha(alpha);
        Lightness = lightness;
        Chroma = chroma;
        HueDegrees = PerceptualColorCoordinates.NormalizeHue(hueDegrees);
        Alpha = alpha;
    }

    /// <summary>Gets lightness, retaining values outside its ordinary zero-to-one range.</summary>
    public float Lightness { get; }

    /// <summary>Gets nonnegative chroma in Oklab coordinate units.</summary>
    public float Chroma { get; }

    /// <summary>Gets hue in the zero-inclusive, 360-exclusive degree range.</summary>
    public float HueDegrees { get; }

    /// <summary>Gets unpremultiplied alpha; color conversion does not transform it.</summary>
    public float Alpha { get; }
}

internal static class PerceptualColorCoordinates
{
    internal static void ValidateFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(parameterName, "Color coordinates must be finite.");
    }

    internal static void ValidateAlpha(float alpha)
    {
        ValidateFinite(alpha, nameof(alpha));
        if (alpha is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(alpha), "Unpremultiplied alpha must be between zero and one.");
    }

    internal static void ValidateChroma(float chroma)
    {
        ValidateFinite(chroma, nameof(chroma));
        if (chroma < 0)
            throw new ArgumentOutOfRangeException(nameof(chroma), "Chroma must be nonnegative.");
    }

    internal static float NormalizeHue(float hueDegrees)
    {
        ValidateFinite(hueDegrees, nameof(hueDegrees));
        float hue = hueDegrees % 360;
        if (hue < 0) hue += 360;
        // A tiny negative angle can round up to 360 when added in FP32.
        return hue >= 360 || hue == 0 ? 0 : hue;
    }
}

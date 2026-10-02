using Mu3D.Color;

namespace Mu3D.Creative;

/// <summary>
/// Describes one application-supplied circular dab in canvas pixel coordinates. The application owns
/// input sampling, pressure interpretation, spacing, interpolation, tool state and undo policy.
/// </summary>
public readonly record struct BrushDab
{
    /// <summary>Creates a circular dab sampled at pixel centers.</summary>
    /// <param name="centerX">The finite horizontal center in canvas pixels.</param>
    /// <param name="centerY">The finite vertical center in canvas pixels.</param>
    /// <param name="radius">The finite positive radius in pixels.</param>
    /// <param name="color">The tagged, straight-alpha, linear-light paint color.</param>
    /// <param name="opacity">Coverage multiplier in [0, 1], independent of color alpha.</param>
    /// <param name="hardness">Inner fully covered radius as a fraction in [0, 1]; the outer annulus fades linearly.</param>
    /// <param name="blendMode">The RGB blend function used before source-over alpha compositing.</param>
    public BrushDab(
        float centerX,
        float centerY,
        float radius,
        LinearRgba color,
        float opacity = 1f,
        float hardness = 1f,
        CanvasBlendMode blendMode = CanvasBlendMode.Normal)
    {
        if (!float.IsFinite(centerX) || !float.IsFinite(centerY) ||
            !float.IsFinite(radius) || radius <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(radius), "Dab center and radius must be finite, with positive radius.");
        }
        ValidateUnit(opacity, nameof(opacity));
        ValidateUnit(hardness, nameof(hardness));
        ArgumentNullException.ThrowIfNull(color.ColorSpace);
        if (!Enum.IsDefined(blendMode))
        {
            throw new ArgumentOutOfRangeException(nameof(blendMode));
        }
        CenterX = centerX;
        CenterY = centerY;
        Radius = radius;
        Color = color;
        Opacity = opacity;
        Hardness = hardness;
        BlendMode = blendMode;
    }

    /// <summary>Gets the finite horizontal center in canvas pixels.</summary>
    public float CenterX { get; }

    /// <summary>Gets the finite vertical center in canvas pixels.</summary>
    public float CenterY { get; }

    /// <summary>Gets the positive radius in canvas pixels.</summary>
    public float Radius { get; }

    /// <summary>Gets the explicitly tagged, straight-alpha linear-light paint color.</summary>
    public LinearRgba Color { get; }

    /// <summary>Gets the coverage multiplier independent of color alpha.</summary>
    public float Opacity { get; }

    /// <summary>Gets the fractional inner radius with full coverage.</summary>
    public float Hardness { get; }

    /// <summary>Gets the RGB blend function applied before source-over compositing.</summary>
    public CanvasBlendMode BlendMode { get; }

    internal static void ValidateUnit(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value must be finite and in [0, 1].");
        }
    }
}

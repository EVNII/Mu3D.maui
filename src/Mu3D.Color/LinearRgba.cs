namespace Mu3D.Color;

/// <summary>
/// Stores finite, unpremultiplied, linear-light RGBA components with explicit color-space identity.
/// Negative and above-one RGB values are preserved.
/// </summary>
public readonly record struct LinearRgba
{
    /// <summary>Initializes an explicitly tagged linear-light color.</summary>
    public LinearRgba(
        float red,
        float green,
        float blue,
        float alpha,
        ColorSpaceReference colorSpace)
    {
        if (!float.IsFinite(red) || !float.IsFinite(green) ||
            !float.IsFinite(blue) || !float.IsFinite(alpha))
        {
            throw new ArgumentOutOfRangeException(nameof(red), "Color components must be finite.");
        }
        if (alpha is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(alpha), "Unpremultiplied alpha must be between zero and one.");
        }
        ArgumentNullException.ThrowIfNull(colorSpace);
        if (!colorSpace.IsLinear)
        {
            throw new ArgumentException("LinearRgba requires a linear-light color space.", nameof(colorSpace));
        }
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
        ColorSpace = colorSpace;
    }

    /// <summary>Gets the red component.</summary>
    public float Red { get; }

    /// <summary>Gets the green component.</summary>
    public float Green { get; }

    /// <summary>Gets the blue component.</summary>
    public float Blue { get; }

    /// <summary>Gets the unpremultiplied alpha component.</summary>
    public float Alpha { get; }

    /// <summary>Gets the explicit linear-light RGB space identity.</summary>
    public ColorSpaceReference ColorSpace { get; }
}

namespace Mu3D.Color;

/// <summary>
/// Stores finite, unpremultiplied encoded RGB components with their target ICC profile identity.
/// </summary>
/// <remarks>
/// RGB components are not implicitly clipped so floating-point device encodings may preserve
/// negative or above-one values. A concrete integer image encoder must apply an explicit encoding
/// range and clipping policy.
/// </remarks>
public readonly record struct IccEncodedRgba
{
    /// <summary>Initializes an encoded target-device RGB value.</summary>
    /// <param name="red">The finite encoded red component.</param>
    /// <param name="green">The finite encoded green component.</param>
    /// <param name="blue">The finite encoded blue component.</param>
    /// <param name="alpha">The unpremultiplied alpha component from zero through one.</param>
    /// <param name="profile">The target ICC profile that defines the encoded RGB components.</param>
    public IccEncodedRgba(
        float red,
        float green,
        float blue,
        float alpha,
        IccProfile profile)
    {
        if (!float.IsFinite(red) || !float.IsFinite(green) ||
            !float.IsFinite(blue) || !float.IsFinite(alpha))
        {
            throw new ArgumentOutOfRangeException(nameof(red), "Color components must be finite.");
        }
        if (alpha is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alpha),
                "Unpremultiplied alpha must be between zero and one.");
        }
        ArgumentNullException.ThrowIfNull(profile);
        Red = red;
        Green = green;
        Blue = blue;
        Alpha = alpha;
        Profile = profile;
    }

    /// <summary>Gets the encoded red component.</summary>
    public float Red { get; }

    /// <summary>Gets the encoded green component.</summary>
    public float Green { get; }

    /// <summary>Gets the encoded blue component.</summary>
    public float Blue { get; }

    /// <summary>Gets the unpremultiplied alpha component.</summary>
    public float Alpha { get; }

    /// <summary>Gets the target ICC profile that defines the encoded RGB components.</summary>
    public IccProfile Profile { get; }
}

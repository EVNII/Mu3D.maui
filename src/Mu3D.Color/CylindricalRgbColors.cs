namespace Mu3D.Color;

/// <summary>SDR hue, saturation and lightness coordinates of an explicitly tagged nonlinear RGB space.</summary>
/// <remarks>These are geometric RGB coordinates, not perceptually uniform lightness or saturation.
/// Only the sRGB encoding corresponds to CSS HSL. Hue is canonicalized to [0,360) degrees;
/// saturation, lightness and alpha are in [0,1]. HDR or out-of-gamut RGB needs explicit mapping first.</remarks>
public readonly record struct HslColor
{
    /// <summary>Constructs bounded SDR coordinates, wrapping any finite hue and rejecting invalid ranges.</summary>
    public HslColor(float hueDegrees, float saturation, float lightness, float alpha,
        StandardRgbEncoding encoding)
    {
        _ = StandardRgbEncodingConverter.GetLinearSpace(encoding);
        HueDegrees = CylindricalRgbConverter.NormalizeHue(hueDegrees);
        CylindricalRgbConverter.ValidateUnit(saturation, nameof(saturation));
        CylindricalRgbConverter.ValidateUnit(lightness, nameof(lightness));
        CylindricalRgbConverter.ValidateUnit(alpha, nameof(alpha));
        Saturation = saturation; Lightness = lightness; Alpha = alpha; Encoding = encoding;
    }

    /// <summary>Gets hue in degrees in [0,360); achromatic RGB conversion chooses zero.</summary>
    public float HueDegrees { get; }
    /// <summary>Gets HSL saturation in [0,1].</summary>
    public float Saturation { get; }
    /// <summary>Gets lightness, the average encoded RGB extrema, in [0,1].</summary>
    public float Lightness { get; }
    /// <summary>Gets unpremultiplied alpha.</summary>
    public float Alpha { get; }
    /// <summary>Gets the underlying nonlinear RGB encoding identity.</summary>
    public StandardRgbEncoding Encoding { get; }
}

/// <summary>SDR hue, saturation and value coordinates of an explicitly tagged nonlinear RGB space.</summary>
/// <remarks>These are geometric RGB coordinates, not perceptually uniform brightness or saturation.
/// Hue is canonicalized to [0,360) degrees; saturation, value and alpha are in [0,1].
/// HDR or out-of-gamut RGB needs explicit mapping first.</remarks>
public readonly record struct HsvColor
{
    /// <summary>Constructs bounded SDR coordinates, wrapping any finite hue and rejecting invalid ranges.</summary>
    public HsvColor(float hueDegrees, float saturation, float value, float alpha,
        StandardRgbEncoding encoding)
    {
        _ = StandardRgbEncodingConverter.GetLinearSpace(encoding);
        HueDegrees = CylindricalRgbConverter.NormalizeHue(hueDegrees);
        CylindricalRgbConverter.ValidateUnit(saturation, nameof(saturation));
        CylindricalRgbConverter.ValidateUnit(value, nameof(value));
        CylindricalRgbConverter.ValidateUnit(alpha, nameof(alpha));
        Saturation = saturation; Value = value; Alpha = alpha; Encoding = encoding;
    }

    /// <summary>Gets hue in degrees in [0,360); achromatic RGB conversion chooses zero.</summary>
    public float HueDegrees { get; }
    /// <summary>Gets HSV saturation in [0,1].</summary>
    public float Saturation { get; }
    /// <summary>Gets value, the maximum encoded RGB component, in [0,1].</summary>
    public float Value { get; }
    /// <summary>Gets unpremultiplied alpha.</summary>
    public float Alpha { get; }
    /// <summary>Gets the underlying nonlinear RGB encoding identity.</summary>
    public StandardRgbEncoding Encoding { get; }
}

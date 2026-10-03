namespace Mu3D.Color;

/// <summary>Converts tagged SDR encoded RGB to and from HSL/HSV coordinates using FP32 calculations.</summary>
/// <remarks>Alpha and RGB encoding are preserved. Extended RGB is rejected rather than implicitly
/// clipped or tone mapped. These coordinates support SDR selection; linear-light RGB remains the
/// rendering and compositing representation. Achromatic RGB has canonical hue zero.</remarks>
public static class CylindricalRgbConverter
{
    /// <summary>Converts bounded encoded RGB to hue, saturation and lightness without color-space conversion.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An encoded RGB component is outside [0,1].</exception>
    public static HslColor ToHsl(StandardEncodedRgba source)
    {
        var (minimum, maximum, delta, hue) = Analyze(source);
        float lightness = (minimum + maximum) * 0.5f;
        // Avoid cancellation around black/white; an achromatic hue is a deterministic zero.
        float saturation = delta == 0 ? 0 : delta / (lightness <= 0.5f
            ? minimum + maximum : (1 - minimum) + (1 - maximum));
        return new(hue, saturation, lightness, source.Alpha, source.Encoding);
    }

    /// <summary>Converts bounded HSL coordinates to RGB retaining the original encoding and alpha.</summary>
    public static StandardEncodedRgba FromHsl(HslColor source)
    {
        float amplitude = source.Saturation * MathF.Min(source.Lightness, 1 - source.Lightness);
        float Channel(float n)
        {
            float k = (n + source.HueDegrees / 30) % 12;
            return source.Lightness - amplitude * Math.Clamp(MathF.Min(k - 3, 9 - k), -1, 1);
        }
        return new(Channel(0), Channel(8), Channel(4), source.Alpha, source.Encoding);
    }

    /// <summary>Converts bounded encoded RGB to hue, saturation and value without color-space conversion.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An encoded RGB component is outside [0,1].</exception>
    public static HsvColor ToHsv(StandardEncodedRgba source)
    {
        var (_, maximum, delta, hue) = Analyze(source);
        return new(hue, maximum == 0 ? 0 : delta / maximum, maximum, source.Alpha, source.Encoding);
    }

    /// <summary>Converts bounded HSV coordinates to RGB retaining the original encoding and alpha.</summary>
    public static StandardEncodedRgba FromHsv(HsvColor source)
    {
        float chroma = source.Value * source.Saturation;
        float Channel(float n)
        {
            float k = (n + source.HueDegrees / 60) % 6;
            return source.Value - chroma * Math.Clamp(MathF.Min(k, 4 - k), 0, 1);
        }
        return new(Channel(5), Channel(3), Channel(1), source.Alpha, source.Encoding);
    }

    internal static float NormalizeHue(float hueDegrees)
    {
        if (!float.IsFinite(hueDegrees)) throw new ArgumentOutOfRangeException(nameof(hueDegrees));
        float result = hueDegrees % 360;
        if (result < 0) result += 360;
        return result == 0 || result >= 360 ? 0 : result;
    }

    internal static void ValidateUnit(float value, string name)
    {
        if (!float.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(name, "SDR components must be in [0,1].");
    }

    private static (float Minimum, float Maximum, float Delta, float Hue) Analyze(StandardEncodedRgba source)
    {
        ValidateUnit(source.Red, nameof(source));
        ValidateUnit(source.Green, nameof(source));
        ValidateUnit(source.Blue, nameof(source));
        float minimum = MathF.Min(source.Red, MathF.Min(source.Green, source.Blue));
        float maximum = MathF.Max(source.Red, MathF.Max(source.Green, source.Blue));
        float delta = maximum - minimum;
        float hue = delta == 0 ? 0 : maximum == source.Red ? (source.Green - source.Blue) / delta
            : maximum == source.Green ? (source.Blue - source.Red) / delta + 2
            : (source.Red - source.Green) / delta + 4;
        return (minimum, maximum, delta, NormalizeHue(hue * 60));
    }
}

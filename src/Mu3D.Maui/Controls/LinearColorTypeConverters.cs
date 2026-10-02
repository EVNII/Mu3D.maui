using System.ComponentModel;
using System.Globalization;
using System.Numerics;
using Mu3D.Color;

namespace Mu3D.Maui.Controls;

/// <summary>Parses XAML colors as <c>r,g,b[,a];space</c>, decoding explicitly named nonlinear spaces to linear light.</summary>
/// <remarks>Encoded spaces: srgb, display-p3, a98-rgb, prophoto-rgb and rec2020.
/// Existing lin_* and acescg values remain linear and are never decoded again.</remarks>
public sealed class LinearRgbaTypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text) return base.ConvertFrom(context, culture, value)!;
        string[] parts = text.Split(';', StringSplitOptions.TrimEntries);
        if (parts.Length != 2) throw new FormatException("Use r,g,b[,a];space with an explicit RGB encoding or linear space.");
        float[] channels = parts[0].Split(',', StringSplitOptions.TrimEntries)
            .Select(static part => float.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        if (channels.Length is not 3 and not 4) throw new FormatException("A linear color requires three or four components.");
        StandardRgbEncoding? encoding = parts[1] switch
        {
            "srgb" => StandardRgbEncoding.Srgb,
            "display-p3" => StandardRgbEncoding.DisplayP3,
            "a98-rgb" => StandardRgbEncoding.AdobeRgb,
            "prophoto-rgb" => StandardRgbEncoding.ProPhotoRgb,
            "rec2020" => StandardRgbEncoding.Rec2020,
            _ => null,
        };
        if (encoding is StandardRgbEncoding encodedSpace)
            return StandardRgbEncodingConverter.Decode(new StandardEncodedRgba(
                channels[0], channels[1], channels[2], channels.Length == 4 ? channels[3] : 1f, encodedSpace));
        StandardRgbColorSpaceReference space = parts[1] switch
        {
            "lin_rec709" => StandardColorSpaces.LinearSrgb,
            "lin_displayp3" => StandardColorSpaces.LinearDisplayP3,
            "lin_adobergb" => StandardColorSpaces.LinearAdobeRgb,
            "lin_prophoto" => StandardColorSpaces.LinearProPhotoRgb,
            "lin_rec2020" => StandardColorSpaces.LinearRec2020,
            "acescg" => StandardColorSpaces.AcesCg,
            _ => throw new FormatException("Unknown linear color space."),
        };
        return new LinearRgba(channels[0], channels[1], channels[2], channels.Length == 4 ? channels[3] : 1f, space);
    }
}

/// <summary>Parses a finite, numeric XAML vector as <c>x,y,z</c> without color conversion.</summary>
public sealed class LinearVector3TypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    /// <inheritdoc />
    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text) return base.ConvertFrom(context, culture, value)!;
        float[] components = text.Split(',', StringSplitOptions.TrimEntries)
            .Select(static part => float.Parse(part, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
        if (components.Length != 3 || components.Any(static number => !float.IsFinite(number)))
            throw new FormatException("A vector requires three finite components.");
        return new Vector3(components[0], components[1], components[2]);
    }
}

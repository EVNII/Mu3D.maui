namespace Mu3D.Color;

/// <summary>Identifies a standard linear-light RGB working space.</summary>
public enum StandardRgbColorSpace
{
    /// <summary>Linear-light sRGB using D65 and sRGB/Rec.709 primaries.</summary>
    LinearSrgb,

    /// <summary>Linear-light Display P3 using D65.</summary>
    LinearDisplayP3,

    /// <summary>Linear-light Adobe RGB (1998) using D65.</summary>
    LinearAdobeRgb,

    /// <summary>Linear-light ProPhoto RGB using D50.</summary>
    LinearProPhotoRgb,

    /// <summary>Linear-light Rec.2020 using D65.</summary>
    LinearRec2020,

    /// <summary>ACEScg/AP1 linear-light RGB using the ACES D60 white point.</summary>
    AcesCg,
}

/// <summary>
/// Base identity for a color space. Future ICC-backed references can extend this type without
/// changing color-bearing material or image properties.
/// </summary>
public abstract record ColorSpaceReference
{
    /// <summary>Initializes a color-space identity.</summary>
    protected ColorSpaceReference(string name, bool isLinear)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        IsLinear = isLinear;
    }

    /// <summary>Gets the stable human-readable space name.</summary>
    public string Name { get; }

    /// <summary>Gets whether components are encoded in linear light.</summary>
    public bool IsLinear { get; }
}

/// <summary>Identifies one built-in standard linear RGB working space.</summary>
public sealed record StandardRgbColorSpaceReference : ColorSpaceReference
{
    /// <summary>Initializes a standard RGB space reference.</summary>
    public StandardRgbColorSpaceReference(StandardRgbColorSpace space)
        : base(GetName(space), isLinear: true) => Space = space;

    /// <summary>Gets the standard RGB space identifier.</summary>
    public StandardRgbColorSpace Space { get; }

    private static string GetName(StandardRgbColorSpace space) => space switch
    {
        StandardRgbColorSpace.LinearSrgb => "Linear sRGB",
        StandardRgbColorSpace.LinearDisplayP3 => "Linear Display P3",
        StandardRgbColorSpace.LinearAdobeRgb => "Linear Adobe RGB (1998)",
        StandardRgbColorSpace.LinearProPhotoRgb => "Linear ProPhoto RGB",
        StandardRgbColorSpace.LinearRec2020 => "Linear Rec.2020",
        StandardRgbColorSpace.AcesCg => "ACEScg",
        _ => throw new ArgumentOutOfRangeException(nameof(space), space, "Unknown standard RGB space."),
    };
}

/// <summary>Provides shared identities for built-in linear RGB working spaces.</summary>
public static class StandardColorSpaces
{
    /// <summary>Gets the linear-light sRGB identity.</summary>
    public static StandardRgbColorSpaceReference LinearSrgb { get; } =
        new(StandardRgbColorSpace.LinearSrgb);

    /// <summary>Gets the linear-light Display P3 identity.</summary>
    public static StandardRgbColorSpaceReference LinearDisplayP3 { get; } =
        new(StandardRgbColorSpace.LinearDisplayP3);

    /// <summary>Gets the linear-light Adobe RGB (1998) identity.</summary>
    public static StandardRgbColorSpaceReference LinearAdobeRgb { get; } =
        new(StandardRgbColorSpace.LinearAdobeRgb);

    /// <summary>Gets the linear-light ProPhoto RGB identity.</summary>
    public static StandardRgbColorSpaceReference LinearProPhotoRgb { get; } =
        new(StandardRgbColorSpace.LinearProPhotoRgb);

    /// <summary>Gets the linear-light Rec.2020 identity.</summary>
    public static StandardRgbColorSpaceReference LinearRec2020 { get; } =
        new(StandardRgbColorSpace.LinearRec2020);

    /// <summary>Gets the ACEScg identity.</summary>
    public static StandardRgbColorSpaceReference AcesCg { get; } =
        new(StandardRgbColorSpace.AcesCg);
}

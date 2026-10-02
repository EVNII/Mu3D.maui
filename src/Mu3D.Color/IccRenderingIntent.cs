namespace Mu3D.Color;

/// <summary>Identifies an ICC colour-rendering objective selected by an application.</summary>
public enum IccRenderingIntent
{
    /// <summary>Uses the profile's vendor-defined perceptual colour re-rendering.</summary>
    Perceptual = 0,

    /// <summary>Preserves media-relative colorimetry and maps media white to destination white.</summary>
    MediaRelativeColorimetric = 1,

    /// <summary>Uses the profile's vendor-defined saturation colour re-rendering.</summary>
    Saturation = 2,

    /// <summary>Preserves ICC-absolute colorimetry, including the source media white.</summary>
    IccAbsoluteColorimetric = 3,
}

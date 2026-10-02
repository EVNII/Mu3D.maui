namespace Mu3D.Color;

/// <summary>
/// Identifies the encoding expected by a presentation surface.
/// </summary>
public enum ColorEncoding
{
    /// <summary>The encoding has not been determined.</summary>
    Unknown,

    /// <summary>Non-linear standard dynamic range sRGB.</summary>
    Srgb,

    /// <summary>Linear extended sRGB/scRGB, where values above one represent HDR highlights.</summary>
    ExtendedSrgbLinear,

    /// <summary>Display P3 with its standard non-linear transfer function.</summary>
    DisplayP3,

    /// <summary>BT.2100 encoded using the perceptual quantizer transfer function.</summary>
    Bt2100Pq,

    /// <summary>BT.2100 encoded using the hybrid log-gamma transfer function.</summary>
    Bt2100Hlg,

    /// <summary>Extended-range sRGB using the non-linear sRGB transfer function.</summary>
    ExtendedSrgb,

    /// <summary>Extended-range Display P3 using the non-linear sRGB transfer function.</summary>
    ExtendedDisplayP3,
}

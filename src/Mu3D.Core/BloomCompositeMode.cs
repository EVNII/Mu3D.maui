namespace Mu3D.Rendering;

/// <summary>Controls how an HDR Bloom image is combined with the unfiltered Beauty result.</summary>
public enum BloomCompositeMode
{
    /// <summary>
    /// Redistributes extracted highlight energy spatially. Intensity selects zero through one of
    /// that redistribution, so enabling Bloom does not intentionally add emitted radiance. Narrow
    /// highlight peaks decrease as their energy spreads into neighboring lower-energy pixels.
    /// </summary>
    EnergyPreserving,

    /// <summary>
    /// Adds the blurred highlight image to Beauty. This common artistic mode intentionally raises
    /// bright-pixel values and is not suitable for direct colorimetric comparison.
    /// </summary>
    Additive,
}

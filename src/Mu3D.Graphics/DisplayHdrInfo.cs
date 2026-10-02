using System.Numerics;

namespace Mu3D.Graphics;

/// <summary>Classifies the approximate gamut covered by a presentation display.</summary>
public enum DisplayGamut
{
    /// <summary>The display gamut is unknown or is not represented by this version of Mu3D.</summary>
    Unknown,

    /// <summary>The display covers approximately sRGB or Rec.709.</summary>
    Srgb,

    /// <summary>The display covers approximately Display P3.</summary>
    DisplayP3,

    /// <summary>The display covers approximately Rec.2020.</summary>
    Rec2020,
}

/// <summary>Reports optional absolute display luminance values in nits.</summary>
/// <param name="MaximumNits">Peak luminance of a small highlight patch.</param>
/// <param name="MaximumFullFrameNits">Sustained full-white-frame luminance.</param>
/// <param name="MinimumNits">Minimum black luminance.</param>
/// <param name="SdrWhiteNits">Luminance assigned to SDR reference white.</param>
public readonly record struct DisplayLuminance(
    float? MaximumNits,
    float? MaximumFullFrameNits,
    float? MinimumNits,
    float? SdrWhiteNits);

/// <summary>Reports optional relative extended-dynamic-range headroom multipliers.</summary>
/// <param name="Current">Headroom available at query time relative to SDR white.</param>
/// <param name="Potential">Headroom available under ideal display conditions.</param>
/// <param name="Reference">Headroom reported for reference-white content.</param>
public readonly record struct DisplayHeadroom(
    float? Current,
    float? Potential,
    float? Reference);

/// <summary>Reports optional CIE 1931 xy display primaries and white point.</summary>
/// <param name="Red">The red-primary xy coordinate.</param>
/// <param name="Green">The green-primary xy coordinate.</param>
/// <param name="Blue">The blue-primary xy coordinate.</param>
/// <param name="White">The white-point xy coordinate.</param>
public readonly record struct DisplayChromaticity(
    Vector2? Red,
    Vector2? Green,
    Vector2? Blue,
    Vector2? White);

/// <summary>Reports coarse display dynamic-range and gamut information.</summary>
/// <param name="SupportsHighDynamicRange">
/// Whether the platform reports that the display can present HDR-range content.
/// </param>
/// <param name="Gamut">The approximate gamut bucket, when known.</param>
public readonly record struct DisplayCoarseRange(
    bool? SupportsHighDynamicRange,
    DisplayGamut? Gamut);

/// <summary>
/// Describes the current HDR characteristics of the display backing a presentation surface.
/// </summary>
/// <remarks>
/// This is display state, not surface capability and not a request to configure HDR. Every value
/// is optional because platforms expose different subsets. Unknown data must remain unknown rather
/// than being represented as zero or inferred to mean SDR.
/// </remarks>
public sealed record DisplayHdrInfo
{
    /// <summary>Gets a snapshot in which every display characteristic is unknown.</summary>
    public static DisplayHdrInfo Unknown { get; } = new();

    /// <summary>Gets optional absolute luminance information.</summary>
    public DisplayLuminance? Luminance { get; init; }

    /// <summary>Gets optional relative EDR headroom information.</summary>
    public DisplayHeadroom? Headroom { get; init; }

    /// <summary>Gets optional display primaries and white-point chromaticity.</summary>
    public DisplayChromaticity? Chromaticity { get; init; }

    /// <summary>Gets optional coarse dynamic-range and gamut classification.</summary>
    public DisplayCoarseRange? CoarseRange { get; init; }

    /// <summary>Gets the reported output bit depth per color channel.</summary>
    public byte? BitsPerColor { get; init; }

    /// <summary>Gets whether every display characteristic is unknown.</summary>
    public bool IsUnknown =>
        Luminance is null &&
        Headroom is null &&
        Chromaticity is null &&
        CoarseRange is null &&
        BitsPerColor is null;

    /// <summary>
    /// Gets the best available linear tone-mapping headroom multiplier over SDR white.
    /// </summary>
    /// <remarks>
    /// Current relative headroom wins. A display explicitly reported as SDR resolves to one.
    /// Otherwise Mu3D derives a value from finite peak and SDR-white luminance when both are known.
    /// The result remains <see langword="null"/> when no safe multiplier can be derived.
    /// </remarks>
    public float? ToneMapHeadroom
    {
        get
        {
            if (Headroom?.Current is float current && float.IsFinite(current))
            {
                return current;
            }

            if (CoarseRange?.SupportsHighDynamicRange == false)
            {
                return 1;
            }

            if (Luminance is DisplayLuminance luminance &&
                luminance.MaximumNits is float maximum &&
                luminance.SdrWhiteNits is float sdrWhite &&
                float.IsFinite(maximum) &&
                float.IsFinite(sdrWhite) &&
                sdrWhite > 0)
            {
                return maximum / sdrWhite;
            }

            return null;
        }
    }
}

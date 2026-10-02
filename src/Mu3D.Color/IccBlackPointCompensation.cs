namespace Mu3D.Color;

/// <summary>
/// Defines explicit source and destination black-point lightness values for an ICC profile link.
/// </summary>
/// <remarks>
/// Values are media-relative CIELAB L*. Callers may supply measured values explicitly or use
/// <see cref="IccBlackPointEstimator"/> for supported RGB ICC profile links.
/// </remarks>
public readonly record struct IccBlackPointCompensation
{
    /// <summary>Initializes an explicit black-point compensation mapping.</summary>
    /// <param name="sourceLightness">The finite source black-point L* from zero up to but not including 100.</param>
    /// <param name="destinationLightness">The finite destination black-point L* from zero up to but not including 100.</param>
    public IccBlackPointCompensation(float sourceLightness, float destinationLightness)
    {
        ValidateLightness(sourceLightness, nameof(sourceLightness));
        ValidateLightness(destinationLightness, nameof(destinationLightness));
        SourceLightness = sourceLightness;
        DestinationLightness = destinationLightness;
    }

    /// <summary>Gets the media-relative source black-point L*.</summary>
    public float SourceLightness { get; }

    /// <summary>Gets the media-relative destination black-point L*.</summary>
    public float DestinationLightness { get; }

    private static void ValidateLightness(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value is < 0f or >= 100f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "ICC black-point lightness must be finite and between zero and 100 exclusive.");
        }
    }
}

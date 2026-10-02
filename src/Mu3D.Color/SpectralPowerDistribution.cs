namespace Mu3D.Color;

/// <summary>Identifies the units of a sampled spectral power distribution.</summary>
public enum SpectralPowerUnit
{
    /// <summary>Relative spectral power per nanometre; its normalization is application-defined.</summary>
    RelativePerNanometre,
    /// <summary>Spectral radiance in watts per square metre, steradian and nanometre.</summary>
    WattsPerSquareMetreSteradianNanometre,
}

/// <summary>Specifies the treatment of an SPD that does not cover the observer's entire wavelength range.</summary>
public enum SpectralCoveragePolicy
{
    /// <summary>Reject incomplete coverage rather than assuming that missing measurements are zero.</summary>
    RequireObserverCoverage,
    /// <summary>Explicitly interpret unmeasured wavelengths outside the SPD as zero power.</summary>
    ZeroOutsideSpectrum,
}

/// <summary>Stores an immutable, uniformly sampled nonnegative spectrum with explicit units and provenance.</summary>
/// <remarks>
/// Experimental spectral API. Samples describe point values at wavelengths in nanometres and are
/// linearly interpolated. This does not infer a spectrum from an RGB color or a color temperature.
/// </remarks>
public sealed class SpectralPowerDistribution
{
    private readonly float[] samples;

    /// <summary>Copies samples and validates their wavelength grid, units and provenance.</summary>
    public SpectralPowerDistribution(float firstWavelengthNanometres, float wavelengthStepNanometres,
        ReadOnlySpan<float> samples, SpectralPowerUnit unit, string sourceDescription)
    {
        SpectralValidation.Grid(firstWavelengthNanometres, wavelengthStepNanometres, samples.Length);
        if (!Enum.IsDefined(unit)) throw new ArgumentOutOfRangeException(nameof(unit));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);
        foreach (float value in samples)
            if (!float.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(samples), "Spectral power must be finite and nonnegative.");
        FirstWavelengthNanometres = firstWavelengthNanometres;
        WavelengthStepNanometres = wavelengthStepNanometres;
        this.samples = samples.ToArray();
        Unit = unit;
        SourceDescription = sourceDescription;
    }

    /// <summary>Gets the first measured wavelength in nanometres.</summary>
    public float FirstWavelengthNanometres { get; }
    /// <summary>Gets the wavelength spacing in nanometres.</summary>
    public float WavelengthStepNanometres { get; }
    /// <summary>Gets the last measured wavelength in nanometres.</summary>
    public float LastWavelengthNanometres => FirstWavelengthNanometres + WavelengthStepNanometres * (samples.Length - 1);
    /// <summary>Gets the number of point samples.</summary>
    public int Count => samples.Length;
    /// <summary>Gets a read-only view of the copied spectral power samples.</summary>
    public ReadOnlySpan<float> Samples => samples;
    /// <summary>Gets the explicit spectral power units.</summary>
    public SpectralPowerUnit Unit { get; }
    /// <summary>Gets the measurement, model or other provenance supplied by the application.</summary>
    public string SourceDescription { get; }

    internal double Evaluate(double wavelength)
    {
        double position = (wavelength - FirstWavelengthNanometres) / WavelengthStepNanometres;
        int index = Math.Clamp((int)Math.Floor(position), 0, samples.Length - 2);
        double fraction = Math.Clamp(position - index, 0, 1);
        return samples[index] * (1 - fraction) + samples[index + 1] * fraction;
    }
}

internal static class SpectralValidation
{
    internal static void Grid(float first, float step, int count)
    {
        if (!float.IsFinite(first) || first <= 0 || !float.IsFinite(step) || step <= 0 || count < 2 ||
            !float.IsFinite(first + step * (count - 1)) || first + step == first)
            throw new ArgumentOutOfRangeException(nameof(first), "At least two samples on a finite, positive, resolvable wavelength grid are required.");
    }
}

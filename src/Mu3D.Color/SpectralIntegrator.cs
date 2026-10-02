using System.Numerics;

namespace Mu3D.Color;

/// <summary>Stores integrated experimental tristimulus values with their observer and source spectrum units.</summary>
/// <remarks>
/// Values are the wavelength integrals of SPD times each matching function. Radiometric input
/// produces observer-weighted radiance, not automatically cd/m²; no efficacy, white normalization,
/// chromatic adaptation or implicit conversion to ICC PCS is applied.
/// </remarks>
public readonly record struct SpectralTristimulusValues
{
    /// <summary>Initializes finite XYZ integrals tagged with the observer and input spectrum units.</summary>
    public SpectralTristimulusValues(Vector3 xyz, SpectralObserver observer, SpectralPowerUnit sourceUnit)
    {
        ArgumentNullException.ThrowIfNull(observer);
        if (!float.IsFinite(xyz.X) || !float.IsFinite(xyz.Y) || !float.IsFinite(xyz.Z))
            throw new ArgumentOutOfRangeException(nameof(xyz));
        if (!Enum.IsDefined(sourceUnit)) throw new ArgumentOutOfRangeException(nameof(sourceUnit));
        Xyz = xyz;
        Observer = observer;
        SourceUnit = sourceUnit;
    }
    /// <summary>Gets the integrated XYZ values.</summary>
    public Vector3 Xyz { get; }
    /// <summary>Gets the exact observer curves used by the integration.</summary>
    public SpectralObserver Observer { get; }
    /// <summary>Gets the units of the input SPD, before wavelength integration removes the per-nanometre factor.</summary>
    public SpectralPowerUnit SourceUnit { get; }
}

/// <summary>Integrates measured spectra against explicit color-matching functions.</summary>
/// <remarks>
/// Experimental API. Integrates the product of piecewise linear interpolants exactly on the union
/// of both sample grids. The CIE 1 nm tables are used as tabulated; this interpolation does not
/// implement the continuous physiological formulas referenced in CIE metadata. Accumulation uses
/// double precision and the public result is finite FP32. No normalization is implicit.
/// </remarks>
public static class SpectralIntegrator
{
    /// <summary>Computes the XYZ wavelength integrals using an explicit missing-spectrum policy.</summary>
    public static SpectralTristimulusValues Integrate(SpectralPowerDistribution spectrum,
        SpectralObserver observer, SpectralCoveragePolicy coveragePolicy = SpectralCoveragePolicy.RequireObserverCoverage)
    {
        ArgumentNullException.ThrowIfNull(spectrum);
        ArgumentNullException.ThrowIfNull(observer);
        if (!Enum.IsDefined(coveragePolicy)) throw new ArgumentOutOfRangeException(nameof(coveragePolicy));
        if (coveragePolicy == SpectralCoveragePolicy.RequireObserverCoverage &&
            (spectrum.FirstWavelengthNanometres > observer.FirstWavelengthNanometres ||
             spectrum.LastWavelengthNanometres < observer.LastWavelengthNanometres))
            throw new ArgumentException("The SPD must cover the observer's full range or explicitly assume zero outside its measured range.", nameof(spectrum));
        double start = Math.Max(spectrum.FirstWavelengthNanometres, observer.FirstWavelengthNanometres);
        double end = Math.Min(spectrum.LastWavelengthNanometres, observer.LastWavelengthNanometres);
        double x = 0, y = 0, z = 0;
        while (start < end)
        {
            double next = Math.Min(end, Math.Min(
                NextGrid(start, spectrum.FirstWavelengthNanometres, spectrum.WavelengthStepNanometres),
                NextGrid(start, observer.FirstWavelengthNanometres, observer.WavelengthStepNanometres)));
            double a = spectrum.Evaluate(start), b = spectrum.Evaluate(next);
            var ca = observer.Evaluate(start);
            var cb = observer.Evaluate(next);
            double factor = (next - start) / 6;
            // Integral of (a + t(b-a)) * (c + t(d-c)) on a segment.
            x += factor * (2 * a * ca.X + a * cb.X + b * ca.X + 2 * b * cb.X);
            y += factor * (2 * a * ca.Y + a * cb.Y + b * ca.Y + 2 * b * cb.Y);
            z += factor * (2 * a * ca.Z + a * cb.Z + b * ca.Z + 2 * b * cb.Z);
            start = next;
        }
        return new(new((float)x, (float)y, (float)z), observer, spectrum.Unit);
    }

    private static double NextGrid(double current, float first, float step)
    {
        double index = Math.Floor((current - first) / step) + 1;
        double next = first + index * step;
        if (next <= current) next = first + (index + 1) * step;
        if (next <= current) throw new ArgumentException("The spectral grid cannot be resolved numerically.");
        return next;
    }
}

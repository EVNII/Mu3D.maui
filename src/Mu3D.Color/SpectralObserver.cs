using System.Numerics;

namespace Mu3D.Color;

/// <summary>Stores immutable experimental XYZ color-matching curves and observer provenance.</summary>
/// <remarks>
/// Standard observers describe fixed published curves. Custom observers must supply measured or
/// independently computed curves; age and field size are descriptive metadata, never an invented
/// age-adjustment formula. No transform from legacy ICC PCS is implied by this class.
/// </remarks>
public sealed class SpectralObserver
{
    private readonly Vector3[] matchingFunctions;

    /// <summary>Copies custom color-matching curves and associates explicit field and optional age metadata.</summary>
    public SpectralObserver(string name, float fieldSizeDegrees, float? observerAgeYears,
        string sourceDescription, float firstWavelengthNanometres, float wavelengthStepNanometres,
        ReadOnlySpan<Vector3> matchingFunctions)
        : this(name, fieldSizeDegrees, observerAgeYears, sourceDescription, firstWavelengthNanometres,
            wavelengthStepNanometres, matchingFunctions, false, null) { }

    internal SpectralObserver(string name, float fieldSizeDegrees, float? observerAgeYears,
        string sourceDescription, float firstWavelengthNanometres, float wavelengthStepNanometres,
        ReadOnlySpan<Vector3> matchingFunctions, bool isStandard, string? dataSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDescription);
        if (!float.IsFinite(fieldSizeDegrees) || fieldSizeDegrees <= 0 || fieldSizeDegrees > 180)
            throw new ArgumentOutOfRangeException(nameof(fieldSizeDegrees));
        if (observerAgeYears is float age && (!float.IsFinite(age) || age < 0))
            throw new ArgumentOutOfRangeException(nameof(observerAgeYears));
        SpectralValidation.Grid(firstWavelengthNanometres, wavelengthStepNanometres, matchingFunctions.Length);
        foreach (Vector3 value in matchingFunctions)
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
                throw new ArgumentOutOfRangeException(nameof(matchingFunctions));
        Name = name;
        FieldSizeDegrees = fieldSizeDegrees;
        ObserverAgeYears = observerAgeYears;
        SourceDescription = sourceDescription;
        FirstWavelengthNanometres = firstWavelengthNanometres;
        WavelengthStepNanometres = wavelengthStepNanometres;
        this.matchingFunctions = matchingFunctions.ToArray();
        IsStandard = isStandard;
        DataSha256 = dataSha256;
    }

    /// <summary>Gets the observer identity supplied with the curves.</summary>
    public string Name { get; }
    /// <summary>Gets the visual field size in degrees.</summary>
    public float FieldSizeDegrees { get; }
    /// <summary>Gets the supplied observer age, or null when the source does not specify one.</summary>
    public float? ObserverAgeYears { get; }
    /// <summary>Gets whether these are unchanged published standard curves.</summary>
    public bool IsStandard { get; }
    /// <summary>Gets the curve provenance, such as a dataset DOI or measurement identifier.</summary>
    public string SourceDescription { get; }
    /// <summary>Gets the pinned source file SHA256, or null for application-supplied curves.</summary>
    public string? DataSha256 { get; }
    /// <summary>Gets the first tabulated wavelength in nanometres.</summary>
    public float FirstWavelengthNanometres { get; }
    /// <summary>Gets the wavelength spacing in nanometres.</summary>
    public float WavelengthStepNanometres { get; }
    /// <summary>Gets the final tabulated wavelength in nanometres.</summary>
    public float LastWavelengthNanometres => FirstWavelengthNanometres + WavelengthStepNanometres * (matchingFunctions.Length - 1);
    /// <summary>Gets a read-only view of the XYZ matching functions.</summary>
    public ReadOnlySpan<Vector3> MatchingFunctions => matchingFunctions;

    internal (double X, double Y, double Z) Evaluate(double wavelength)
    {
        double position = (wavelength - FirstWavelengthNanometres) / WavelengthStepNanometres;
        int index = Math.Clamp((int)Math.Floor(position), 0, matchingFunctions.Length - 2);
        double f = Math.Clamp(position - index, 0, 1);
        Vector3 a = matchingFunctions[index], b = matchingFunctions[index + 1];
        return (a.X * (1 - f) + b.X * f, a.Y * (1 - f) + b.Y * f, a.Z * (1 - f) + b.Z * f);
    }
}

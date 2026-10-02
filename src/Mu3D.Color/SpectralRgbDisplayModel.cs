using System.Numerics;

namespace Mu3D.Color;

/// <summary>Models a linear additive display using the measured SPDs of its three unit-drive primaries.</summary>
/// <remarks>
/// Experimental API. Black emission is assumed zero. Primary units must agree and their amplitudes
/// must share a calibration. This supports observer conversion only for the span of these measured
/// primaries; it is not a general transform between arbitrary spectra or a replacement for ICC.
/// </remarks>
public sealed class SpectralRgbDisplayModel
{
    /// <summary>Initializes a display model from three immutable unit-drive primary SPDs.</summary>
    public SpectralRgbDisplayModel(SpectralPowerDistribution red, SpectralPowerDistribution green,
        SpectralPowerDistribution blue, string name)
    {
        ArgumentNullException.ThrowIfNull(red);
        ArgumentNullException.ThrowIfNull(green);
        ArgumentNullException.ThrowIfNull(blue);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (red.Unit != green.Unit || red.Unit != blue.Unit)
            throw new ArgumentException("The three primaries must use the same calibrated spectral units.");
        Red = red;
        Green = green;
        Blue = blue;
        Name = name;
    }
    /// <summary>Gets the display calibration name.</summary>
    public string Name { get; }
    /// <summary>Gets the red unit-drive primary spectrum.</summary>
    public SpectralPowerDistribution Red { get; }
    /// <summary>Gets the green unit-drive primary spectrum.</summary>
    public SpectralPowerDistribution Green { get; }
    /// <summary>Gets the blue unit-drive primary spectrum.</summary>
    public SpectralPowerDistribution Blue { get; }

    /// <summary>Computes a row-vector RGB-to-XYZ matrix for the supplied observer.</summary>
    /// <remarks>Apply with <see cref="Vector3.Transform(Vector3, Matrix4x4)"/>; translation is zero.</remarks>
    public Matrix4x4 GetRgbToXyzMatrix(SpectralObserver observer,
        SpectralCoveragePolicy coveragePolicy = SpectralCoveragePolicy.RequireObserverCoverage)
    {
        Vector3 r = SpectralIntegrator.Integrate(Red, observer, coveragePolicy).Xyz;
        Vector3 g = SpectralIntegrator.Integrate(Green, observer, coveragePolicy).Xyz;
        Vector3 b = SpectralIntegrator.Integrate(Blue, observer, coveragePolicy).Xyz;
        return new(r.X, r.Y, r.Z, 0, g.X, g.Y, g.Z, 0, b.X, b.Y, b.Z, 0, 0, 0, 0, 1);
    }

    /// <summary>Computes an observer-to-observer XYZ matrix for emissions from this display alone.</summary>
    /// <remarks>
    /// The result is inverse(source RGB-to-XYZ) times destination RGB-to-XYZ in row-vector order.
    /// Ill-conditioned or linearly dependent primary responses are rejected. No appearance or
    /// white adaptation model is implied.
    /// </remarks>
    public Matrix4x4 GetObserverConversionMatrix(SpectralObserver source, SpectralObserver destination,
        SpectralCoveragePolicy coveragePolicy = SpectralCoveragePolicy.RequireObserverCoverage)
    {
        Matrix4x4 sourceMatrix = GetRgbToXyzMatrix(source, coveragePolicy);
        float sourceScale = MathF.Max(MathF.Max(MaxRow(sourceMatrix.M11, sourceMatrix.M12, sourceMatrix.M13),
            MaxRow(sourceMatrix.M21, sourceMatrix.M22, sourceMatrix.M23)), MaxRow(sourceMatrix.M31, sourceMatrix.M32, sourceMatrix.M33));
        if (sourceScale == 0 || !Matrix4x4.Invert(Divide3x3(sourceMatrix, sourceScale), out Matrix4x4 inverse))
            throw new ArgumentException("The source observer cannot distinguish these three primary responses.", nameof(source));
        float norm = Norm(Divide3x3(sourceMatrix, sourceScale)), inverseNorm = Norm(inverse);
        if (inverseNorm <= 0 || !float.IsFinite(norm * inverseNorm) || norm * inverseNorm > 1e6f)
            throw new ArgumentException("The measured primary response matrix is too ill-conditioned for FP32 observer conversion.", nameof(source));
        // Normalize before inversion so arbitrary SPD calibration cannot overflow a cubic determinant.
        Matrix4x4 result = inverse * Divide3x3(GetRgbToXyzMatrix(destination, coveragePolicy), sourceScale);
        if (!float.IsFinite(Norm(result))) throw new ArgumentException("Observer conversion overflows FP32.", nameof(destination));
        return result;
    }

    /// <summary>Integrates a nonnegative display-linear RGB drive value for the specified observer.</summary>
    public SpectralTristimulusValues Evaluate(Vector3 linearRgb, SpectralObserver observer,
        SpectralCoveragePolicy coveragePolicy = SpectralCoveragePolicy.RequireObserverCoverage)
    {
        if (!float.IsFinite(linearRgb.X) || !float.IsFinite(linearRgb.Y) || !float.IsFinite(linearRgb.Z) ||
            linearRgb.X < 0 || linearRgb.Y < 0 || linearRgb.Z < 0)
            throw new ArgumentOutOfRangeException(nameof(linearRgb), "Emitted primary powers must be finite and nonnegative.");
        return new(Vector3.Transform(linearRgb, GetRgbToXyzMatrix(observer, coveragePolicy)), observer, Red.Unit);
    }

    private static float Norm(Matrix4x4 m) => MathF.Max(MathF.Abs(m.M11) + MathF.Abs(m.M12) + MathF.Abs(m.M13),
        MathF.Max(MathF.Abs(m.M21) + MathF.Abs(m.M22) + MathF.Abs(m.M23), MathF.Abs(m.M31) + MathF.Abs(m.M32) + MathF.Abs(m.M33)));

    private static float MaxRow(float a, float b, float c) => MathF.Max(MathF.Abs(a), MathF.Max(MathF.Abs(b), MathF.Abs(c)));

    private static Matrix4x4 Divide3x3(Matrix4x4 m, float divisor) => new(
        m.M11 / divisor, m.M12 / divisor, m.M13 / divisor, 0,
        m.M21 / divisor, m.M22 / divisor, m.M23 / divisor, 0,
        m.M31 / divisor, m.M32 / divisor, m.M33 / divisor, 0,
        0, 0, 0, 1);
}

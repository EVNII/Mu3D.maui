using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.Color;

/// <summary>Controls evaluation outside a lookup table's explicitly authored input domain.</summary>
public enum ColorLutRangePolicy
{
    /// <summary>Reject out-of-domain input rather than silently clipping HDR or negative values.</summary>
    Reject,
    /// <summary>Explicitly clamp input to the table domain; output remains unclipped.</summary>
    Clamp,
}

/// <summary>Maps explicitly tagged straight-alpha linear RGB while preserving alpha.</summary>
public interface ILinearRgbTransform
{
    /// <summary>Gets the required input color-space identity.</summary>
    ColorSpaceReference SourceSpace { get; }
    /// <summary>Gets the output color-space identity.</summary>
    ColorSpaceReference DestinationSpace { get; }
    /// <summary>Transforms one color without implicitly managing alpha, gamut or display range.</summary>
    LinearRgba Transform(LinearRgba source);
}

/// <summary>
/// Immutable per-channel FP32 shaper table in explicit linear-light spaces. Each RGB column is
/// sampled independently; this table cannot represent cross-channel gamut conversion by itself.
/// </summary>
public sealed class LinearRgbLut1D : ILinearRgbTransform
{
    private readonly Vector3[] values;

    /// <summary>Copies 2 through 65536 samples and validates all metadata and finite values.</summary>
    public LinearRgbLut1D(
        IReadOnlyList<Vector3> samples,
        Vector3 domainMinimum,
        Vector3 domainMaximum,
        ColorSpaceReference sourceSpace,
        ColorSpaceReference destinationSpace,
        ColorLutRangePolicy rangePolicy = ColorLutRangePolicy.Reject)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count is < 2 or > 65536)
            throw new ArgumentOutOfRangeException(nameof(samples), "A 1D table requires 2 through 65536 samples.");
        LutValidation.Validate(domainMinimum, domainMaximum, sourceSpace, destinationSpace, rangePolicy);
        values = LutValidation.Copy(samples);
        Samples = Array.AsReadOnly(values);
        DomainMinimum = domainMinimum;
        DomainMaximum = domainMaximum;
        SourceSpace = sourceSpace;
        DestinationSpace = destinationSpace;
        RangePolicy = rangePolicy;
    }

    /// <summary>Gets the independent R/G/B sample columns in increasing input order.</summary>
    public ReadOnlyCollection<Vector3> Samples { get; }
    /// <summary>Gets the input value mapped to the first sample in each channel.</summary>
    public Vector3 DomainMinimum { get; }
    /// <summary>Gets the input value mapped to the last sample in each channel.</summary>
    public Vector3 DomainMaximum { get; }
    /// <inheritdoc />
    public ColorSpaceReference SourceSpace { get; }
    /// <inheritdoc />
    public ColorSpaceReference DestinationSpace { get; }
    /// <summary>Gets the explicitly selected input range policy.</summary>
    public ColorLutRangePolicy RangePolicy { get; }

    /// <inheritdoc />
    public LinearRgba Transform(LinearRgba source)
    {
        Vector3 p = LutValidation.Coordinates(source, SourceSpace, DomainMinimum, DomainMaximum, RangePolicy) * (values.Length - 1);
        return new LinearRgba(Sample(p.X, 0), Sample(p.Y, 1), Sample(p.Z, 2), source.Alpha, DestinationSpace);
    }

    private float Sample(float coordinate, int channel)
    {
        int first = Math.Min((int)coordinate, values.Length - 2);
        float amount = coordinate - first;
        return values[first][channel] * (1f - amount) + values[first + 1][channel] * amount;
    }
}

/// <summary>
/// Immutable, bounded FP32 RGB cube with trilinear interpolation. Input/output color identities,
/// HDR domain and out-of-domain behavior are explicit; alpha is copied unchanged.
/// </summary>
public sealed class LinearRgbLut3D : ILinearRgbTransform
{
    private readonly Vector3[] values;

    /// <summary>Copies a cube in R-fastest, then G, then B order; size must be 2 through 65.</summary>
    public LinearRgbLut3D(
        int size,
        IReadOnlyList<Vector3> samples,
        Vector3 domainMinimum,
        Vector3 domainMaximum,
        ColorSpaceReference sourceSpace,
        ColorSpaceReference destinationSpace,
        ColorLutRangePolicy rangePolicy = ColorLutRangePolicy.Reject)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (size is < 2 or > 65)
            throw new ArgumentOutOfRangeException(nameof(size), "A 3D table edge requires 2 through 65 samples.");
        if (samples.Count != size * size * size)
            throw new ArgumentException("The table must contain exactly size cubed samples.", nameof(samples));
        LutValidation.Validate(domainMinimum, domainMaximum, sourceSpace, destinationSpace, rangePolicy);
        values = LutValidation.Copy(samples);
        Samples = Array.AsReadOnly(values);
        Size = size;
        DomainMinimum = domainMinimum;
        DomainMaximum = domainMaximum;
        SourceSpace = sourceSpace;
        DestinationSpace = destinationSpace;
        RangePolicy = rangePolicy;
    }

    /// <summary>Gets the number of samples along each axis.</summary>
    public int Size { get; }
    /// <summary>Gets samples at index R + Size * (G + Size * B).</summary>
    public ReadOnlyCollection<Vector3> Samples { get; }
    /// <summary>Gets the minimum input RGB represented by the cube.</summary>
    public Vector3 DomainMinimum { get; }
    /// <summary>Gets the maximum input RGB represented by the cube.</summary>
    public Vector3 DomainMaximum { get; }
    /// <inheritdoc />
    public ColorSpaceReference SourceSpace { get; }
    /// <inheritdoc />
    public ColorSpaceReference DestinationSpace { get; }
    /// <summary>Gets the explicitly selected input range policy.</summary>
    public ColorLutRangePolicy RangePolicy { get; }

    /// <inheritdoc />
    public LinearRgba Transform(LinearRgba source)
    {
        Vector3 p = LutValidation.Coordinates(source, SourceSpace, DomainMinimum, DomainMaximum, RangePolicy) * (Size - 1);
        int r = Math.Min((int)p.X, Size - 2);
        int g = Math.Min((int)p.Y, Size - 2);
        int b = Math.Min((int)p.Z, Size - 2);
        float fr = p.X - r, fg = p.Y - g, fb = p.Z - b;
        Vector3 lower = Mix(Mix(Get(r, g, b), Get(r + 1, g, b), fr),
            Mix(Get(r, g + 1, b), Get(r + 1, g + 1, b), fr), fg);
        Vector3 upper = Mix(Mix(Get(r, g, b + 1), Get(r + 1, g, b + 1), fr),
            Mix(Get(r, g + 1, b + 1), Get(r + 1, g + 1, b + 1), fr), fg);
        Vector3 result = Mix(lower, upper, fb);
        return new LinearRgba(result.X, result.Y, result.Z, source.Alpha, DestinationSpace);
    }

    /// <summary>
    /// Samples an explicit transform on this bounded domain. This is an approximation: callers
    /// select resolution and validate off-grid error against their reference transform.
    /// </summary>
    public static LinearRgbLut3D Bake(
        int size,
        Vector3 domainMinimum,
        Vector3 domainMaximum,
        ColorSpaceReference sourceSpace,
        ColorSpaceReference destinationSpace,
        Func<LinearRgba, LinearRgba> transform,
        ColorLutRangePolicy rangePolicy = ColorLutRangePolicy.Reject)
    {
        ArgumentNullException.ThrowIfNull(transform);
        if (size is < 2 or > 65) throw new ArgumentOutOfRangeException(nameof(size));
        LutValidation.Validate(domainMinimum, domainMaximum, sourceSpace, destinationSpace, rangePolicy);
        Vector3[] samples = new Vector3[size * size * size];
        for (int b = 0; b < size; b++)
        for (int g = 0; g < size; g++)
        for (int r = 0; r < size; r++)
        {
            Vector3 t = new Vector3(r, g, b) / (size - 1);
            Vector3 p = domainMinimum * (Vector3.One - t) + domainMaximum * t;
            LinearRgba output = transform(new LinearRgba(p.X, p.Y, p.Z, 1f, sourceSpace));
            if (output.ColorSpace != destinationSpace || output.Alpha != 1f)
                throw new InvalidOperationException("A baked transform must return the requested destination space and preserve alpha.");
            samples[r + size * (g + size * b)] = new(output.Red, output.Green, output.Blue);
        }
        return new LinearRgbLut3D(size, samples, domainMinimum, domainMaximum, sourceSpace, destinationSpace, rangePolicy);
    }

    private Vector3 Get(int r, int g, int b) => values[r + Size * (g + Size * b)];
    private static Vector3 Mix(Vector3 a, Vector3 b, float t) => a * (1f - t) + b * t;
}

internal static class LutValidation
{
    internal static void Validate(Vector3 min, Vector3 max, ColorSpaceReference source,
        ColorSpaceReference destination, ColorLutRangePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (!source.IsLinear || !destination.IsLinear)
            throw new ArgumentException("Linear RGB tables require explicitly linear input and output identities.");
        if (!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        for (int i = 0; i < 3; i++)
            if (!float.IsFinite(min[i]) || !float.IsFinite(max[i]) || max[i] <= min[i] || !float.IsFinite(max[i] - min[i]))
                throw new ArgumentOutOfRangeException(nameof(max), "Each domain extent must be positive and finite.");
    }

    internal static Vector3[] Copy(IReadOnlyList<Vector3> samples)
    {
        Vector3[] result = new Vector3[samples.Count];
        for (int i = 0; i < result.Length; i++)
        {
            Vector3 p = samples[i];
            if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))
                throw new ArgumentException("LUT samples must be finite.", nameof(samples));
            result[i] = p;
        }
        return result;
    }

    internal static Vector3 Coordinates(LinearRgba color, ColorSpaceReference source, Vector3 min,
        Vector3 max, ColorLutRangePolicy policy)
    {
        if (color.ColorSpace != source)
            throw new ArgumentException("The input color does not carry the table's source color-space identity.", nameof(color));
        Vector3 p = new(color.Red, color.Green, color.Blue);
        if (policy == ColorLutRangePolicy.Reject &&
            (p.X < min.X || p.Y < min.Y || p.Z < min.Z || p.X > max.X || p.Y > max.Y || p.Z > max.Z))
            throw new ArgumentOutOfRangeException(nameof(color), "Color is outside the authored LUT domain.");
        return (Vector3.Clamp(p, min, max) - min) / (max - min);
    }
}

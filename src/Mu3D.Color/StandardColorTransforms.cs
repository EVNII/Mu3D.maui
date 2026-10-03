namespace Mu3D.Color;

/// <summary>Adapts tagged standard linear RGB to D50 CIE Lab for <see cref="ColorBatch"/>.</summary>
/// <remarks>Preserves straight alpha and extended values without clipping or display mapping.</remarks>
public readonly struct LinearRgbToLabTransform : IColorTransform<LinearRgba, CieLabColor>
{
    /// <inheritdoc />
    public CieLabColor Transform(LinearRgba source) => PerceptualColorConverter.ToLab(source);
}

/// <summary>Adapts D50 CIE Lab to an explicitly selected standard linear RGB space.</summary>
/// <remarks>Preserves straight alpha and extended values. A default-initialized instance has no destination and cannot convert.</remarks>
public readonly struct LabToLinearRgbTransform : IColorTransform<CieLabColor, LinearRgba>
{
    private readonly StandardRgbColorSpaceReference? destination;

    /// <summary>Creates a conversion to the specified linear RGB identity.</summary>
    /// <param name="destination">The destination primaries and white-point identity.</param>
    /// <exception cref="ArgumentNullException">The destination is null.</exception>
    public LabToLinearRgbTransform(StandardRgbColorSpaceReference destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        this.destination = destination;
    }

    /// <summary>Gets the explicitly selected destination linear RGB identity.</summary>
    /// <exception cref="InvalidOperationException">The transform was default-initialized without a destination.</exception>
    public StandardRgbColorSpaceReference Destination => destination
        ?? throw new InvalidOperationException("Construct the transform with an explicit destination color space.");

    /// <inheritdoc />
    public LinearRgba Transform(CieLabColor source) => PerceptualColorConverter.FromLab(source, Destination);
}

/// <summary>Adapts tagged standard linear RGB to D65 Oklab for <see cref="ColorBatch"/>.</summary>
/// <remarks>Preserves straight alpha and signed extended values without clipping or display mapping.</remarks>
public readonly struct LinearRgbToOklabTransform : IColorTransform<LinearRgba, OklabColor>
{
    /// <inheritdoc />
    public OklabColor Transform(LinearRgba source) => PerceptualColorConverter.ToOklab(source);
}

/// <summary>Adapts D65 Oklab to an explicitly selected standard linear RGB space.</summary>
/// <remarks>Preserves straight alpha and extended values. A default-initialized instance has no destination and cannot convert.</remarks>
public readonly struct OklabToLinearRgbTransform : IColorTransform<OklabColor, LinearRgba>
{
    private readonly StandardRgbColorSpaceReference? destination;

    /// <summary>Creates a conversion to the specified linear RGB identity.</summary>
    /// <param name="destination">The destination primaries and white-point identity.</param>
    /// <exception cref="ArgumentNullException">The destination is null.</exception>
    public OklabToLinearRgbTransform(StandardRgbColorSpaceReference destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        this.destination = destination;
    }

    /// <summary>Gets the explicitly selected destination linear RGB identity.</summary>
    /// <exception cref="InvalidOperationException">The transform was default-initialized without a destination.</exception>
    public StandardRgbColorSpaceReference Destination => destination
        ?? throw new InvalidOperationException("Construct the transform with an explicit destination color space.");

    /// <inheritdoc />
    public LinearRgba Transform(OklabColor source) => PerceptualColorConverter.FromOklab(source, Destination);
}

/// <summary>Adapts encoded RGB to floating-point luma/chroma using an explicitly selected matrix.</summary>
/// <remarks>Preserves the source RGB encoding and alpha; selecting a matrix does not change primaries or encoding.</remarks>
public readonly struct EncodedRgbToLumaChromaTransform : IColorTransform<StandardEncodedRgba, LumaChromaColor>
{
    /// <summary>Creates a conversion using the specified luma/chroma matrix.</summary>
    /// <param name="model">The luma coefficient and color-difference scaling identity.</param>
    /// <exception cref="ArgumentOutOfRangeException">The model is not a supported matrix identity.</exception>
    public EncodedRgbToLumaChromaTransform(LumaChromaModel model)
    {
        _ = LumaChromaConverter.Coefficients(model);
        Model = model;
    }

    /// <summary>Gets the selected luma/chroma matrix identity; the default instance uses BT.601 YUV.</summary>
    public LumaChromaModel Model { get; }

    /// <inheritdoc />
    public LumaChromaColor Transform(StandardEncodedRgba source) => LumaChromaConverter.FromRgb(source, Model);
}

/// <summary>Adapts floating-point luma/chroma back to its tagged encoded RGB identity.</summary>
/// <remarks>Uses the coordinate's matrix and RGB encoding, preserves alpha and applies no clipping.</remarks>
public readonly struct LumaChromaToEncodedRgbTransform : IColorTransform<LumaChromaColor, StandardEncodedRgba>
{
    /// <inheritdoc />
    public StandardEncodedRgba Transform(LumaChromaColor source) => LumaChromaConverter.ToRgb(source);
}

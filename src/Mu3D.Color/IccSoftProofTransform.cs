namespace Mu3D.Color;

/// <summary>Selects the explicit treatment of out-of-range encoded device RGB at soft-proof boundaries.</summary>
public enum IccSoftProofRangePolicy
{
    /// <summary>Reject encoded values outside [0, 1].</summary>
    Reject,
    /// <summary>Clamp a temporary encoded sample to [0, 1] and report that clipping occurred.</summary>
    Clamp,
}

/// <summary>Specifies ICC proof and display intents, black-point compensation and encoded range policy.</summary>
/// <remarks>Profile-defined perceptual mapping or LUT-domain limits remain part of the ICC transform.</remarks>
public sealed record IccSoftProofOptions
{
    /// <summary>Gets the intent used to render working RGB into the proof device.</summary>
    public IccRenderingIntent ProofIntent { get; init; } = IccRenderingIntent.MediaRelativeColorimetric;
    /// <summary>Gets the policy for proof-device RGB outside [0, 1].</summary>
    public IccSoftProofRangePolicy ProofRangePolicy { get; init; } = IccSoftProofRangePolicy.Reject;
    /// <summary>Gets an explicit working-to-proof black-point mapping, or null for none.</summary>
    public IccBlackPointCompensation? ProofBlackPointCompensation { get; init; }
    /// <summary>Gets the final display intent; absolute colorimetry preserves simulated paper white by default.</summary>
    public IccRenderingIntent DisplayIntent { get; init; } = IccRenderingIntent.IccAbsoluteColorimetric;
    /// <summary>Gets the policy for final encoded display RGB outside [0, 1].</summary>
    public IccSoftProofRangePolicy DisplayRangePolicy { get; init; } = IccSoftProofRangePolicy.Reject;
}

/// <summary>Stores a linear soft-proof result and reports explicit proof-boundary clipping.</summary>
public readonly record struct IccSoftProofLinearResult
{
    /// <summary>Initializes a tagged linear proof result.</summary>
    public IccSoftProofLinearResult(LinearRgba color, bool proofWasClipped)
    { Color = color; ProofWasClipped = proofWasClipped; }
    /// <summary>Gets the simulated proof appearance in the requested linear RGB space.</summary>
    public LinearRgba Color { get; }
    /// <summary>Gets whether the explicit proof-device range policy clamped a component.</summary>
    public bool ProofWasClipped { get; }
}

/// <summary>Stores an encoded display result and reports explicit boundary clipping.</summary>
public readonly record struct IccSoftProofDisplayResult
{
    /// <summary>Initializes an encoded display result and its boundary clipping flags.</summary>
    public IccSoftProofDisplayResult(IccEncodedRgba color, bool proofWasClipped, bool displayWasClipped)
    { Color = color; ProofWasClipped = proofWasClipped; DisplayWasClipped = displayWasClipped; }
    /// <summary>Gets the encoded result tagged with the display profile.</summary>
    public IccEncodedRgba Color { get; }
    /// <summary>Gets whether proof-device RGB was explicitly clamped.</summary>
    public bool ProofWasClipped { get; }
    /// <summary>Gets whether display-device RGB was explicitly clamped.</summary>
    public bool DisplayWasClipped { get; }
}

/// <summary>Compiles an immutable RGB ICC soft-proof chain with absolute proof-media simulation.</summary>
/// <remarks>
/// Working linear RGB is rendered to the proof device, then decoded with ICC-absolute intent so
/// paper/media white and black are retained. It can be returned in a standard linear RGB space or
/// rendered into an optional display ICC profile. The source is never changed. Only the existing
/// supported RGB ICC profile types are accepted; CMYK and spectral printer models are unsupported.
/// Clipping flags report explicit encoded-boundary clamping, not gamut mapping inside profile LUTs.
/// Black-point compensation is confined to the initial working-to-proof rendering; it is not
/// reapplied to the subsequent absolute paper/black simulation. A non-absolute display intent
/// explicitly requests further display rendering and may change the simulated appearance.
/// HDR tone mapping and conversion of scene exposure into print-relative light are caller-owned.
/// </remarks>
public sealed class IccSoftProofTransform
{
    private readonly IccLinearToRgbTransform proofOutput;
    private readonly IccRgbToLinearTransform proofAbsoluteInput;
    private readonly IccLinearToRgbTransform? displayOutput;

    /// <summary>Compiles the selected proof chain and optionally a final display-profile transform.</summary>
    /// <param name="proofProfile">RGB proof/output device profile with usable forward and inverse transforms.</param>
    /// <param name="options">Explicit immutable rendering, black-point and range settings.</param>
    /// <param name="displayProfile">Optional RGB display profile; required by <see cref="TransformToDisplay"/>.</param>
    public IccSoftProofTransform(IccProfile proofProfile, IccSoftProofOptions options, IccProfile? displayProfile = null)
    {
        ArgumentNullException.ThrowIfNull(proofProfile);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options.ProofIntent, options.ProofRangePolicy, options.ProofBlackPointCompensation);
        Validate(options.DisplayIntent, options.DisplayRangePolicy, null);
        ProofProfile = proofProfile;
        DisplayProfile = displayProfile;
        Options = options with { };
        proofOutput = new(proofProfile, options.ProofIntent);
        proofAbsoluteInput = new(proofProfile, IccRenderingIntent.IccAbsoluteColorimetric);
        if (displayProfile is not null) displayOutput = new(displayProfile, options.DisplayIntent);
    }

    /// <summary>Gets the immutable proof profile.</summary>
    public IccProfile ProofProfile { get; }
    /// <summary>Gets the optional immutable display profile.</summary>
    public IccProfile? DisplayProfile { get; }
    /// <summary>Gets the copied immutable settings used to compile the transforms.</summary>
    public IccSoftProofOptions Options { get; }

    /// <summary>Simulates proof media and returns an explicitly tagged linear RGB sample without display clipping.</summary>
    public IccSoftProofLinearResult TransformToLinear(LinearRgba source, StandardRgbColorSpaceReference destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        (LinearRgba proof, bool clipped) = Simulate(source);
        return new(StandardLinearRgbConverter.Convert(proof, destination), clipped);
    }

    /// <summary>Simulates proof media and converts it to the compiled display profile.</summary>
    public IccSoftProofDisplayResult TransformToDisplay(LinearRgba source)
    {
        if (displayOutput is null)
            throw new InvalidOperationException("An ICC display profile must be supplied when compiling this proof transform.");
        (LinearRgba proof, bool proofClipped) = Simulate(source);
        IccEncodedRgba encoded = displayOutput.Transform(proof);
        (IccEncodedRgba bounded, bool clipped) = Bound(encoded, Options.DisplayRangePolicy);
        return new(bounded, proofClipped, clipped);
    }

    private (LinearRgba Color, bool Clipped) Simulate(LinearRgba source)
    {
        LinearRgba working = StandardLinearRgbConverter.Convert(source, StandardColorSpaces.LinearProPhotoRgb);
        IccEncodedRgba encoded = proofOutput.Transform(ApplyBlackPoint(working, Options.ProofBlackPointCompensation));
        (IccEncodedRgba bounded, bool clipped) = Bound(encoded, Options.ProofRangePolicy);
        LinearRgba simulation = proofAbsoluteInput.TransformEncodedRgb(bounded.Red, bounded.Green, bounded.Blue,
            bounded.Alpha, StandardColorSpaces.LinearProPhotoRgb);
        return (simulation, clipped);
    }

    private static void Validate(IccRenderingIntent intent, IccSoftProofRangePolicy range, IccBlackPointCompensation? compensation)
    {
        if (!Enum.IsDefined(intent)) throw new ArgumentOutOfRangeException(nameof(intent));
        if (!Enum.IsDefined(range)) throw new ArgumentOutOfRangeException(nameof(range));
        if (intent == IccRenderingIntent.IccAbsoluteColorimetric && compensation is not null)
            throw new ArgumentException("Black-point compensation is media-relative and cannot be combined with absolute intent.");
    }

    private static (IccEncodedRgba Color, bool Clipped) Bound(IccEncodedRgba color, IccSoftProofRangePolicy policy)
    {
        bool outside = color.Red is < 0 or > 1 || color.Green is < 0 or > 1 || color.Blue is < 0 or > 1;
        if (!outside) return (color, false);
        if (policy == IccSoftProofRangePolicy.Reject)
            throw new InvalidOperationException("The ICC soft-proof chain produced encoded device RGB outside [0, 1]; choose an explicit clamp policy or a suitable profile/intent.");
        return (new(Math.Clamp(color.Red, 0, 1), Math.Clamp(color.Green, 0, 1), Math.Clamp(color.Blue, 0, 1), color.Alpha, color.Profile), true);
    }

    private static LinearRgba ApplyBlackPoint(LinearRgba working, IccBlackPointCompensation? value)
    {
        if (value is not IccBlackPointCompensation compensation) return working;
        (float x, float y, float z) = StandardLinearRgbConverter.ToXyzD50(working, StandardColorSpaces.LinearProPhotoRgb);
        float sourceBlack = DecodeLightness(compensation.SourceLightness), destinationBlack = DecodeLightness(compensation.DestinationLightness);
        float scale = (1 - destinationBlack) / (1 - sourceBlack), offset = 1 - scale;
        return StandardLinearRgbConverter.FromXyzD50((x / 0.9642f * scale + offset) * 0.9642f,
            y * scale + offset, (z / 0.8249f * scale + offset) * 0.8249f, working.Alpha, StandardColorSpaces.LinearProPhotoRgb);
    }

    private static float DecodeLightness(float lightness) => lightness <= 8
        ? lightness * MathF.Pow(24f / 116, 3) / 8 : MathF.Pow((lightness + 16) / 116, 3);
}

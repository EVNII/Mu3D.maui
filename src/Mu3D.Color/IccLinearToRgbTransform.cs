namespace Mu3D.Color;

/// <summary>
/// Converts a standard linear-light working-space color through one selected ICC PCS-to-device
/// transform into explicitly profile-tagged encoded RGB components.
/// </summary>
/// <remarks>
/// The current output slice supports RGB-ended ICC v4 <c>B2D0</c>, <c>B2D1</c> and
/// <c>B2D2</c> <c>multiProcessElementsType</c> transforms with bounded
/// one-to-sixteen-channel intermediate elements, with matching <c>B2A0</c>,
/// <c>B2A1</c> or <c>B2A2</c> legacy LUT and <c>lutBToAType</c> fallback. Legacy PCS XYZ input
/// matrices are applied before their input tables; device-to-PCS and Lab matrices remain identity.
/// ICC-absolute output uses
/// direct <c>B2D3</c> when available or converts through the target media white before the relative
/// path. Black-point compensation remains unsupported. Floating B2D output components are not
/// clipped; normalized LUT domains follow their ICC-defined range.
/// </remarks>
public sealed class IccLinearToRgbTransform
{
    private readonly IccProfile profile;
    private readonly IccMultiProcessElementsTransform? multiProcess;
    private readonly IccRgbToLinearTransform.PcsToRgbTransform? legacyOrModernLut;
    private readonly IccRgbToLinearTransform.PcsScale? absoluteToRelativePcsScale;

    /// <summary>Initializes and validates a target-profile transform for one rendering intent.</summary>
    /// <param name="profile">The immutable RGB ICC v4 target profile.</param>
    /// <param name="renderingIntent">The requested colour-rendering objective.</param>
    public IccLinearToRgbTransform(
        IccProfile profile,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!Enum.IsDefined(renderingIntent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderingIntent),
                renderingIntent,
                "Unknown ICC rendering intent.");
        }
        IccRenderingIntent selectedIntent = renderingIntent;
        if (renderingIntent == IccRenderingIntent.IccAbsoluteColorimetric)
        {
            try
            {
                if (IccMultiProcessElementsTransform.TryCreate(
                    profile,
                    "B2D3",
                    out IccMultiProcessElementsTransform? absoluteMultiProcess))
                {
                    multiProcess = absoluteMultiProcess;
                    this.profile = profile;
                    RenderingIntent = selectedIntent;
                    return;
                }
            }
            catch (NotSupportedException)
            {
                // ICC 8.10 permits an unsupported BToD chain to use the corresponding BToA path.
            }
            IccRgbToLinearTransform.PcsScale relativeToAbsolute =
                IccRgbToLinearTransform.ReadMediaRelativeToAbsolutePcsScale(profile);
            absoluteToRelativePcsScale = new IccRgbToLinearTransform.PcsScale(
                1f / relativeToAbsolute.X,
                1f / relativeToAbsolute.Y,
                1f / relativeToAbsolute.Z);
            renderingIntent = IccRenderingIntent.MediaRelativeColorimetric;
        }

        string tagSignature = renderingIntent switch
        {
            IccRenderingIntent.Perceptual => "B2D0",
            IccRenderingIntent.MediaRelativeColorimetric => "B2D1",
            IccRenderingIntent.Saturation => "B2D2",
            _ => throw new InvalidOperationException("Unexpected validated ICC rendering intent."),
        };
        bool useFallback = false;
        try
        {
            if (IccMultiProcessElementsTransform.TryCreate(
                profile,
                tagSignature,
                out IccMultiProcessElementsTransform? parsed))
            {
                multiProcess = parsed;
            }
            else
            {
                useFallback = true;
            }
        }
        catch (NotSupportedException)
        {
            useFallback = true;
        }
        if (useFallback)
        {
            string fallbackSignature = renderingIntent switch
            {
                IccRenderingIntent.Perceptual => "B2A0",
                IccRenderingIntent.MediaRelativeColorimetric => "B2A1",
                IccRenderingIntent.Saturation => "B2A2",
                _ => throw new InvalidOperationException(
                    "Unexpected validated ICC rendering intent."),
            };
            if (!IccRgbToLinearTransform.TryCreatePcsToRgbTransform(
                profile,
                fallbackSignature,
                out legacyOrModernLut))
            {
                throw new NotSupportedException(
                    $"ICC profile contains neither a supported {tagSignature} nor {fallbackSignature} transform.");
            }
        }
        if (multiProcess is null && legacyOrModernLut is null)
        {
            throw new NotSupportedException(
                $"ICC {tagSignature} and its legacy fallback are unsupported.");
        }
        this.profile = profile;
        RenderingIntent = selectedIntent;
    }

    /// <summary>Gets the application-selected rendering intent used by this output transform.</summary>
    public IccRenderingIntent RenderingIntent { get; }

    /// <summary>Gets the target ICC profile that defines the returned encoded RGB components.</summary>
    public IccProfile Profile => profile;

    /// <summary>Transforms one explicitly tagged standard linear-light color to target encoded RGB.</summary>
    /// <param name="source">The finite, unpremultiplied source color.</param>
    /// <returns>An unclipped encoded RGB value carrying the target profile identity.</returns>
    public IccEncodedRgba Transform(LinearRgba source)
    {
        if (source.ColorSpace is not StandardRgbColorSpaceReference sourceSpace)
        {
            throw new NotSupportedException(
                "ICC output currently requires a built-in standard linear RGB source space.");
        }
        (float x, float y, float z) = StandardLinearRgbConverter.ToXyzD50(source, sourceSpace);
        if (absoluteToRelativePcsScale is IccRgbToLinearTransform.PcsScale scale)
        {
            x *= scale.X;
            y *= scale.Y;
            z *= scale.Z;
        }
        (float red, float green, float blue) = multiProcess is not null
            ? multiProcess.TransformPcsToEncodedRgb(x, y, z)
            : legacyOrModernLut!(x, y, z);
        return new IccEncodedRgba(red, green, blue, source.Alpha, profile);
    }
}

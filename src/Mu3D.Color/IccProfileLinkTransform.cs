namespace Mu3D.Color;

/// <summary>
/// Links one RGB ICC source profile to one RGB ICC target profile through PCS XYZ D50.
/// </summary>
/// <remarks>
/// Black-point compensation is optional and explicit. When supplied, it follows the ICC/ISO
/// media-relative neutral-axis scale-and-offset procedure. Use
/// <see cref="CreateWithAutomaticBlackPointCompensation"/> to estimate the RGB profile black points
/// with <see cref="IccBlackPointEstimator"/> before creating the link.
/// </remarks>
public sealed class IccProfileLinkTransform
{
    private readonly IccRgbToLinearTransform sourceTransform;
    private readonly IccLinearToRgbTransform destinationTransform;
    private readonly IccBlackPointCompensation? blackPointCompensation;

    /// <summary>Initializes a source-to-destination ICC profile link.</summary>
    /// <param name="sourceProfile">The immutable source RGB profile.</param>
    /// <param name="destinationProfile">The immutable destination RGB profile.</param>
    /// <param name="renderingIntent">The rendering intent applied on both profile sides.</param>
    /// <param name="blackPointCompensation">
    /// Explicit source and destination black points, or null to preserve the unadjusted PCS.
    /// </param>
    public IccProfileLinkTransform(
        IccProfile sourceProfile,
        IccProfile destinationProfile,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric,
        IccBlackPointCompensation? blackPointCompensation = null)
    {
        ArgumentNullException.ThrowIfNull(sourceProfile);
        ArgumentNullException.ThrowIfNull(destinationProfile);
        if (blackPointCompensation is not null &&
            renderingIntent == IccRenderingIntent.IccAbsoluteColorimetric)
        {
            throw new ArgumentException(
                "Black-point compensation operates on media-relative PCS and cannot be combined " +
                "with ICC-absolute intent.",
                nameof(blackPointCompensation));
        }
        sourceTransform = new IccRgbToLinearTransform(sourceProfile, renderingIntent);
        destinationTransform = new IccLinearToRgbTransform(destinationProfile, renderingIntent);
        this.blackPointCompensation = blackPointCompensation;
        SourceProfile = sourceProfile;
        DestinationProfile = destinationProfile;
        RenderingIntent = renderingIntent;
    }

    /// <summary>Gets the source ICC profile.</summary>
    public IccProfile SourceProfile { get; }

    /// <summary>Gets the destination ICC profile.</summary>
    public IccProfile DestinationProfile { get; }

    /// <summary>Gets the rendering intent applied by this link.</summary>
    public IccRenderingIntent RenderingIntent { get; }

    /// <summary>Gets the explicit black-point mapping, or null when compensation is disabled.</summary>
    public IccBlackPointCompensation? BlackPointCompensation => blackPointCompensation;

    /// <summary>Creates a profile link with automatically estimated ISO 18619 RGB black points.</summary>
    /// <param name="sourceProfile">The immutable source RGB profile.</param>
    /// <param name="destinationProfile">The immutable destination RGB profile.</param>
    /// <param name="renderingIntent">Perceptual, media-relative colorimetric or saturation intent.</param>
    /// <returns>A profile link carrying the estimated black-point compensation mapping.</returns>
    public static IccProfileLinkTransform CreateWithAutomaticBlackPointCompensation(
        IccProfile sourceProfile,
        IccProfile destinationProfile,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric)
    {
        IccBlackPointCompensation compensation = IccBlackPointEstimator.Estimate(
            sourceProfile,
            destinationProfile,
            renderingIntent);
        return new IccProfileLinkTransform(
            sourceProfile,
            destinationProfile,
            renderingIntent,
            compensation);
    }

    /// <summary>Transforms one unpremultiplied source-profile RGB sample to target-profile RGB.</summary>
    /// <param name="red">The encoded source red component from zero through one.</param>
    /// <param name="green">The encoded source green component from zero through one.</param>
    /// <param name="blue">The encoded source blue component from zero through one.</param>
    /// <param name="alpha">The unpremultiplied alpha component from zero through one.</param>
    /// <returns>An encoded RGB value carrying the destination profile identity.</returns>
    public IccEncodedRgba TransformEncodedRgb(float red, float green, float blue, float alpha)
    {
        LinearRgba working = sourceTransform.TransformEncodedRgb(
            red,
            green,
            blue,
            alpha,
            StandardColorSpaces.LinearProPhotoRgb);
        if (blackPointCompensation is not IccBlackPointCompensation compensation)
        {
            return destinationTransform.Transform(working);
        }

        (float x, float y, float z) = StandardLinearRgbConverter.ToXyzD50(
            working,
            StandardColorSpaces.LinearProPhotoRgb);
        float sourceBlackY = DecodeLightness(compensation.SourceLightness);
        float destinationBlackY = DecodeLightness(compensation.DestinationLightness);
        float scale = (1f - destinationBlackY) / (1f - sourceBlackY);
        float offset = 1f - scale;
        x = ((x / 0.9642f) * scale + offset) * 0.9642f;
        y = y * scale + offset;
        z = ((z / 0.8249f) * scale + offset) * 0.8249f;
        LinearRgba compensated = StandardLinearRgbConverter.FromXyzD50(
            x,
            y,
            z,
            alpha,
            StandardColorSpaces.LinearProPhotoRgb);
        return destinationTransform.Transform(compensated);
    }

    private static float DecodeLightness(float lightness)
    {
        const float transition = 8f;
        float transitionValue = MathF.Pow((transition + 16f) / 116f, 3f);
        return lightness <= transition
            ? lightness * transitionValue / transition
            : MathF.Pow((lightness + 16f) / 116f, 3f);
    }
}

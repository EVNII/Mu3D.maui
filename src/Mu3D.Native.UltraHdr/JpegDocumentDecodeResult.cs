using Mu3D.Color;

namespace Mu3D.Native.UltraHdr;

/// <summary>
/// Describes an ordinary JPEG decoded into an explicit linear working space while retaining its
/// source ICC identity.
/// </summary>
public sealed class JpegDocumentDecodeResult
{
    /// <summary>Initializes one immutable document-color decode result.</summary>
    /// <param name="image">The transformed linear FP32 image.</param>
    /// <param name="embeddedIccProfile">The source profile, or null when the JPEG was untagged.</param>
    /// <param name="assumedSrgb">Whether the untagged source was decoded using the sRGB convention.</param>
    /// <param name="appliedRenderingIntent">The selected ICC intent, or null for an untagged JPEG.</param>
    public JpegDocumentDecodeResult(
        LinearRgbaImage image,
        IccProfile? embeddedIccProfile,
        bool assumedSrgb,
        IccRenderingIntent? appliedRenderingIntent)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (assumedSrgb && embeddedIccProfile is not null)
        {
            throw new ArgumentException(
                "A JPEG with an embedded ICC profile cannot also be reported as assumed sRGB.",
                nameof(assumedSrgb));
        }
        if ((embeddedIccProfile is null) != (appliedRenderingIntent is null))
        {
            throw new ArgumentException(
                "An applied ICC rendering intent must be reported exactly when a profile was applied.",
                nameof(appliedRenderingIntent));
        }

        Image = image;
        EmbeddedIccProfile = embeddedIccProfile;
        AssumedSrgb = assumedSrgb;
        AppliedRenderingIntent = appliedRenderingIntent;
    }

    /// <summary>Gets the decoded linear FP32 image in the requested destination space.</summary>
    public LinearRgbaImage Image { get; }

    /// <summary>Gets the validated embedded ICC profile, or null for an untagged JPEG.</summary>
    public IccProfile? EmbeddedIccProfile { get; }

    /// <summary>Gets whether an untagged source was interpreted as encoded sRGB.</summary>
    public bool AssumedSrgb { get; }

    /// <summary>Gets the selected ICC rendering intent, or null when no profile was present.</summary>
    public IccRenderingIntent? AppliedRenderingIntent { get; }
}

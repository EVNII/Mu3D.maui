using Mu3D.Color;

namespace Mu3D.Graphics;

/// <summary>Identifies a presentation scheduling mode without exposing a backend ABI.</summary>
public enum SurfacePresentMode
{
    /// <summary>Vertical-synchronised FIFO presentation.</summary>
    Fifo,

    /// <summary>FIFO presentation that may tear after missing a refresh deadline.</summary>
    FifoRelaxed,

    /// <summary>Immediate presentation.</summary>
    Immediate,

    /// <summary>Low-latency mailbox presentation.</summary>
    Mailbox,

    /// <summary>A backend mode not yet represented by Mu3D.</summary>
    Unknown,
}

/// <summary>Identifies how a presentation surface composites alpha.</summary>
public enum SurfaceAlphaMode
{
    /// <summary>The platform selects the alpha mode.</summary>
    Automatic,

    /// <summary>The surface is opaque.</summary>
    Opaque,

    /// <summary>Color channels contain premultiplied alpha.</summary>
    Premultiplied,

    /// <summary>Color channels contain straight alpha.</summary>
    Unpremultiplied,

    /// <summary>The surface inherits its platform alpha behavior.</summary>
    Inherit,

    /// <summary>A backend mode not yet represented by Mu3D.</summary>
    Unknown,
}

/// <summary>
/// Describes the presentation color encodings supported by one surface format.
/// </summary>
/// <param name="Format">The presentation texture format.</param>
/// <param name="ColorEncodings">
/// Color-space and transfer encodings that the compositor can interpret for the format.
/// </param>
/// <remarks>
/// A format alone does not prove HDR or wide-gamut presentation. Backends that expose explicit
/// format/color-space capability pairs populate this value; older backends may expose only
/// <see cref="SurfaceCapabilities.Formats"/>.
/// </remarks>
public sealed record SurfaceFormatCapability(
    PresentationFormat Format,
    IReadOnlyList<ColorEncoding> ColorEncodings);

/// <summary>Reports formats and modes advertised for a concrete native surface.</summary>
/// <param name="Formats">Presentation formats known to Mu3D.</param>
/// <param name="PresentModes">Available presentation scheduling modes.</param>
/// <param name="AlphaModes">Available compositor alpha modes.</param>
/// <param name="SupportsRgba16Float">Whether the surface directly advertises RGBA16Float.</param>
/// <param name="UnmappedBackendFormats">Backend format names not yet mapped by Mu3D.</param>
public sealed record SurfaceCapabilities(
    IReadOnlyList<PresentationFormat> Formats,
    IReadOnlyList<SurfacePresentMode> PresentModes,
    IReadOnlyList<SurfaceAlphaMode> AlphaModes,
    bool SupportsRgba16Float,
    IReadOnlyList<string> UnmappedBackendFormats)
{
    /// <summary>
    /// Gets explicit format/color-space capability pairs reported by the backend.
    /// </summary>
    /// <remarks>
    /// An empty list means that the backend exposes only format capabilities. It does not mean
    /// that every format supports every color encoding. Mu3D's pinned wgpu-native v29 backend uses
    /// this legacy form; a future backend with paired capability reporting populates this list.
    /// </remarks>
    public IReadOnlyList<SurfaceFormatCapability> FormatCapabilities { get; init; } = [];

    /// <summary>Gets the explicit color encodings advertised for a presentation format.</summary>
    /// <param name="format">The format to inspect.</param>
    /// <returns>
    /// A de-duplicated snapshot of advertised encodings. The result is empty when the backend did
    /// not report explicit format/color-space pairs or did not advertise the format.
    /// </returns>
    public IReadOnlyList<ColorEncoding> GetColorEncodings(PresentationFormat format) =>
        FormatCapabilities
            .Where(capability => capability.Format == format)
            .SelectMany(static capability => capability.ColorEncodings)
            .Distinct()
            .ToArray();
}

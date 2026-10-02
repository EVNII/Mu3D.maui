using Mu3D.Color;

namespace Mu3D.Graphics;

/// <summary>
/// Describes the presentation mode that was actually negotiated with the platform.
/// </summary>
/// <param name="Format">The selected presentation format.</param>
/// <param name="DynamicRange">The selected dynamic range.</param>
/// <param name="Encoding">The encoding expected by the platform compositor.</param>
/// <param name="HdrHeadroom">
/// The configuration-time HDR headroom relative to SDR reference white, or <see langword="null"/>
/// when the backend did not report it.
/// </param>
/// <param name="FallbackReason">The reason the requested output was unavailable, if any.</param>
public sealed record ActualOutputState(
    PresentationFormat Format,
    OutputDynamicRange DynamicRange,
    ColorEncoding Encoding,
    float? HdrHeadroom,
    string? FallbackReason);

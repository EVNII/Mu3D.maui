using Mu3D.Color;

namespace Mu3D.Graphics;

/// <summary>Describes the backend-independent output configuration selected for a surface.</summary>
/// <param name="Output">The output state to report to the application.</param>
/// <param name="RequiresToneMapping">Whether the renderer must insert explicit HDR-to-SDR mapping.</param>
public sealed record SurfaceOutputPlan(
    ActualOutputState Output,
    bool RequiresToneMapping);

/// <summary>Selects an HDR-first presentation format using an application's explicit fallback policy.</summary>
public static class SurfaceOutputNegotiator
{
    /// <summary>Chooses an output plan from advertised surface capabilities.</summary>
    public static SurfaceOutputPlan Negotiate(
        SurfaceCapabilities capabilities,
        OutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(settings);

        if (capabilities.Formats.Count == 0)
        {
            throw new InvalidOperationException("The surface did not advertise a usable presentation format.");
        }

        if (settings.DynamicRange != OutputDynamicRange.Sdr &&
            SupportsExtendedLinearHdr(capabilities, settings.PreferredHdrFormat))
        {
            return new SurfaceOutputPlan(
                new ActualOutputState(
                    settings.PreferredHdrFormat,
                    OutputDynamicRange.Hdr,
                    ColorEncoding.ExtendedSrgbLinear,
                    HdrHeadroom: null,
                    FallbackReason: null),
                RequiresToneMapping: false);
        }

        PresentationFormat sdrFormat = SelectSdrFormat(capabilities.Formats);
        if (settings.DynamicRange == OutputDynamicRange.Sdr)
        {
            return SdrPlan(sdrFormat, fallbackReason: null, requiresToneMapping: false);
        }

        const string reason = "The surface did not advertise the requested HDR presentation format.";
        return settings.SdrFallback switch
        {
            SdrFallbackMode.Clamp => SdrPlan(sdrFormat, reason, requiresToneMapping: false),
            SdrFallbackMode.ToneMap => SdrPlan(sdrFormat, reason, requiresToneMapping: true),
            SdrFallbackMode.Fail => throw new InvalidOperationException(reason),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.SdrFallback, "Unknown fallback mode."),
        };
    }

    private static SurfaceOutputPlan SdrPlan(
        PresentationFormat format,
        string? fallbackReason,
        bool requiresToneMapping) =>
        new(
            new ActualOutputState(
                format,
                OutputDynamicRange.Sdr,
                ColorEncoding.Srgb,
                HdrHeadroom: null,
                fallbackReason),
            requiresToneMapping);

    private static bool SupportsExtendedLinearHdr(
        SurfaceCapabilities capabilities,
        PresentationFormat format)
    {
        if (!capabilities.Formats.Contains(format))
        {
            return false;
        }

        if (capabilities.FormatCapabilities.Count != 0)
        {
            return capabilities.FormatCapabilities.Any(capability =>
                capability.Format == format &&
                capability.ColorEncodings.Contains(ColorEncoding.ExtendedSrgbLinear));
        }

        // wgpu-native v29 reports formats without format/color-space pairs. Mu3D's verified
        // legacy configuration requests extended tone mapping only for RGBA16Float; no other
        // format is promoted to HDR merely because an application selected it as preferred.
        return format == PresentationFormat.Rgba16Float;
    }

    private static PresentationFormat SelectSdrFormat(IReadOnlyList<PresentationFormat> formats)
    {
        PresentationFormat[] preference =
        [
            PresentationFormat.Bgra8UnormSrgb,
            PresentationFormat.Rgba8UnormSrgb,
            PresentationFormat.Bgra8Unorm,
            PresentationFormat.Rgba8Unorm,
        ];
        foreach (PresentationFormat candidate in preference)
        {
            if (formats.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The surface did not advertise a supported SDR fallback format.");
    }
}

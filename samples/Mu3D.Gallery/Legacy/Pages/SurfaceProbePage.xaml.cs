using System.Text;
using Mu3D.Samples;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Native.Wgpu;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Reports capabilities of the concrete MAUI presentation surface.</summary>
public partial class SurfaceProbePage : ContentPage
{
    private WgpuSurfaceSession? session;
    private bool referencePatternPresented;
    private SurfaceReferencePattern? pattern;

    /// <summary>Initializes the surface probe page.</summary>
    public SurfaceProbePage()
    {
        InitializeComponent();
        SurfaceView.PresentationSessionChanged += OnPresentationSessionChanged;
        SurfaceView.Draw += OnDraw;
        SurfaceView.FramePresented += OnFramePresented;
        SurfaceView.SurfaceError += OnSurfaceError;
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        _ = sender;
        pattern?.Dispose();
        pattern = null;
        referencePatternPresented = false;
        session = e.Session as WgpuSurfaceSession;
        ProbeButton.IsEnabled = session is not null;
        PresentButton.IsEnabled = session is not null;
        ProbeStatus.Text = e.Session switch
        {
            null => "Presentation surface unavailable",
            WgpuSurfaceSession => "Control-managed wgpu surface ready",
            _ => "The configured backend does not support this wgpu diagnostic",
        };
        ProbeDetails.Text = session is null ? string.Empty : Format(session);
    }

    private void OnProbeClicked(object? sender, EventArgs e)
    {
        _ = sender;
        if (session is not WgpuSurfaceSession current)
        {
            return;
        }

        ProbeStatus.Text = current.OutputPlan.Output.DynamicRange == OutputDynamicRange.Hdr
            ? "RGBA16Float HDR surface configured"
            : "SDR fallback surface configured";
        ProbeDetails.Text = Format(current);
    }

    private void OnPresentClicked(object? sender, EventArgs e)
    {
        _ = sender;
        if (session is not WgpuSurfaceSession current)
        {
            return;
        }

        referencePatternPresented = true;
        SurfaceView.InvalidateSurface();
    }

    private void OnDraw(object? sender, SurfaceDrawEventArgs e)
    {
        if (!referencePatternPresented)
        {
            using var encoder = e.Device.CreateCommandEncoder();
            using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
                new GraphicsRenderPassColorAttachment(e.Target, GraphicsLoadOperation.Clear,
                    GraphicsStoreOperation.Store, new GraphicsClearColor(0, 0, 0, 1))))) { }
            using var commands = encoder.Finish();
            e.Device.Queue.Submit(commands);
            return;
        }
        if (pattern is null || pattern.Format != e.Target.Descriptor.Format)
        {
            var replacement = new SurfaceReferencePattern(e.Device, e.Target.Descriptor.Format);
            pattern?.Dispose();
            pattern = replacement;
        }
        pattern.Draw(e.Device, e.Target);
    }

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        if (!referencePatternPresented || session is null) return;
        ProbeStatus.Text = $"Reference pattern: {e.Status}";
        ProbeDetails.Text = "Pattern space: extended-linear sRGB\n" +
            "Top: smooth neutral ramp 0–4\nBottom: 0 | 0.18 | 0.5 | 1 | 2 | 4\n" +
            $"Windows SDR white matching: {SurfaceView.WindowsMatchSdrWhite}\n" +
            $"System SDR white: {SurfaceView.SystemSdrWhiteNits?.ToString("0.##") ?? "unknown"} nits\n" + Format(session);
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        ProbeStatus.Text = $"Presentation {e.Operation} failed";
        ProbeDetails.Text = e.Exception.ToString();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (session is null && SurfaceView.PresentationSession is IPresentationSurfaceSession current)
        {
            OnPresentationSessionChanged(
                SurfaceView,
                new PresentationSessionChangedEventArgs(current));
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        pattern?.Dispose();
        pattern = null;
        referencePatternPresented = false;
        session = null;
        base.OnDisappearing();
    }

    private static string Format(IPresentationSurfaceSession session)
    {
        DisplayHdrInfo displayInfo = session.QueryDisplayHdrInfo();
        StringBuilder text = new();
        _ = text.AppendLine($"wgpu: {WgpuBackendInfo.WgpuVersion}");
        _ = text.AppendLine($"wgpu-native runtime: {WgpuBackendInfo.GetRuntimeVersion()}");
        _ = text.AppendLine($"Extent: {session.Width} x {session.Height} physical pixels");
        _ = text.AppendLine($"Format: {session.OutputPlan.Output.Format}");
        _ = text.AppendLine($"Dynamic range: {session.OutputPlan.Output.DynamicRange}");
        _ = text.AppendLine($"Color encoding: {session.OutputPlan.Output.Encoding}");
        _ = text.AppendLine(
            $"Configuration headroom: {FormatOptional(session.OutputPlan.Output.HdrHeadroom)}");
        _ = text.AppendLine($"Live display headroom: {FormatOptional(displayInfo.ToneMapHeadroom)}");
        _ = text.AppendLine($"Display HDR metadata: {(displayInfo.IsUnknown ? "unavailable" : "available")}");
        _ = text.AppendLine($"Fallback: {session.OutputPlan.Output.FallbackReason ?? "none"}");
        _ = text.AppendLine($"RGBA16Float advertised: {session.Capabilities.SupportsRgba16Float}");
        if (session.Capabilities.FormatCapabilities.Count == 0)
        {
            _ = text.AppendLine("Format/color-space pairs: unavailable (legacy format-only backend)");
        }
        else
        {
            foreach (SurfaceFormatCapability capability in session.Capabilities.FormatCapabilities)
            {
                _ = text.AppendLine(
                    $"{capability.Format} color spaces: {string.Join(", ", capability.ColorEncodings)}");
            }
        }
        _ = text.AppendLine($"Present modes: {string.Join(", ", session.Capabilities.PresentModes)}");
        _ = text.AppendLine("Physical HDR output verified: false");
        return text.ToString();
    }

    private static string FormatOptional(float? value) =>
        value is float present ? $"{present:0.###}x" : "unknown";
}

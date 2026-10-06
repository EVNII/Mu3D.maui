using Mu3D.Gallery.Controls;
using System.Text;
using Mu3D.Samples;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Native.Wgpu;

namespace Mu3D.Gallery.Pages;

/// <summary>Reports capabilities of the concrete MAUI presentation surface.</summary>
public partial class HdrProbePage : ContentPage, IGalleryPageActivation
{
    private WgpuSurfaceSession? session;
    private bool referencePatternPresented;
    private SurfaceReferencePattern? pattern;
    private bool navigationActive;
    private string? lastSurfaceError;

    /// <summary>Initializes the surface probe page.</summary>
    public HdrProbePage()
    {
        InitializeComponent();
        SurfaceView.PresentationSessionChanged += OnPresentationSessionChanged;
        SurfaceView.Draw += OnDraw;
        SurfaceView.FramePresented += OnFramePresented;
        SurfaceView.SurfaceError += OnSurfaceError;
    }

    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (navigationActive == active) return;
        navigationActive = active;
        if (active) OnPageLoaded(this, EventArgs.Empty);
        else OnPageUnloaded(this, EventArgs.Empty);
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        _ = sender;
        CancelReferenceReadback();
        lastSurfaceError = null;
        pattern?.Dispose();
        pattern = null;
        referencePatternPresented = false;
        session = e.Session as WgpuSurfaceSession;
        ProbeButton.IsEnabled = session is not null;
        PresentButton.IsEnabled = session is not null;
        ReadbackButton.IsEnabled = session is not null;
        ProbeStatus.Text = e.Session switch
        {
            null => "Presentation surface unavailable",
            WgpuSurfaceSession => "Control-managed wgpu surface ready",
            _ => "The configured backend does not support this wgpu diagnostic",
        };
        ProbeDetails.Text = session is null ? string.Empty : Format(session, "after configure");
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
        RefreshReport(current, "manual probe");
    }

    private void OnPresentClicked(object? sender, EventArgs e)
    {
        _ = sender;
        if (session is not WgpuSurfaceSession current)
        {
            return;
        }

        CancelReferenceReadback();
        referencePatternPresented = true;
        SurfaceView.InvalidateSurface();
    }

    private async void OnCopyDiagnosticsClicked(object? sender, EventArgs e)
    {
        _ = sender;
        try
        {
            if (session is WgpuSurfaceSession current)
                RefreshReport(current, "at copy");
            await Clipboard.Default.SetTextAsync($"{ProbeStatus.Text}\n{ProbeDetails.Text}");
        }
        catch (Exception exception)
        {
            ProbeStatus.Text = $"Copy diagnostics failed: {exception.Message}";
        }
    }

    private void OnDraw(object? sender, SurfaceDrawEventArgs e)
    {
        if (!navigationActive) return;
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
        SubmitReferenceReadback(e.Device, e.Target);
    }

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        if (!referencePatternPresented || session is null) return;
        RecordReferenceReadbackFrameStatus(e.Status.ToString());
        lastSurfaceError = null;
        ProbeStatus.Text = $"Reference pattern: {e.Status}";
        RefreshReport(session, $"after present ({e.Status})");
    }

    private void RefreshReport(IPresentationSurfaceSession current, string captureStage)
    {
        ProbeDetails.Text = (referencePatternPresented ? "Pattern space: extended-linear sRGB\n" +
            "Top: smooth neutral ramp 0–4\nBottom: 0 | 0.18 | 0.5 | 1 | 2 | 4\n" +
            $"Windows SDR white matching: {SurfaceView.WindowsMatchSdrWhite}\n" +
            $"System SDR white: {SurfaceView.SystemSdrWhiteNits?.ToString("0.##") ?? "unknown"} nits\n"
            : string.Empty) + Format(current, captureStage) + FormatReferenceReadback() +
            (lastSurfaceError is null ? string.Empty : $"\nLast surface error:\n{lastSurfaceError}");
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        FailReferenceReadback(e.Exception, $"error ({e.Operation})");
        lastSurfaceError = e.Exception.ToString();
        ProbeStatus.Text = $"Presentation {e.Operation} failed";
        ProbeDetails.Text = e.Exception.ToString();
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        if (session is null && SurfaceView.PresentationSession is IPresentationSurfaceSession current)
        {
            OnPresentationSessionChanged(
                SurfaceView,
                new PresentationSessionChangedEventArgs(current));
        }
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        CancelReferenceReadback();
        pattern?.Dispose();
        pattern = null;
        referencePatternPresented = false;
        session = null;
    }

    private string Format(IPresentationSurfaceSession session, string captureStage)
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
#if ANDROID
        _ = text.AppendLine($"Android producer snapshot: {captureStage}");
        AndroidHdrBufferDiagnostics.Append(text, SurfaceView.NativeSurfaceSource,
            SurfaceView.Handler?.PlatformView as global::Android.Views.View);
#endif
        _ = text.AppendLine("Physical HDR output: not measured by this probe");
        return text.ToString();
    }

    private static string FormatOptional(float? value) =>
        value is float present ? $"{present:0.###}x" : "unknown";
}

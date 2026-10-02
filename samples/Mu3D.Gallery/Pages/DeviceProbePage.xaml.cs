using System.Text;
using Mu3D.Native.Wgpu;

namespace Mu3D.Gallery.Pages;

/// <summary>Runs the opt-in native device capability harness on a deployed device.</summary>
public partial class DeviceProbePage : ContentPage
{
    /// <summary>Initializes the native device probe page.</summary>
    public DeviceProbePage()
    {
        InitializeComponent();
    }

    private async void OnRunProbeClicked(object? sender, EventArgs e)
    {
        _ = sender;
        SetProbeButtonsEnabled(false);
        ProbeProgress.IsVisible = true;
        ProbeProgress.IsRunning = true;
        ProbeStatus.Text = "Probing native adapter and device…";
        ProbeDetails.Text = string.Empty;

        try
        {
            WgpuDeviceProbeResult result = await WgpuDeviceProbe.ProbeAsync(
                TimeSpan.FromSeconds(30));
            ProbeStatus.Text = "Native device probe completed";
            ProbeDetails.Text = Format(result);
        }
        catch (Exception exception)
        {
            ProbeStatus.Text = "Native device probe failed";
            ProbeDetails.Text = exception.Message;
        }
        finally
        {
            ProbeProgress.IsRunning = false;
            ProbeProgress.IsVisible = false;
            SetProbeButtonsEnabled(true);
        }
    }

    private async void OnRunTriangleProbeClicked(object? sender, EventArgs e)
    {
        _ = sender;
        SetProbeButtonsEnabled(false);
        TriangleProbeProgress.IsVisible = true;
        TriangleProbeProgress.IsRunning = true;
        TriangleProbeStatus.Text = "Submitting native offscreen triangle…";
        TriangleProbeDetails.Text = string.Empty;

        try
        {
            WgpuOffscreenTriangleProbeResult result = await WgpuOffscreenTriangleProbe.ProbeAsync(
                TimeSpan.FromSeconds(30));
            TriangleProbeStatus.Text = "Native offscreen triangle completed";
            TriangleProbeDetails.Text = Format(result);
        }
        catch (Exception exception)
        {
            TriangleProbeStatus.Text = "Native offscreen triangle failed";
            TriangleProbeDetails.Text = exception.ToString();
        }
        finally
        {
            TriangleProbeProgress.IsRunning = false;
            TriangleProbeProgress.IsVisible = false;
            SetProbeButtonsEnabled(true);
        }
    }

    private static string Format(WgpuDeviceProbeResult result)
    {
        StringBuilder text = new();
        _ = text.AppendLine($"wgpu: {WgpuBackendInfo.WgpuVersion}");
        _ = text.AppendLine($"wgpu-native pinned: {WgpuBackendInfo.NativeVersion}");
        _ = text.AppendLine($"wgpu-native runtime: {WgpuBackendInfo.GetRuntimeVersion()}");
        _ = text.AppendLine($"wgpu-native commit: {WgpuBackendInfo.NativeCommit}");
        _ = text.AppendLine($"Backend: {result.Backend}");
        _ = text.AppendLine($"Adapter type: {result.AdapterType}");
        _ = text.AppendLine($"Vendor: {result.Vendor}");
        _ = text.AppendLine($"Architecture: {result.Architecture}");
        _ = text.AppendLine($"Device: {result.Device}");
        _ = text.AppendLine($"Description: {result.Description}");
        _ = text.AppendLine($"ShaderF16 available: {result.ShaderF16Available}");
        _ = text.AppendLine($"ShaderF16 enabled: {result.ShaderF16Enabled}");
        _ = text.AppendLine($"RGBA16Float texture created: {result.Rgba16FloatTextureCreated}");
        _ = text.AppendLine($"Cube-array shader compiled: {result.CubeArrayTextureShaderCompiled}");
        _ = text.AppendLine($"Max sampled textures/stage: {result.MaxSampledTexturesPerShaderStage}");
        _ = text.AppendLine($"Surface/HDR probed: {result.SurfaceProbed}");
        _ = text.AppendLine("Features:");
        foreach (string feature in result.AvailableFeatures)
        {
            _ = text.AppendLine($"  {feature}");
        }

        return text.ToString();
    }

    private static string Format(WgpuOffscreenTriangleProbeResult result)
    {
        StringBuilder text = new();
        _ = text.AppendLine($"wgpu: {WgpuBackendInfo.WgpuVersion}");
        _ = text.AppendLine($"wgpu-native pinned: {WgpuBackendInfo.NativeVersion}");
        _ = text.AppendLine($"wgpu-native runtime: {WgpuBackendInfo.GetRuntimeVersion()}");
        _ = text.AppendLine($"Target format: {result.TargetFormat}");
        _ = text.AppendLine($"Command submitted: {result.CommandSubmitted}");
        _ = text.AppendLine($"Queue completion observed: {result.QueueCompletionObserved}");
        _ = text.AppendLine($"ShaderF16 enabled: {result.ShaderF16Enabled}");
        _ = text.AppendLine($"Device state: {result.DeviceState}");
        _ = text.AppendLine($"Device lost reason: {result.DeviceLostReason ?? "none"}");
        _ = text.AppendLine($"Surface probed: {result.SurfaceProbed}");
        _ = text.AppendLine($"HDR output verified: {result.HdrOutputVerified}");
        return text.ToString();
    }

    private void SetProbeButtonsEnabled(bool enabled)
    {
        RunProbeButton.IsEnabled = enabled;
        RunTriangleProbeButton.IsEnabled = enabled;
    }
}

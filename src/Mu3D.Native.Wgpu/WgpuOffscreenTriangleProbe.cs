using Mu3D.Graphics;

namespace Mu3D.Native.Wgpu;

/// <summary>Validates the backend-independent render command path against a real wgpu device.</summary>
public static class WgpuOffscreenTriangleProbe
{
    /// <summary>Creates, submits and waits for one real FP16 offscreen triangle.</summary>
    /// <param name="timeout">Maximum time allowed for each native adapter/device request.</param>
    /// <param name="cancellationToken">Cancels the managed device-creation wait.</param>
    /// <returns>A report that distinguishes offscreen execution from surface or HDR validation.</returns>
    public static async Task<WgpuOffscreenTriangleProbeResult> ProbeAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Probe timeout must be positive.");
        }

        using WgpuGraphicsDevice device = await WgpuGraphicsDevice.CreateAsync(
            timeout,
            cancellationToken).ConfigureAwait(false);
        return Probe(device);
    }

    internal static WgpuOffscreenTriangleProbeResult Probe(WgpuGraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        string? lostReason = null;
        device.DeviceLost += (_, eventArgs) => lostReason ??= eventArgs.Reason;

        using GraphicsTexture target = device.CreateTexture(
            new GraphicsTextureDescriptor(
                new GraphicsExtent3D(32, 32),
                GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource,
                label: "M2 offscreen FP16 target"));
        WgpuOffscreenTriangleRenderer.Render(device, target);
        device.WaitForSubmittedWork("offscreen triangle completion");

        return new WgpuOffscreenTriangleProbeResult(
            CommandSubmitted: true,
            QueueCompletionObserved: true,
            TargetFormat: GraphicsTextureFormat.Rgba16Float,
            ShaderF16Enabled: device.Capabilities.SupportsShaderFloat16,
            DeviceState: device.State,
            DeviceLostReason: lostReason,
            SurfaceProbed: false,
            HdrOutputVerified: false);
    }
}

/// <summary>Reports a real offscreen command probe without claiming presentation or HDR output.</summary>
/// <param name="CommandSubmitted">Whether the command buffer was submitted.</param>
/// <param name="QueueCompletionObserved">Whether native queue completion was observed.</param>
/// <param name="TargetFormat">The offscreen render-target format.</param>
/// <param name="ShaderF16Enabled">Whether optional ShaderF16 was enabled on the device.</param>
/// <param name="DeviceState">The device state after queue completion.</param>
/// <param name="DeviceLostReason">The native device-loss reason, if reported.</param>
/// <param name="SurfaceProbed">Whether this probe acquired or configured a surface.</param>
/// <param name="HdrOutputVerified">Whether this probe verified physical HDR output.</param>
public sealed record WgpuOffscreenTriangleProbeResult(
    bool CommandSubmitted,
    bool QueueCompletionObserved,
    GraphicsTextureFormat TargetFormat,
    bool ShaderF16Enabled,
    GraphicsDeviceState DeviceState,
    string? DeviceLostReason,
    bool SurfaceProbed,
    bool HdrOutputVerified);

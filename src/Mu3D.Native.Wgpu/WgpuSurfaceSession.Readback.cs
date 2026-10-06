using Mu3D.Graphics;
using Mu3D.Native.Wgpu.Interop;

namespace Mu3D.Native.Wgpu;

public sealed partial class WgpuSurfaceSession
{
    /// <summary>Opts this session into copying acquired presentation textures for diagnostics.</summary>
    /// <returns>
    /// True when copy-source usage is enabled; false when this exact surface does not advertise it.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The session is being released or has been disposed.</exception>
    /// <exception cref="InvalidOperationException">Called inside an active rendering callback.</exception>
    /// <remarks>
    /// Call outside the control's Draw callback, then request a normal frame. This reconfigures only
    /// this session, preserving its output format, color encoding, present mode, alpha mode and size.
    /// Copy-source usage remains enabled across resize until the session is disposed; ordinary
    /// sessions retain their default usage. Enabling copies may affect presentation performance.
    /// Submit copies through the Draw target before the callback returns. Do not retain, acquire,
    /// present or dispose the control-owned target/session. A readback observes GPU texture values
    /// before presentation, not the queued buffer, compositor interpretation or physical HDR output.
    /// </remarks>
    public bool TryEnableReadback()
    {
        lock (surfaceGate)
        {
            ObjectDisposedException.ThrowIf(disposed || dependentResourceReleaseStarted, this);
            if (frameInProgress)
            {
                throw new InvalidOperationException("Enable surface readback outside the render callback.");
            }
            if ((textureUsage & GraphicsTextureUsage.CopySource) != 0)
            {
                return true;
            }

            _ = WgpuSurfaceProbe.ReadCapabilities(surface, graphicsDevice.NativeAdapter,
                out GraphicsTextureUsage supportedUsages);
            GraphicsTextureUsage requested = textureUsage | GraphicsTextureUsage.CopySource;
            if ((supportedUsages & requested) != requested)
            {
                return false;
            }

            Configure(surface, graphicsDevice, OutputPlan.Output.Format, SelectPresentMode(Capabilities),
                AlphaMode, width, height, requested);
            textureUsage = requested;
            return true;
        }
    }

    private unsafe GraphicsTexture WrapAcquiredTexture(WGPUTextureImpl* texture)
    {
        try
        {
            GraphicsTextureDescriptor descriptor;
            if ((textureUsage & GraphicsTextureUsage.CopySource) != 0)
            {
                // Query the acquired native object rather than describing the output plan as an
                // independently observed format/extent/usage in a diagnostic capture.
                WGPUTextureFormat actualFormat = WgpuNative.wgpuTextureGetFormat(texture);
                GraphicsTextureUsage actualUsage = WgpuSurfaceProbe.MapTextureUsages(
                    WgpuNative.wgpuTextureGetUsage(texture));
                if (actualFormat != MapFormat(OutputPlan.Output.Format) ||
                    (actualUsage & textureUsage) != textureUsage)
                {
                    throw new InvalidOperationException(
                        $"Acquired surface texture is {actualFormat}/{actualUsage}; " +
                        $"configured {OutputPlan.Output.Format}/{textureUsage}.");
                }
                descriptor = new GraphicsTextureDescriptor(
                    new GraphicsExtent3D(WgpuNative.wgpuTextureGetWidth(texture),
                        WgpuNative.wgpuTextureGetHeight(texture),
                        WgpuNative.wgpuTextureGetDepthOrArrayLayers(texture)),
                    MapGraphicsFormat(OutputPlan.Output.Format), actualUsage,
                    WgpuNative.wgpuTextureGetMipLevelCount(texture),
                    WgpuNative.wgpuTextureGetSampleCount(texture),
                    "native-verified acquired surface texture");
            }
            else
            {
                descriptor = new GraphicsTextureDescriptor(new GraphicsExtent3D(width, height),
                    MapGraphicsFormat(OutputPlan.Output.Format), textureUsage,
                    label: "acquired surface texture");
            }
            return new WgpuGraphicsTexture(graphicsDevice, descriptor, texture);
        }
        catch
        {
            ReleaseTexture(texture);
            throw;
        }
    }
}

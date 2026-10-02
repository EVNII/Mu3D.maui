using Mu3D.GalleryApp.Web.Infrastructure;

namespace Mu3D.Native.Wgpu;

// The shared adapter calls this after recording. Browser errors arrive asynchronously;
// the probe additionally checks after readback and after browser animation boundaries.
public sealed partial class WgpuGraphicsDevice
{
    internal void ThrowIfNativeErrors(string operation) => BrowserGpu.ThrowIfErrors(operation);
}

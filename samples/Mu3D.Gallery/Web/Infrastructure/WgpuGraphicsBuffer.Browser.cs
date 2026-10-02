using Mu3D.GalleryApp.Web.Infrastructure;

namespace Mu3D.Native.Wgpu;

internal sealed unsafe partial class WgpuGraphicsBuffer
{
    protected override Task<byte[]> ReadAsyncCore(ulong offset, int byteCount, CancellationToken cancellationToken) =>
        BrowserGpu.ReadAsync((nint)pointer, offset, byteCount, cancellationToken);
}

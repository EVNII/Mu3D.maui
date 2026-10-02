using Mu3D.Color;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

internal static class GalleryEnvironment
{
    internal static async Task<EquirectangularHdrEnvironment> LoadStudioAsync(
        CancellationToken cancellationToken)
    {
        using Stream stream = new MemoryStream(await GalleryAssets.ReadBytesAsync(
            "Hdri/studio_small_02_1k.hdr", cancellationToken), writable: false);
        cancellationToken.ThrowIfCancellationRequested();
        EquirectangularHdrEnvironment environment = RadianceHdrReader.Read(
            stream,
            StandardColorSpaces.LinearSrgb,
            "Studio Small 02 (Poly Haven, CC0, 1K HDR)");
        await SceneRenderer.PrepareImageBasedLightingAsync(environment, cancellationToken);
        return environment;
    }
}

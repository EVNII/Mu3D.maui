using Mu3D.Color;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.Gallery.Pages;

/// <summary>Loads the shared studio HDRI environment used by the material example pages.</summary>
internal static class SampleEnvironment
{
    /// <summary>Reads the packaged 1K studio HDRI and prepares it for image-based lighting.</summary>
    internal static async Task<EquirectangularHdrEnvironment> LoadStudioAsync(
        CancellationToken cancellationToken)
    {
        await using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(
            "Hdri/studio_small_02_1k.hdr");
        cancellationToken.ThrowIfCancellationRequested();
        EquirectangularHdrEnvironment environment = RadianceHdrReader.Read(
            stream,
            StandardColorSpaces.LinearSrgb,
            "Studio Small 02 (Poly Haven, CC0, 1K HDR)");
        await SceneRenderer.PrepareImageBasedLightingAsync(environment, cancellationToken);
        return environment;
    }
}

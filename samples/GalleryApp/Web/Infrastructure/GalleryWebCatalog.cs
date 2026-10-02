using Mu3D.GalleryApp;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Titles/order/descriptions belong to the original Gallery catalog, not a second Web inventory.
internal static class GalleryWebCatalog
{
    internal static string FeatureId(GalleryExample entry) => entry.Route switch
    {
        GalleryRoutes.Welcome => "gallery-overview",
        GalleryRoutes.LightingLab => "lighting-direct-ibl",
        GalleryRoutes.OcclusionLab => "shadows-ao",
        GalleryRoutes.MaterialTexture => "pbr-material-textures",
        GalleryRoutes.OpenPbr => "openpbr-materialx",
        GalleryRoutes.OpenPbrFurnace => "openpbr-white-furnace",
        GalleryRoutes.AnimationLab => "skinning-animation",
        GalleryRoutes.GltfLoad => "gltf-loading",
        GalleryRoutes.GltfVariants => "gltf-material-variants",
        GalleryRoutes.HdrJpegLab => "hdr-jpeg",
        GalleryRoutes.KtxDecode => "ktx2-decode",
        GalleryRoutes.KtxTextureLab => "ktx2-gpu-sampling",
        _ => entry.Route["gallery-".Length..],
    };

    internal static IEnumerable<string> PageText(string pageName)
    {
        using Stream source = typeof(GalleryWebCatalog).Assembly.GetManifestResourceStream($"Mu3D.GalleryApp.NativePages.{pageName}.xaml")
            ?? throw new InvalidOperationException($"Gallery source missing: {pageName}.");
        return System.Xml.Linq.XDocument.Load(source).Descendants()
            .Where(element => element.Name.LocalName == "Label")
            .Select(element => (string?)element.Attribute("Text")).OfType<string>().ToArray();
    }
}

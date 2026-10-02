namespace Mu3D.GalleryApp;

// Stable native/Web documentation IDs share one portable mapping.
internal static class GalleryFeatureCatalog
{
    internal static string FeatureId(string route) => route switch
    {
        GalleryRoutes.Welcome => "gallery-overview",
        GalleryRoutes.ModelLab => "material-conformance",
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
        _ => route["gallery-".Length..],
    };

}

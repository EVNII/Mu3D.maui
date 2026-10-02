using Legacy = Mu3D.GalleryApp.Pages;

namespace Mu3D.Gallery;

internal static class GalleryPageFactory
{
    internal static Page Create(string id) => id switch
    {
        "gallery-overview" => new Legacy.WelcomePage(),
        "device-probe" => new Pages.DeviceProbePage(),
        "surface-probe" => new Pages.HdrProbePage(),
        "custom-draw" => new Legacy.CustomDrawPage(),
        "scene-assets" => new Legacy.SceneAssetPage(),
        "declarative-scene" => new Legacy.DeclarativeScenePage(),
        "declarative-tools" => new Legacy.DeclarativeToolsPage(),
        "orbit-controls" => new Legacy.OrbitControlsPage(),
        "map-controls" => new Legacy.MapControlsPage(),
        "fly-controls" => new Legacy.FlyControlsPage(),
        "pointer-pen-input" => new Legacy.PointerPenInputPage(),
        "progress-bridge" => new Legacy.ProgressBridgePage(),
        "playback-toolbar" => new Legacy.PlaybackToolbarPage(),
        "axes-helper" => new Legacy.AxesHelperPage(),
        "grid-helper" => new Legacy.GridHelperPage(),
        "bounds-helper" => new Legacy.BoundsHelperPage(),
        "ui-anchors" => new Legacy.UiAnchorsPage(),
        "render-outputs" => new Legacy.RenderOutputsPage(),
        "transform-gizmo" => new Pages.Toolkit3DPage(),
        "frame-statistics" => new Legacy.FrameStatisticsPage(),
        "lighting-direct-ibl" => new Legacy.LightingLabPage(),
        "shadows-ao" => new Legacy.OcclusionLabPage(),
        "pbr-material-textures" => new Pages.PbrMaterialPage(),
        "openpbr-materialx" => new Pages.OpenPbrPage(),
        "openpbr-white-furnace" => new Legacy.OpenPbrFurnacePage(),
        "color-management" => new Pages.ColorRampsPage(),
#if MU3D_PRINTING
        "cmyk-printing" => new Legacy.CmykPrintingPage(),
#endif
        "color-lut" => new Pages.ColorLutPage(),
        "hdr-canvas" => new Legacy.HdrCanvasPage(),
        "skinning-animation" => new Legacy.AnimationLabPage(),
        "gltf-loading" => new Legacy.GltfLoadPage(),
        "gltf-instances" => new Legacy.GltfInstancesPage(),
        "gltf-product-feed" => new Legacy.ProductFeedPage(),
        "gltf-animation" => new Legacy.GltfAnimationPage(),
        "gltf-material-variants" => new Legacy.GltfVariantsPage(),
        "hdr-jpeg" => new Legacy.HdrJpegLabPage(),
        "ktx2-decode" => new Legacy.KtxDecodePage(),
        "ktx2-gpu-sampling" => new Legacy.KtxTextureLabPage(),
        "material-conformance" => new Legacy.ModelLabPage(),
        "emissive-material" => new Pages.EmissiveMaterialPage(),
        "translucent-canvas" => new Pages.TranslucentCanvasPage(),
        "procedural-feed" => new Pages.FeedPage(),
        "licenses" => new Pages.LicensePage(),
        _ => throw new ArgumentException($"Unknown Gallery example '{id}'.", nameof(id)),
    };
}

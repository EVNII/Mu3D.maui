namespace Mu3D.GalleryApp;

internal static class GallerySourceCatalog
{
    internal static string? AdaptivePageName(string id) => id switch
    {
        "device-probe" => "DeviceProbePage", "surface-probe" => "HdrProbePage",
        "transform-gizmo" => "Toolkit3DPage", "pbr-material-textures" => "PbrMaterialPage",
        "openpbr-materialx" => "OpenPbrPage", "color-management" => "ColorRampsPage",
        "color-lut" => "ColorLutPage", "emissive-material" => "EmissiveMaterialPage",
        "translucent-canvas" => "TranslucentCanvasPage", "procedural-feed" => "FeedPage",
        _ => null,
    };

    // Distinct source paths keep same-named native/Web fixture pages from showing the wrong code.
    internal static string[] AdaptiveHelpers(string pageName)
    {
        string[] helpers = pageName switch
        {
        "PbrMaterialPage" => ["Adaptive/SceneHostView.cs", "Adaptive/MaterialTexturePresenter.cs", "Adaptive/SampleEnvironment.cs"],
        "OpenPbrPage" => ["Adaptive/SceneHostView.cs", "OpenPbrTextureExample.cs",
            "OpenPbrParameterFields.cs", "OpenPbrParameterEditor.cs"],
        "Toolkit3DPage" => ["Adaptive/SceneHostView.cs"],
        "EmissiveMaterialPage" => ["Adaptive/SceneHostView.cs", "EmissiveMaterialExample.cs"],
        "TranslucentCanvasPage" => ["Adaptive/SceneHostView.cs", "HdrCanvasExample.cs"],
        "FeedPage" => ["ProceduralFeedExample.cs"],
        "ColorRampsPage" => ["ColorComparisonRenderer.cs"],
        "ColorLutPage" => ["ColorLutExample.cs"],
        "HdrProbePage" => ["SurfaceReferencePattern.cs"],
        _ => [],
        };
        return ["Adaptive/IGalleryPageActivation.cs", .. helpers];
    }

    internal static string[] Helpers(string pageName) => pageName switch
        {
            "CustomDrawPage" => ["HdrTriangleExample.cs"],
            "SceneAssetPage" => ["SceneAssetExample.cs"],
            "TransformGizmoPage" or "FrameStatisticsPage" or "UiAnchorsPage"
                => ["ToolkitSceneExamples.cs"],
            "MapControlsPage" => ["MapControlsPage.Keyboard.Desktop.cs"],
            "FlyControlsPage" => ["FlyControlsPage.Keyboard.Desktop.cs"],
            "PointerPenInputPage" => ["PenTipPalette.cs", "PointerPenInputPage.PencilPreview.cs", "PointerPenInputPage.Windows.cs",
                "PenTipPreview.cs", "PenTipScreenSpaceShadowPass.cs", "PenTipDisplayNormalShadowFeature.cs", "PointerInputBridge.cs",
                "PointerInputBridge.Apple.cs", "PointerInputBridge.Windows.cs", "PointerInputBridge.Android.cs"],
            "RenderOutputsPage" => ["RenderOutputsExample.cs"],
            "OpenPbrPage" => ["OpenPbrTextureExample.cs", "OpenPbrGalleryExample.cs", "OpenPbrParameterFields.cs", "OpenPbrParameterEditor.cs"],
            "SurfaceProbePage" => ["SurfaceReferencePattern.cs"],
            "OpenPbrFurnacePage" => ["OpenPbrFurnaceRenderer.cs", "FurnaceSettingsQueue.cs"],
            "ColorManagementPage" => ["ColorComparisonRenderer.cs"],
            "ColorLutPage" => ["ColorLutExample.cs"],
            "HdrCanvasPage" => ["HdrCanvasExample.cs"],
            "LightingLabPage" => ["LightingLabPresenter.cs", "GalleryEnvironment.cs", "GalleryAssets.cs"],
            "OcclusionLabPage" => ["OcclusionLabPresenter.cs", "GalleryEnvironment.cs", "GalleryAssets.cs"],
            "MaterialTexturePage" => ["MaterialTexturePresenter.cs"],
            "AnimationLabPage" => ["AnimationLabPresenter.cs"],
            "GltfLoadPage" or "GltfAnimationPage" or "GltfVariantsPage"
                => ["GltfScenePresenter.cs", "GalleryEnvironment.cs", "GalleryAssets.cs"],
            "GltfInstancesPage" => ["GltfInstancesExample.cs", "GalleryEnvironment.cs", "GalleryAssets.cs"],
            "ProductFeedPage" => ["ProductFeedExample.cs", "ProductFeedData.cs", "ProductFeedPage.Output.cs", "ProductFeedPage.Diagnostics.cs"],
            "HdrJpegLabPage" => ["HdrJpegRoundTrip.cs"],
            "KtxTextureLabPage" => ["KtxTextureLabPresenter.cs"],
            "ModelLabPage" => ["ModelLabCatalog.cs", "ModelLabAssets.cs", "ModelLabPresenter.cs"],
    #if MU3D_PRINTING
            "CmykPrintingPage" => ["CmykPrintingExample.cs", "CmykPrintingPattern.cs"],
    #endif
            _ => [],
        };
    internal static string PageName(string route) => route switch
    {
        GalleryRoutes.Welcome => "WelcomePage", GalleryRoutes.SceneAssets => "SceneAssetPage",
        GalleryRoutes.GltfProductFeed => "ProductFeedPage", GalleryRoutes.OpenPbr => "OpenPbrPage",
        GalleryRoutes.OpenPbrFurnace => "OpenPbrFurnacePage",
        _ => string.Concat(route["gallery-".Length..].Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..])) + "Page",
    };
}

using System.Collections.ObjectModel;

namespace Mu3D.GalleryApp;

/// <summary>Provides the focused examples shown by the compact native Shell tab.</summary>
public static class GalleryNavigationCatalog
{
    private static readonly ReadOnlyCollection<GalleryExample> examples = new(
    [
        new("W", "Welcome", "Gallery orientation and the HDR-first rendering contract.", GalleryRoutes.Welcome),
        new("D", "Device Probe", "Validate the native adapter, ShaderF16 and an RGBA16Float texture.", GalleryRoutes.DeviceProbe),
        new("S", "Surface Probe", "Inspect and exercise the physical presentation surface.", GalleryRoutes.SurfaceProbe),
        new("C", "Custom Draw", "Draw an HDR triangle through Mu3DView's automatic frame callback.", GalleryRoutes.CustomDraw),
        new("I", "Scene Assets", "Share immutable resources across independent instances and SceneViews.", GalleryRoutes.SceneAssets),
        new("X", "Declarative Scene", "Declare a camera, lights, primitives, transforms, and materials in XAML.", GalleryRoutes.DeclarativeScene),
        new("O", "Declarative Tools", "Connect XAML Orbit and Gizmo tools to explicit scene elements.", GalleryRoutes.DeclarativeTools),
        new("◎", "Orbit Controls", "Rotate around a target, screen-pan, and dolly through a declarative OrbitTool.", GalleryRoutes.OrbitControls),
        new("M", "Map Controls", "Pan a world-up plane, zoom, and rotate through a declarative MapTool.", GalleryRoutes.MapControls),
        new("F", "Fly Controls", "Move and look from MAUI commands without viewport focus.", GalleryRoutes.FlyControls),
        new("✎", "Pointer + Pen Input", "Feed application-owned mouse, touch, or pen samples into scene raycasting.", GalleryRoutes.PointerPenInput),
        new("B", "Progress Bridge", "Drive node and camera paths from MAUI animation or scroll progress.", GalleryRoutes.ProgressBridge),
        new("▶", "Playback Toolbar", "Control any application-owned IPlayable from a viewport overlay.", GalleryRoutes.PlaybackToolbar),
        new("A", "Axes Helper", "Inspect orientation and click cardinal or 45-degree camera views.", GalleryRoutes.AxesHelper),
        new("#", "Grid Helper", "Draw a finite HDR grid that is occluded by scene depth.", GalleryRoutes.GridHelper),
        new("□", "Bounds + Outline", "Compare a world AABB with the target's actual depth-aware silhouette.", GalleryRoutes.BoundsHelper),
        new("U", "UI Anchors", "Attach real MAUI UI to moving scene nodes after successful frames.", GalleryRoutes.UiAnchors),
        new("R", "Render Outputs", "Switch Beauty, AO, reflection, normal, depth, and registered outputs.", GalleryRoutes.RenderOutputs),
        new("T", "Transform Gizmo", "Drag HDR-linear translate, rotate and scale handles over a Mu3DSceneView.", GalleryRoutes.TransformGizmo),
        new("R", "Frame Statistics", "Inspect throttled frame timings and resource counts over a Mu3DSceneView.", GalleryRoutes.FrameStatistics),
        new("L", "Direct + IBL Lighting", "Compare direct light with an HDR image-based environment.", GalleryRoutes.LightingLab),
        new("O", "Shadows + AO", "Adjust directional shadows and screen-space ambient occlusion.", GalleryRoutes.OcclusionLab),
        new("P", "PBR Material + Textures", "Edit metallic/roughness values and material texture sampling.", GalleryRoutes.MaterialTexture),
        new("P", "OpenPBR + MaterialX", "Compare raster, hybrid and path-traced OpenPBR, with XAML authoring and MaterialX XML.", GalleryRoutes.OpenPbr),
        new("W", "OpenPBR White Furnace", "Inspect energy preservation in a uniform white environment, with linear ratios and explicit measurements.", GalleryRoutes.OpenPbrFurnace),
        new("C", "Color Management", "Compare AgX and ACES 2 SDR/HDR display views on one linear scene.", GalleryRoutes.ColorManagement),
#if MU3D_PRINTING
        new("P", "CMYK + Soft Proof", "Compare RGB with ICC print simulation, paper white and total ink coverage.", GalleryRoutes.CmykPrinting),
#endif
        new("C", "Color LUT", "Apply an explicit FP32 color lookup table directly to an HDR surface.", GalleryRoutes.ColorLut),
        new("H", "HDR Canvas", "Composite tagged HDR dabs in bounded Float16 tiles and present a native scene texture.", GalleryRoutes.HdrCanvas),
        new("A", "Animation Lab", "Blend clips, skinning and morph animation in FP32.", GalleryRoutes.AnimationLab),
        new("G", "glTF Loading", "Load a bounded .gltf with external BIN and PNG resources.", GalleryRoutes.GltfLoad),
        new("I", "glTF Instances", "Import one GLB and create isolated product instances.", GalleryRoutes.GltfInstances),
        new("P", "3D Product Feed", "MAUI-owned HDR Surface proxies share one device and support compositor alpha.", GalleryRoutes.GltfProductFeed),
        new("F", "glTF Animation", "Select and play animation clips imported from Fox.glb.", GalleryRoutes.GltfAnimation),
        new("V", "glTF Material Variants", "Switch authored KHR_materials_variants choices.", GalleryRoutes.GltfVariants),
        new("H", "HDR JPEG Lab", "Measure an Ultra HDR gain-map JPEG round trip.", GalleryRoutes.HdrJpegLab),
        new("K", "KTX Decode", "Decode KTX2 into application-owned linear RGBA pixels.", GalleryRoutes.KtxDecode),
        new("X", "KTX GPU Sampling", "Compare decoded and GPU-compressed KTX2 material sampling.", GalleryRoutes.KtxTextureLab),
    ]);

    /// <summary>Gets all focused examples in their stable display order.</summary>
    public static IReadOnlyList<GalleryExample> Examples => examples;
}

/// <summary>Describes one focused Gallery navigation entry.</summary>
public sealed record GalleryExample(string Monogram, string Title, string Description, string Route);

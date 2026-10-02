namespace Mu3D.GalleryApp;

/// <summary>Defines stable Shell routes used by the Gallery.</summary>
public static class GalleryRoutes
{
    /// <summary>The welcome route.</summary>
    public const string Welcome = "gallery-welcome";

    /// <summary>The native device-probe route.</summary>
    public const string DeviceProbe = "gallery-device-probe";

    /// <summary>The native surface-probe route.</summary>
    public const string SurfaceProbe = "gallery-surface-probe";

    /// <summary>The automatic custom-draw route.</summary>
    public const string CustomDraw = "gallery-custom-draw";

    /// <summary>The reusable scene-asset and independent-instance route.</summary>
    public const string SceneAssets = "gallery-scene-assets";

    /// <summary>The declarative Scene3D XAML route.</summary>
    public const string DeclarativeScene = "gallery-declarative-scene";

    /// <summary>The declarative ViewportTools route.</summary>
    public const string DeclarativeTools = "gallery-declarative-tools";

    /// <summary>The target-centered orbit camera-controls route.</summary>
    public const string OrbitControls = "gallery-orbit-controls";

    /// <summary>The declarative map and CAD-style camera-controls route.</summary>
    public const string MapControls = "gallery-map-controls";

    /// <summary>The declarative first-person camera-controls route.</summary>
    public const string FlyControls = "gallery-fly-controls";

    /// <summary>The application-owned pointer and pen plumbing route.</summary>
    public const string PointerPenInput = "gallery-pointer-pen-input";

    /// <summary>The MAUI animation/scroll to Mu3D progress-bridge route.</summary>
    public const string ProgressBridge = "gallery-progress-bridge";

    /// <summary>The application-owned playable and viewport transport-bar route.</summary>
    public const string PlaybackToolbar = "gallery-playback-toolbar";

    /// <summary>The optional interactive orientation-axes route.</summary>
    public const string AxesHelper = "gallery-axes-helper";

    /// <summary>The finite scene-space reference-grid route.</summary>
    public const string GridHelper = "gallery-grid-helper";

    /// <summary>The target-bound world-axis-aligned outline route.</summary>
    public const string BoundsHelper = "gallery-bounds-helper";

    /// <summary>The successful-frame MAUI UI-anchor route.</summary>
    public const string UiAnchors = "gallery-ui-anchors";

    /// <summary>The stable and extensible render-output route.</summary>
    public const string RenderOutputs = "gallery-render-outputs";

    /// <summary>The interactive transform-gizmo route.</summary>
    public const string TransformGizmo = "gallery-transform-gizmo";

    /// <summary>The throttled frame-statistics route.</summary>
    public const string FrameStatistics = "gallery-frame-statistics";

    /// <summary>The lighting laboratory route.</summary>
    public const string LightingLab = "gallery-lighting-lab";

    /// <summary>The shadow and ambient-occlusion route.</summary>
    public const string OcclusionLab = "gallery-occlusion-lab";

    /// <summary>The PBR material and texture route.</summary>
    public const string MaterialTexture = "gallery-material-texture";

    /// <summary>The OpenPBR authoring and MaterialX interchange route.</summary>
    public const string OpenPbr = "gallery-openpbr";

    /// <summary>The OpenPBR white-furnace energy diagnostic route.</summary>
    public const string OpenPbrFurnace = "gallery-openpbr-furnace";

    /// <summary>The explicit GPU color LUT route.</summary>
    public const string ColorLut = "gallery-color-lut";

    /// <summary>Gets the explicit AgX and ACES display-view example route.</summary>
    public const string ColorManagement = "gallery-color-management";
#if MU3D_PRINTING
    /// <summary>Optional CMYK conversion and soft-proof example.</summary>
    public const string CmykPrinting = "gallery-cmyk-printing";
#endif

    /// <summary>The HDR tiled canvas route.</summary>
    public const string HdrCanvas = "gallery-hdr-canvas";

    /// <summary>The animation laboratory route.</summary>
    public const string AnimationLab = "gallery-animation-lab";

    /// <summary>The basic glTF loading route.</summary>
    public const string GltfLoad = "gallery-gltf-load";

    /// <summary>The reusable glTF definition and independent-instance route.</summary>
    public const string GltfInstances = "gallery-gltf-instances";

    /// <summary>The bounded virtualized glTF product-feed route.</summary>
    public const string GltfProductFeed = "gallery-gltf-product-feed";

    /// <summary>The glTF animation route.</summary>
    public const string GltfAnimation = "gallery-gltf-animation";

    /// <summary>The authored glTF material-variants route.</summary>
    public const string GltfVariants = "gallery-gltf-variants";

    /// <summary>The material-conformance route.</summary>
    public const string ModelLab = "gallery-model-lab";

    /// <summary>The HDR JPEG laboratory route.</summary>
    public const string HdrJpegLab = "gallery-hdr-jpeg-lab";

    /// <summary>The KTX texture laboratory route.</summary>
    public const string KtxTextureLab = "gallery-ktx-texture-lab";

    /// <summary>The KTX image-decode route.</summary>
    public const string KtxDecode = "gallery-ktx-decode";

    /// <summary>The license index route.</summary>
    public const string Licenses = "gallery-licenses";
}

using Mu3D.GalleryApp;

namespace Mu3D.Gallery;

// Metadata is portable; navigation templates and page creation belong to AppShell.
internal static class GalleryCatalog
{
    internal static IReadOnlyList<GallerySection> Sections { get; } =
    [
        new("basics", "Basics", "Surfaces, scene authoring and HDR color.", "palette.svg"),
        new("rendering", "Rendering", "Materials, lighting, shadows and animation.", "material.svg"),
        new("toolkit", "Toolkit", "Camera controls, input, helpers and viewport tools.", "toolbox.svg"),
        new("assets", "Assets", "Models, instances, feeds and image codecs.", "feed.svg"),
        new("about", "About", "Licenses and acknowledgements.", "about.svg"),
    ];

    internal static IReadOnlyList<GalleryEntry> Examples { get; } = CreateExamples();

    internal static GalleryEntry Find(string id) => Examples.Single(entry => entry.Id == id);

    internal static IEnumerable<GalleryEntry> Search(string? query, string? sectionId = null)
    {
        string[] terms = (query ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return Examples.Where(entry => (sectionId is null || entry.SectionId == sectionId) &&
            terms.All(term => entry.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    private static IReadOnlyList<GalleryEntry> CreateExamples()
    {
        List<GalleryEntry> entries = [];
        foreach (GalleryExample example in GalleryNavigationCatalog.Examples)
        {
            string featureId = GalleryFeatureCatalog.FeatureId(example.Route);
            string sectionId = SectionFor(featureId);
            entries.Add(new(featureId, example.Title, example.Description, sectionId,
                Sections.Single(section => section.Id == sectionId).Title, featureId, example.Route));
        }
        entries.Add(new("material-conformance", "Material Conformance",
            "Inspect the complete glTF material reference assets and renderer diagnostics.",
            "rendering", "Rendering", "material-conformance", GalleryRoutes.ModelLab));
        entries.Add(new("emissive-material", "Emissive Material",
            "Adjust emissive HDR radiance and exposure on declarative PBR materials.",
            "rendering", "Rendering", "pbr-material-textures"));
        entries.Add(new("translucent-canvas", "Translucent HDR Canvas",
            "Paint HDR dabs in ACEScg and inspect explicit surface alpha and its fallback.",
            "basics", "Basics", "hdr-canvas"));
        entries.Add(new("painting-color-spaces", "Painting Color Spaces",
            "Adjust Lab, LCh, OKLab, OKLCh, YUV, YCbCr, HSL and HSV with live color ramps. 绘画调色空间",
            "basics", "Basics", "painting-color-spaces"));
        entries.Add(new("procedural-feed", "Procedural 3D Feed",
            "Inspect shared-device surface proxies using generated geometry and scroll visibility.",
            "assets", "Assets", "gltf-product-feed"));
        entries.Add(new("licenses", "Licenses",
            "Read the navigation, native runtime and bundled asset acknowledgements.",
            "about", "About", null));
        return entries.AsReadOnly();
    }

    private static string SectionFor(string id) => id switch
    {
        "pbr-material-textures" or "openpbr-materialx" or "openpbr-white-furnace" or
        "lighting-direct-ibl" or "shadows-ao" or "render-outputs" or "skinning-animation" => "rendering",
        "declarative-tools" or "orbit-controls" or "map-controls" or "fly-controls" or
        "pointer-pen-input" or "progress-bridge" or "playback-toolbar" or "axes-helper" or
        "grid-helper" or "bounds-helper" or "ui-anchors" or "transform-gizmo" or "frame-statistics" => "toolkit",
        "gltf-loading" or "gltf-instances" or "gltf-product-feed" or "gltf-animation" or
        "gltf-material-variants" or "hdr-jpeg" or "ktx2-decode" or "ktx2-gpu-sampling" => "assets",
        _ => "basics",
    };
}

internal sealed record GallerySection(string Id, string Title, string Description, string Icon);

internal sealed record GalleryEntry(string Id, string Title, string Description, string SectionId,
    string SectionTitle, string? FeatureId, string? LegacyRoute = null)
{
    public string SearchText => $"{Id} {Title} {Description} {SectionTitle}";
}

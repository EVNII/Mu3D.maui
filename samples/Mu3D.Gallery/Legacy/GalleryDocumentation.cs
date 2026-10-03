using System.Reflection;

namespace Mu3D.GalleryApp;

internal static class GalleryDocumentation
{
    private static readonly GalleryDocumentationFeature[] Features =
    [
        new("gallery-overview", "articles/features/gallery-overview.html"),
        new("device-probe", "articles/features/device-probe.html"),
        new("surface-probe", "articles/features/surface-probe.html"),
        new("custom-draw", "articles/features/custom-draw.html"),
        new("scene-assets", "articles/features/scene-assets.html"),
        new("declarative-scene", "articles/features/declarative-scene.html"),
        new("declarative-tools", "articles/features/declarative-tools.html"),
        new("orbit-controls", "articles/features/orbit-controls.html"),
        new("map-controls", "articles/features/map-controls.html"),
        new("fly-controls", "articles/features/fly-controls.html"),
        new("pointer-pen-input", "articles/features/pointer-pen-input.html"),
        new("progress-bridge", "articles/features/progress-bridge.html"),
        new("playback-toolbar", "articles/features/playback-toolbar.html"),
        new("axes-helper", "articles/features/axes-helper.html"),
        new("grid-helper", "articles/features/grid-helper.html"),
        new("bounds-helper", "articles/features/bounds-helper.html"),
        new("ui-anchors", "articles/features/ui-anchors.html"),
        new("render-outputs", "articles/features/render-outputs.html"),
        new("transform-gizmo", "articles/features/transform-gizmo.html"),
        new("frame-statistics", "articles/features/frame-statistics.html"),
        new("lighting-direct-ibl", "articles/features/lighting-direct-ibl.html"),
        new("shadows-ao", "articles/features/shadows-ao.html"),
        new("pbr-material-textures", "articles/features/pbr-material-textures.html"),
        new("openpbr-materialx", "articles/features/openpbr-materialx.html"),
        new("openpbr-white-furnace", "articles/features/openpbr-white-furnace.html"),
        new("color-management", "articles/features/color-management.html"),
        new("painting-color-spaces", "articles/features/painting-color-spaces.html"),
#if MU3D_PRINTING
        new("cmyk-printing", "articles/features/cmyk-printing.html"),
#endif
        new("color-lut", "articles/features/color-lut.html"),
        new("hdr-canvas", "articles/features/hdr-canvas.html"),
        new("skinning-animation", "articles/features/skinning-animation.html"),
        new("gltf-loading", "articles/features/gltf-loading.html"),
        new("gltf-instances", "articles/features/gltf-instances.html"),
        new("gltf-product-feed", "articles/features/gltf-product-feed.html"),
        new("gltf-animation", "articles/features/gltf-animation.html"),
        new("gltf-material-variants", "articles/features/gltf-material-variants.html"),
        new("hdr-jpeg", "articles/features/hdr-jpeg.html"),
        new("ktx2-decode", "articles/features/ktx2-decode.html"),
        new("ktx2-gpu-sampling", "articles/features/ktx2-gpu-sampling.html"),
        new("material-conformance", "articles/features/material-conformance.html"),
    ];

    internal static async Task<bool> OpenAsync(string featureId)
    {
        GalleryDocumentationFeature feature = Features.SingleOrDefault(
            candidate => candidate.Id.Equals(featureId, StringComparison.Ordinal)) ??
            throw new ArgumentException($"Unknown Gallery documentation feature '{featureId}'.", nameof(featureId));
        Uri uri = new(ReadBaseUri(), feature.Article);
        return await Browser.Default.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
    }

    private static Uri ReadBaseUri()
    {
        string? value = typeof(GalleryDocumentation).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute =>
                attribute.Key.Equals("Mu3DDocumentationBaseUrl", StringComparison.Ordinal))
            ?.Value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not "http" and not "https" ||
            !uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Mu3DDocumentationBaseUrl must be an absolute HTTP(S) URI ending in '/'.");
        }
        return uri;
    }

    private sealed record GalleryDocumentationFeature(string Id, string Article);
}

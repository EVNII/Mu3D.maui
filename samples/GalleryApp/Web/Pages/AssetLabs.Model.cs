using System.Diagnostics;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.GalleryApp.Pages;
using Mu3D.Native.Ktx;
using Mu3D.Native.UltraHdr;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Web.Pages;

public partial class AssetLabs
{
    private SceneRenderLayer layer;
    private BloomCompositeMode bloomMode;
    private bool shadows = true, ao = true, bloom;
    private ModelResourceCounts resources;
    private ModelSpec? loadedModel;
    private double performanceElapsed;
    private int performanceFrames;
    private readonly ModelControl[] modelControls =
    [
        new("Camera distance", .45f, 1.4f, 1, (p, v) => p.CameraDistanceScale = v),
        new("Camera orbit", -180, 180, 0, (p, v) => p.CameraAzimuthDegrees = v),
        new("Camera elevation", -80, 80, 0, (p, v) => p.CameraElevationDegrees = v),
        new("AO radius", .1f, 4, 1.5f, (p, v) => p.AmbientOcclusionRadius = v),
        new("AO strength", 0, 1, 1, (p, v) => p.AmbientOcclusionStrength = v),
        new("Bloom threshold", 0, 8, 1, (p, v) => p.BloomThreshold = v),
        new("Bloom soft knee", 0, 2, .5f, (p, v) => p.BloomSoftKnee = v),
        new("Bloom intensity", 0, 2, .15f, (p, v) => p.BloomIntensity = v),
        new("Bloom radius (px)", 1, 96, 16, (p, v) => p.BloomRadiusPixels = v),
        new("Clearcoat × authored", 0, 2, 1, (p, v) => p.ClearcoatFactorScale = v),
        new("Anisotropy × authored", 0, 1, 1, (p, v) => p.AnisotropyStrengthScale = v),
        new("Anisotropy rotation (degrees)", -180, 180, 0, (p, v) => p.AnisotropyRotationOffset = v * MathF.PI / 180),
        new("Transmission × authored", 0, 1, 1, (p, v) => p.TransmissionFactorScale = v),
        new("IOR offset", -.5f, 1.5f, 0, (p, v) => p.TransmissionIndexOfRefractionOffset = v),
        new("Volume thickness × authored", 0, 8, 1, (p, v) => p.VolumeThicknessScale = v),
        new("Dispersion × authored", 0, 2, 1, (p, v) => p.DispersionScale = v),
        new("Volume absorption × authored", 0, 8, 1, (p, v) => p.VolumeAbsorptionStrength = v),
        new("Iridescence × authored", 0, 1, 1, (p, v) => p.IridescenceFactorScale = v),
        new("Iridescence IOR offset", -.3f, 1.5f, 0, (p, v) => p.IridescenceIndexOfRefractionOffset = v),
        new("Film thickness × authored", 0, 4, 1, (p, v) => p.IridescenceThicknessScale = v),
        new("Specular factor × authored", 0, 1, 1, (p, v) => p.SpecularFactorScale = v),
        new("Specular color × authored", 0, 2, 1, (p, v) => p.SpecularColorScale = v),
        new("Light azimuth", -180, 180, -150, (p, v) => p.LightAzimuthDegrees = v),
        new("Light elevation", 5, 85, 35, (p, v) => p.LightElevationDegrees = v),
        new("Light angular diameter (rad)", 0, .4f, .08f, (p, v) => p.LightAngularDiameter = v, .001f),
    ];
    private sealed class ModelControl(string label, float minimum, float maximum, float value,
        Action<ModelLabPresenter, float> apply, float step = .01f)
    {
        internal string Label { get; } = label;
        internal float Minimum { get; } = minimum;
        internal float Maximum { get; } = maximum;
        internal float Step { get; } = step;
        internal float Value { get; set; } = value;
        internal void Apply(ModelLabPresenter presenter) => apply(presenter, Value);
    }
    private async Task LoadModelAsync(int version, CancellationTokenSource operation)
    {
        ModelSpec spec = ModelLabCatalog.Models[modelIndex];
        CancellationToken token = operation.Token;
        Stopwatch timer = Stopwatch.StartNew();
        byte[] source = await GalleryAssets.ReadBytesAsync(spec.PackagePath, token);
        Dictionary<string, ReadOnlyMemory<byte>> external = new(StringComparer.Ordinal);
        string directory = spec.PackagePath[..(spec.PackagePath.LastIndexOf('/') + 1)];
        foreach (string uri in spec.ExternalResources ?? []) external.Add(uri, await GalleryAssets.ReadBytesAsync(directory + uri, token));
        double resourceMilliseconds = timer.Elapsed.TotalMilliseconds;
        if (!Current(version, operation) || surface is null) return;
        ReadOnlyMemory<byte> Resolve(string uri) => external.TryGetValue(uri, out var value) ? value :
            throw new InvalidDataException($"Packaged Model Lab resource '{uri}' was not declared by the model gate.");
        JpegImageDecoder jpeg = new();
        timer.Restart();
        GltfAsset imported = GltfImporter.ImportAssetWithOptions(source, new GltfImportOptions
        {
            ExternalBufferResolver = Resolve,
            ExternalImageResolver = Resolve,
            Name = spec.SceneName,
            ImageDecoder = spec.UseKtx ? new Ktx2ImageDecoder(jpeg, allowBt709PrimariesForLinearData: true) : jpeg,
            TextureTranscoder = spec.UseKtx ? new Ktx2TextureTranscoder(allowBt709PrimariesForLinearData: true) : null,
            GraphicsCapabilities = spec.UseKtx ? surface.Device.Capabilities : null,
        });
        double importMilliseconds = timer.Elapsed.TotalMilliseconds;
        if (spec.AddAmbientOcclusionProbe) ModelLabAssets.AddAmbientOcclusionProbe(imported, spec.CameraTarget);
        if (spec.AddDiagnosticGround) ModelLabAssets.AddDiagnosticGround(imported);
        EquirectangularHdrEnvironment? environment = null;
        if (spec.EnvironmentPath is not null)
        {
            byte[] hdr = await GalleryAssets.ReadBytesAsync(spec.EnvironmentPath, token);
            using MemoryStream stream = new(hdr, writable: false);
            environment = RadianceHdrReader.Read(stream, StandardColorSpaces.LinearSrgb, "Artist Workshop (Poly Haven, CC0, 1K HDR)");
            await SceneRenderer.PrepareImageBasedLightingAsync(environment, token);
        }
        if (!Current(version, operation) || surface is null) return;
        DisposePresenters(); asset = imported; loadedModel = spec; clipIndex = 0; variantIndex = 0; animationTime = 0;
        playing = imported.Animations.Count != 0;
        resources = ModelLabAssets.CountResources(imported);
        modelPresenter = new ModelLabPresenter(surface, imported, spec.CameraPosition, spec.CameraTarget,
            spec.ApplyAllAnimations, environment, spec.DirectionalLightEnabled);
        ApplyModelControls();
        status = $"{spec.DisplayName} ready";
        details = spec.Details + $"\nResource load: {resourceMilliseconds:F1} ms; import: {importMilliseconds:F1} ms\n" +
            $"Source: {source.Length:N0} bytes; external resources: {external.Count}\n" +
            $"Compressed formats: {resources.CompressedFormats}\nIgnored optional extensions: {string.Join(", ", imported.IgnoredOptionalExtensions)}";
        performanceElapsed = 0; performanceFrames = 0;
    }
    private void ApplyModelControls()
    {
        if (modelPresenter is not { } presenter) return;
        presenter.RenderLayer = layer; presenter.ShadowsEnabled = shadows;
        presenter.AmbientOcclusionEnabled = ao; presenter.BloomEnabled = bloom; presenter.BloomCompositeMode = bloomMode;
        foreach (ModelControl control in modelControls) control.Apply(presenter);
    }
    private void UpdateModelPerformance(double delta, uint width, uint height)
    {
        performanceElapsed += delta; performanceFrames++;
        if (performanceElapsed < .25 || modelPresenter is not { } presenter) return;
        var timings = presenter.LastRendererFrameTimings;
        performance = $"FPS: {performanceFrames / performanceElapsed:F1}\nSurface: {width} × {height} · FP16 scene target\n" +
            $"Renderer prepare: {timings.PrepareMilliseconds:F2} ms; encode: {timings.EncodeMilliseconds:F2} ms; submit: {timings.SubmitMilliseconds:F2} ms; cache trim: {timings.CacheTrimMilliseconds:F2} ms\n" +
            $"Managed heap: {GC.GetTotalMemory(false):N0} bytes; GC collections: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}\n" +
            $"Scene: {resources.Meshes} meshes; {resources.Vertices} vertices; {resources.Triangles} triangles\n" +
            $"Geometry channels: {resources.GeometryBytes:N0} bytes; texture CPU: {resources.TextureCpuBytes:N0} bytes; GPU mip estimate: {resources.TextureGpuBytes:N0} bytes\n" +
            $"Textures: {resources.ColorTextures} color / {resources.DataTextures} data; morph targets: {resources.MorphTargets}; skin joints: {resources.Joints}\n" +
            $"Shader variants: {presenter.ObservedMaterialShaderVariantCount}; prepared: {presenter.CachedMaterialShaderVariantCount}; cache hits/misses: {presenter.MaterialShaderVariantCacheHits}/{presenter.MaterialShaderVariantCacheMisses}\n" +
            $"Maximum material sampled textures: {presenter.MaximumObservedMaterialSampledTextureCount}";
        performanceElapsed = 0; performanceFrames = 0;
        _ = InvokeAsync(StateHasChanged);
    }
}

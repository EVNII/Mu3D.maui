using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering;

/// <summary>
/// Renders visible unlit, directly lit and image-lit meshes through the backend-independent
/// graphics API and caches immutable geometry, environments and per-mesh bindings. This renderer is
/// not thread-safe.
/// </summary>
public sealed partial class SceneRenderer : IDisposable
{
    private const int ParallelTextureWorkThreshold = 256 * 256;
    private const int MaximumAdditionalPunctualLightCount = 16;
    private const int FrameUniformSize = 304 + MaximumAdditionalPunctualLightCount * 64;
    private const int DrawUniformSize = 960;
    private const int MaximumMorphTargetCount = 8;
    private const uint DiffuseEnvironmentFaceSize = 32;
    private const uint SpecularEnvironmentFaceSize = 64;
    private const uint SpecularEnvironmentMipCount = 7;
    private const uint SpecularEnvironmentSampleCount = 256;
    private const uint BrdfLutSize = 64;
    private const uint BrdfLutSampleCount = 256;
    private const uint ShadowMapSize = 1024;
    private static readonly ConditionalWeakTable<
        EquirectangularHdrEnvironment,
        Lazy<PreparedEnvironmentData>> PreparedEnvironmentCache = new();
    private static readonly Lazy<SplitSumBrdfLut> SharedBrdfLut = new(
        () => HdrEnvironmentConverter.CreateSplitSumBrdfLut(BrdfLutSize, BrdfLutSampleCount),
        LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly GraphicsDevice device;
    private readonly SharedResources shared;
    private readonly Dictionary<MeshGeometry, GeometryResources> geometryCache = [];
    private readonly Dictionary<Mesh, MeshResources> meshCache = [];
    private readonly Dictionary<EquirectangularHdrEnvironment, EnvironmentResources> environmentCache = [];
    private readonly Dictionary<LinearRgbaImage, MaterialTextureResources> materialTextureCache = [];
    private readonly Dictionary<CompressedMaterialTexture, MaterialTextureResources>
        compressedMaterialTextureCache = [];
    private readonly Dictionary<DataTextureCacheKey, DataTextureResources> dataTextureCache = [];
    private readonly Dictionary<CompressedDataTextureCacheKey, DataTextureResources>
        compressedDataTextureCache = [];
    private readonly Dictionary<MaterialTextureSampling, GraphicsSampler> materialSamplerCache = [];
    private readonly HashSet<MaterialShaderVariant> observedMaterialShaderVariants = [];
    private readonly MaterialTextureResources fallbackMaterialTexture;
    private readonly DataTextureResources fallbackNormalTexture;
    private readonly DataTextureResources fallbackOrmTexture;
    private readonly DataTextureResources fallbackAnisotropyTexture;
    private AmbientOcclusionResources? ambientOcclusionResources;
    private TransmissionResources? transmissionResources;
    private BloomResources? bloomResources;
    private float ambientOcclusionRadius = 0.65f;
    private float ambientOcclusionStrength = 1f;
    private float bloomThreshold = 1f;
    private float bloomSoftKnee = 0.5f;
    private float bloomIntensity = 0.15f;
    private float bloomRadiusPixels = 16f;
    private BloomCompositeMode bloomCompositeMode;
    private SceneRenderLayer renderLayer;
    private bool disposed;

    /// <summary>Gets CPU wall-clock timings for the most recent render invocation.</summary>
    /// <remarks>
    /// Submit may include backend or GPU back-pressure. These values are diagnostic wall-clock
    /// intervals and are not GPU timestamp queries.
    /// </remarks>
    public SceneRendererFrameTimings LastFrameTimings { get; private set; }

    /// <summary>Initializes a scene renderer for one color-target format.</summary>
    public SceneRenderer(GraphicsDevice device, GraphicsTextureFormat colorFormat)
    {
        ArgumentNullException.ThrowIfNull(device);
        Device = device;
        ColorFormat = colorFormat;
        this.device = device;
        SharedResources createdShared = SharedResources.Create(device, colorFormat);
        MaterialTextureResources? createdMaterialFallback = null;
        DataTextureResources? createdNormalFallback = null;
        DataTextureResources? createdOrmFallback = null;
        DataTextureResources? createdAnisotropyFallback = null;
        try
        {
            createdMaterialFallback = MaterialTextureResources.Create(
                device,
                new LinearRgbaImage(
                    1,
                    1,
                    [Vector4.One],
                    StandardColorSpaces.LinearSrgb,
                    "white material fallback"));
            createdNormalFallback = DataTextureResources.Create(
                device,
                new NormalizedRgbaDataImage(
                    1,
                    1,
                    [new Vector4(0.5f, 0.5f, 1f, 1f)],
                    "flat normal fallback"),
                DataTextureSemantic.Normal);
            createdOrmFallback = DataTextureResources.Create(
                device,
                new NormalizedRgbaDataImage(
                    1,
                    1,
                    [Vector4.One],
                    "neutral ORM fallback"),
                DataTextureSemantic.LinearChannels);
            createdAnisotropyFallback = DataTextureResources.Create(
                device,
                new NormalizedRgbaDataImage(
                    1,
                    1,
                    [new Vector4(1f, 0.5f, 1f, 1f)],
                    "default tangent anisotropy fallback"),
                DataTextureSemantic.LinearChannels);
            fallbackMaterialTexture = createdMaterialFallback;
            fallbackNormalTexture = createdNormalFallback;
            fallbackOrmTexture = createdOrmFallback;
            fallbackAnisotropyTexture = createdAnisotropyFallback;
            shared = createdShared;
            createdMaterialFallback = null;
            createdNormalFallback = null;
            createdOrmFallback = null;
            createdAnisotropyFallback = null;
        }
        catch
        {
            createdOrmFallback?.Dispose();
            createdAnisotropyFallback?.Dispose();
            createdNormalFallback?.Dispose();
            createdMaterialFallback?.Dispose();
            createdShared.Dispose();
            throw;
        }
    }

    /// <summary>Gets the owning graphics device.</summary>
    public GraphicsDevice Device { get; }

    /// <summary>Gets the color-target format compiled into the renderer pipelines.</summary>
    public GraphicsTextureFormat ColorFormat { get; }

    /// <summary>Gets the linear-light space used by the current presentation shader.</summary>
    public ColorSpaceReference WorkingColorSpace => StandardColorSpaces.LinearSrgb;

    /// <summary>Gets the number of uploaded immutable geometry objects.</summary>
    public int CachedGeometryCount => geometryCache.Count;

    /// <summary>Gets the number of per-mesh uniform bindings.</summary>
    public int CachedMeshCount => meshCache.Count;

    /// <summary>Gets the number of uploaded diffuse environment cubes.</summary>
    public int CachedEnvironmentCount => environmentCache.Count;

    /// <summary>Gets the number of uploaded authored two-dimensional material images.</summary>
    public int CachedMaterialTextureCount =>
        materialTextureCache.Count + compressedMaterialTextureCache.Count;

    /// <summary>Gets the number of uploaded authored non-color material data images.</summary>
    public int CachedMaterialDataTextureCount =>
        dataTextureCache.Count + compressedDataTextureCache.Count;

    /// <summary>
    /// Gets the number of normalized material shader variants observed by this renderer. This is
    /// the input cardinality for the generated shader/pipeline cache; numeric uniform changes do not
    /// create another entry.
    /// </summary>
    public int ObservedMaterialShaderVariantCount => observedMaterialShaderVariants.Count;

    /// <summary>Gets the largest material-texture count among observed normalized variants.</summary>
    public int MaximumObservedMaterialSampledTextureCount => observedMaterialShaderVariants.Count == 0
        ? 0
        : observedMaterialShaderVariants.Max(static key => key.SampledMaterialTextureCount);

    /// <summary>Gets the number of exact material variants prepared by this renderer.</summary>
    public int CachedMaterialShaderVariantCount => shared.CachedMaterialShaderVariantCount;

    /// <summary>Gets successful lookups of an already prepared exact material variant.</summary>
    public long MaterialShaderVariantCacheHits => shared.MaterialShaderVariantCacheHits;

    /// <summary>Gets first-time preparations of exact material variants.</summary>
    public long MaterialShaderVariantCacheMisses => shared.MaterialShaderVariantCacheMisses;

    /// <summary>
    /// Prepares every exact variant in a manifest before its first draw. This operation creates GPU
    /// shader modules and pipelines and must run on the graphics-device thread. It performs no
    /// reflection or dynamic managed-code generation and is safe for AOT callers.
    /// </summary>
    public void PrewarmMaterialVariants(MaterialVariantManifest manifest)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(manifest);
        foreach (MaterialShaderVariant variant in manifest.Variants)
        {
            shared.PrepareMaterialVariant(variant);
        }
    }

    /// <summary>
    /// Gets or sets whether the renderer performs a camera-depth prepass and applies screen-space
    /// ambient occlusion to indirect diffuse lighting. The default is false because this is an
    /// explicit additional render pass and sampling cost.
    /// </summary>
    public bool AmbientOcclusionEnabled { get; set; }

    /// <summary>
    /// Gets or sets the world-space SSAO search radius. The default is 0.65 world units.
    /// </summary>
    public float AmbientOcclusionRadius
    {
        get => ambientOcclusionRadius;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Ambient-occlusion radius must be finite and greater than zero.");
            }

            ambientOcclusionRadius = value;
        }
    }

    /// <summary>
    /// Gets or sets the SSAO strength in the inclusive range zero to one. The default is one.
    /// </summary>
    public float AmbientOcclusionStrength
    {
        get => ambientOcclusionStrength;
        set
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Ambient-occlusion strength must be finite and in the range [0, 1].");
            }

            ambientOcclusionStrength = value;
        }
    }

    /// <summary>
    /// Gets or sets whether the Beauty layer receives an HDR bloom post-process. The default is
    /// false, so enabling a scene renderer does not silently change authored pixel values.
    /// </summary>
    public bool BloomEnabled { get; set; }

    /// <summary>
    /// Gets or sets the linear working-space brightness at which bloom extraction begins. The
    /// default is one, corresponding to SDR reference white in the renderer's relative-linear
    /// convention.
    /// </summary>
    public float BloomThreshold
    {
        get => bloomThreshold;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Bloom threshold must be finite and nonnegative.");
            }

            bloomThreshold = value;
        }
    }

    /// <summary>
    /// Gets or sets the nonnegative linear-width of the soft transition around
    /// <see cref="BloomThreshold"/>. The default is 0.5.
    /// </summary>
    public float BloomSoftKnee
    {
        get => bloomSoftKnee;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Bloom soft knee must be finite and nonnegative.");
            }

            bloomSoftKnee = value;
        }
    }

    /// <summary>
    /// Gets or sets the nonnegative multiplier applied when the blurred highlight image is added
    /// to Beauty. The default is 0.15.
    /// </summary>
    public float BloomIntensity
    {
        get => bloomIntensity;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Bloom intensity must be finite and nonnegative.");
            }

            bloomIntensity = value;
        }
    }

    /// <summary>
    /// Gets or sets the approximate Bloom blur radius in full-resolution output pixels. The default
    /// is 16 pixels. Larger values spread the halo farther without changing its intensity. The
    /// value must be finite and greater than zero.
    /// </summary>
    public float BloomRadiusPixels
    {
        get => bloomRadiusPixels;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Bloom radius must be finite and greater than zero.");
            }

            bloomRadiusPixels = value;
        }
    }

    /// <summary>
    /// Gets or sets how the blurred HDR highlights are combined with Beauty. The default is
    /// <see cref="BloomCompositeMode.EnergyPreserving"/>; additive brightening must be selected
    /// explicitly.
    /// </summary>
    public BloomCompositeMode BloomCompositeMode
    {
        get => bloomCompositeMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            bloomCompositeMode = value;
        }
    }

    /// <summary>
    /// Gets or sets the lighting contribution shown by the renderer. The default composes the final
    /// beauty result; the other modes are diagnostic and do not alter scene or material data.
    /// </summary>
    public SceneRenderLayer RenderLayer
    {
        get => renderLayer;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            renderLayer = value;
        }
    }

    /// <summary>
    /// Gets or sets the stable built-in output used by the compatibility
    /// <see cref="Render(Scene, Camera, GraphicsTexture, GraphicsTexture)"/> and
    /// <see cref="RenderPass(Scene, Camera, GraphicsTexture, GraphicsTexture, SceneRenderPassOptions)"/>
    /// entry points.
    /// </summary>
    /// <remarks>
    /// Use <see cref="SceneRenderOutputPass"/> or <see cref="RenderOutputRegistry"/> when output
    /// selection belongs to a specific pass. Third-party identifiers require their registered pass
    /// implementation and cannot be assigned directly to this built-in renderer.
    /// </remarks>
    public RenderOutputId RenderOutput
    {
        get => RenderOutputIds.FromSceneRenderLayer(renderLayer);
        set
        {
            if (!RenderOutputIds.TryGetSceneRenderLayer(value, out SceneRenderLayer layer))
            {
                throw new ArgumentException(
                    $"Render output '{value}' is not implemented by SceneRenderer.",
                    nameof(value));
            }

            renderLayer = layer;
        }
    }

    /// <summary>
    /// Precomputes and process-caches the renderer's diffuse and specular image-based-lighting data
    /// without creating GPU resources. Applications may await this off the presentation path so the
    /// first rendered frame only uploads the immutable result. The cache is released when the source
    /// environment is no longer referenced.
    /// </summary>
    /// <param name="environment">The immutable, explicitly color-tagged HDR environment.</param>
    /// <param name="cancellationToken">
    /// A token checked before and after preparation. An integration already executing for another
    /// caller remains available to that caller and may finish populating the shared cache.
    /// </param>
    public static Task PrepareImageBasedLightingAsync(
        EquirectangularHdrEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = GetPreparedEnvironment(environment);
                    cancellationToken.ThrowIfCancellationRequested();
            },
            cancellationToken);
    }

    /// <summary>Clears to opaque linear-sRGB black and renders all visible meshes.</summary>
    public void Render(
        Scene scene,
        Camera camera,
        GraphicsTexture colorTarget,
        GraphicsTexture depthTarget) =>
        RenderPass(scene, camera, colorTarget, depthTarget, SceneRenderPassOptions.Default);

    /// <summary>
    /// Clears and renders all visible <see cref="Mesh"/> descendants using one camera and an
    /// explicitly tagged linear-light clear color. One directional light may cast the initial
    /// shadow map; up to sixteen additional directional, point, or spot lights contribute direct
    /// lighting, alongside one diffuse-plus-specular <see cref="ImageBasedLight"/>.
    /// </summary>
    public void Render(
        Scene scene,
        Camera camera,
        GraphicsTexture colorTarget,
        GraphicsTexture depthTarget,
        LinearRgba clearColor) =>
        RenderPass(
            scene,
            camera,
            colorTarget,
            depthTarget,
            new SceneRenderPassOptions(GraphicsLoadOperation.Clear, clearColor));

    /// <summary>
    /// Renders all camera-visible meshes with explicit scene-linear color and depth attachment
    /// initialization. Loading existing attachments permits ordered composition without inserting
    /// display encoding, gamut mapping, tone mapping, or clipping.
    /// </summary>
    public void RenderPass(
        Scene scene,
        Camera camera,
        GraphicsTexture colorTarget,
        GraphicsTexture depthTarget,
        SceneRenderPassOptions passOptions)
        => RenderPassCore(
            scene,
            camera,
            colorTarget,
            depthTarget,
            passOptions,
            renderLayer);

    /// <summary>
    /// Renders non-overlapping scene regions into one shared color/depth target while preserving a
    /// single renderer and its device-scoped caches.
    /// </summary>
    /// <param name="viewports">The ordered scene regions to render.</param>
    /// <param name="colorTarget">The shared scene-linear color attachment.</param>
    /// <param name="depthTarget">The matching shared Depth32Float attachment.</param>
    /// <param name="clearColor">The explicitly tagged linear-light color used for the frame.</param>
    /// <param name="retainedScenes">
    /// Optional non-rendered warm scenes whose uploaded cache entries remain available.
    /// </param>
    /// <remarks>
    /// This first shared-target path deliberately excludes ambient occlusion, bloom, and
    /// screen-space transmission because those effects currently own full-frame intermediate
    /// targets. Empty input still clears both attachments. The regions must not overlap.
    /// </remarks>
    public void RenderViewports(
        IReadOnlyList<SceneRenderViewport> viewports,
        GraphicsTexture colorTarget,
        GraphicsTexture depthTarget,
        LinearRgba clearColor,
        IEnumerable<Scene>? retainedScenes = null)
    {
        long totalStarted = Stopwatch.GetTimestamp();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(viewports);
        ArgumentNullException.ThrowIfNull(colorTarget);
        ArgumentNullException.ThrowIfNull(depthTarget);
        if (AmbientOcclusionEnabled || BloomEnabled)
        {
            throw new NotSupportedException(
                "Shared scene viewports do not yet support full-frame ambient occlusion or bloom.");
        }
        ValidateRenderTargets(colorTarget, depthTarget);

        SceneRenderPassOptions clearOptions = new(GraphicsLoadOperation.Clear, clearColor);
        if (viewports.Count == 0)
        {
            ClearTargets(colorTarget, depthTarget, clearOptions);
            TrimCaches(retainedScenes ?? []);
            LastFrameTimings = new SceneRendererFrameTimings(
                0d,
                0d,
                0d,
                0d,
                Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds);
            return;
        }

        ValidateViewports(viewports, colorTarget.Descriptor.Size);
        double prepareMilliseconds = 0d;
        double encodeMilliseconds = 0d;
        double submitMilliseconds = 0d;
        for (int index = 0; index < viewports.Count; index++)
        {
            SceneRenderViewport viewport = viewports[index];
            if (viewport.AutomaticallyUpdateCameraAspectRatio &&
                viewport.Camera is PerspectiveCamera perspectiveCamera)
            {
                perspectiveCamera.AspectRatio = (float)viewport.Width / viewport.Height;
            }

            SceneRenderPassOptions options = index == 0
                ? clearOptions
                : new SceneRenderPassOptions(
                    GraphicsLoadOperation.Load,
                    clearColor,
                    GraphicsLoadOperation.Load);
            RenderPassCore(
                viewport.Scene,
                viewport.Camera,
                colorTarget,
                depthTarget,
                options,
                renderLayer,
                viewport,
                trimCaches: false);
            prepareMilliseconds += LastFrameTimings.PrepareMilliseconds;
            encodeMilliseconds += LastFrameTimings.EncodeMilliseconds;
            submitMilliseconds += LastFrameTimings.SubmitMilliseconds;
        }

        long trimStarted = Stopwatch.GetTimestamp();
        IEnumerable<Scene> cacheScenes = viewports.Select(static viewport => viewport.Scene);
        if (retainedScenes is not null)
        {
            cacheScenes = cacheScenes.Concat(retainedScenes);
        }
        TrimCaches(cacheScenes);
        double trimMilliseconds = Stopwatch.GetElapsedTime(trimStarted).TotalMilliseconds;
        LastFrameTimings = new SceneRendererFrameTimings(
            prepareMilliseconds,
            encodeMilliseconds,
            submitMilliseconds,
            trimMilliseconds,
            Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds);
    }

    /// <summary>
    /// Renders one explicit built-in output without changing <see cref="RenderOutput"/> or
    /// <see cref="RenderLayer"/>.
    /// </summary>
    /// <param name="scene">The scene whose camera-visible nodes are rendered.</param>
    /// <param name="camera">The camera and visibility mask used for this pass.</param>
    /// <param name="colorTarget">The scene-linear color attachment.</param>
    /// <param name="depthTarget">The matching Depth32Float attachment.</param>
    /// <param name="output">A stable output identifier implemented by this renderer.</param>
    /// <param name="passOptions">The color and depth attachment initialization policy.</param>
    public void RenderOutputPass(
        Scene scene,
        Camera camera,
        GraphicsTexture colorTarget,
        GraphicsTexture depthTarget,
        RenderOutputId output,
        SceneRenderPassOptions passOptions)
    {
        if (!RenderOutputIds.TryGetSceneRenderLayer(output, out SceneRenderLayer layer))
        {
            throw new ArgumentException(
                $"Render output '{output}' is not implemented by SceneRenderer.",
                nameof(output));
        }

        RenderPassCore(scene, camera, colorTarget, depthTarget, passOptions, layer);
    }

    private void RenderPassCore(
        Scene scene,
        Camera camera,
        GraphicsTexture colorTarget,
        GraphicsTexture depthTarget,
        SceneRenderPassOptions passOptions,
        SceneRenderLayer activeRenderLayer,
        SceneRenderViewport? viewport = null,
        bool trimCaches = true)
    {
        long totalStarted = Stopwatch.GetTimestamp();
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(colorTarget);
        ArgumentNullException.ThrowIfNull(depthTarget);
        ArgumentNullException.ThrowIfNull(passOptions);
        ValidateRenderTargets(colorTarget, depthTarget);

        LinearRgba convertedClear = passOptions.ColorLoadOperation == GraphicsLoadOperation.Clear
            ? ConvertToWorkingSpace(passOptions.ClearColor)
            : new LinearRgba(0f, 0f, 0f, 1f, StandardColorSpaces.LinearSrgb);
        List<SceneNode> visibleNodes = [.. scene.EnumerateVisible(camera.VisibilityMask)];
        List<Mesh> meshes = [.. visibleNodes.OfType<Mesh>()];
        List<PunctualLight> punctualLights = [.. visibleNodes.OfType<PunctualLight>()];
        List<DirectionalLight> directionalLights = [.. punctualLights.OfType<DirectionalLight>()];
        List<ImageBasedLight> environmentLights = [.. visibleNodes.OfType<ImageBasedLight>()];
        if (environmentLights.Count > 1)
        {
            throw new NotSupportedException(
                "The initial image-based-light path supports at most one visible environment.");
        }
        bool requiresLighting = meshes.Any(static mesh =>
            mesh.ShadowCastingMode != MeshShadowCastingMode.ShadowsOnly &&
            mesh.Material is PbrMaterial);
        if (requiresLighting && punctualLights.Count == 0 && environmentLights.Count == 0)
        {
            throw new InvalidOperationException(
                "A visible metallic/roughness material requires a punctual or image-based light.");
        }
        DirectionalLight? light = requiresLighting && directionalLights.Count != 0
            ? directionalLights[0]
            : null;
        List<PunctualLight> additionalLights = requiresLighting
            ? [.. punctualLights.Where(candidate => !ReferenceEquals(candidate, light))]
            : [];
        if (additionalLights.Count > MaximumAdditionalPunctualLightCount)
        {
            throw new NotSupportedException(
                $"The direct-light renderer supports one primary directional light plus at most {MaximumAdditionalPunctualLightCount} additional punctual lights.");
        }

        ImageBasedLight? environmentLight = requiresLighting && environmentLights.Count != 0
            ? environmentLights[0]
            : null;
        GraphicsBindGroup environmentBindGroup = shared.FallbackEnvironmentBindGroup;
        if (environmentLight is not null && environmentLight.Intensity > 0f)
        {
            environmentBindGroup = GetOrCreateEnvironment(environmentLight.Environment).BindGroup;
        }
        GraphicsExtent3D renderExtent = colorTarget.Descriptor.Size;
        AmbientOcclusionResources aoResources = EnsureAmbientOcclusionResources(renderExtent);
        bool useBloom = BloomEnabled && activeRenderLayer == SceneRenderLayer.Beauty;
        if (useBloom && passOptions.ColorLoadOperation == GraphicsLoadOperation.Load)
        {
            throw new NotSupportedException(
                "Loading an existing color attachment is not supported while HDR Bloom owns an intermediate scene target.");
        }
        BloomResources? bloom = useBloom
            ? EnsureBloomResources(renderExtent, bloomRadiusPixels)
            : null;
        GraphicsTexture sceneColorTarget = bloom?.SceneTexture ?? colorTarget;
        bloom?.WriteSettings(
            bloomThreshold,
            bloomSoftKnee,
            bloomIntensity,
            bloomRadiusPixels,
            bloomCompositeMode);
        WriteFrameUniform(
            camera,
            light,
            additionalLights,
            environmentLight,
            activeRenderLayer);

        Matrix4x4 viewProjection = camera.ViewProjectionMatrix;
        List<PreparedDraw> draws = new(meshes.Count);
        Vector3 cameraWorldPosition = Vector3.Transform(Vector3.Zero, camera.WorldMatrix);
        foreach (Mesh mesh in meshes)
        {
            PreparedMaterial material = PrepareMaterial(mesh);
            MaterialShaderVariant shaderKey = material.ShaderKey;
            observedMaterialShaderVariants.Add(shaderKey);
            ValidateSkin(mesh);
            if (material.Kind == MaterialKind.MetallicRoughness &&
                !Matrix4x4.Invert(mesh.WorldMatrix, out _))
            {
                // A zero-scale animation key has no defined normal transform and produces
                // degenerate lit geometry. Omit only that mesh for this frame; it becomes
                // renderable again as soon as its transform is invertible.
                continue;
            }
            if (material.AlphaMode == MaterialAlphaMode.Mask &&
                material.Color.Alpha < material.AlphaCutoff)
            {
                continue;
            }
            GeometryResources geometry;
            MeshResources resources;
            if (meshCache.TryGetValue(mesh, out MeshResources? cachedMesh) &&
                cachedMesh.Matches(
                    mesh.Material,
                    mesh.Material.TextureBindingRevision,
                    shaderKey,
                    mesh.Geometry,
                    mesh.Skin))
            {
                geometry = cachedMesh.Geometry;
                resources = cachedMesh;
            }
            else
            {
            geometry = GetOrCreateGeometry(mesh.Geometry);
            MaterialTextureResources materialTexture = material.CompressedBaseColorTexture is not null
                ? GetOrCreateCompressedMaterialTexture(material.CompressedBaseColorTexture)
                : material.BaseColorTexture is not null
                    ? GetOrCreateMaterialTexture(material.BaseColorTexture)
                    : fallbackMaterialTexture;
            MaterialTextureResources emissiveTexture = material.CompressedEmissiveTexture is not null
                ? GetOrCreateCompressedMaterialTexture(material.CompressedEmissiveTexture)
                : material.EmissiveTexture is not null
                    ? GetOrCreateMaterialTexture(material.EmissiveTexture)
                    : fallbackMaterialTexture;
            DataTextureResources normalTexture = material.CompressedNormalTexture is not null
                ? GetOrCreateCompressedDataTexture(
                    material.CompressedNormalTexture,
                    DataTextureSemantic.Normal)
                : material.NormalTexture is not null
                    ? GetOrCreateDataTexture(material.NormalTexture, DataTextureSemantic.Normal)
                    : fallbackNormalTexture;
            DataTextureResources ormTexture = material.CompressedOrmTexture is not null
                ? GetOrCreateCompressedDataTexture(
                    material.CompressedOrmTexture,
                    DataTextureSemantic.LinearChannels)
                : material.OrmTexture is not null
                    ? GetOrCreateDataTexture(material.OrmTexture, DataTextureSemantic.LinearChannels)
                    : fallbackOrmTexture;
            DataTextureResources clearcoatTexture = material.CompressedClearcoatTexture is not null
                ? GetOrCreateCompressedDataTexture(
                    material.CompressedClearcoatTexture,
                    DataTextureSemantic.LinearChannels)
                : material.ClearcoatTexture is not null
                    ? GetOrCreateDataTexture(material.ClearcoatTexture, DataTextureSemantic.LinearChannels)
                    : fallbackOrmTexture;
            DataTextureResources clearcoatRoughnessTexture =
                material.CompressedClearcoatRoughnessTexture is not null
                    ? GetOrCreateCompressedDataTexture(
                        material.CompressedClearcoatRoughnessTexture,
                        DataTextureSemantic.LinearChannels)
                    : material.ClearcoatRoughnessTexture is not null
                        ? GetOrCreateDataTexture(
                            material.ClearcoatRoughnessTexture,
                            DataTextureSemantic.LinearChannels)
                        : fallbackOrmTexture;
            DataTextureResources clearcoatNormalTexture =
                material.CompressedClearcoatNormalTexture is not null
                    ? GetOrCreateCompressedDataTexture(
                        material.CompressedClearcoatNormalTexture,
                        DataTextureSemantic.Normal)
                    : material.ClearcoatNormalTexture is not null
                        ? GetOrCreateDataTexture(
                            material.ClearcoatNormalTexture,
                            DataTextureSemantic.Normal)
                        : fallbackNormalTexture;
            DataTextureResources anisotropyTexture = material.CompressedAnisotropyTexture is not null
                ? GetOrCreateCompressedDataTexture(
                    material.CompressedAnisotropyTexture,
                    DataTextureSemantic.LinearChannels)
                : material.AnisotropyTexture is not null
                    ? GetOrCreateDataTexture(material.AnisotropyTexture, DataTextureSemantic.LinearChannels)
                    : fallbackAnisotropyTexture;
            bool usesCompactTextureBindings = MaterialTextureBindingPlan.RequiresSpecialization(
                shaderKey);
            DataTextureResources transmissionTexture = material.CompressedTransmissionTexture is not null
                ? GetOrCreateCompressedDataTexture(
                    material.CompressedTransmissionTexture,
                    DataTextureSemantic.LinearChannels)
                : material.TransmissionTexture is not null
                    ? GetOrCreateDataTexture(material.TransmissionTexture, DataTextureSemantic.LinearChannels)
                    : fallbackOrmTexture;
            DataTextureResources thicknessTexture = material.CompressedVolumeThicknessTexture is not null
                ? GetOrCreateCompressedDataTexture(
                    material.CompressedVolumeThicknessTexture,
                    DataTextureSemantic.LinearChannels)
                : material.VolumeThicknessTexture is not null
                    ? GetOrCreateDataTexture(material.VolumeThicknessTexture, DataTextureSemantic.LinearChannels)
                    : fallbackOrmTexture;
            MaterialTextureResources sheenColorTexture = material.CompressedSheenColorTexture is not null
                ? GetOrCreateCompressedMaterialTexture(material.CompressedSheenColorTexture)
                : material.SheenColorTexture is not null
                    ? GetOrCreateMaterialTexture(material.SheenColorTexture)
                    : fallbackMaterialTexture;
            DataTextureResources sheenRoughnessTexture =
                material.CompressedSheenRoughnessTexture is not null
                    ? GetOrCreateCompressedDataTexture(
                        material.CompressedSheenRoughnessTexture,
                        DataTextureSemantic.LinearChannels)
                    : material.SheenRoughnessTexture is not null
                        ? GetOrCreateDataTexture(
                            material.SheenRoughnessTexture,
                            DataTextureSemantic.LinearChannels)
                        : fallbackOrmTexture;
            DataTextureResources iridescenceTexture =
                material.CompressedIridescenceTexture is not null
                    ? GetOrCreateCompressedDataTexture(
                        material.CompressedIridescenceTexture,
                        DataTextureSemantic.LinearChannels)
                    : material.IridescenceTexture is not null
                        ? GetOrCreateDataTexture(
                            material.IridescenceTexture,
                            DataTextureSemantic.LinearChannels)
                        : fallbackOrmTexture;
            DataTextureResources iridescenceThicknessTexture =
                material.CompressedIridescenceThicknessTexture is not null
                    ? GetOrCreateCompressedDataTexture(
                        material.CompressedIridescenceThicknessTexture,
                        DataTextureSemantic.LinearChannels)
                    : material.IridescenceThicknessTexture is not null
                        ? GetOrCreateDataTexture(
                            material.IridescenceThicknessTexture,
                            DataTextureSemantic.LinearChannels)
                        : fallbackOrmTexture;
            DataTextureResources specularTexture = material.CompressedSpecularTexture is not null
                ? GetOrCreateCompressedDataTexture(
                    material.CompressedSpecularTexture,
                    DataTextureSemantic.LinearChannels)
                : material.SpecularTexture is not null
                    ? GetOrCreateDataTexture(material.SpecularTexture, DataTextureSemantic.LinearChannels)
                    : fallbackOrmTexture;
            MaterialTextureResources specularColorTexture =
                material.CompressedSpecularColorTexture is not null
                    ? GetOrCreateCompressedMaterialTexture(material.CompressedSpecularColorTexture)
                    : material.SpecularColorTexture is not null
                        ? GetOrCreateMaterialTexture(material.SpecularColorTexture)
                        : fallbackMaterialTexture;
            DataTextureResources diffuseTransmissionTexture =
                material.CompressedDiffuseTransmissionTexture is not null
                    ? GetOrCreateCompressedDataTexture(
                        material.CompressedDiffuseTransmissionTexture,
                        DataTextureSemantic.LinearChannels)
                    : material.DiffuseTransmissionTexture is not null
                        ? GetOrCreateDataTexture(
                            material.DiffuseTransmissionTexture,
                            DataTextureSemantic.LinearChannels)
                        : fallbackOrmTexture;
            MaterialTextureResources diffuseTransmissionColorTexture =
                material.CompressedDiffuseTransmissionColorTexture is not null
                    ? GetOrCreateCompressedMaterialTexture(material.CompressedDiffuseTransmissionColorTexture)
                    : material.DiffuseTransmissionColorTexture is not null
                        ? GetOrCreateMaterialTexture(material.DiffuseTransmissionColorTexture)
                        : fallbackMaterialTexture;
            GraphicsSampler baseColorSampler = GetOrCreateMaterialSampler(
                material.BaseColorTextureMapping.Sampling);
            GraphicsSampler emissiveSampler = GetOrCreateMaterialSampler(
                material.EmissiveTextureMapping.Sampling);
            GraphicsSampler normalSampler = GetOrCreateMaterialSampler(
                material.NormalTextureMapping.Sampling);
            GraphicsSampler ormSampler = GetOrCreateMaterialSampler(
                material.OrmTextureMapping.Sampling);
            GraphicsSampler clearcoatSampler = GetOrCreateMaterialSampler(
                material.ClearcoatTextureMapping.Sampling);
            GraphicsSampler clearcoatRoughnessSampler = GetOrCreateMaterialSampler(
                material.ClearcoatRoughnessTextureMapping.Sampling);
            GraphicsSampler clearcoatNormalSampler = GetOrCreateMaterialSampler(
                material.ClearcoatNormalTextureMapping.Sampling);
            GraphicsSampler anisotropySampler = GetOrCreateMaterialSampler(
                material.AnisotropyTextureMapping.Sampling);
            GraphicsSampler transmissionSampler = GetOrCreateMaterialSampler(
                material.TransmissionTextureMapping.Sampling);
            GraphicsSampler thicknessSampler = GetOrCreateMaterialSampler(
                material.VolumeThicknessTextureMapping.Sampling);
            GraphicsSampler sheenColorSampler = GetOrCreateMaterialSampler(
                material.SheenColorTextureMapping.Sampling);
            GraphicsSampler sheenRoughnessSampler = GetOrCreateMaterialSampler(
                material.SheenRoughnessTextureMapping.Sampling);
            GraphicsSampler iridescenceSampler = GetOrCreateMaterialSampler(
                material.IridescenceTextureMapping.Sampling);
            GraphicsSampler iridescenceThicknessSampler = GetOrCreateMaterialSampler(
                material.IridescenceThicknessTextureMapping.Sampling);
            GraphicsSampler specularSampler = GetOrCreateMaterialSampler(
                material.SpecularTextureMapping.Sampling);
            GraphicsSampler specularColorSampler = GetOrCreateMaterialSampler(
                material.SpecularColorTextureMapping.Sampling);
            GraphicsSampler diffuseTransmissionSampler = GetOrCreateMaterialSampler(
                material.DiffuseTransmissionTextureMapping.Sampling);
            GraphicsSampler diffuseTransmissionColorSampler = GetOrCreateMaterialSampler(
                material.DiffuseTransmissionColorTextureMapping.Sampling);
            MaterialPhysicalTextureBinding[] textureBindings =
            [
                new(baseColorSampler, materialTexture, materialTexture.View),
                new(normalSampler, normalTexture, normalTexture.View),
                new(ormSampler, ormTexture, ormTexture.View),
                new(emissiveSampler, emissiveTexture, emissiveTexture.View),
                new(clearcoatSampler, clearcoatTexture, clearcoatTexture.View),
                new(clearcoatRoughnessSampler, clearcoatRoughnessTexture, clearcoatRoughnessTexture.View),
                new(clearcoatNormalSampler, clearcoatNormalTexture, clearcoatNormalTexture.View),
                new(anisotropySampler, anisotropyTexture, anisotropyTexture.View),
                new(transmissionSampler, transmissionTexture, transmissionTexture.View),
                new(thicknessSampler, thicknessTexture, thicknessTexture.View),
            ];
            if (usesCompactTextureBindings)
            {
                MaterialTextureBindingPlan plan = MaterialTextureBindingPlan.Create(shaderKey);
                MaterialPhysicalTextureBinding fallbackBinding =
                    new(ormSampler, fallbackOrmTexture, fallbackOrmTexture.View);
                MaterialPhysicalTextureBinding[] compactBindings =
                    Enumerable.Repeat(fallbackBinding, MaterialTextureBindingPlan.PhysicalSlotCount).ToArray();
                SetCompactBinding(PbrMaterialTextureBindings.BaseColor, textureBindings[0]);
                SetCompactBinding(PbrMaterialTextureBindings.Normal, textureBindings[1]);
                SetCompactBinding(PbrMaterialTextureBindings.OcclusionRoughnessMetallic, textureBindings[2]);
                SetCompactBinding(PbrMaterialTextureBindings.Emissive, textureBindings[3]);
                SetCompactBinding(PbrMaterialTextureBindings.ClearcoatFactor, textureBindings[4]);
                SetCompactBinding(PbrMaterialTextureBindings.ClearcoatRoughness, textureBindings[5]);
                SetCompactBinding(PbrMaterialTextureBindings.ClearcoatNormal, textureBindings[6]);
                SetCompactBinding(PbrMaterialTextureBindings.Anisotropy, textureBindings[7]);
                SetCompactBinding(PbrMaterialTextureBindings.Transmission, textureBindings[8]);
                SetCompactBinding(PbrMaterialTextureBindings.VolumeThickness, textureBindings[9]);
                SetCompactBinding(PbrMaterialTextureBindings.SheenColor,
                    new(sheenColorSampler, sheenColorTexture, sheenColorTexture.View));
                SetCompactBinding(PbrMaterialTextureBindings.SheenRoughness,
                    new(sheenRoughnessSampler, sheenRoughnessTexture, sheenRoughnessTexture.View));
                SetCompactBinding(PbrMaterialTextureBindings.IridescenceFactor,
                    new(iridescenceSampler, iridescenceTexture, iridescenceTexture.View));
                SetCompactBinding(PbrMaterialTextureBindings.IridescenceThickness,
                    new(iridescenceThicknessSampler,
                        iridescenceThicknessTexture,
                        iridescenceThicknessTexture.View));
                SetCompactBinding(PbrMaterialTextureBindings.SpecularFactor,
                    new(specularSampler, specularTexture, specularTexture.View));
                SetCompactBinding(PbrMaterialTextureBindings.SpecularColor,
                    new(specularColorSampler, specularColorTexture, specularColorTexture.View));
                SetCompactBinding(PbrMaterialTextureBindings.DiffuseTransmissionFactor,
                    new(diffuseTransmissionSampler,
                        diffuseTransmissionTexture,
                        diffuseTransmissionTexture.View));
                SetCompactBinding(PbrMaterialTextureBindings.DiffuseTransmissionColor,
                    new(diffuseTransmissionColorSampler,
                        diffuseTransmissionColorTexture,
                        diffuseTransmissionColorTexture.View));
                textureBindings = compactBindings;

                void SetCompactBinding(
                    PbrMaterialTextureBindings semantic,
                    MaterialPhysicalTextureBinding binding)
                {
                    int slot = plan.GetPhysicalSlot(semantic);
                    if (slot >= 0)
                    {
                        compactBindings[slot] = binding;
                    }
                }
            }
            resources = GetOrCreateMesh(
                mesh,
                geometry,
                textureBindings,
                shaderKey);
            }
            WriteSkinPalette(resources, mesh);
            WriteDrawUniform(resources, mesh, material);
            bool isTransmission = material.TransmissionFactor > 0f;
            bool isTransparent = material.AlphaMode == MaterialAlphaMode.Blend || isTransmission;
            bool usesAlphaTestedDepth = material.AlphaMode == MaterialAlphaMode.Mask &&
                (material.BaseColorTexture is not null || material.CompressedBaseColorTexture is not null);
            bool isCameraVisible = mesh.ShadowCastingMode != MeshShadowCastingMode.ShadowsOnly;
            bool castsShadow = mesh.ShadowCastingMode != MeshShadowCastingMode.Off &&
                !isTransparent;
            Vector3 meshWorldPosition = Vector3.Transform(Vector3.Zero, mesh.WorldMatrix);
            GraphicsRenderPipeline pipeline = shared.SelectPipeline(
                material.Kind,
                material.IsDoubleSided,
                isTransparent,
                shaderKey);
            draws.Add(new PreparedDraw(
                geometry,
                resources,
                pipeline,
                shaderKey,
                isCameraVisible,
                castsShadow,
                isTransparent,
                isTransmission,
                usesAlphaTestedDepth,
                material.IsDoubleSided,
                Vector3.DistanceSquared(cameraWorldPosition, meshWorldPosition)));
        }
        PreparedDraw[] opaqueDraws =
            [.. draws.Where(static draw => draw.IsCameraVisible && !draw.IsTransparent)];
        PreparedDraw[] transparentDraws =
        [
            .. draws
                .Where(static draw => draw.IsCameraVisible && draw.IsTransparent)
                .OrderByDescending(static draw => draw.CameraDistanceSquared),
        ];
        bool hasTransmission = transparentDraws.Any(static draw => draw.IsTransmission);
        if (hasTransmission && passOptions.DepthLoadOperation == GraphicsLoadOperation.Load)
        {
            throw new NotSupportedException(
                "Loading an existing depth attachment is not supported while screen-space transmission performs its opaque background pass.");
        }
        TransmissionResources? transmission = hasTransmission
            ? EnsureTransmissionResources(renderExtent, aoResources)
            : null;

        double prepareMilliseconds = Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds;
        long encodeStarted = Stopwatch.GetTimestamp();

        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("scene frame encoder");
        if (AmbientOcclusionEnabled)
        {
            using GraphicsRenderPassEncoder depthPrepass = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassDepthAttachment(aoResources.DepthTexture),
                    "scene ambient-occlusion depth prepass"));
            foreach (PreparedDraw draw in opaqueDraws)
            {
                depthPrepass.SetPipeline(draw.UsesAlphaTestedDepth
                    ? shared.AlphaTestedDepthPrepassPipeline
                    : shared.DepthPrepassPipeline);
                depthPrepass.SetBindGroup(0, shared.FrameBindGroup);
                depthPrepass.SetBindGroup(1, draw.Mesh.BindGroup);
                depthPrepass.SetVertexBuffer(0, draw.Geometry.VertexBuffer);
                depthPrepass.SetIndexBuffer(draw.Geometry.IndexBuffer, draw.Geometry.IndexFormat);
                depthPrepass.DrawIndexed(draw.Geometry.IndexCount);
            }
        }
        if (light?.CastsShadows == true && light.ShadowOpacity > 0f)
        {
            using GraphicsRenderPassEncoder shadowPass = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassDepthAttachment(shared.ShadowTexture),
                    "scene directional shadow pass"));
            foreach (PreparedDraw draw in draws)
            {
                if (!draw.CastsShadow)
                {
                    continue;
                }
                shadowPass.SetPipeline(shared.SelectShadowPipeline(
                    draw.UsesAlphaTestedDepth,
                    draw.IsDoubleSided));
                shadowPass.SetBindGroup(0, shared.FrameBindGroup);
                shadowPass.SetBindGroup(1, draw.Mesh.BindGroup);
                shadowPass.SetVertexBuffer(0, draw.Geometry.VertexBuffer);
                shadowPass.SetIndexBuffer(draw.Geometry.IndexBuffer, draw.Geometry.IndexFormat);
                shadowPass.DrawIndexed(draw.Geometry.IndexCount);
            }
        }
        if (hasTransmission)
        {
            if (activeRenderLayer is SceneRenderLayer.Transmission or SceneRenderLayer.VolumeAttenuation)
            {
                WriteFrameUniform(
                    camera,
                    light,
                    additionalLights,
                    environmentLight,
                    SceneRenderLayer.Beauty,
                    shared.BackgroundFrameUniformBuffer);
            }
            using GraphicsRenderPassEncoder backgroundPass = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(
                        transmission!.BackgroundRenderView,
                        clearColor: new GraphicsClearColor(
                            convertedClear.Red,
                            convertedClear.Green,
                            convertedClear.Blue,
                            convertedClear.Alpha)),
                    "scene opaque HDR transmission-background pass",
                    new GraphicsRenderPassDepthAttachment(
                        depthTarget,
                        GraphicsLoadOperation.Clear,
                        clearValue: passOptions.ClearDepth)));
            foreach (PreparedDraw draw in opaqueDraws)
            {
                backgroundPass.SetPipeline(draw.Pipeline);
                backgroundPass.SetBindGroup(
                    0,
                    activeRenderLayer is SceneRenderLayer.Transmission or SceneRenderLayer.VolumeAttenuation
                        ? shared.BackgroundFrameBindGroup
                        : shared.FrameBindGroup);
                backgroundPass.SetBindGroup(1, draw.Mesh.BindGroup);
                backgroundPass.SetBindGroup(2, environmentBindGroup);
                backgroundPass.SetBindGroup(3, aoResources.BindGroup);
                backgroundPass.SetVertexBuffer(0, draw.Geometry.VertexBuffer);
                backgroundPass.SetIndexBuffer(draw.Geometry.IndexBuffer, draw.Geometry.IndexFormat);
                backgroundPass.DrawIndexed(draw.Geometry.IndexCount);
            }
            backgroundPass.End();
            transmission.RecordMipPyramid(encoder);
        }
        using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(
                sceneColorTarget,
                bloom is null
                    ? passOptions.ColorLoadOperation
                    : GraphicsLoadOperation.Clear,
                GraphicsStoreOperation.Store,
                clearColor: new GraphicsClearColor(
                    convertedClear.Red,
                    convertedClear.Green,
                    convertedClear.Blue,
                    convertedClear.Alpha)),
            "scene frame pass",
            new GraphicsRenderPassDepthAttachment(
                depthTarget,
                passOptions.DepthLoadOperation,
                clearValue: passOptions.ClearDepth))))
        {
            ApplyViewport(pass, viewport);
            foreach (PreparedDraw draw in opaqueDraws.Concat(transparentDraws))
            {
                pass.SetPipeline(draw.Pipeline);
                pass.SetBindGroup(0, shared.FrameBindGroup);
                pass.SetBindGroup(1, draw.Mesh.BindGroup);
                pass.SetBindGroup(2, environmentBindGroup);
                pass.SetBindGroup(3, transmission?.BindGroup ?? aoResources.BindGroup);
                pass.SetVertexBuffer(0, draw.Geometry.VertexBuffer);
                pass.SetIndexBuffer(draw.Geometry.IndexBuffer, draw.Geometry.IndexFormat);
                pass.DrawIndexed(draw.Geometry.IndexCount);
            }
        }
        bloom?.RecordPostProcess(encoder, colorTarget);
        using GraphicsCommandBuffer commands = encoder.Finish("scene frame commands");
        double encodeMilliseconds = Stopwatch.GetElapsedTime(encodeStarted).TotalMilliseconds;
        long submitStarted = Stopwatch.GetTimestamp();
        device.Queue.Submit(commands);
        double submitMilliseconds = Stopwatch.GetElapsedTime(submitStarted).TotalMilliseconds;
        long trimStarted = Stopwatch.GetTimestamp();
        if (trimCaches)
        {
            TrimCaches(scene);
        }
        double trimMilliseconds = Stopwatch.GetElapsedTime(trimStarted).TotalMilliseconds;
        LastFrameTimings = new SceneRendererFrameTimings(
            prepareMilliseconds,
            encodeMilliseconds,
            submitMilliseconds,
            trimMilliseconds,
            Stopwatch.GetElapsedTime(totalStarted).TotalMilliseconds);
    }

    private void ValidateRenderTargets(
        GraphicsTexture colorTarget,
        GraphicsTexture depthTarget)
    {
        if (colorTarget.Descriptor.Format != ColorFormat)
        {
            throw new ArgumentException(
                "The color target does not match the renderer format.",
                nameof(colorTarget));
        }
        if (depthTarget.Descriptor.Format != GraphicsTextureFormat.Depth32Float)
        {
            throw new ArgumentException("The scene renderer requires Depth32Float.", nameof(depthTarget));
        }
        if (colorTarget.Descriptor.Size != depthTarget.Descriptor.Size)
        {
            throw new ArgumentException(
                "The shared color and depth targets must have matching extents.",
                nameof(depthTarget));
        }
    }

    private static void ValidateViewports(
        IReadOnlyList<SceneRenderViewport> viewports,
        GraphicsExtent3D targetExtent)
    {
        for (int index = 0; index < viewports.Count; index++)
        {
            SceneRenderViewport viewport = viewports[index] ??
                throw new ArgumentException("A shared viewport cannot be null.", nameof(viewports));
            if (viewport.X > targetExtent.Width || viewport.Width > targetExtent.Width - viewport.X ||
                viewport.Y > targetExtent.Height || viewport.Height > targetExtent.Height - viewport.Y)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(viewports),
                    "Every shared viewport must be contained by the render target.");
            }
            if (viewport.Scene
                .EnumerateVisible(viewport.Camera.VisibilityMask)
                .OfType<Mesh>()
                .Any(static mesh =>
                    mesh.Material is PbrMaterial material && material.TransmissionFactor > 0f))
            {
                throw new NotSupportedException(
                    "Shared scene viewports do not yet support screen-space transmission.");
            }

            for (int previousIndex = 0; previousIndex < index; previousIndex++)
            {
                if (Overlaps(viewport, viewports[previousIndex]))
                {
                    throw new ArgumentException(
                        "Shared scene viewport rectangles must not overlap.",
                        nameof(viewports));
                }
            }
        }
    }

    private static bool Overlaps(SceneRenderViewport first, SceneRenderViewport second) =>
        first.X < (ulong)second.X + second.Width &&
        second.X < (ulong)first.X + first.Width &&
        first.Y < (ulong)second.Y + second.Height &&
        second.Y < (ulong)first.Y + first.Height;

    private static void ApplyViewport(
        GraphicsRenderPassEncoder pass,
        SceneRenderViewport? viewport)
    {
        if (viewport is null)
        {
            return;
        }

        pass.SetViewport(
            viewport.X,
            viewport.Y,
            viewport.Width,
            viewport.Height);
        pass.SetScissorRect(
            viewport.X,
            viewport.Y,
            viewport.Width,
            viewport.Height);
    }

    private void ClearTargets(
        GraphicsTexture colorTarget,
        GraphicsTexture depthTarget,
        SceneRenderPassOptions passOptions)
    {
        LinearRgba convertedClear = ConvertToWorkingSpace(passOptions.ClearColor);
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("shared scene viewports clear");
        using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(
                colorTarget,
                GraphicsLoadOperation.Clear,
                GraphicsStoreOperation.Store,
                new GraphicsClearColor(
                    convertedClear.Red,
                    convertedClear.Green,
                    convertedClear.Blue,
                    convertedClear.Alpha)),
            "shared scene viewports clear pass",
            new GraphicsRenderPassDepthAttachment(
                depthTarget,
                GraphicsLoadOperation.Clear,
                GraphicsStoreOperation.Store,
                passOptions.ClearDepth))))
        {
        }
        using GraphicsCommandBuffer commands = encoder.Finish("shared scene viewports clear commands");
        device.Queue.Submit(commands);
    }

    /// <summary>Releases cache entries no longer referenced by any mesh in the supplied scene.</summary>
    /// <exception cref="InvalidOperationException">Cache trimming is reentered during resource disposal.</exception>
    public void TrimCaches(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ObjectDisposedException.ThrowIf(disposed, this);
        ThrowIfCacheRetentionInUse();
        cacheRetentionInUse = true;
        try
        {
            cacheRetention.Scenes.Add(scene);
            TrimRetainedCaches();
        }
        finally
        {
            cacheRetention.Clear();
            cacheRetentionInUse = false;
            if (disposed)
            {
                cacheRetention.Release();
            }
        }
    }

    /// <summary>Releases cache entries absent from every supplied scene.</summary>
    /// <exception cref="InvalidOperationException">Cache trimming is reentered during resource disposal.</exception>
    public void TrimCaches(IEnumerable<Scene> scenes)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(scenes);
        ThrowIfCacheRetentionInUse();
        // Enumerating application input may invoke this renderer again. Materialize and
        // validate it before borrowing the renderer's reusable scratch state.
        Scene[] retainedScenes = scenes.ToArray();
        foreach (Scene scene in retainedScenes)
        {
            if (scene is null)
            {
                throw new ArgumentException("The retained scene set cannot contain null.", nameof(scenes));
            }
        }
        ObjectDisposedException.ThrowIf(disposed, this);
        ThrowIfCacheRetentionInUse();
        cacheRetentionInUse = true;
        try
        {
            cacheRetention.Scenes.AddRange(retainedScenes);
            TrimRetainedCaches();
        }
        finally
        {
            cacheRetention.Clear();
            cacheRetentionInUse = false;
            if (disposed)
            {
                cacheRetention.Release();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (!cacheRetentionInUse)
        {
            cacheRetention.Release();
        }
        foreach (MeshResources resources in meshCache.Values)
        {
            resources.Dispose();
        }
        meshCache.Clear();
        foreach (GeometryResources resources in geometryCache.Values)
        {
            resources.Dispose();
        }
        geometryCache.Clear();
        foreach (EnvironmentResources resources in environmentCache.Values)
        {
            resources.Dispose();
        }
        environmentCache.Clear();
        foreach (MaterialTextureResources resources in materialTextureCache.Values)
        {
            resources.Dispose();
        }
        materialTextureCache.Clear();
        foreach (MaterialTextureResources resources in compressedMaterialTextureCache.Values)
        {
            resources.Dispose();
        }
        compressedMaterialTextureCache.Clear();
        foreach (DataTextureResources resources in dataTextureCache.Values)
        {
            resources.Dispose();
        }
        dataTextureCache.Clear();
        foreach (DataTextureResources resources in compressedDataTextureCache.Values)
        {
            resources.Dispose();
        }
        compressedDataTextureCache.Clear();
        foreach (GraphicsSampler sampler in materialSamplerCache.Values)
        {
            sampler.Dispose();
        }
        materialSamplerCache.Clear();
        fallbackOrmTexture.Dispose();
        fallbackAnisotropyTexture.Dispose();
        fallbackNormalTexture.Dispose();
        fallbackMaterialTexture.Dispose();
        ambientOcclusionResources?.Dispose();
        ambientOcclusionResources = null;
        transmissionResources?.Dispose();
        transmissionResources = null;
        bloomResources?.Dispose();
        bloomResources = null;
        shared.Dispose();
    }

    private PreparedMaterial PrepareMaterial(Mesh mesh)
    {
        return mesh.Material switch
        {
            OpenPbrMaterial => throw new NotSupportedException(
                "OpenPBR materials require an OpenPBR render pass. The default SceneRenderer does not " +
                "convert them to metallic/roughness previews."),
            UnlitMaterial material when mesh.Geometry.TextureCoordinates.Count == 0 &&
                (material.BaseColorTexture is not null || material.CompressedBaseColorTexture is not null) &&
                material.BaseColorTextureMapping.TextureCoordinateSet == 0 =>
                throw new InvalidOperationException(
                    $"Mesh {mesh.Name ?? "<unnamed>"} requires TEXCOORD_0 for unlit base-color sampling."),
            UnlitMaterial material when mesh.Geometry.TextureCoordinates1.Count == 0 &&
                (material.BaseColorTexture is not null || material.CompressedBaseColorTexture is not null) &&
                material.BaseColorTextureMapping.TextureCoordinateSet == 1 =>
                throw new InvalidOperationException(
                    $"Mesh {mesh.Name ?? "<unnamed>"} requires TEXCOORD_1 for unlit base-color sampling."),
            UnlitMaterial material => new PreparedMaterial(
                MaterialKind.Unlit,
                ConvertToWorkingSpace(material.Color),
                new LinearRgba(0f, 0f, 0f, 1f, WorkingColorSpace),
                1f,
                0f,
                0f,
                1f,
                material.AlphaMode,
                material.AlphaCutoff,
                material.BaseColorTexture,
                material.CompressedBaseColorTexture,
                null,
                null,
                null,
                null,
                null,
                null,
                1f,
                0f,
                0f,
                1f,
                null,
                null,
                null,
                null,
                null,
                null,
                0f,
                0f,
                null,
                null,
                0f,
                1.5f,
                0f,
                0f,
                float.PositiveInfinity,
                new LinearRgba(1f, 1f, 1f, 1f, WorkingColorSpace),
                null,
                null,
                null,
                null,
                new LinearRgba(0f, 0f, 0f, 1f, WorkingColorSpace),
                0f,
                null,
                null,
                null,
                null,
                0f,
                1.3f,
                100f,
                400f,
                null,
                null,
                null,
                null,
                1f,
                new LinearRgba(1f, 1f, 1f, 1f, WorkingColorSpace),
                null,
                null,
                null,
                null,
                0f,
                new LinearRgba(1f, 1f, 1f, 1f, WorkingColorSpace),
                null,
                null,
                null,
                null,
                material.BaseColorTextureMapping,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                MaterialTextureMapping.Default,
                material.IsDoubleSided),
            PbrMaterial material when mesh.Geometry.Normals.Count == 0 =>
                throw new InvalidOperationException(
                    $"Mesh {mesh.Name ?? "<unnamed>"} requires normals for metallic/roughness rendering."),
            PbrMaterial material when mesh.Geometry.TextureCoordinates.Count == 0 &&
                RequiresMissingTextureCoordinates(
                material,
                mesh.Geometry,
                0) =>
                throw new InvalidOperationException(
                    $"Mesh {mesh.Name ?? "<unnamed>"} requires TEXCOORD_0 for material texture sampling."),
            PbrMaterial material when mesh.Geometry.TextureCoordinates1.Count == 0 &&
                RequiresMissingTextureCoordinates(
                material,
                mesh.Geometry,
                1) =>
                throw new InvalidOperationException(
                    $"Mesh {mesh.Name ?? "<unnamed>"} requires TEXCOORD_1 for material texture sampling."),
            PbrMaterial material => PreparePbrMaterial(material),
            _ => throw new NotSupportedException(
                $"Material type {mesh.Material.GetType().Name} is not supported by the scene renderer."),
        };
    }

    private PreparedMaterial PreparePbrMaterial(PbrMaterial material)
    {
        MaterialShaderTemplate template = material.ShaderTemplate;
        bool emissive = template.Includes(PbrMaterialExtensions.Emissive) &&
            material.EmissiveStrength > 0f && MaxRgb(material.EmissiveColor) > 0f;
        bool clearcoat = template.Includes(PbrMaterialExtensions.Clearcoat) &&
            material.ClearcoatFactor > 0f;
        bool anisotropy = template.Includes(PbrMaterialExtensions.Anisotropy) &&
            material.AnisotropyStrength > 0f;
        bool transmission = template.Includes(PbrMaterialExtensions.Transmission) &&
            material.TransmissionFactor > 0f;
        bool volume = transmission && template.Includes(PbrMaterialExtensions.Volume) &&
            material.VolumeThicknessFactor > 0f;
        bool dispersion = volume && template.Includes(PbrMaterialExtensions.Dispersion) &&
            material.Dispersion > 0f;
        bool sheen = template.Includes(PbrMaterialExtensions.Sheen) &&
            MaxRgb(material.SheenColor) > 0f;
        bool iridescence = template.Includes(PbrMaterialExtensions.Iridescence) &&
            material.IridescenceFactor > 0f;
        bool specular = template.Includes(PbrMaterialExtensions.Specular) &&
            (material.SpecularFactor != 1f || !IsWhite(material.SpecularColor) ||
             material.SpecularTexture is not null || material.CompressedSpecularTexture is not null ||
             material.SpecularColorTexture is not null ||
             material.CompressedSpecularColorTexture is not null);
        bool diffuseTransmission = template.Includes(PbrMaterialExtensions.DiffuseTransmission) &&
            material.DiffuseTransmissionFactor > 0f;
        bool baseColorTexture = template.Includes(PbrMaterialTextureSlots.BaseColor);
        bool emissiveTexture = emissive && template.Includes(PbrMaterialTextureSlots.Emissive);
        bool normalTexture = template.Includes(PbrMaterialTextureSlots.Normal);
        bool ormTexture = template.Includes(PbrMaterialTextureSlots.OcclusionRoughnessMetallic);
        bool clearcoatTextures = clearcoat && template.Includes(PbrMaterialTextureSlots.Clearcoat);
        bool anisotropyTexture = anisotropy && template.Includes(PbrMaterialTextureSlots.Anisotropy);
        bool transmissionTextures = transmission &&
            template.Includes(PbrMaterialTextureSlots.TransmissionVolume);
        bool sheenTextures = sheen && template.Includes(PbrMaterialTextureSlots.Sheen);
        bool iridescenceTextures = iridescence &&
            template.Includes(PbrMaterialTextureSlots.Iridescence);
        bool specularTextures = specular && template.Includes(PbrMaterialTextureSlots.Specular);
        bool diffuseTransmissionTextures = diffuseTransmission &&
            template.Includes(PbrMaterialTextureSlots.DiffuseTransmission);

        return new PreparedMaterial(
                MaterialKind.MetallicRoughness,
                ConvertToWorkingSpace(material.BaseColor),
                emissive
                    ? ConvertToWorkingSpace(material.EmissiveColor)
                    : new LinearRgba(0f, 0f, 0f, 1f, WorkingColorSpace),
                emissive ? material.EmissiveStrength : 0f,
                material.Metallic,
                material.Roughness,
                material.IndirectOcclusion,
                material.AlphaMode,
                material.AlphaCutoff,
                baseColorTexture ? material.BaseColorTexture : null,
                baseColorTexture ? material.CompressedBaseColorTexture : null,
                emissiveTexture ? material.EmissiveTexture : null,
                emissiveTexture ? material.CompressedEmissiveTexture : null,
                normalTexture ? material.NormalTexture : null,
                normalTexture ? material.CompressedNormalTexture : null,
                ormTexture ? material.OcclusionRoughnessMetallicTexture : null,
                ormTexture ? material.CompressedOcclusionRoughnessMetallicTexture : null,
                material.NormalScale,
                clearcoat ? material.ClearcoatFactor : 0f,
                material.ClearcoatRoughness,
                material.ClearcoatNormalScale,
                clearcoatTextures ? material.ClearcoatTexture : null,
                clearcoatTextures ? material.CompressedClearcoatTexture : null,
                clearcoatTextures ? material.ClearcoatRoughnessTexture : null,
                clearcoatTextures ? material.CompressedClearcoatRoughnessTexture : null,
                clearcoatTextures ? material.ClearcoatNormalTexture : null,
                clearcoatTextures ? material.CompressedClearcoatNormalTexture : null,
                anisotropy ? material.AnisotropyStrength : 0f,
                material.AnisotropyRotation,
                anisotropyTexture ? material.AnisotropyTexture : null,
                anisotropyTexture ? material.CompressedAnisotropyTexture : null,
                transmission ? material.TransmissionFactor : 0f,
                material.IndexOfRefraction,
                dispersion ? material.Dispersion : 0f,
                volume ? material.VolumeThicknessFactor : 0f,
                volume ? material.VolumeAttenuationDistance : float.PositiveInfinity,
                volume
                    ? ConvertToWorkingSpace(material.VolumeAttenuationColor)
                    : new LinearRgba(1f, 1f, 1f, 1f, WorkingColorSpace),
                transmissionTextures ? material.TransmissionTexture : null,
                transmissionTextures ? material.CompressedTransmissionTexture : null,
                volume && transmissionTextures ? material.VolumeThicknessTexture : null,
                volume && transmissionTextures ? material.CompressedVolumeThicknessTexture : null,
                sheen
                    ? ConvertToWorkingSpace(material.SheenColor)
                    : new LinearRgba(0f, 0f, 0f, 1f, WorkingColorSpace),
                material.SheenRoughness,
                sheenTextures ? material.SheenColorTexture : null,
                sheenTextures ? material.CompressedSheenColorTexture : null,
                sheenTextures ? material.SheenRoughnessTexture : null,
                sheenTextures ? material.CompressedSheenRoughnessTexture : null,
                iridescence ? material.IridescenceFactor : 0f,
                material.IridescenceIndexOfRefraction,
                material.IridescenceThicknessMinimum,
                material.IridescenceThicknessMaximum,
                iridescenceTextures ? material.IridescenceTexture : null,
                iridescenceTextures ? material.CompressedIridescenceTexture : null,
                iridescenceTextures ? material.IridescenceThicknessTexture : null,
                iridescenceTextures ? material.CompressedIridescenceThicknessTexture : null,
                specular ? material.SpecularFactor : 1f,
                specular
                    ? ConvertToWorkingSpace(material.SpecularColor)
                    : new LinearRgba(1f, 1f, 1f, 1f, WorkingColorSpace),
                specularTextures ? material.SpecularTexture : null,
                specularTextures ? material.CompressedSpecularTexture : null,
                specularTextures ? material.SpecularColorTexture : null,
                specularTextures ? material.CompressedSpecularColorTexture : null,
                diffuseTransmission ? material.DiffuseTransmissionFactor : 0f,
                diffuseTransmission
                    ? ConvertToWorkingSpace(material.DiffuseTransmissionColor)
                    : new LinearRgba(1f, 1f, 1f, 1f, WorkingColorSpace),
                diffuseTransmissionTextures ? material.DiffuseTransmissionTexture : null,
                diffuseTransmissionTextures ? material.CompressedDiffuseTransmissionTexture : null,
                diffuseTransmissionTextures ? material.DiffuseTransmissionColorTexture : null,
                diffuseTransmissionTextures ? material.CompressedDiffuseTransmissionColorTexture : null,
                material.BaseColorTextureMapping,
                material.EmissiveTextureMapping,
                material.NormalTextureMapping,
                material.OcclusionRoughnessMetallicTextureMapping,
                material.ClearcoatTextureMapping,
                material.ClearcoatRoughnessTextureMapping,
                material.ClearcoatNormalTextureMapping,
                material.AnisotropyTextureMapping,
                material.TransmissionTextureMapping,
                material.VolumeThicknessTextureMapping,
                material.SheenColorTextureMapping,
                material.SheenRoughnessTextureMapping,
                material.IridescenceTextureMapping,
                material.IridescenceThicknessTextureMapping,
                material.SpecularTextureMapping,
                material.SpecularColorTextureMapping,
                material.DiffuseTransmissionTextureMapping,
                material.DiffuseTransmissionColorTextureMapping,
                material.IsDoubleSided);
    }

    private static bool RequiresMissingTextureCoordinates(
        PbrMaterial material,
        MeshGeometry geometry,
        int set)
    {
        MaterialShaderTemplate template = material.ShaderTemplate;
        bool emissive = template.Includes(PbrMaterialExtensions.Emissive) &&
            material.EmissiveStrength > 0f && MaxRgb(material.EmissiveColor) > 0f;
        bool clearcoat = template.Includes(PbrMaterialExtensions.Clearcoat) &&
            material.ClearcoatFactor > 0f;
        bool anisotropy = template.Includes(PbrMaterialExtensions.Anisotropy) &&
            material.AnisotropyStrength > 0f;
        bool transmission = template.Includes(PbrMaterialExtensions.Transmission) &&
            material.TransmissionFactor > 0f;
        bool volume = transmission && template.Includes(PbrMaterialExtensions.Volume) &&
            material.VolumeThicknessFactor > 0f;
        bool sheen = template.Includes(PbrMaterialExtensions.Sheen) &&
            MaxRgb(material.SheenColor) > 0f;
        bool iridescence = template.Includes(PbrMaterialExtensions.Iridescence) &&
            material.IridescenceFactor > 0f;
        bool specular = template.Includes(PbrMaterialExtensions.Specular) &&
            (material.SpecularFactor != 1f || !IsWhite(material.SpecularColor) ||
             material.SpecularTexture is not null || material.CompressedSpecularTexture is not null ||
             material.SpecularColorTexture is not null ||
             material.CompressedSpecularColorTexture is not null);
        bool diffuseTransmission = template.Includes(PbrMaterialExtensions.DiffuseTransmission) &&
            material.DiffuseTransmissionFactor > 0f;
        bool required =
            (template.Includes(PbrMaterialTextureSlots.BaseColor) &&
                (material.BaseColorTexture is not null || material.CompressedBaseColorTexture is not null) &&
                material.BaseColorTextureMapping.TextureCoordinateSet == set) ||
            (emissive && template.Includes(PbrMaterialTextureSlots.Emissive) &&
                (material.EmissiveTexture is not null || material.CompressedEmissiveTexture is not null) &&
                material.EmissiveTextureMapping.TextureCoordinateSet == set) ||
            (template.Includes(PbrMaterialTextureSlots.Normal) &&
                (material.NormalTexture is not null || material.CompressedNormalTexture is not null) &&
                material.NormalTextureMapping.TextureCoordinateSet == set) ||
            (template.Includes(PbrMaterialTextureSlots.OcclusionRoughnessMetallic) &&
                (material.OcclusionRoughnessMetallicTexture is not null ||
                material.CompressedOcclusionRoughnessMetallicTexture is not null) &&
                material.OcclusionRoughnessMetallicTextureMapping.TextureCoordinateSet == set) ||
            (clearcoat && template.Includes(PbrMaterialTextureSlots.Clearcoat) &&
                (material.ClearcoatTexture is not null || material.CompressedClearcoatTexture is not null) &&
                material.ClearcoatTextureMapping.TextureCoordinateSet == set) ||
            (clearcoat && template.Includes(PbrMaterialTextureSlots.Clearcoat) &&
                (material.ClearcoatRoughnessTexture is not null ||
                material.CompressedClearcoatRoughnessTexture is not null) &&
                material.ClearcoatRoughnessTextureMapping.TextureCoordinateSet == set) ||
            (clearcoat && template.Includes(PbrMaterialTextureSlots.Clearcoat) &&
                (material.ClearcoatNormalTexture is not null ||
                material.CompressedClearcoatNormalTexture is not null) &&
                material.ClearcoatNormalTextureMapping.TextureCoordinateSet == set) ||
            (anisotropy && template.Includes(PbrMaterialTextureSlots.Anisotropy) &&
                (material.AnisotropyTexture is not null || material.CompressedAnisotropyTexture is not null) &&
                material.AnisotropyTextureMapping.TextureCoordinateSet == set) ||
            (transmission && template.Includes(PbrMaterialTextureSlots.TransmissionVolume) &&
                (material.TransmissionTexture is not null || material.CompressedTransmissionTexture is not null) &&
                material.TransmissionTextureMapping.TextureCoordinateSet == set) ||
            (volume && template.Includes(PbrMaterialTextureSlots.TransmissionVolume) &&
                (material.VolumeThicknessTexture is not null || material.CompressedVolumeThicknessTexture is not null) &&
                material.VolumeThicknessTextureMapping.TextureCoordinateSet == set) ||
            (sheen && template.Includes(PbrMaterialTextureSlots.Sheen) &&
                (material.SheenColorTexture is not null || material.CompressedSheenColorTexture is not null) &&
                material.SheenColorTextureMapping.TextureCoordinateSet == set) ||
            (sheen && template.Includes(PbrMaterialTextureSlots.Sheen) &&
                (material.SheenRoughnessTexture is not null ||
                material.CompressedSheenRoughnessTexture is not null) &&
                material.SheenRoughnessTextureMapping.TextureCoordinateSet == set) ||
            (iridescence && template.Includes(PbrMaterialTextureSlots.Iridescence) &&
                (material.IridescenceTexture is not null ||
                material.CompressedIridescenceTexture is not null) &&
                material.IridescenceTextureMapping.TextureCoordinateSet == set) ||
            (iridescence && template.Includes(PbrMaterialTextureSlots.Iridescence) &&
                (material.IridescenceThicknessTexture is not null ||
                material.CompressedIridescenceThicknessTexture is not null) &&
                material.IridescenceThicknessTextureMapping.TextureCoordinateSet == set) ||
            (specular && template.Includes(PbrMaterialTextureSlots.Specular) &&
                (material.SpecularTexture is not null || material.CompressedSpecularTexture is not null) &&
                material.SpecularTextureMapping.TextureCoordinateSet == set) ||
            (specular && template.Includes(PbrMaterialTextureSlots.Specular) &&
                (material.SpecularColorTexture is not null ||
                 material.CompressedSpecularColorTexture is not null) &&
                material.SpecularColorTextureMapping.TextureCoordinateSet == set) ||
            (diffuseTransmission && template.Includes(PbrMaterialTextureSlots.DiffuseTransmission) &&
                (material.DiffuseTransmissionTexture is not null ||
                 material.CompressedDiffuseTransmissionTexture is not null) &&
                material.DiffuseTransmissionTextureMapping.TextureCoordinateSet == set) ||
            (diffuseTransmission && template.Includes(PbrMaterialTextureSlots.DiffuseTransmission) &&
                (material.DiffuseTransmissionColorTexture is not null ||
                 material.CompressedDiffuseTransmissionColorTexture is not null) &&
                material.DiffuseTransmissionColorTextureMapping.TextureCoordinateSet == set);
        return required && (set == 0
            ? geometry.TextureCoordinates.Count == 0
            : geometry.TextureCoordinates1.Count == 0);
    }

    private static float MaxRgb(LinearRgba color) =>
        MathF.Max(color.Red, MathF.Max(color.Green, color.Blue));

    private static bool IsWhite(LinearRgba color) =>
        color.Red == 1f && color.Green == 1f && color.Blue == 1f;

    private GeometryResources GetOrCreateGeometry(MeshGeometry geometry)
    {
        if (geometryCache.TryGetValue(geometry, out GeometryResources? existing))
        {
            return existing;
        }
        GeometryResources created = GeometryResources.Create(device, geometry);
        geometryCache.Add(geometry, created);
        return created;
    }

    private MeshResources GetOrCreateMesh(
        Mesh mesh,
        GeometryResources geometry,
        IReadOnlyList<MaterialPhysicalTextureBinding> textureBindings,
        MaterialShaderVariant shaderKey)
    {
        if (meshCache.TryGetValue(mesh, out MeshResources? existing) &&
            existing.Matches(textureBindings) &&
            ReferenceEquals(existing.MorphBuffer, geometry.MorphBuffer) &&
            ReferenceEquals(existing.Skin, mesh.Skin))
        {
            existing.UpdateIdentity(
                geometry,
                mesh.Material,
                mesh.Material.TextureBindingRevision,
                shaderKey);
            return existing;
        }
        if (existing is not null)
        {
            existing.Dispose();
            meshCache.Remove(mesh);
        }
        MeshResources created = MeshResources.Create(
            device,
            shared.DrawBindGroupLayout,
            geometry,
            textureBindings,
            mesh.Material,
            mesh.Material.TextureBindingRevision,
            shaderKey,
            mesh.Skin);
        meshCache.Add(mesh, created);
        return created;
    }

    private MaterialTextureResources GetOrCreateMaterialTexture(LinearRgbaImage image)
    {
        if (materialTextureCache.TryGetValue(image, out MaterialTextureResources? existing))
        {
            return existing;
        }
        MaterialTextureResources created = MaterialTextureResources.Create(device, image);
        materialTextureCache.Add(image, created);
        return created;
    }

    private MaterialTextureResources GetOrCreateCompressedMaterialTexture(
        CompressedMaterialTexture image)
    {
        if (compressedMaterialTextureCache.TryGetValue(
            image,
            out MaterialTextureResources? existing))
        {
            return existing;
        }
        MaterialTextureResources created = MaterialTextureResources.Create(device, image);
        compressedMaterialTextureCache.Add(image, created);
        return created;
    }

    private DataTextureResources GetOrCreateDataTexture(
        NormalizedRgbaDataImage image,
        DataTextureSemantic semantic)
    {
        DataTextureCacheKey key = new(image, semantic);
        if (dataTextureCache.TryGetValue(key, out DataTextureResources? existing))
        {
            return existing;
        }
        DataTextureResources created = DataTextureResources.Create(device, image, semantic);
        dataTextureCache.Add(key, created);
        return created;
    }

    private DataTextureResources GetOrCreateCompressedDataTexture(
        CompressedMaterialTexture image,
        DataTextureSemantic semantic)
    {
        CompressedDataTextureCacheKey key = new(image, semantic);
        if (compressedDataTextureCache.TryGetValue(key, out DataTextureResources? existing))
        {
            return existing;
        }
        DataTextureResources created = DataTextureResources.Create(device, image, semantic);
        compressedDataTextureCache.Add(key, created);
        return created;
    }

    private GraphicsSampler GetOrCreateMaterialSampler(MaterialTextureSampling sampling)
    {
        if (materialSamplerCache.TryGetValue(sampling, out GraphicsSampler? existing))
        {
            return existing;
        }
        GraphicsSampler created = device.CreateSampler(new GraphicsSamplerDescriptor(
            0f,
            sampling.UseMipmaps ? 32f : 0f,
            MapAddressMode(sampling.AddressModeU),
            MapAddressMode(sampling.AddressModeV),
            GraphicsAddressMode.ClampToEdge,
            MapFilter(sampling.MagnificationFilter),
            MapFilter(sampling.MinificationFilter),
            MapFilter(sampling.MipFilter),
            "material texture sampler"));
        materialSamplerCache.Add(sampling, created);
        return created;
    }

    private static GraphicsAddressMode MapAddressMode(MaterialTextureAddressMode mode) => mode switch
    {
        MaterialTextureAddressMode.ClampToEdge => GraphicsAddressMode.ClampToEdge,
        MaterialTextureAddressMode.Repeat => GraphicsAddressMode.Repeat,
        MaterialTextureAddressMode.MirrorRepeat => GraphicsAddressMode.MirrorRepeat,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private static GraphicsFilterMode MapFilter(MaterialTextureFilter filter) => filter switch
    {
        MaterialTextureFilter.Nearest => GraphicsFilterMode.Nearest,
        MaterialTextureFilter.Linear => GraphicsFilterMode.Linear,
        _ => throw new ArgumentOutOfRangeException(nameof(filter)),
    };

    private EnvironmentResources GetOrCreateEnvironment(EquirectangularHdrEnvironment environment)
    {
        if (environmentCache.TryGetValue(environment, out EnvironmentResources? existing))
        {
            return existing;
        }
        EnvironmentResources created = EnvironmentResources.Create(
            device,
            shared.EnvironmentBindGroupLayout,
            shared.BrdfLut.View,
            environment);
        environmentCache.Add(environment, created);
        return created;
    }

    private AmbientOcclusionResources EnsureAmbientOcclusionResources(GraphicsExtent3D extent)
    {
        if (ambientOcclusionResources is not null && ambientOcclusionResources.Extent == extent)
        {
            return ambientOcclusionResources;
        }

        ambientOcclusionResources?.Dispose();
        transmissionResources?.Dispose();
        transmissionResources = null;
        ambientOcclusionResources = AmbientOcclusionResources.Create(
            device,
            shared.ShadowBindGroupLayout,
            shared.ShadowSampler,
            shared.ShadowView,
            extent,
            GetOrCreateMaterialSampler(MaterialTextureMapping.Default.Sampling),
            fallbackMaterialTexture.View);
        return ambientOcclusionResources;
    }

    private TransmissionResources EnsureTransmissionResources(
        GraphicsExtent3D extent,
        AmbientOcclusionResources aoResources)
    {
        if (transmissionResources is not null && transmissionResources.Extent == extent)
        {
            return transmissionResources;
        }
        transmissionResources?.Dispose();
        transmissionResources = TransmissionResources.Create(
            device,
            shared.ShadowBindGroupLayout,
            shared.ShadowSampler,
            shared.ShadowView,
            aoResources.DepthView,
            extent,
            ColorFormat);
        return transmissionResources;
    }

    private BloomResources EnsureBloomResources(GraphicsExtent3D extent, float radiusPixels)
    {
        uint downsampleFactor = BloomResources.SelectDownsampleFactor(radiusPixels);
        if (bloomResources is not null &&
            bloomResources.Extent == extent &&
            bloomResources.DownsampleFactor == downsampleFactor)
        {
            return bloomResources;
        }

        bloomResources?.Dispose();
        bloomResources = BloomResources.Create(device, ColorFormat, extent, downsampleFactor);
        return bloomResources;
    }

    private void WriteFrameUniform(
        Camera camera,
        DirectionalLight? light,
        IReadOnlyList<PunctualLight> additionalLights,
        ImageBasedLight? environmentLight,
        SceneRenderLayer activeRenderLayer,
        GraphicsBuffer? targetBuffer = null)
    {
        Span<float> values = stackalloc float[FrameUniformSize / sizeof(float)];
        values.Clear();
        MemoryMarshal.Cast<float, Matrix4x4>(values[..16])[0] = camera.ViewProjectionMatrix;
        Vector3 cameraPosition = Vector3.Transform(Vector3.Zero, camera.WorldMatrix);
        values[16] = cameraPosition.X;
        values[17] = cameraPosition.Y;
        values[18] = cameraPosition.Z;
        values[19] = 1f;

        if (light is not null)
        {
            Vector3 directionToLight = -light.WorldDirection;
            LinearRgba lightColor = ConvertToWorkingSpace(light.Color);
            values[20] = directionToLight.X;
            values[21] = directionToLight.Y;
            values[22] = directionToLight.Z;
            values[23] = light.Intensity;
            values[24] = lightColor.Red;
            values[25] = lightColor.Green;
            values[26] = lightColor.Blue;
            values[27] = 1f;
        }

        Quaternion environmentRotation = Quaternion.Identity;
        float environmentIntensity = 0f;
        if (environmentLight is not null && environmentLight.Intensity > 0f)
        {
            if (!Matrix4x4.Decompose(
                    environmentLight.WorldMatrix,
                    out _,
                    out environmentRotation,
                    out _))
            {
                throw new InvalidOperationException("An image-based-light world transform must be decomposable.");
            }
            environmentRotation = Quaternion.Normalize(environmentRotation);
            environmentIntensity = environmentLight.Intensity;
        }
        values[28] = environmentRotation.X;
        values[29] = environmentRotation.Y;
        values[30] = environmentRotation.Z;
        values[31] = environmentRotation.W;
        values[32] = environmentIntensity;
        values[33] = SpecularEnvironmentMipCount - 1u;
        values[34] = (float)activeRenderLayer;

        Matrix4x4 shadowViewProjection = light?.CastsShadows != true
            ? Matrix4x4.Identity
            : CreateDirectionalShadowViewProjection(light);
        MemoryMarshal.Cast<float, Matrix4x4>(values.Slice(36, 16))[0] = shadowViewProjection;
        values[52] = light?.CastsShadows == true ? light.ShadowOpacity : 0f;
        values[53] = light?.CastsShadows != true
            ? 0f
            : MathF.Tan(light.AngularDiameterRadians * 0.5f);
        values[54] = 8f;
        values[55] = 23.9f;

        if (!Matrix4x4.Invert(camera.ViewProjectionMatrix, out Matrix4x4 inverseViewProjection))
        {
            throw new InvalidOperationException("The camera view-projection matrix must be invertible.");
        }
        MemoryMarshal.Cast<float, Matrix4x4>(values.Slice(56, 16))[0] = inverseViewProjection;
        values[72] = AmbientOcclusionEnabled ? 1f : 0f;
        values[73] = AmbientOcclusionRadius;
        values[74] = AmbientOcclusionStrength;
        values[75] = 0.025f;

        values[35] = additionalLights.Count;
        for (int index = 0; index < additionalLights.Count; index++)
        {
            PunctualLight punctual = additionalLights[index];
            int offset = 76 + index * 16;
            LinearRgba color = ConvertToWorkingSpace(punctual.Color);
            values[offset + 8] = color.Red;
            values[offset + 9] = color.Green;
            values[offset + 10] = color.Blue;
            values[offset + 11] = punctual.Intensity;
            switch (punctual)
            {
                case DirectionalLight directional:
                    Vector3 directionalRay = directional.WorldDirection;
                    values[offset + 3] = 0f;
                    values[offset + 4] = directionalRay.X;
                    values[offset + 5] = directionalRay.Y;
                    values[offset + 6] = directionalRay.Z;
                    break;
                case PointLight point:
                    WritePositionalLight(values, offset, point.WorldPosition, point.Range, 1f);
                    break;
                case SpotLight spot:
                    WritePositionalLight(values, offset, spot.WorldPosition, spot.Range, 2f);
                    Vector3 spotDirection = spot.WorldDirection;
                    values[offset + 4] = spotDirection.X;
                    values[offset + 5] = spotDirection.Y;
                    values[offset + 6] = spotDirection.Z;
                    float innerCosine = MathF.Cos(spot.InnerConeAngle);
                    float outerCosine = MathF.Cos(spot.OuterConeAngle);
                    float angleScale = 1f / MathF.Max(0.001f, innerCosine - outerCosine);
                    values[offset + 12] = angleScale;
                    values[offset + 13] = -outerCosine * angleScale;
                    break;
            }
        }

        device.Queue.WriteBuffer(
            targetBuffer ?? shared.FrameUniformBuffer,
            0,
            MemoryMarshal.AsBytes(values));
    }

    private static void WritePositionalLight(
        Span<float> values,
        int offset,
        Vector3 position,
        float range,
        float type)
    {
        values[offset] = position.X;
        values[offset + 1] = position.Y;
        values[offset + 2] = position.Z;
        values[offset + 3] = type;
        values[offset + 7] = float.IsPositiveInfinity(range) ? 0f : 1f / range;
    }

    private static Matrix4x4 CreateDirectionalShadowViewProjection(DirectionalLight light)
    {
        Vector3 lightDirection = light.WorldDirection;
        Vector3 target = Vector3.Zero;
        Vector3 eye = target - lightDirection * 10f;
        Vector3 up = MathF.Abs(Vector3.Dot(lightDirection, Vector3.UnitY)) > 0.95f
            ? Vector3.UnitZ
            : Vector3.UnitY;
        return Matrix4x4.CreateLookAt(eye, target, up) *
            Matrix4x4.CreateOrthographic(8f, 6f, 0.1f, 24f);
    }

    private void WriteDrawUniform(MeshResources resources, Mesh mesh, PreparedMaterial material)
    {
        Matrix4x4 model = mesh.WorldMatrix;
        Matrix4x4 normalMatrix = Matrix4x4.Identity;
        if (material.Kind == MaterialKind.MetallicRoughness)
        {
            if (!Matrix4x4.Invert(model, out Matrix4x4 inverseModel))
            {
                throw new InvalidOperationException("A lit mesh world transform must be invertible.");
            }
            normalMatrix = Matrix4x4.Transpose(inverseModel);
        }

        Span<float> values = stackalloc float[DrawUniformSize / sizeof(float)];
        values.Clear();
        MemoryMarshal.Cast<float, Matrix4x4>(values[..16])[0] = model;
        MemoryMarshal.Cast<float, Matrix4x4>(values.Slice(16, 16))[0] = normalMatrix;
        values[32] = material.Color.Red;
        values[33] = material.Color.Green;
        values[34] = material.Color.Blue;
        values[35] = material.Color.Alpha;
        values[36] = material.Metallic;
        values[37] = material.Roughness;
        values[38] = material.IndirectOcclusion;
        values[39] = material.NormalScale;
        values[40] = (float)material.AlphaMode;
        values[41] = material.AlphaCutoff;
        values[44] = material.BaseColorTextureMapping.Scale.X;
        values[45] = material.BaseColorTextureMapping.Scale.Y;
        values[46] = material.BaseColorTextureMapping.Offset.X;
        values[47] = material.BaseColorTextureMapping.Offset.Y;
        for (int index = 0; index < mesh.MorphWeights.Count; index++)
        {
            values[48 + index] = mesh.MorphWeights[index];
        }
        values[56] = mesh.MorphWeights.Count;
        values[57] = mesh.Geometry.Positions.Count;
        values[60] = material.EmissiveColor.Red;
        values[61] = material.EmissiveColor.Green;
        values[62] = material.EmissiveColor.Blue;
        values[63] = material.EmissiveStrength;
        WriteTextureMapping(values, 64, material.BaseColorTextureMapping);
        WriteTextureMapping(values, 72, material.NormalTextureMapping);
        WriteTextureMapping(values, 80, material.OrmTextureMapping);
        WriteTextureMapping(values, 88, material.EmissiveTextureMapping);
        values[96] = material.ClearcoatFactor;
        values[97] = material.ClearcoatRoughness;
        values[98] = material.ClearcoatNormalScale;
        WriteTextureMapping(values, 100, material.ClearcoatTextureMapping);
        WriteTextureMapping(values, 108, material.ClearcoatRoughnessTextureMapping);
        WriteTextureMapping(values, 116, material.ClearcoatNormalTextureMapping);
        values[124] = material.AnisotropyStrength;
        values[125] = material.AnisotropyRotation;
        WriteTextureMapping(values, 128, material.AnisotropyTextureMapping);
        values[136] = material.TransmissionFactor;
        values[137] = material.IndexOfRefraction;
        values[138] = material.VolumeThicknessFactor;
        values[139] = float.IsPositiveInfinity(material.VolumeAttenuationDistance)
            ? 0f
            : 1f / material.VolumeAttenuationDistance;
        values[140] = material.VolumeAttenuationColor.Red;
        values[141] = material.VolumeAttenuationColor.Green;
        values[142] = material.VolumeAttenuationColor.Blue;
        values[143] = material.Dispersion;
        WriteTextureMapping(values, 144, material.TransmissionTextureMapping);
        WriteTextureMapping(values, 152, material.VolumeThicknessTextureMapping);
        values[160] = material.SheenColor.Red;
        values[161] = material.SheenColor.Green;
        values[162] = material.SheenColor.Blue;
        values[163] = material.SheenRoughness;
        WriteTextureMapping(values, 164, material.SheenColorTextureMapping);
        WriteTextureMapping(values, 172, material.SheenRoughnessTextureMapping);
        values[180] = material.IridescenceFactor;
        values[181] = material.IridescenceIndexOfRefraction;
        values[182] = material.IridescenceThicknessMinimum;
        values[183] = material.IridescenceThicknessMaximum;
        WriteTextureMapping(values, 184, material.IridescenceTextureMapping);
        WriteTextureMapping(values, 192, material.IridescenceThicknessTextureMapping);
        values[200] = material.SpecularFactor;
        values[201] = material.SpecularColor.Red;
        values[202] = material.SpecularColor.Green;
        values[203] = material.SpecularColor.Blue;
        WriteTextureMapping(values, 204, material.SpecularTextureMapping);
        WriteTextureMapping(values, 212, material.SpecularColorTextureMapping);
        values[220] = material.DiffuseTransmissionFactor;
        values[221] = material.DiffuseTransmissionColor.Red;
        values[222] = material.DiffuseTransmissionColor.Green;
        values[223] = material.DiffuseTransmissionColor.Blue;
        WriteTextureMapping(values, 224, material.DiffuseTransmissionTextureMapping);
        WriteTextureMapping(values, 232, material.DiffuseTransmissionColorTextureMapping);
        device.Queue.WriteBuffer(resources.UniformBuffer, 0, MemoryMarshal.AsBytes(values));
    }

    private static void WriteTextureMapping(
        Span<float> values,
        int offset,
        MaterialTextureMapping mapping)
    {
        float cosine = MathF.Cos(mapping.Rotation);
        float sine = MathF.Sin(mapping.Rotation);
        values[offset] = cosine * mapping.Scale.X;
        values[offset + 1] = sine * mapping.Scale.Y;
        values[offset + 2] = -sine * mapping.Scale.X;
        values[offset + 3] = cosine * mapping.Scale.Y;
        values[offset + 4] = mapping.Offset.X;
        values[offset + 5] = mapping.Offset.Y;
        values[offset + 6] = mapping.TextureCoordinateSet;
    }

    private static void ValidateSkin(Mesh mesh)
    {
        if (mesh.Geometry.MorphTargets.Count > MaximumMorphTargetCount)
        {
            throw new NotSupportedException(
                $"The current renderer supports at most {MaximumMorphTargetCount} morph targets per mesh.");
        }
        bool hasVertexSkinning = mesh.Geometry.JointIndices.Count != 0;
        if (hasVertexSkinning != (mesh.Skin is not null))
        {
            throw new InvalidOperationException(
                $"Mesh {mesh.Name ?? "<unnamed>"} must provide both vertex skinning data and a skin.");
        }
        if (mesh.Skin is null)
        {
            return;
        }
        uint jointCount = checked((uint)mesh.Skin.Joints.Count);
        if (mesh.Geometry.JointIndices.Any(indices => indices.Maximum >= jointCount))
        {
            throw new InvalidOperationException(
                $"Mesh {mesh.Name ?? "<unnamed>"} references a joint outside its skin.");
        }
    }

    private void WriteSkinPalette(MeshResources resources, Mesh mesh)
    {
        if (mesh.Skin is null && resources.SkinPaletteInitialized)
        {
            return;
        }
        int jointCount = mesh.Skin?.Joints.Count ?? 1;
        float[] values = resources.SkinPaletteValues;
        Span<Matrix4x4> matrices = MemoryMarshal.Cast<float, Matrix4x4>(values.AsSpan());
        if (mesh.Skin is null)
        {
            matrices[0] = Matrix4x4.Identity;
            matrices[1] = Matrix4x4.Identity;
        }
        else
        {
            if (!Matrix4x4.Invert(mesh.WorldMatrix, out Matrix4x4 inverseMeshWorld))
            {
                throw new InvalidOperationException("A skinned mesh world transform must be invertible.");
            }
            for (int index = 0; index < jointCount; index++)
            {
                Matrix4x4 position =
                    mesh.Skin.InverseBindMatrices[index] *
                    mesh.Skin.Joints[index].WorldMatrix *
                    inverseMeshWorld;
                if (!Matrix4x4.Invert(position, out Matrix4x4 inversePosition))
                {
                    throw new InvalidOperationException("A skin joint palette matrix must be invertible.");
                }
                matrices[index * 2] = position;
                matrices[(index * 2) + 1] = Matrix4x4.Transpose(inversePosition);
            }
        }
        device.Queue.WriteBuffer(resources.SkinBuffer, 0, MemoryMarshal.AsBytes(values.AsSpan()));
        resources.SkinPaletteInitialized = true;
    }

    private static LinearRgba ConvertToWorkingSpace(LinearRgba color)
    {
        if (ReferenceEquals(color.ColorSpace, StandardColorSpaces.LinearSrgb))
        {
            return color;
        }
        if (color.ColorSpace is not StandardRgbColorSpaceReference)
        {
            throw new NotSupportedException(
                color.ColorSpace is null
                    ? "The scene renderer requires an explicitly tagged linear color."
                    : $"The scene renderer cannot transform {color.ColorSpace.Name} to linear sRGB.");
        }
        return StandardLinearRgbConverter.Convert(color, StandardColorSpaces.LinearSrgb);
    }

    private static uint GetMipLevelCount(uint width, uint height)
    {
        uint levels = 1;
        while (width > 1 || height > 1)
        {
            width = Math.Max(1, width / 2);
            height = Math.Max(1, height / 2);
            levels++;
        }
        return levels;
    }

    private static Vector4[] DownsampleBox(
        Vector4[] source,
        uint sourceWidth,
        uint sourceHeight,
        bool normalizeNormals)
    {
        uint destinationWidth = Math.Max(1, sourceWidth / 2);
        uint destinationHeight = Math.Max(1, sourceHeight / 2);
        Vector4[] destination = new Vector4[checked((int)(destinationWidth * destinationHeight))];
        void DownsampleRow(int row)
        {
            uint y = checked((uint)row);
            for (uint x = 0; x < destinationWidth; x++)
            {
                uint x0 = x * sourceWidth / destinationWidth;
                uint y0 = y * sourceHeight / destinationHeight;
                uint x1 = ((x + 1) * sourceWidth / destinationWidth) - 1;
                uint y1 = ((y + 1) * sourceHeight / destinationHeight) - 1;
                Vector4 encodedSum = Vector4.Zero;
                Vector3 normalSum = Vector3.Zero;
                float alphaSum = 0f;
                uint sampleCount = 0;
                for (uint sourceY = y0; sourceY <= y1; sourceY++)
                {
                    for (uint sourceX = x0; sourceX <= x1; sourceX++)
                    {
                        Vector4 value = source[checked((int)((sourceY * sourceWidth) + sourceX))];
                        encodedSum += value;
                        normalSum += DecodeNormal(value);
                        alphaSum += value.W;
                        sampleCount++;
                    }
                }
                Vector4 average;
                if (normalizeNormals)
                {
                    Vector3 normal = normalSum.LengthSquared() > 1e-12f
                        ? Vector3.Normalize(normalSum)
                        : Vector3.UnitZ;
                    average = new Vector4(
                        (normal * 0.5f) + new Vector3(0.5f),
                        alphaSum / sampleCount);
                }
                else
                {
                    average = encodedSum / sampleCount;
                }
                destination[checked((int)((y * destinationWidth) + x))] = average;
            }
        }
        if (destination.Length >= ParallelTextureWorkThreshold)
        {
            Parallel.For(0, checked((int)destinationHeight), DownsampleRow);
        }
        else
        {
            for (int y = 0; y < checked((int)destinationHeight); y++)
            {
                DownsampleRow(y);
            }
        }
        return destination;
    }

    private static Vector3 DecodeNormal(Vector4 encoded) =>
        new(encoded.X * 2f - 1f, encoded.Y * 2f - 1f, encoded.Z * 2f - 1f);

    private enum MaterialKind
    {
        Unlit,
        MetallicRoughness,
    }

    // This is intentionally a reference type. It is a large immutable frame-local snapshot;
    // keeping it as a record struct caused dozens of full-value copies in the hot render path,
    // which is especially expensive under Mono AOT and the Mac Catalyst interpreter.
    private sealed record PreparedMaterial(
        MaterialKind Kind,
        LinearRgba Color,
        LinearRgba EmissiveColor,
        float EmissiveStrength,
        float Metallic,
        float Roughness,
        float IndirectOcclusion,
        MaterialAlphaMode AlphaMode,
        float AlphaCutoff,
        LinearRgbaImage? BaseColorTexture,
        CompressedMaterialTexture? CompressedBaseColorTexture,
        LinearRgbaImage? EmissiveTexture,
        CompressedMaterialTexture? CompressedEmissiveTexture,
        NormalizedRgbaDataImage? NormalTexture,
        CompressedMaterialTexture? CompressedNormalTexture,
        NormalizedRgbaDataImage? OrmTexture,
        CompressedMaterialTexture? CompressedOrmTexture,
        float NormalScale,
        float ClearcoatFactor,
        float ClearcoatRoughness,
        float ClearcoatNormalScale,
        NormalizedRgbaDataImage? ClearcoatTexture,
        CompressedMaterialTexture? CompressedClearcoatTexture,
        NormalizedRgbaDataImage? ClearcoatRoughnessTexture,
        CompressedMaterialTexture? CompressedClearcoatRoughnessTexture,
        NormalizedRgbaDataImage? ClearcoatNormalTexture,
        CompressedMaterialTexture? CompressedClearcoatNormalTexture,
        float AnisotropyStrength,
        float AnisotropyRotation,
        NormalizedRgbaDataImage? AnisotropyTexture,
        CompressedMaterialTexture? CompressedAnisotropyTexture,
        float TransmissionFactor,
        float IndexOfRefraction,
        float Dispersion,
        float VolumeThicknessFactor,
        float VolumeAttenuationDistance,
        LinearRgba VolumeAttenuationColor,
        NormalizedRgbaDataImage? TransmissionTexture,
        CompressedMaterialTexture? CompressedTransmissionTexture,
        NormalizedRgbaDataImage? VolumeThicknessTexture,
        CompressedMaterialTexture? CompressedVolumeThicknessTexture,
        LinearRgba SheenColor,
        float SheenRoughness,
        LinearRgbaImage? SheenColorTexture,
        CompressedMaterialTexture? CompressedSheenColorTexture,
        NormalizedRgbaDataImage? SheenRoughnessTexture,
        CompressedMaterialTexture? CompressedSheenRoughnessTexture,
        float IridescenceFactor,
        float IridescenceIndexOfRefraction,
        float IridescenceThicknessMinimum,
        float IridescenceThicknessMaximum,
        NormalizedRgbaDataImage? IridescenceTexture,
        CompressedMaterialTexture? CompressedIridescenceTexture,
        NormalizedRgbaDataImage? IridescenceThicknessTexture,
        CompressedMaterialTexture? CompressedIridescenceThicknessTexture,
        float SpecularFactor,
        LinearRgba SpecularColor,
        NormalizedRgbaDataImage? SpecularTexture,
        CompressedMaterialTexture? CompressedSpecularTexture,
        LinearRgbaImage? SpecularColorTexture,
        CompressedMaterialTexture? CompressedSpecularColorTexture,
        float DiffuseTransmissionFactor,
        LinearRgba DiffuseTransmissionColor,
        NormalizedRgbaDataImage? DiffuseTransmissionTexture,
        CompressedMaterialTexture? CompressedDiffuseTransmissionTexture,
        LinearRgbaImage? DiffuseTransmissionColorTexture,
        CompressedMaterialTexture? CompressedDiffuseTransmissionColorTexture,
        MaterialTextureMapping BaseColorTextureMapping,
        MaterialTextureMapping EmissiveTextureMapping,
        MaterialTextureMapping NormalTextureMapping,
        MaterialTextureMapping OrmTextureMapping,
        MaterialTextureMapping ClearcoatTextureMapping,
        MaterialTextureMapping ClearcoatRoughnessTextureMapping,
        MaterialTextureMapping ClearcoatNormalTextureMapping,
        MaterialTextureMapping AnisotropyTextureMapping,
        MaterialTextureMapping TransmissionTextureMapping,
        MaterialTextureMapping VolumeThicknessTextureMapping,
        MaterialTextureMapping SheenColorTextureMapping,
        MaterialTextureMapping SheenRoughnessTextureMapping,
        MaterialTextureMapping IridescenceTextureMapping,
        MaterialTextureMapping IridescenceThicknessTextureMapping,
        MaterialTextureMapping SpecularTextureMapping,
        MaterialTextureMapping SpecularColorTextureMapping,
        MaterialTextureMapping DiffuseTransmissionTextureMapping,
        MaterialTextureMapping DiffuseTransmissionColorTextureMapping,
        bool IsDoubleSided)
    {
        internal MaterialShaderVariant ShaderKey
        {
            get
            {
                PbrMaterialExtensions extensions = PbrMaterialExtensions.None;
                PbrMaterialTextureBindings textures = PbrMaterialTextureBindings.None;
                if (BaseColorTexture is not null || CompressedBaseColorTexture is not null)
                {
                    textures |= PbrMaterialTextureBindings.BaseColor;
                }
                if (NormalTexture is not null || CompressedNormalTexture is not null)
                {
                    textures |= PbrMaterialTextureBindings.Normal;
                }
                if (OrmTexture is not null || CompressedOrmTexture is not null)
                {
                    textures |= PbrMaterialTextureBindings.OcclusionRoughnessMetallic;
                }
                if (EmissiveStrength > 0f &&
                    MathF.Max(EmissiveColor.Red, MathF.Max(EmissiveColor.Green, EmissiveColor.Blue)) > 0f)
                {
                    extensions |= PbrMaterialExtensions.Emissive;
                    if (EmissiveTexture is not null || CompressedEmissiveTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.Emissive;
                    }
                }
                if (ClearcoatFactor > 0f)
                {
                    extensions |= PbrMaterialExtensions.Clearcoat;
                    if (ClearcoatTexture is not null || CompressedClearcoatTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.ClearcoatFactor;
                    }
                    if (ClearcoatRoughnessTexture is not null ||
                        CompressedClearcoatRoughnessTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.ClearcoatRoughness;
                    }
                    if (ClearcoatNormalTexture is not null || CompressedClearcoatNormalTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.ClearcoatNormal;
                    }
                }
                if (AnisotropyStrength > 0f)
                {
                    extensions |= PbrMaterialExtensions.Anisotropy;
                    if (AnisotropyTexture is not null || CompressedAnisotropyTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.Anisotropy;
                    }
                }
                if (TransmissionFactor > 0f)
                {
                    extensions |= PbrMaterialExtensions.Transmission;
                    if (TransmissionTexture is not null || CompressedTransmissionTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.Transmission;
                    }
                    if (VolumeThicknessFactor > 0f)
                    {
                        extensions |= PbrMaterialExtensions.Volume;
                        if (Dispersion > 0f)
                        {
                            extensions |= PbrMaterialExtensions.Dispersion;
                        }
                        if (VolumeThicknessTexture is not null ||
                            CompressedVolumeThicknessTexture is not null)
                        {
                            textures |= PbrMaterialTextureBindings.VolumeThickness;
                        }
                    }
                }
                if (MathF.Max(SheenColor.Red, MathF.Max(SheenColor.Green, SheenColor.Blue)) > 0f)
                {
                    extensions |= PbrMaterialExtensions.Sheen;
                    if (SheenColorTexture is not null || CompressedSheenColorTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.SheenColor;
                    }
                    if (SheenRoughnessTexture is not null || CompressedSheenRoughnessTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.SheenRoughness;
                    }
                }
                if (IridescenceFactor > 0f)
                {
                    extensions |= PbrMaterialExtensions.Iridescence;
                    if (IridescenceTexture is not null || CompressedIridescenceTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.IridescenceFactor;
                    }
                    if (IridescenceThicknessTexture is not null ||
                        CompressedIridescenceThicknessTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.IridescenceThickness;
                    }
                }
                if (SpecularFactor != 1f || !IsWhite(SpecularColor) ||
                    SpecularTexture is not null || CompressedSpecularTexture is not null ||
                    SpecularColorTexture is not null || CompressedSpecularColorTexture is not null)
                {
                    extensions |= PbrMaterialExtensions.Specular;
                    if (SpecularTexture is not null || CompressedSpecularTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.SpecularFactor;
                    }
                    if (SpecularColorTexture is not null || CompressedSpecularColorTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.SpecularColor;
                    }
                }
                if (DiffuseTransmissionFactor > 0f)
                {
                    extensions |= PbrMaterialExtensions.DiffuseTransmission;
                    if (DiffuseTransmissionTexture is not null ||
                        CompressedDiffuseTransmissionTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.DiffuseTransmissionFactor;
                    }
                    if (DiffuseTransmissionColorTexture is not null ||
                        CompressedDiffuseTransmissionColorTexture is not null)
                    {
                        textures |= PbrMaterialTextureBindings.DiffuseTransmissionColor;
                    }
                }
                MaterialBaseModel baseModel = Kind == MaterialKind.Unlit
                    ? MaterialBaseModel.Unlit
                    : MaterialBaseModel.PbrMetallicRoughness;
                return new MaterialShaderVariant(baseModel, extensions, textures, AlphaMode, IsDoubleSided);
            }
        }
    }

    private enum DataTextureSemantic
    {
        Normal,
        LinearChannels,
    }

    private readonly record struct DataTextureCacheKey(
        NormalizedRgbaDataImage Image,
        DataTextureSemantic Semantic);

    private readonly record struct CompressedDataTextureCacheKey(
        CompressedMaterialTexture Image,
        DataTextureSemantic Semantic);

    private readonly record struct PreparedDraw(
        GeometryResources Geometry,
        MeshResources Mesh,
        GraphicsRenderPipeline Pipeline,
        MaterialShaderVariant ShaderKey,
        bool IsCameraVisible,
        bool CastsShadow,
        bool IsTransparent,
        bool IsTransmission,
        bool UsesAlphaTestedDepth,
        bool IsDoubleSided,
        float CameraDistanceSquared);

    private readonly record struct MaterialPhysicalTextureBinding(
        GraphicsSampler Sampler,
        object Resource,
        GraphicsTextureView View);

    private sealed class MaterialTextureBindingPlan
    {
        internal const int PhysicalSlotCount = 10;
        private static readonly PbrMaterialTextureBindings[] SemanticOrder =
        [
            PbrMaterialTextureBindings.BaseColor,
            PbrMaterialTextureBindings.Normal,
            PbrMaterialTextureBindings.OcclusionRoughnessMetallic,
            PbrMaterialTextureBindings.Emissive,
            PbrMaterialTextureBindings.ClearcoatFactor,
            PbrMaterialTextureBindings.ClearcoatRoughness,
            PbrMaterialTextureBindings.ClearcoatNormal,
            PbrMaterialTextureBindings.Anisotropy,
            PbrMaterialTextureBindings.Transmission,
            PbrMaterialTextureBindings.VolumeThickness,
            PbrMaterialTextureBindings.SheenColor,
            PbrMaterialTextureBindings.SheenRoughness,
            PbrMaterialTextureBindings.IridescenceFactor,
            PbrMaterialTextureBindings.IridescenceThickness,
            PbrMaterialTextureBindings.SpecularFactor,
            PbrMaterialTextureBindings.SpecularColor,
            PbrMaterialTextureBindings.DiffuseTransmissionFactor,
            PbrMaterialTextureBindings.DiffuseTransmissionColor,
        ];
        private readonly Dictionary<PbrMaterialTextureBindings, int> slots;

        private MaterialTextureBindingPlan(Dictionary<PbrMaterialTextureBindings, int> slots) =>
            this.slots = slots;

        internal static bool RequiresSpecialization(MaterialShaderVariant variant) =>
            variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.SheenColor) ||
            variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.SheenRoughness) ||
            variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.IridescenceFactor) ||
            variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.IridescenceThickness) ||
            variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.SpecularFactor) ||
            variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.SpecularColor) ||
            variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.DiffuseTransmissionFactor) ||
            variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.DiffuseTransmissionColor);

        internal static MaterialTextureBindingPlan Create(MaterialShaderVariant variant)
        {
            int optionalTextureCount = variant.SampledMaterialTextureCount;
            foreach (PbrMaterialTextureBindings baseline in SemanticOrder[..3])
            {
                if (variant.TextureBindings.HasFlag(baseline))
                {
                    optionalTextureCount--;
                }
            }
            if (optionalTextureCount > PhysicalSlotCount - 3)
            {
                throw new NotSupportedException(
                    $"Material shader variant {variant} requires {optionalTextureCount} optional " +
                    $"material textures, but the portable layout permits {PhysicalSlotCount - 3} after " +
                    "reserving BaseColor, Normal and ORM for the base PBR closure.");
            }
            Dictionary<PbrMaterialTextureBindings, int> slots = new()
            {
                [PbrMaterialTextureBindings.BaseColor] = 0,
                [PbrMaterialTextureBindings.Normal] = 1,
                [PbrMaterialTextureBindings.OcclusionRoughnessMetallic] = 2,
            };
            int next = 3;
            foreach (PbrMaterialTextureBindings semantic in SemanticOrder[3..])
            {
                if (variant.TextureBindings.HasFlag(semantic))
                {
                    slots.Add(semantic, next++);
                }
            }
            return new(slots);
        }

        internal int GetPhysicalSlot(PbrMaterialTextureBindings semantic) =>
            slots.TryGetValue(semantic, out int slot) ? slot : -1;
    }

    private sealed class SharedResources : IDisposable
    {
        private readonly GraphicsDevice device;
        private readonly GraphicsTextureFormat colorFormat;
        private readonly HashSet<MaterialShaderVariant> preparedMaterialVariants = [];
        private readonly Dictionary<MaterialShaderVariant, GraphicsRenderPipeline>
            specializedMaterialPipelines = [];
        private readonly Dictionary<MaterialShaderVariant, GraphicsShaderModule>
            specializedMaterialShaders = [];
        private long materialShaderVariantCacheHits;
        private long materialShaderVariantCacheMisses;

        private SharedResources(
            GraphicsDevice device,
            GraphicsTextureFormat colorFormat,
            GraphicsShaderModule shader,
            GraphicsBindGroupLayout frameBindGroupLayout,
            GraphicsBindGroupLayout drawBindGroupLayout,
            GraphicsBindGroupLayout environmentBindGroupLayout,
            GraphicsBindGroupLayout shadowBindGroupLayout,
            GraphicsPipelineLayout pipelineLayout,
            GraphicsPipelineLayout shadowPipelineLayout,
            GraphicsBuffer frameUniformBuffer,
            GraphicsBindGroup frameBindGroup,
            GraphicsBuffer backgroundFrameUniformBuffer,
            GraphicsBindGroup backgroundFrameBindGroup,
            EnvironmentCubeGpuResources fallbackDiffuseEnvironment,
            EnvironmentCubeGpuResources fallbackSpecularEnvironment,
            BrdfLutGpuResources brdfLut,
            GraphicsBindGroup fallbackEnvironmentBindGroup,
            GraphicsTexture shadowTexture,
            GraphicsTextureView shadowView,
            GraphicsSampler shadowSampler,
            GraphicsRenderPipeline shadowBackCullPipeline,
            GraphicsRenderPipeline shadowDoubleSidedPipeline,
            GraphicsRenderPipeline alphaTestedShadowBackCullPipeline,
            GraphicsRenderPipeline alphaTestedShadowDoubleSidedPipeline,
            GraphicsRenderPipeline depthPrepassPipeline,
            GraphicsRenderPipeline alphaTestedDepthPrepassPipeline,
            GraphicsRenderPipeline unlitBackCullPipeline,
            GraphicsRenderPipeline unlitDoubleSidedPipeline,
            GraphicsRenderPipeline unlitBlendBackCullPipeline,
            GraphicsRenderPipeline unlitBlendDoubleSidedPipeline,
            GraphicsRenderPipeline pbrBackCullPipeline,
            GraphicsRenderPipeline pbrDoubleSidedPipeline,
            GraphicsRenderPipeline pbrBlendBackCullPipeline,
            GraphicsRenderPipeline pbrBlendDoubleSidedPipeline)
        {
            this.device = device;
            this.colorFormat = colorFormat;
            Shader = shader;
            FrameBindGroupLayout = frameBindGroupLayout;
            DrawBindGroupLayout = drawBindGroupLayout;
            EnvironmentBindGroupLayout = environmentBindGroupLayout;
            ShadowBindGroupLayout = shadowBindGroupLayout;
            PipelineLayout = pipelineLayout;
            ShadowPipelineLayout = shadowPipelineLayout;
            FrameUniformBuffer = frameUniformBuffer;
            FrameBindGroup = frameBindGroup;
            BackgroundFrameUniformBuffer = backgroundFrameUniformBuffer;
            BackgroundFrameBindGroup = backgroundFrameBindGroup;
            FallbackDiffuseEnvironment = fallbackDiffuseEnvironment;
            FallbackSpecularEnvironment = fallbackSpecularEnvironment;
            BrdfLut = brdfLut;
            FallbackEnvironmentBindGroup = fallbackEnvironmentBindGroup;
            ShadowTexture = shadowTexture;
            ShadowView = shadowView;
            ShadowSampler = shadowSampler;
            ShadowBackCullPipeline = shadowBackCullPipeline;
            ShadowDoubleSidedPipeline = shadowDoubleSidedPipeline;
            AlphaTestedShadowBackCullPipeline = alphaTestedShadowBackCullPipeline;
            AlphaTestedShadowDoubleSidedPipeline = alphaTestedShadowDoubleSidedPipeline;
            DepthPrepassPipeline = depthPrepassPipeline;
            AlphaTestedDepthPrepassPipeline = alphaTestedDepthPrepassPipeline;
            UnlitBackCullPipeline = unlitBackCullPipeline;
            UnlitDoubleSidedPipeline = unlitDoubleSidedPipeline;
            UnlitBlendBackCullPipeline = unlitBlendBackCullPipeline;
            UnlitBlendDoubleSidedPipeline = unlitBlendDoubleSidedPipeline;
            PbrBackCullPipeline = pbrBackCullPipeline;
            PbrDoubleSidedPipeline = pbrDoubleSidedPipeline;
            PbrBlendBackCullPipeline = pbrBlendBackCullPipeline;
            PbrBlendDoubleSidedPipeline = pbrBlendDoubleSidedPipeline;
        }

        internal GraphicsShaderModule Shader { get; }

        internal GraphicsBindGroupLayout FrameBindGroupLayout { get; }

        internal GraphicsBindGroupLayout DrawBindGroupLayout { get; }

        internal GraphicsBindGroupLayout EnvironmentBindGroupLayout { get; }

        internal GraphicsBindGroupLayout ShadowBindGroupLayout { get; }

        internal GraphicsPipelineLayout PipelineLayout { get; }

        internal GraphicsPipelineLayout ShadowPipelineLayout { get; }

        internal GraphicsBuffer FrameUniformBuffer { get; }

        internal GraphicsBindGroup FrameBindGroup { get; }

        internal GraphicsBuffer BackgroundFrameUniformBuffer { get; }

        internal GraphicsBindGroup BackgroundFrameBindGroup { get; }

        internal EnvironmentCubeGpuResources FallbackDiffuseEnvironment { get; }

        internal EnvironmentCubeGpuResources FallbackSpecularEnvironment { get; }

        internal BrdfLutGpuResources BrdfLut { get; }

        internal GraphicsBindGroup FallbackEnvironmentBindGroup { get; }

        internal GraphicsTexture ShadowTexture { get; }

        internal GraphicsTextureView ShadowView { get; }

        internal GraphicsSampler ShadowSampler { get; }

        internal GraphicsRenderPipeline ShadowBackCullPipeline { get; }

        internal GraphicsRenderPipeline ShadowDoubleSidedPipeline { get; }

        internal GraphicsRenderPipeline AlphaTestedShadowBackCullPipeline { get; }

        internal GraphicsRenderPipeline AlphaTestedShadowDoubleSidedPipeline { get; }

        internal GraphicsRenderPipeline DepthPrepassPipeline { get; }

        internal GraphicsRenderPipeline AlphaTestedDepthPrepassPipeline { get; }

        internal GraphicsRenderPipeline UnlitBackCullPipeline { get; }

        internal GraphicsRenderPipeline UnlitDoubleSidedPipeline { get; }

        internal GraphicsRenderPipeline UnlitBlendBackCullPipeline { get; }

        internal GraphicsRenderPipeline UnlitBlendDoubleSidedPipeline { get; }

        internal GraphicsRenderPipeline PbrBackCullPipeline { get; }

        internal GraphicsRenderPipeline PbrDoubleSidedPipeline { get; }

        internal GraphicsRenderPipeline PbrBlendBackCullPipeline { get; }

        internal GraphicsRenderPipeline PbrBlendDoubleSidedPipeline { get; }

        internal int CachedMaterialShaderVariantCount => preparedMaterialVariants.Count;

        internal long MaterialShaderVariantCacheHits => materialShaderVariantCacheHits;

        internal long MaterialShaderVariantCacheMisses => materialShaderVariantCacheMisses;

        internal static SharedResources Create(GraphicsDevice device, GraphicsTextureFormat colorFormat)
        {
            const string shaderCode = """
                struct PunctualLightUniform {
                    position_type: vec4f,
                    direction_inverse_range: vec4f,
                    color_intensity: vec4f,
                    spot_parameters: vec4f,
                }

                struct FrameUniforms {
                    view_projection: mat4x4f,
                    camera_position: vec4f,
                    light_direction_intensity: vec4f,
                    light_color: vec4f,
                    environment_rotation: vec4f,
                    environment_parameters: vec4f,
                    shadow_view_projection: mat4x4f,
                    shadow_parameters: vec4f,
                    inverse_view_projection: mat4x4f,
                    ambient_occlusion_parameters: vec4f,
                    punctual_lights: array<PunctualLightUniform, 16>,
                }

                struct DrawUniforms {
                    model: mat4x4f,
                    normal_matrix: mat4x4f,
                    base_color: vec4f,
                    material_parameters: vec4f,
                    alpha_parameters: vec4f,
                    texture_transform: vec4f,
                    morph_weights_0: vec4f,
                    morph_weights_1: vec4f,
                    morph_parameters: vec4f,
                    emissive_color: vec4f,
                    base_texture_transform_0: vec4f,
                    base_texture_transform_1: vec4f,
                    normal_texture_transform_0: vec4f,
                    normal_texture_transform_1: vec4f,
                    orm_texture_transform_0: vec4f,
                    orm_texture_transform_1: vec4f,
                    emissive_texture_transform_0: vec4f,
                    emissive_texture_transform_1: vec4f,
                    clearcoat_parameters: vec4f,
                    clearcoat_texture_transform_0: vec4f,
                    clearcoat_texture_transform_1: vec4f,
                    clearcoat_roughness_texture_transform_0: vec4f,
                    clearcoat_roughness_texture_transform_1: vec4f,
                    clearcoat_normal_texture_transform_0: vec4f,
                    clearcoat_normal_texture_transform_1: vec4f,
                    anisotropy_parameters: vec4f,
                    anisotropy_texture_transform_0: vec4f,
                    anisotropy_texture_transform_1: vec4f,
                    transmission_parameters: vec4f,
                    attenuation_color: vec4f,
                    transmission_texture_transform_0: vec4f,
                    transmission_texture_transform_1: vec4f,
                    thickness_texture_transform_0: vec4f,
                    thickness_texture_transform_1: vec4f,
                    sheen_color_roughness: vec4f,
                    sheen_color_texture_transform_0: vec4f,
                    sheen_color_texture_transform_1: vec4f,
                    sheen_roughness_texture_transform_0: vec4f,
                    sheen_roughness_texture_transform_1: vec4f,
                    iridescence_parameters: vec4f,
                    iridescence_texture_transform_0: vec4f,
                    iridescence_texture_transform_1: vec4f,
                    iridescence_thickness_texture_transform_0: vec4f,
                    iridescence_thickness_texture_transform_1: vec4f,
                    specular_parameters: vec4f,
                    specular_texture_transform_0: vec4f,
                    specular_texture_transform_1: vec4f,
                    specular_color_texture_transform_0: vec4f,
                    specular_color_texture_transform_1: vec4f,
                    diffuse_transmission_parameters: vec4f,
                    diffuse_transmission_texture_transform_0: vec4f,
                    diffuse_transmission_texture_transform_1: vec4f,
                    diffuse_transmission_color_texture_transform_0: vec4f,
                    diffuse_transmission_color_texture_transform_1: vec4f,
                }

                struct SkinJoint {
                    position: mat4x4f,
                    normal: mat4x4f,
                }

                struct MorphDelta {
                    position: vec4f,
                    normal: vec4f,
                    tangent: vec4f,
                }

                struct VertexOutput {
                    @builtin(position) clip_position: vec4f,
                    @location(0) world_position: vec3f,
                    @location(1) world_normal: vec3f,
                    @location(2) shadow_position: vec4f,
                    @location(3) clip_w: f32,
                    @location(4) world_bent_normal: vec3f,
                    @location(5) texture_coordinate_0: vec2f,
                    @location(6) world_tangent: vec4f,
                    @location(7) texture_coordinate_1: vec2f,
                }

                struct DepthVertexOutput {
                    @builtin(position) clip_position: vec4f,
                    @location(0) texture_coordinate_0: vec2f,
                    @location(1) texture_coordinate_1: vec2f,
                }

                @group(0) @binding(0) var<uniform> frame: FrameUniforms;
                @group(1) @binding(0) var<uniform> draw: DrawUniforms;
                @group(1) @binding(1) var base_color_sampler: sampler;
                @group(1) @binding(2) var material_base_color: texture_2d<f32>;
                @group(1) @binding(3) var normal_sampler: sampler;
                @group(1) @binding(4) var material_normal: texture_2d<f32>;
                @group(1) @binding(5) var orm_sampler: sampler;
                @group(1) @binding(6) var material_orm: texture_2d<f32>;
                @group(1) @binding(7) var emissive_sampler: sampler;
                @group(1) @binding(8) var material_emissive: texture_2d<f32>;
                @group(1) @binding(9) var<storage, read> skin_joints: array<SkinJoint>;
                @group(1) @binding(10) var<storage, read> morph_deltas: array<MorphDelta>;
                @group(1) @binding(11) var clearcoat_sampler: sampler;
                @group(1) @binding(12) var material_clearcoat: texture_2d<f32>;
                @group(1) @binding(13) var clearcoat_roughness_sampler: sampler;
                @group(1) @binding(14) var material_clearcoat_roughness: texture_2d<f32>;
                @group(1) @binding(15) var clearcoat_normal_sampler: sampler;
                @group(1) @binding(16) var material_clearcoat_normal: texture_2d<f32>;
                @group(1) @binding(17) var anisotropy_sampler: sampler;
                @group(1) @binding(18) var material_anisotropy: texture_2d<f32>;
                @group(1) @binding(19) var transmission_sampler: sampler;
                @group(1) @binding(20) var material_transmission: texture_2d<f32>;
                @group(1) @binding(21) var thickness_sampler: sampler;
                @group(1) @binding(22) var material_thickness: texture_2d<f32>;
                @group(2) @binding(0) var environment_sampler: sampler;
                @group(2) @binding(1) var environment_diffuse: texture_cube<f32>;
                @group(2) @binding(2) var environment_specular: texture_cube_array<f32>;
                @group(2) @binding(3) var environment_brdf: texture_2d<f32>;
                @group(3) @binding(0) var shadow_sampler: sampler_comparison;
                @group(3) @binding(1) var shadow_map: texture_depth_2d;
                @group(3) @binding(2) var ambient_occlusion_depth: texture_depth_2d;
                @group(3) @binding(3) var scene_background_sampler: sampler;
                @group(3) @binding(4) var scene_background: texture_2d<f32>;

                fn morph_weight(index: u32) -> f32 {
                    if (index < 4u) {
                        return draw.morph_weights_0[index];
                    }
                    return draw.morph_weights_1[index - 4u];
                }

                fn morph_position(position: vec3f, vertex_index: u32) -> vec3f {
                    var result = position;
                    let target_count = u32(draw.morph_parameters.x + 0.5);
                    let vertex_count = u32(draw.morph_parameters.y + 0.5);
                    for (var morph_index = 0u; morph_index < target_count; morph_index += 1u) {
                        result += morph_deltas[morph_index * vertex_count + vertex_index].position.xyz *
                            morph_weight(morph_index);
                    }
                    return result;
                }

                fn morph_normal(normal: vec3f, vertex_index: u32) -> vec3f {
                    var result = normal;
                    let target_count = u32(draw.morph_parameters.x + 0.5);
                    let vertex_count = u32(draw.morph_parameters.y + 0.5);
                    for (var morph_index = 0u; morph_index < target_count; morph_index += 1u) {
                        result += morph_deltas[morph_index * vertex_count + vertex_index].normal.xyz *
                            morph_weight(morph_index);
                    }
                    return result;
                }

                fn morph_tangent(tangent: vec3f, vertex_index: u32) -> vec3f {
                    var result = tangent;
                    let target_count = u32(draw.morph_parameters.x + 0.5);
                    let vertex_count = u32(draw.morph_parameters.y + 0.5);
                    for (var morph_index = 0u; morph_index < target_count; morph_index += 1u) {
                        result += morph_deltas[morph_index * vertex_count + vertex_index].tangent.xyz *
                            morph_weight(morph_index);
                    }
                    return result;
                }

                fn skin_position(position: vec3f, indices: vec4u, weights: vec4f) -> vec4f {
                    return
                        (skin_joints[indices.x].position * vec4f(position, 1.0)) * weights.x +
                        (skin_joints[indices.y].position * vec4f(position, 1.0)) * weights.y +
                        (skin_joints[indices.z].position * vec4f(position, 1.0)) * weights.z +
                        (skin_joints[indices.w].position * vec4f(position, 1.0)) * weights.w;
                }

                fn skin_direction(direction: vec3f, indices: vec4u, weights: vec4f) -> vec3f {
                    return
                        (skin_joints[indices.x].normal * vec4f(direction, 0.0)).xyz * weights.x +
                        (skin_joints[indices.y].normal * vec4f(direction, 0.0)).xyz * weights.y +
                        (skin_joints[indices.z].normal * vec4f(direction, 0.0)).xyz * weights.z +
                        (skin_joints[indices.w].normal * vec4f(direction, 0.0)).xyz * weights.w;
                }

                @vertex fn vs_main(
                    @location(0) position: vec3f,
                    @location(1) normal: vec3f,
                    @location(2) bent_normal: vec3f,
                    @location(3) texture_coordinate: vec2f,
                    @location(4) joint_indices_float: vec4f,
                    @location(5) joint_weights: vec4f,
                    @location(6) tangent: vec4f,
                    @location(7) texture_coordinate_1: vec2f,
                    @builtin(vertex_index) vertex_index: u32,
                ) -> VertexOutput {
                    let joint_indices = vec4u(joint_indices_float);
                    let morphed_position = morph_position(position, vertex_index);
                    let morphed_normal = morph_normal(normal, vertex_index);
                    let morphed_bent_normal = morph_normal(bent_normal, vertex_index);
                    let skinned_position = skin_position(
                        morphed_position,
                        joint_indices,
                        joint_weights);
                    let skinned_normal = skin_direction(
                        morphed_normal,
                        joint_indices,
                        joint_weights);
                    let skinned_bent_normal = skin_direction(
                        morphed_bent_normal,
                        joint_indices,
                        joint_weights);
                    let world_position = draw.model * skinned_position;
                    var output: VertexOutput;
                    output.clip_position = frame.view_projection * world_position;
                    output.world_position = world_position.xyz;
                    output.world_normal =
                        (draw.normal_matrix * vec4f(skinned_normal, 0.0)).xyz;
                    output.shadow_position = frame.shadow_view_projection * world_position;
                    output.clip_w = output.clip_position.w;
                    output.world_bent_normal =
                        (draw.normal_matrix * vec4f(skinned_bent_normal, 0.0)).xyz;
                    output.texture_coordinate_0 = texture_coordinate;
                    output.texture_coordinate_1 = texture_coordinate_1;
                    output.world_tangent = vec4f(0.0);
                    if (abs(tangent.w) > 0.5) {
                        let skinned_tangent = skin_direction(
                            morph_tangent(tangent.xyz, vertex_index),
                            joint_indices,
                            joint_weights);
                        let transformed_tangent =
                            (draw.normal_matrix * vec4f(skinned_tangent, 0.0)).xyz;
                        let world_normal = normalize(output.world_normal);
                        output.world_tangent = vec4f(
                            normalize(transformed_tangent -
                                world_normal * dot(world_normal, transformed_tangent)),
                            tangent.w);
                    }
                    return output;
                }

                @vertex fn vs_depth_prepass(
                    @location(0) position: vec3f,
                    @location(1) normal: vec3f,
                    @location(2) bent_normal: vec3f,
                    @location(3) texture_coordinate: vec2f,
                    @location(4) joint_indices_float: vec4f,
                    @location(5) joint_weights: vec4f,
                    @location(6) tangent: vec4f,
                    @location(7) texture_coordinate_1: vec2f,
                    @builtin(vertex_index) vertex_index: u32,
                ) -> DepthVertexOutput {
                    _ = normal;
                    _ = bent_normal;
                    _ = tangent;
                    var output: DepthVertexOutput;
                    output.clip_position = frame.view_projection * draw.model * skin_position(
                        morph_position(position, vertex_index),
                        vec4u(joint_indices_float),
                        joint_weights);
                    output.texture_coordinate_0 = texture_coordinate;
                    output.texture_coordinate_1 = texture_coordinate_1;
                    return output;
                }

                @vertex fn vs_shadow(
                    @location(0) position: vec3f,
                    @location(1) normal: vec3f,
                    @location(2) bent_normal: vec3f,
                    @location(3) texture_coordinate: vec2f,
                    @location(4) joint_indices_float: vec4f,
                    @location(5) joint_weights: vec4f,
                    @location(6) tangent: vec4f,
                    @location(7) texture_coordinate_1: vec2f,
                    @builtin(vertex_index) vertex_index: u32,
                ) -> DepthVertexOutput {
                    _ = normal;
                    _ = bent_normal;
                    _ = tangent;
                    var output: DepthVertexOutput;
                    output.clip_position = frame.shadow_view_projection * draw.model * skin_position(
                        morph_position(position, vertex_index),
                        vec4u(joint_indices_float),
                        joint_weights);
                    output.texture_coordinate_0 = texture_coordinate;
                    output.texture_coordinate_1 = texture_coordinate_1;
                    return output;
                }

                fn map_texture_coordinate(
                    texture_coordinate_0: vec2f,
                    texture_coordinate_1: vec2f,
                    transform_0: vec4f,
                    transform_1: vec4f,
                ) -> vec2f {
                    let source = select(
                        texture_coordinate_0,
                        texture_coordinate_1,
                        transform_1.z > 0.5);
                    return vec2f(
                        transform_0.x * source.x + transform_0.y * source.y,
                        transform_0.z * source.x + transform_0.w * source.y) +
                        transform_1.xy;
                }

                fn depth_material_alpha(input: DepthVertexOutput) -> f32 {
                    let texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.base_texture_transform_0,
                        draw.base_texture_transform_1);
                    return draw.base_color.a * textureSample(
                        material_base_color,
                        base_color_sampler,
                        texture_coordinate).a;
                }

                @fragment fn fs_alpha_test_depth(input: DepthVertexOutput) {
                    let alpha = depth_material_alpha(input);
                    if (alpha < draw.alpha_parameters.y) {
                        discard;
                    }
                }

                fn resolve_material_alpha(material_alpha: f32) -> f32 {
                    let alpha_mode = u32(draw.alpha_parameters.x + 0.5);
                    if (alpha_mode == 1u && material_alpha < draw.alpha_parameters.y) {
                        discard;
                    }
                    return select(1.0, material_alpha, alpha_mode == 2u);
                }

                fn material_fragment(color: vec3f, alpha: f32) -> vec4f {
                    let is_blended = u32(draw.alpha_parameters.x + 0.5) == 2u;
                    return vec4f(select(color, color * alpha, is_blended), alpha);
                }

                fn apply_normal_map(
                    world_position: vec3f,
                    texture_coordinate: vec2f,
                    geometric_normal: vec3f,
                    authored_tangent: vec4f,
                    sampled_normal: vec3f,
                    normal_scale: f32,
                ) -> vec3f {
                    // Tangent availability may vary across fragments; derivatives must not.
                    let position_x = dpdx(world_position);
                    let position_y = dpdy(world_position);
                    let uv_x = dpdx(texture_coordinate);
                    let uv_y = dpdy(texture_coordinate);
                    var tangent = vec3f(0.0);
                    var bitangent = vec3f(0.0);
                    if (abs(authored_tangent.w) > 0.5) {
                        tangent = normalize(authored_tangent.xyz - geometric_normal *
                            dot(geometric_normal, authored_tangent.xyz));
                        bitangent = cross(geometric_normal, tangent) * authored_tangent.w;
                    } else {
                        let determinant = uv_x.x * uv_y.y - uv_x.y * uv_y.x;
                        if (abs(determinant) < 0.0000001) {
                            return geometric_normal;
                        }
                        let raw_tangent =
                            (position_x * uv_y.y - position_y * uv_x.y) / determinant;
                        tangent = normalize(
                            raw_tangent - geometric_normal * dot(geometric_normal, raw_tangent));
                        bitangent = cross(geometric_normal, tangent) * sign(determinant);
                    }
                    var mapped = sampled_normal * 2.0 - vec3f(1.0);
                    mapped.x *= normal_scale;
                    mapped.y *= normal_scale;
                    return normalize(
                        tangent * mapped.x + bitangent * mapped.y + geometric_normal * mapped.z);
                }

                @fragment fn fs_unlit(
                    input: VertexOutput,
                    @builtin(front_facing) front_facing: bool,
                ) -> @location(0) vec4f {
                    let texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.base_texture_transform_0,
                        draw.base_texture_transform_1);
                    let sampled_base_color = draw.base_color * textureSample(
                        material_base_color,
                        base_color_sampler,
                        texture_coordinate);
                    let alpha = resolve_material_alpha(sampled_base_color.a);
                    let render_layer = u32(frame.environment_parameters.z + 0.5);
                    if (render_layer == 9u) {
                        let geometric_normal = normalize(input.world_normal);
                        let oriented_normal = select(
                            -geometric_normal,
                            geometric_normal,
                            front_facing);
                        return material_fragment(
                            oriented_normal * 0.5 + vec3f(0.5),
                            alpha);
                    }
                    return material_fragment(sampled_base_color.rgb, alpha);
                }

                fn distribution_ggx(normal: vec3f, half_vector: vec3f, roughness: f32) -> f32 {
                    let alpha = roughness * roughness;
                    let alpha_squared = alpha * alpha;
                    let normal_dot_half = max(dot(normal, half_vector), 0.0);
                    let denominator = normal_dot_half * normal_dot_half * (alpha_squared - 1.0) + 1.0;
                    return alpha_squared / max(3.14159265 * denominator * denominator, 0.000001);
                }

                fn distribution_charlie(normal_dot_half: f32, sheen_roughness: f32) -> f32 {
                    let alpha = max(sheen_roughness * sheen_roughness, 0.0001);
                    let inverse_alpha = 1.0 / alpha;
                    let sine_squared_half = max(1.0 - normal_dot_half * normal_dot_half, 0.000001);
                    return (2.0 + inverse_alpha) *
                        pow(sine_squared_half, inverse_alpha * 0.5) / (2.0 * 3.14159265);
                }

                fn sheen_lambda_fit(cosine: f32, alpha: f32) -> f32 {
                    let one_minus_alpha_squared = (1.0 - alpha) * (1.0 - alpha);
                    let a = mix(21.5473, 25.3245, one_minus_alpha_squared);
                    let b = mix(3.82987, 3.32435, one_minus_alpha_squared);
                    let c = mix(0.19823, 0.16801, one_minus_alpha_squared);
                    let d = mix(-1.97760, -1.27393, one_minus_alpha_squared);
                    let e = mix(-4.32054, -4.85967, one_minus_alpha_squared);
                    return a / (1.0 + b * pow(max(cosine, 0.000001), c)) + d * cosine + e;
                }

                fn sheen_lambda(cosine: f32, alpha: f32) -> f32 {
                    let fitted = select(
                        2.0 * sheen_lambda_fit(0.5, alpha) - sheen_lambda_fit(1.0 - cosine, alpha),
                        sheen_lambda_fit(cosine, alpha),
                        abs(cosine) < 0.5);
                    return exp(fitted);
                }

                fn visibility_sheen(normal_dot_view: f32, normal_dot_light: f32, alpha: f32) -> f32 {
                    if (normal_dot_view <= 0.0001 || normal_dot_light <= 0.0001) {
                        return 0.0;
                    }
                    return min(64.0, 1.0 / max(
                        (1.0 + sheen_lambda(normal_dot_view, alpha) +
                            sheen_lambda(normal_dot_light, alpha)) *
                            (4.0 * normal_dot_view * normal_dot_light),
                        0.0001));
                }

                fn anisotropic_tangent(
                    world_position: vec3f,
                    texture_coordinate: vec2f,
                    normal: vec3f,
                    authored_tangent: vec4f,
                ) -> vec3f {
                    // Evaluate derivatives before the per-fragment authored-tangent branch.
                    let position_x = dpdx(world_position);
                    let position_y = dpdy(world_position);
                    let uv_x = dpdx(texture_coordinate);
                    let uv_y = dpdy(texture_coordinate);
                    if (abs(authored_tangent.w) > 0.5) {
                        return normalize(authored_tangent.xyz - normal *
                            dot(normal, authored_tangent.xyz));
                    }
                    let determinant = uv_x.x * uv_y.y - uv_x.y * uv_y.x;
                    if (abs(determinant) < 0.0000001) {
                        let fallback_axis = select(
                            vec3f(1.0, 0.0, 0.0),
                            vec3f(0.0, 1.0, 0.0),
                            abs(normal.x) > 0.9);
                        return normalize(cross(fallback_axis, normal));
                    }
                    let raw_tangent =
                        (position_x * uv_y.y - position_y * uv_x.y) / determinant;
                    return normalize(raw_tangent - normal * dot(normal, raw_tangent));
                }

                fn distribution_ggx_anisotropic(
                    normal_dot_half: f32,
                    tangent_dot_half: f32,
                    bitangent_dot_half: f32,
                    alpha_t: f32,
                    alpha_b: f32,
                ) -> f32 {
                    let alpha_product = alpha_t * alpha_b;
                    let vector = vec3f(
                        alpha_b * tangent_dot_half,
                        alpha_t * bitangent_dot_half,
                        alpha_product * normal_dot_half);
                    let weight = alpha_product / max(dot(vector, vector), 0.000001);
                    return alpha_product * weight * weight / 3.14159265;
                }

                fn visibility_ggx_anisotropic(
                    normal_dot_light: f32,
                    normal_dot_view: f32,
                    tangent_dot_view: f32,
                    bitangent_dot_view: f32,
                    tangent_dot_light: f32,
                    bitangent_dot_light: f32,
                    alpha_t: f32,
                    alpha_b: f32,
                ) -> f32 {
                    let ggx_view = normal_dot_light * length(vec3f(
                        alpha_t * tangent_dot_view,
                        alpha_b * bitangent_dot_view,
                        normal_dot_view));
                    let ggx_light = normal_dot_view * length(vec3f(
                        alpha_t * tangent_dot_light,
                        alpha_b * bitangent_dot_light,
                        normal_dot_light));
                    return clamp(0.5 / max(ggx_view + ggx_light, 0.000001), 0.0, 1.0);
                }

                fn geometry_schlick_ggx(normal_dot_direction: f32, roughness: f32) -> f32 {
                    let factor = roughness + 1.0;
                    let k = factor * factor / 8.0;
                    return normal_dot_direction /
                        max(normal_dot_direction * (1.0 - k) + k, 0.000001);
                }

                fn fresnel_schlick(cosine: f32, reflectance: vec3f) -> vec3f {
                    return reflectance + (vec3f(1.0) - reflectance) *
                        pow(clamp(1.0 - cosine, 0.0, 1.0), 5.0);
                }

                fn iridescence_fresnel(
                    outside_ior: f32,
                    film_ior: f32,
                    base_fresnel: vec3f,
                    film_thickness: f32,
                    cosine_theta_1: f32,
                ) -> vec3f {
                    let safe_film_ior = max(film_ior, 1.0);
                    let cosine_1 = clamp(cosine_theta_1, 0.0, 1.0);
                    let eta = outside_ior / safe_film_ior;
                    let cosine_2 = sqrt(max(
                        1.0 - eta * eta * (1.0 - cosine_1 * cosine_1),
                        0.0));
                    let interface_numerator = outside_ior * cosine_1 -
                        safe_film_ior * cosine_2;
                    let interface_denominator = max(
                        outside_ior * cosine_1 + safe_film_ior * cosine_2,
                        0.000001);
                    let interface_reflectance = clamp(
                        pow(interface_numerator / interface_denominator, 2.0),
                        0.0,
                        0.999);
                    let film_amplitude = sqrt(interface_reflectance);
                    let base_amplitude = sqrt(clamp(base_fresnel, vec3f(0.0), vec3f(0.999)));
                    let interference_amplitude = film_amplitude * base_amplitude;
                    let phase = 12.5663706 * safe_film_ior * max(film_thickness, 0.0) *
                        cosine_2 / vec3f(650.0, 510.0, 475.0);
                    let interference = 2.0 * interference_amplitude * cos(phase);
                    return clamp(
                        (vec3f(interface_reflectance) + base_fresnel + interference) /
                            max(vec3f(1.0) + vec3f(interface_reflectance) * base_fresnel +
                                interference, vec3f(0.000001)),
                        vec3f(0.0),
                        vec3f(1.0));
                }

                fn specular_ambient_visibility(
                    normal_dot_view: f32,
                    ambient_visibility: f32,
                    roughness: f32,
                ) -> f32 {
                    let exponent = exp2(-16.0 * roughness - 1.0);
                    return clamp(
                        pow(normal_dot_view + ambient_visibility, exponent) -
                            1.0 + ambient_visibility,
                        0.0,
                        1.0);
                }

                fn rotate_by_quaternion(value: vec3f, quaternion: vec4f) -> vec3f {
                    let intermediate = 2.0 * cross(quaternion.xyz, value);
                    return value + quaternion.w * intermediate +
                        cross(quaternion.xyz, intermediate);
                }

                fn directional_shadow_visibility(
                    shadow_position: vec4f,
                    geometric_normal: vec3f,
                    light: vec3f,
                ) -> f32 {
                    if (frame.shadow_parameters.x <= 0.0) {
                        return 1.0;
                    }
                    let projected = shadow_position.xyz / shadow_position.w;
                    let uv = projected.xy * vec2f(0.5, -0.5) + vec2f(0.5);
                    let uv_x = dpdx(uv);
                    let uv_y = dpdy(uv);
                    let depth_x = dpdx(projected.z);
                    let depth_y = dpdy(projected.z);
                    let derivative_determinant = uv_x.x * uv_y.y - uv_x.y * uv_y.x;
                    var receiver_depth_gradient = vec2f(0.0);
                    if (abs(derivative_determinant) > 0.0000001) {
                        receiver_depth_gradient = vec2f(
                            (depth_x * uv_y.y - uv_x.y * depth_y) / derivative_determinant,
                            (uv_x.x * depth_y - depth_x * uv_y.x) / derivative_determinant);
                    }
                    if (projected.z <= 0.0 || projected.z >= 1.0 ||
                        any(uv < vec2f(0.0)) || any(uv > vec2f(1.0))) {
                        return 1.0;
                    }
                    let dimensions_u = textureDimensions(shadow_map);
                    let dimensions = vec2f(dimensions_u);
                    let texel = vec2f(1.0) / dimensions;
                    let bias = max(
                        0.0015 * (1.0 - max(dot(geometric_normal, light), 0.0)),
                        0.00025);
                    let emitter_tangent = frame.shadow_parameters.y;
                    let shadow_world_width = frame.shadow_parameters.z;
                    let shadow_depth_range = frame.shadow_parameters.w;
                    let maximum_search_radius = min(
                        emitter_tangent * shadow_depth_range / shadow_world_width,
                        0.08);
                    let search_radius = max(texel.x * 2.0, maximum_search_radius * projected.z);
                    var blocker_depth_sum = 0.0;
                    var blocker_count = 0.0;
                    for (var index = 0u; index < 32u; index += 1u) {
                        let sample_index = f32(index) + 0.5;
                        let sample_angle = sample_index * 2.39996322973;
                        let disk_sample = vec2f(
                            cos(sample_angle),
                            sin(sample_angle)) * sqrt(sample_index / 32.0);
                        let sample_offset = disk_sample * search_radius;
                        let sample_uv = clamp(
                            uv + sample_offset,
                            vec2f(0.0),
                            vec2f(1.0) - texel);
                        let sample_coordinate = clamp(
                            vec2i(sample_uv * dimensions),
                            vec2i(0),
                            vec2i(dimensions_u) - vec2i(1));
                        let sample_depth = textureLoad(shadow_map, sample_coordinate, 0);
                        let receiver_depth = projected.z +
                            dot(receiver_depth_gradient, sample_offset);
                        if (sample_depth < receiver_depth - bias) {
                            blocker_depth_sum += sample_depth;
                            blocker_count += 1.0;
                        }
                    }
                    if (blocker_count == 0.0) {
                        return 1.0;
                    }
                    let average_blocker_depth = blocker_depth_sum / blocker_count;
                    let receiver_blocker_distance =
                        (projected.z - average_blocker_depth) * shadow_depth_range;
                    let penumbra_radius = clamp(
                        emitter_tangent * receiver_blocker_distance / shadow_world_width,
                        texel.x * 1.5,
                        0.08);
                    var visibility = 0.0;
                    for (var index = 0u; index < 32u; index += 1u) {
                        let sample_index = f32(index) + 0.5;
                        let sample_angle = sample_index * 2.39996322973;
                        let disk_sample = vec2f(
                            cos(sample_angle),
                            sin(sample_angle)) * sqrt(sample_index / 32.0);
                        let sample_offset = disk_sample * penumbra_radius;
                        let receiver_depth = projected.z +
                            dot(receiver_depth_gradient, sample_offset);
                        // The shadow map has one mip. Explicit level zero avoids implicit
                        // derivatives after the non-uniform blocker-search early returns.
                        visibility += textureSampleCompareLevel(
                            shadow_map,
                            shadow_sampler,
                            uv + sample_offset,
                            receiver_depth - bias);
                    }
                    let sampled_visibility = visibility / 32.0;
                    return mix(
                        1.0,
                        sampled_visibility,
                        clamp(frame.shadow_parameters.x, 0.0, 1.0));
                }

                fn reconstruct_world_position(uv: vec2f, depth: f32) -> vec3f {
                    let clip = vec4f(
                        uv.x * 2.0 - 1.0,
                        (1.0 - uv.y) * 2.0 - 1.0,
                        depth,
                        1.0);
                    let world = frame.inverse_view_projection * clip;
                    return world.xyz / max(abs(world.w), 0.000001);
                }

                fn screen_space_ambient_occlusion(
                    fragment_position: vec4f,
                    clip_w: f32,
                    world_position: vec3f,
                    normal: vec3f,
                ) -> f32 {
                    if (frame.ambient_occlusion_parameters.x < 0.5) {
                        return 1.0;
                    }
                    let dimensions_u = textureDimensions(ambient_occlusion_depth);
                    let dimensions = vec2f(dimensions_u);
                    let center_uv = fragment_position.xy / dimensions;
                    let radius = frame.ambient_occlusion_parameters.y;
                    let strength = frame.ambient_occlusion_parameters.z;
                    let normal_bias = frame.ambient_occlusion_parameters.w;
                    let projected_radius = clamp(
                        radius * frame.view_projection[1][1] * dimensions.y /
                            max(2.0 * abs(clip_w), 0.000001),
                        2.0,
                        64.0);
                    let noise = fract(
                        52.9829189 * fract(
                            dot(floor(fragment_position.xy), vec2f(0.06711056, 0.00583715))));
                    let rotation_angle = noise * 6.28318530718;
                    let rotation_sine = sin(rotation_angle);
                    let rotation_cosine = cos(rotation_angle);
                    let rotation = mat2x2f(
                        rotation_cosine,
                        rotation_sine,
                        -rotation_sine,
                        rotation_cosine);
                    var obscurance = 0.0;
                    for (var index = 0u; index < 32u; index += 1u) {
                        let sample_index = f32(index) + 0.5;
                        let sample_angle = sample_index * 2.39996322973;
                        let sample_radius = sqrt(sample_index / 32.0);
                        let sample_offset = rotation * vec2f(
                            cos(sample_angle),
                            sin(sample_angle)) * sample_radius;
                        let sample_coordinate = clamp(
                            vec2i(fragment_position.xy + sample_offset * projected_radius),
                            vec2i(0),
                            vec2i(dimensions_u) - vec2i(1));
                        let sample_depth = textureLoad(
                            ambient_occlusion_depth,
                            sample_coordinate,
                            0);
                        if (sample_depth < 1.0) {
                            let sample_uv = (vec2f(sample_coordinate) + vec2f(0.5)) / dimensions;
                            let sample_world = reconstruct_world_position(sample_uv, sample_depth);
                            let difference = sample_world - world_position;
                            let distance = length(difference);
                            if (distance > 0.0001 && distance < radius) {
                                let hemisphere = smoothstep(
                                    normal_bias,
                                    0.35,
                                    dot(normal, difference / distance));
                                let range_weight = 1.0 - smoothstep(radius * 0.15, radius, distance);
                                obscurance += hemisphere * range_weight;
                            }
                        }
                    }
                    return clamp(1.0 - strength * obscurance / 32.0, 0.0, 1.0);
                }

                @fragment fn fs_pbr(
                    input: VertexOutput,
                    @builtin(front_facing) front_facing: bool,
                ) -> @location(0) vec4f {
                    let base_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.base_texture_transform_0,
                        draw.base_texture_transform_1);
                    let normal_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.normal_texture_transform_0,
                        draw.normal_texture_transform_1);
                    let orm_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.orm_texture_transform_0,
                        draw.orm_texture_transform_1);
                    let emissive_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.emissive_texture_transform_0,
                        draw.emissive_texture_transform_1);
                    let clearcoat_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.clearcoat_texture_transform_0,
                        draw.clearcoat_texture_transform_1);
                    let clearcoat_roughness_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.clearcoat_roughness_texture_transform_0,
                        draw.clearcoat_roughness_texture_transform_1);
                    let clearcoat_normal_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.clearcoat_normal_texture_transform_0,
                        draw.clearcoat_normal_texture_transform_1);
                    let anisotropy_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.anisotropy_texture_transform_0,
                        draw.anisotropy_texture_transform_1);
                    let transmission_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.transmission_texture_transform_0,
                        draw.transmission_texture_transform_1);
                    let thickness_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.thickness_texture_transform_0,
                        draw.thickness_texture_transform_1);
                    let sheen_color_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.sheen_color_texture_transform_0,
                        draw.sheen_color_texture_transform_1);
                    let sheen_roughness_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.sheen_roughness_texture_transform_0,
                        draw.sheen_roughness_texture_transform_1);
                    let iridescence_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.iridescence_texture_transform_0,
                        draw.iridescence_texture_transform_1);
                    let iridescence_thickness_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.iridescence_thickness_texture_transform_0,
                        draw.iridescence_thickness_texture_transform_1);
                    let specular_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.specular_texture_transform_0,
                        draw.specular_texture_transform_1);
                    let specular_color_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.specular_color_texture_transform_0,
                        draw.specular_color_texture_transform_1);
                    let diffuse_transmission_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.diffuse_transmission_texture_transform_0,
                        draw.diffuse_transmission_texture_transform_1);
                    let diffuse_transmission_color_texture_coordinate = map_texture_coordinate(
                        input.texture_coordinate_0,
                        input.texture_coordinate_1,
                        draw.diffuse_transmission_color_texture_transform_0,
                        draw.diffuse_transmission_color_texture_transform_1);
                    let sampled_base_color = draw.base_color * textureSample(
                        material_base_color,
                        base_color_sampler,
                        base_texture_coordinate);
                    let output_alpha = resolve_material_alpha(sampled_base_color.a);
                    let geometric_normal = normalize(input.world_normal);
                    let oriented_geometric_normal = select(
                        -geometric_normal,
                        geometric_normal,
                        front_facing);
                    let normal = apply_normal_map(
                        input.world_position,
                        normal_texture_coordinate,
                        oriented_geometric_normal,
                        vec4f(
                            select(-input.world_tangent.xyz, input.world_tangent.xyz, front_facing),
                            input.world_tangent.w),
                        textureSample(
                            material_normal,
                            normal_sampler,
                            normal_texture_coordinate).xyz,
                        draw.material_parameters.w);
                    let clearcoat_normal = apply_normal_map(
                        input.world_position,
                        clearcoat_normal_texture_coordinate,
                        oriented_geometric_normal,
                        vec4f(
                            select(-input.world_tangent.xyz, input.world_tangent.xyz, front_facing),
                            input.world_tangent.w),
                        textureSample(
                            material_clearcoat_normal,
                            clearcoat_normal_sampler,
                            clearcoat_normal_texture_coordinate).xyz,
                        draw.clearcoat_parameters.z);
                    let geometric_bent_normal = normalize(input.world_bent_normal);
                    let bent_normal = select(
                        -geometric_bent_normal,
                        geometric_bent_normal,
                        front_facing);
                    let indirect_normal = normalize(
                        bent_normal + normal - oriented_geometric_normal);
                    let view = normalize(frame.camera_position.xyz - input.world_position);
                    let normal_dot_view = max(dot(normal, view), 0.0);
                    let sampled_orm = textureSample(
                        material_orm,
                        orm_sampler,
                        orm_texture_coordinate);
                    let sampled_emissive = draw.emissive_color.rgb * draw.emissive_color.a *
                        textureSample(
                            material_emissive,
                            emissive_sampler,
                            emissive_texture_coordinate).rgb;
                    let metallic = draw.material_parameters.x * sampled_orm.b;
                    let roughness = max(draw.material_parameters.y * sampled_orm.g, 0.045);
                    let clearcoat = draw.clearcoat_parameters.x * textureSample(
                        material_clearcoat,
                        clearcoat_sampler,
                        clearcoat_texture_coordinate).r;
                    let clearcoat_roughness = max(
                        draw.clearcoat_parameters.y * textureSample(
                            material_clearcoat_roughness,
                            clearcoat_roughness_sampler,
                            clearcoat_roughness_texture_coordinate).g,
                        0.045);
                    let sampled_anisotropy = textureSample(
                        material_anisotropy,
                        anisotropy_sampler,
                        anisotropy_texture_coordinate).rgb;
                    let sheen_color = draw.sheen_color_roughness.rgb;
                    let sheen_roughness = clamp(draw.sheen_color_roughness.w, 0.07, 1.0);
                    let iridescence_factor = clamp(draw.iridescence_parameters.x, 0.0, 1.0);
                    let iridescence_thickness = draw.iridescence_parameters.w;
                    let specular_factor = clamp(draw.specular_parameters.x, 0.0, 1.0);
                    let specular_color = draw.specular_parameters.yzw;
                    let diffuse_transmission_factor = clamp(draw.diffuse_transmission_parameters.x, 0.0, 1.0);
                    let diffuse_transmission_color = draw.diffuse_transmission_parameters.yzw;
                    let surface_transmission = clamp(
                        draw.transmission_parameters.x * textureSample(
                            material_transmission,
                            transmission_sampler,
                            transmission_texture_coordinate).r,
                        0.0,
                        1.0);
                    let effective_diffuse_transmission = diffuse_transmission_factor *
                        (1.0 - surface_transmission);
                    let sheen_max_color = max(sheen_color.r, max(sheen_color.g, sheen_color.b));
                    let sheen_directional_albedo = textureSample(
                        environment_brdf,
                        environment_sampler,
                        vec2f(normal_dot_view, sheen_roughness)).b;
                    let sheen_layer_attenuation = 1.0 - sheen_max_color *
                        sheen_directional_albedo;
                    let anisotropy_strength = draw.anisotropy_parameters.x * sampled_anisotropy.b;
                    let sampled_direction = sampled_anisotropy.rg * 2.0 - vec2f(1.0);
                    let safe_direction = select(
                        vec2f(1.0, 0.0),
                        normalize(sampled_direction),
                        dot(sampled_direction, sampled_direction) > 0.000001);
                    let rotation = draw.anisotropy_parameters.y;
                    let anisotropy_direction = mat2x2f(
                        cos(rotation), sin(rotation),
                        -sin(rotation), cos(rotation)) * safe_direction;
                    let base_tangent = anisotropic_tangent(
                        input.world_position,
                        anisotropy_texture_coordinate,
                        normal,
                        vec4f(
                            select(-input.world_tangent.xyz, input.world_tangent.xyz, front_facing),
                            input.world_tangent.w));
                    let base_bitangent = normalize(cross(normal, base_tangent));
                    let anisotropy_tangent = normalize(
                        base_tangent * anisotropy_direction.x +
                        base_bitangent * anisotropy_direction.y);
                    let anisotropy_bitangent = normalize(cross(normal, anisotropy_tangent));
                    let alpha_roughness = roughness * roughness;
                    let alpha_t = mix(
                        alpha_roughness,
                        1.0,
                        anisotropy_strength * anisotropy_strength);
                    let alpha_b = alpha_roughness;
                    let base_color = sampled_base_color.rgb;
                    let dielectric_f0_scalar = pow(
                        (draw.transmission_parameters.y - 1.0) /
                            (draw.transmission_parameters.y + 1.0),
                        2.0);
                    let dielectric_f0 = min(
                        vec3f(dielectric_f0_scalar) * specular_color,
                        vec3f(1.0));
                    let f0 = mix(dielectric_f0, base_color, metallic);
                    let clearcoat_f0 = vec3f(0.04);
                    let clearcoat_normal_dot_view = max(dot(clearcoat_normal, view), 0.0);
                    let dielectric_view_fresnel = mix(
                        dielectric_f0 * specular_factor,
                        vec3f(specular_factor),
                        pow(clamp(1.0 - normal_dot_view, 0.0, 1.0), 5.0));
                    let base_view_fresnel = mix(
                        dielectric_view_fresnel,
                        fresnel_schlick(normal_dot_view, base_color),
                        metallic);
                    let thin_film_view_fresnel = iridescence_fresnel(
                        1.0,
                        draw.iridescence_parameters.y,
                        base_view_fresnel,
                        iridescence_thickness,
                        normal_dot_view);
                    var direct_lighting = vec3f(0.0);
                    var base_specular_direct_lighting = vec3f(0.0);
                    var diffuse_transmission_direct_lighting = vec3f(0.0);
                    var sheen_direct_lighting = vec3f(0.0);
                    var clearcoat_direct_lighting = vec3f(0.0);
                    var directional_shadow = 1.0;
                    if (frame.light_direction_intensity.w > 0.0) {
                        let light = normalize(frame.light_direction_intensity.xyz);
                        directional_shadow = directional_shadow_visibility(
                            input.shadow_position,
                            oriented_geometric_normal,
                            light);
                        let half_vector = normalize(view + light);
                        let normal_dot_light = max(dot(normal, light), 0.0);
                        let opposite_normal_dot_light = max(dot(-normal, light), 0.0);
                        let half_dot_view = max(dot(half_vector, view), 0.0);
                        let dielectric_fresnel = mix(
                            dielectric_f0 * specular_factor,
                            vec3f(specular_factor),
                            pow(clamp(1.0 - half_dot_view, 0.0, 1.0), 5.0));
                        let base_fresnel = mix(
                            dielectric_fresnel,
                            fresnel_schlick(half_dot_view, base_color),
                            metallic);
                        let fresnel = mix(
                            base_fresnel,
                            thin_film_view_fresnel,
                            iridescence_factor);
                        let distribution = distribution_ggx_anisotropic(
                            max(dot(normal, half_vector), 0.0),
                            dot(anisotropy_tangent, half_vector),
                            dot(anisotropy_bitangent, half_vector),
                            alpha_t,
                            alpha_b);
                        let visibility = visibility_ggx_anisotropic(
                            normal_dot_light,
                            normal_dot_view,
                            dot(anisotropy_tangent, view),
                            dot(anisotropy_bitangent, view),
                            dot(anisotropy_tangent, light),
                            dot(anisotropy_bitangent, light),
                            alpha_t,
                            alpha_b);
                        let specular = distribution * visibility * fresnel;
                        let base_diffuse_attenuation = vec3f(1.0 - max(
                            base_fresnel.r,
                            max(base_fresnel.g, base_fresnel.b)));
                        let iridescence_diffuse_attenuation = vec3f(1.0 - max(
                            thin_film_view_fresnel.r,
                            max(thin_film_view_fresnel.g, thin_film_view_fresnel.b)));
                        let diffuse = mix(
                            base_diffuse_attenuation,
                            iridescence_diffuse_attenuation,
                            iridescence_factor) * (1.0 - metallic) *
                            base_color / 3.14159265;
                        let radiance = frame.light_color.rgb * frame.light_direction_intensity.w;
                        base_specular_direct_lighting = specular * radiance * normal_dot_light *
                            directional_shadow;
                        direct_lighting = diffuse * (1.0 - effective_diffuse_transmission) *
                            radiance * normal_dot_light *
                            directional_shadow + base_specular_direct_lighting;
                        diffuse_transmission_direct_lighting = mix(
                            base_diffuse_attenuation,
                            iridescence_diffuse_attenuation,
                            iridescence_factor) * (1.0 - metallic) *
                            diffuse_transmission_color / 3.14159265 *
                            effective_diffuse_transmission * radiance *
                            opposite_normal_dot_light * directional_shadow;
                        direct_lighting += diffuse_transmission_direct_lighting;
                        let sheen_alpha = max(sheen_roughness * sheen_roughness, 0.0001);
                        let sheen_distribution = distribution_charlie(
                            max(dot(normal, half_vector), 0.0),
                            sheen_roughness);
                        let sheen_visibility = visibility_sheen(
                            normal_dot_view,
                            normal_dot_light,
                            sheen_alpha);
                        direct_lighting *= sheen_layer_attenuation;
                        sheen_direct_lighting = sheen_color * sheen_distribution *
                            sheen_visibility * radiance * normal_dot_light *
                            directional_shadow;
                        let geometric_view_visibility = smoothstep(
                            0.0,
                            0.01,
                            dot(oriented_geometric_normal, view));
                        let geometric_light_visibility = smoothstep(
                            0.0,
                            0.01,
                            dot(oriented_geometric_normal, light));
                        sheen_direct_lighting *= geometric_view_visibility *
                            geometric_light_visibility;
                        let clearcoat_normal_dot_light = max(dot(clearcoat_normal, light), 0.0);
                        let clearcoat_half_vector = normalize(view + light);
                        let clearcoat_half_dot_view = max(
                            dot(clearcoat_half_vector, view),
                            0.0);
                        let clearcoat_fresnel = fresnel_schlick(
                            clearcoat_half_dot_view,
                            clearcoat_f0);
                        let clearcoat_distribution = distribution_ggx(
                            clearcoat_normal,
                            clearcoat_half_vector,
                            clearcoat_roughness);
                        let clearcoat_geometry = geometry_schlick_ggx(
                            clearcoat_normal_dot_view,
                            clearcoat_roughness) * geometry_schlick_ggx(
                                clearcoat_normal_dot_light,
                                clearcoat_roughness);
                        let clearcoat_specular = clearcoat_distribution * clearcoat_geometry *
                            clearcoat_fresnel / max(
                                4.0 * clearcoat_normal_dot_view * clearcoat_normal_dot_light,
                                0.000001);
                        direct_lighting *= vec3f(1.0) - clearcoat * clearcoat_fresnel;
                        sheen_direct_lighting *= vec3f(1.0) - clearcoat * clearcoat_fresnel;
                        clearcoat_direct_lighting = clearcoat * clearcoat_specular * radiance *
                            clearcoat_normal_dot_light * directional_shadow;
                    }

                    let punctual_light_count = u32(frame.environment_parameters.w + 0.5);
                    for (var light_index = 0u; light_index < 16u; light_index += 1u) {
                        if (light_index >= punctual_light_count) {
                            break;
                        }
                        let punctual = frame.punctual_lights[light_index];
                        let light_type = punctual.position_type.w;
                        var light = normalize(-punctual.direction_inverse_range.xyz);
                        var attenuation = 1.0;
                        if (light_type >= 0.5) {
                            let to_light = punctual.position_type.xyz - input.world_position;
                            let distance_squared = max(dot(to_light, to_light), 0.000001);
                            let distance = sqrt(distance_squared);
                            light = to_light / distance;
                            attenuation = 1.0 / distance_squared;
                            let inverse_range = punctual.direction_inverse_range.w;
                            if (inverse_range > 0.0) {
                                let normalized_distance = distance * inverse_range;
                                attenuation *= clamp(
                                    1.0 - normalized_distance * normalized_distance *
                                        normalized_distance * normalized_distance,
                                    0.0,
                                    1.0);
                            }
                            if (light_type >= 1.5) {
                                let cosine = dot(
                                    punctual.direction_inverse_range.xyz,
                                    -light);
                                let angular = clamp(
                                    cosine * punctual.spot_parameters.x +
                                        punctual.spot_parameters.y,
                                    0.0,
                                    1.0);
                                attenuation *= angular * angular;
                            }
                        }
                        if (attenuation <= 0.0 || punctual.color_intensity.w <= 0.0) {
                            continue;
                        }

                        let half_vector = normalize(view + light);
                        let normal_dot_light = max(dot(normal, light), 0.0);
                        let opposite_normal_dot_light = max(dot(-normal, light), 0.0);
                        let half_dot_view = max(dot(half_vector, view), 0.0);
                        let dielectric_fresnel = mix(
                            dielectric_f0 * specular_factor,
                            vec3f(specular_factor),
                            pow(clamp(1.0 - half_dot_view, 0.0, 1.0), 5.0));
                        let base_fresnel = mix(
                            dielectric_fresnel,
                            fresnel_schlick(half_dot_view, base_color),
                            metallic);
                        let fresnel = mix(
                            base_fresnel,
                            thin_film_view_fresnel,
                            iridescence_factor);
                        let distribution = distribution_ggx_anisotropic(
                            max(dot(normal, half_vector), 0.0),
                            dot(anisotropy_tangent, half_vector),
                            dot(anisotropy_bitangent, half_vector),
                            alpha_t,
                            alpha_b);
                        let visibility = visibility_ggx_anisotropic(
                            normal_dot_light,
                            normal_dot_view,
                            dot(anisotropy_tangent, view),
                            dot(anisotropy_bitangent, view),
                            dot(anisotropy_tangent, light),
                            dot(anisotropy_bitangent, light),
                            alpha_t,
                            alpha_b);
                        let specular = distribution * visibility * fresnel;
                        let base_diffuse_attenuation = vec3f(1.0 - max(
                            base_fresnel.r,
                            max(base_fresnel.g, base_fresnel.b)));
                        let iridescence_diffuse_attenuation = vec3f(1.0 - max(
                            thin_film_view_fresnel.r,
                            max(thin_film_view_fresnel.g, thin_film_view_fresnel.b)));
                        let diffuse = mix(
                            base_diffuse_attenuation,
                            iridescence_diffuse_attenuation,
                            iridescence_factor) * (1.0 - metallic) *
                            base_color / 3.14159265;
                        let radiance = punctual.color_intensity.rgb *
                            punctual.color_intensity.w * attenuation;
                        let punctual_base_specular = specular * radiance * normal_dot_light;
                        var punctual_direct = diffuse *
                            (1.0 - effective_diffuse_transmission) *
                            radiance * normal_dot_light + punctual_base_specular;
                        let punctual_diffuse_transmission = mix(
                            base_diffuse_attenuation,
                            iridescence_diffuse_attenuation,
                            iridescence_factor) * (1.0 - metallic) *
                            diffuse_transmission_color / 3.14159265 *
                            effective_diffuse_transmission * radiance *
                            opposite_normal_dot_light;
                        punctual_direct += punctual_diffuse_transmission;

                        let sheen_alpha = max(sheen_roughness * sheen_roughness, 0.0001);
                        let sheen_distribution = distribution_charlie(
                            max(dot(normal, half_vector), 0.0),
                            sheen_roughness);
                        let sheen_visibility = visibility_sheen(
                            normal_dot_view,
                            normal_dot_light,
                            sheen_alpha);
                        var punctual_sheen = sheen_color * sheen_distribution *
                            sheen_visibility * radiance * normal_dot_light;
                        let geometric_view_visibility = smoothstep(
                            0.0,
                            0.01,
                            dot(oriented_geometric_normal, view));
                        let geometric_light_visibility = smoothstep(
                            0.0,
                            0.01,
                            dot(oriented_geometric_normal, light));
                        punctual_sheen *= geometric_view_visibility *
                            geometric_light_visibility;
                        punctual_direct *= sheen_layer_attenuation;

                        let clearcoat_normal_dot_light = max(
                            dot(clearcoat_normal, light),
                            0.0);
                        let clearcoat_half_dot_view = max(dot(half_vector, view), 0.0);
                        let clearcoat_fresnel = fresnel_schlick(
                            clearcoat_half_dot_view,
                            clearcoat_f0);
                        let clearcoat_distribution = distribution_ggx(
                            clearcoat_normal,
                            half_vector,
                            clearcoat_roughness);
                        let clearcoat_geometry = geometry_schlick_ggx(
                            clearcoat_normal_dot_view,
                            clearcoat_roughness) * geometry_schlick_ggx(
                                clearcoat_normal_dot_light,
                                clearcoat_roughness);
                        let clearcoat_specular = clearcoat_distribution * clearcoat_geometry *
                            clearcoat_fresnel / max(
                                4.0 * clearcoat_normal_dot_view * clearcoat_normal_dot_light,
                                0.000001);
                        let clearcoat_attenuation = vec3f(1.0) -
                            clearcoat * clearcoat_fresnel;
                        punctual_direct *= clearcoat_attenuation;
                        punctual_sheen *= clearcoat_attenuation;
                        let punctual_clearcoat = clearcoat * clearcoat_specular * radiance *
                            clearcoat_normal_dot_light;

                        direct_lighting += punctual_direct;
                        base_specular_direct_lighting += punctual_base_specular;
                        diffuse_transmission_direct_lighting +=
                            punctual_diffuse_transmission;
                        sheen_direct_lighting += punctual_sheen;
                        clearcoat_direct_lighting += punctual_clearcoat;
                    }

                    let inverse_environment_rotation = vec4f(
                        -frame.environment_rotation.xyz,
                        frame.environment_rotation.w);
                    let environment_direction = rotate_by_quaternion(
                        indirect_normal,
                        inverse_environment_rotation);
                    let environment_radiance = textureSample(
                        environment_diffuse,
                        environment_sampler,
                        environment_direction).rgb;
                    let base_environment_fresnel = base_view_fresnel;
                    let iridescence_environment_fresnel = thin_film_view_fresnel;
                    let environment_fresnel = mix(
                        base_environment_fresnel,
                        iridescence_environment_fresnel,
                        iridescence_factor);
                    let environment_diffuse_attenuation = mix(
                        vec3f(1.0) - base_environment_fresnel,
                        vec3f(1.0 - max(
                            iridescence_environment_fresnel.r,
                            max(iridescence_environment_fresnel.g,
                                iridescence_environment_fresnel.b))),
                        iridescence_factor);
                    let unoccluded_environment_diffuse =
                        environment_diffuse_attenuation * (1.0 - metallic) *
                        base_color * (1.0 - effective_diffuse_transmission) *
                        environment_radiance * frame.environment_parameters.x;
                    let opposite_environment_direction = rotate_by_quaternion(
                        -indirect_normal,
                        inverse_environment_rotation);
                    let opposite_environment_radiance = textureSample(
                        environment_diffuse,
                        environment_sampler,
                        opposite_environment_direction).rgb;
                    let diffuse_transmission_environment_lighting =
                        environment_diffuse_attenuation * (1.0 - metallic) *
                        diffuse_transmission_color * effective_diffuse_transmission *
                        opposite_environment_radiance * frame.environment_parameters.x;
                    let ambient_occlusion = screen_space_ambient_occlusion(
                        input.clip_position,
                        input.clip_w,
                        input.world_position,
                        normal);
                    let ambient_visibility = clamp(
                        ambient_occlusion * draw.material_parameters.z * sampled_orm.r,
                        0.0,
                        1.0);
                    let environment_diffuse_lighting =
                        unoccluded_environment_diffuse * ambient_visibility;
                    var anisotropic_bent_normal = cross(anisotropy_bitangent, view);
                    anisotropic_bent_normal = normalize(cross(
                        anisotropic_bent_normal,
                        anisotropy_bitangent));
                    let anisotropy_bend = pow(
                        1.0 - anisotropy_strength * (1.0 - roughness),
                        4.0);
                    anisotropic_bent_normal = normalize(mix(
                        anisotropic_bent_normal,
                        normal,
                        anisotropy_bend));
                    let anisotropic_reflection = normalize(mix(
                        reflect(-view, anisotropic_bent_normal),
                        anisotropic_bent_normal,
                        roughness * roughness));
                    let reflection_direction = rotate_by_quaternion(
                        anisotropic_reflection,
                        inverse_environment_rotation);
                    let prefiltered_radiance = textureSampleLevel(
                        environment_specular,
                        environment_sampler,
                        reflection_direction,
                        0,
                        roughness * frame.environment_parameters.y).rgb;
                    let integrated_brdf = textureSample(
                        environment_brdf,
                        environment_sampler,
                        vec2f(normal_dot_view, roughness)).rg;
                    let iridescent_environment_f0 = mix(
                        mix(dielectric_f0 * specular_factor, base_color, metallic),
                        iridescence_environment_fresnel,
                        iridescence_factor);
                    let base_environment_f90 = mix(
                        vec3f(specular_factor),
                        vec3f(1.0),
                        metallic);
                    let iridescent_environment_f90 = mix(
                        base_environment_f90,
                        vec3f(1.0),
                        iridescence_factor);
                    let unoccluded_environment_specular = prefiltered_radiance *
                        (iridescent_environment_f0 * integrated_brdf.x +
                            iridescent_environment_f90 * integrated_brdf.y) *
                        frame.environment_parameters.x;
                    let environment_specular_lighting = unoccluded_environment_specular *
                        specular_ambient_visibility(
                            normal_dot_view,
                            ambient_visibility,
                            roughness);
                    let sheen_reflection_direction = rotate_by_quaternion(
                        reflect(-view, normal),
                        inverse_environment_rotation);
                    let sheen_prefiltered_radiance = textureSampleLevel(
                        environment_specular,
                        environment_sampler,
                        sheen_reflection_direction,
                        1,
                        sheen_roughness * frame.environment_parameters.y).rgb;
                    let sheen_environment_lighting = sheen_color * sheen_prefiltered_radiance *
                        frame.environment_parameters.x * ambient_visibility *
                        sheen_directional_albedo;
                    let clearcoat_reflection_direction = rotate_by_quaternion(
                        reflect(-view, clearcoat_normal),
                        inverse_environment_rotation);
                    let clearcoat_prefiltered_radiance = textureSampleLevel(
                        environment_specular,
                        environment_sampler,
                        clearcoat_reflection_direction,
                        0,
                        clearcoat_roughness * frame.environment_parameters.y).rgb;
                    let clearcoat_integrated_brdf = textureSample(
                        environment_brdf,
                        environment_sampler,
                        vec2f(clearcoat_normal_dot_view, clearcoat_roughness)).rg;
                    let clearcoat_environment_fresnel = fresnel_schlick(
                        clearcoat_normal_dot_view,
                        clearcoat_f0);
                    let clearcoat_environment_specular = clearcoat *
                        clearcoat_prefiltered_radiance *
                        (clearcoat_f0 * clearcoat_integrated_brdf.x +
                            clearcoat_integrated_brdf.y) *
                        frame.environment_parameters.x * specular_ambient_visibility(
                            clearcoat_normal_dot_view,
                            ambient_visibility,
                            clearcoat_roughness);
                    let clearcoat_layer_attenuation =
                        vec3f(1.0) - clearcoat * clearcoat_environment_fresnel;
                    let render_layer = u32(frame.environment_parameters.z + 0.5);
                    if (render_layer == 1u) {
                        return material_fragment(vec3f(ambient_visibility), output_alpha);
                    }
                    if (render_layer == 2u) {
                        return material_fragment(
                            direct_lighting + sheen_direct_lighting + clearcoat_direct_lighting,
                            output_alpha);
                    }
                    if (render_layer == 12u) {
                        return material_fragment(
                            sheen_direct_lighting + sheen_environment_lighting,
                            output_alpha);
                    }
                    if (render_layer == 15u) {
                        return material_fragment(
                            base_specular_direct_lighting + environment_specular_lighting,
                            output_alpha);
                    }
                    if (render_layer == 16u) {
                        return material_fragment(
                            diffuse_transmission_direct_lighting +
                                diffuse_transmission_environment_lighting,
                            output_alpha);
                    }
                    if (render_layer == 3u) {
                        return material_fragment(
                            ((environment_diffuse_lighting +
                                diffuse_transmission_environment_lighting +
                                environment_specular_lighting) *
                                sheen_layer_attenuation + sheen_environment_lighting) *
                                clearcoat_layer_attenuation + clearcoat_environment_specular,
                            output_alpha);
                    }
                    if (render_layer == 4u) {
                        let view_distance = length(frame.camera_position.xyz - input.world_position);
                        let normalized_depth = clamp(view_distance / 12.0, 0.0, 1.0);
                        return material_fragment(vec3f(1.0 - normalized_depth), output_alpha);
                    }
                    if (render_layer == 5u) {
                        return material_fragment(vec3f(directional_shadow), output_alpha);
                    }
                    if (render_layer == 6u) {
                        return material_fragment(
                            indirect_normal * 0.5 + vec3f(0.5),
                            output_alpha);
                    }
                    if (render_layer == 7u) {
                        return material_fragment(base_color, output_alpha);
                    }
                    if (render_layer == 8u) {
                        return material_fragment(
                            vec3f(fract(base_texture_coordinate), 0.0),
                            output_alpha);
                    }
                    if (render_layer == 9u) {
                        return material_fragment(normal * 0.5 + vec3f(0.5), output_alpha);
                    }
                    if (render_layer == 10u) {
                        return material_fragment(sampled_orm.rgb, output_alpha);
                    }
                    if (render_layer == 11u) {
                        return material_fragment(sampled_emissive, output_alpha);
                    }
                    let opaque_lighting =
                            direct_lighting + sheen_direct_lighting +
                            clearcoat_direct_lighting +
                            ((environment_diffuse_lighting +
                                diffuse_transmission_environment_lighting +
                                environment_specular_lighting) *
                                sheen_layer_attenuation + sheen_environment_lighting) *
                                clearcoat_layer_attenuation +
                            clearcoat_environment_specular +
                            sampled_emissive;
                    let transmission = surface_transmission;
                    let sampled_thickness = textureSample(
                        material_thickness,
                        thickness_sampler,
                        thickness_texture_coordinate).g;
                    if ((render_layer == 13u || render_layer == 14u) && transmission <= 0.0) {
                        return material_fragment(vec3f(0.0), output_alpha);
                    }
                    if (transmission > 0.0) {
                        let thickness = draw.transmission_parameters.z * sampled_thickness;
                        let authored_ior = draw.transmission_parameters.y;
                        let transmission_ior = select(authored_ior, 1000000.0, authored_ior == 0.0);
                        let refracted = refract(
                            -view,
                            normal,
                            1.0 / transmission_ior);
                        let exit_world = input.world_position + refracted * thickness;
                        let exit_clip = frame.view_projection * vec4f(exit_world, 1.0);
                        let refracted_uv = clamp(
                            vec2f(
                                exit_clip.x / exit_clip.w * 0.5 + 0.5,
                                0.5 - exit_clip.y / exit_clip.w * 0.5),
                            vec2f(0.0),
                            vec2f(1.0));
                        // Khronos' real-time transmission model keeps IOR 1 perfectly sharp and
                        // progressively exposes authored surface roughness as the interface IOR
                        // rises. The HDR background pyramid supplies the corresponding footprint.
                        let transmission_roughness = roughness * clamp(
                            transmission_ior * 2.0 - 2.0,
                            0.0,
                            1.0);
                        let background_dimensions = vec2f(textureDimensions(scene_background));
                        let maximum_background_lod = log2(max(
                            background_dimensions.x,
                            background_dimensions.y));
                        var transmitted = textureSampleLevel(
                            scene_background,
                            scene_background_sampler,
                            refracted_uv,
                            transmission_roughness * maximum_background_lod).rgb;
                        let dispersion = select(
                            draw.attenuation_color.w,
                            0.0,
                            authored_ior == 0.0);
                        if (dispersion > 0.0 && thickness > 0.0) {
                            let half_spread = (transmission_ior - 1.0) * 0.025 * dispersion;
                            let channel_iors = max(
                                vec3f(
                                    transmission_ior - half_spread,
                                    transmission_ior,
                                    transmission_ior + half_spread),
                                vec3f(1.0));
                            var dispersed = vec3f(0.0);
                            for (var channel = 0u; channel < 3u; channel += 1u) {
                                let channel_refracted = refract(
                                    -view,
                                    normal,
                                    1.0 / channel_iors[channel]);
                                let channel_exit_world = input.world_position +
                                    channel_refracted * thickness;
                                let channel_exit_clip = frame.view_projection *
                                    vec4f(channel_exit_world, 1.0);
                                let channel_uv = clamp(
                                    vec2f(
                                        channel_exit_clip.x / channel_exit_clip.w * 0.5 + 0.5,
                                        0.5 - channel_exit_clip.y / channel_exit_clip.w * 0.5),
                                    vec2f(0.0),
                                    vec2f(1.0));
                                dispersed[channel] = textureSampleLevel(
                                    scene_background,
                                    scene_background_sampler,
                                    channel_uv,
                                    transmission_roughness * maximum_background_lod)[channel];
                            }
                            transmitted = dispersed;
                        }
                        var volume_attenuation = vec3f(1.0);
                        if (draw.transmission_parameters.w > 0.0 && thickness > 0.0) {
                            let path_length = thickness / max(abs(dot(refracted, normal)), 0.05);
                            volume_attenuation = exp(
                                log(max(draw.attenuation_color.rgb, vec3f(0.000001))) *
                                path_length * draw.transmission_parameters.w);
                            transmitted *= volume_attenuation;
                        }
                        if (render_layer == 13u) {
                            return material_fragment(transmitted, output_alpha);
                        }
                        if (render_layer == 14u) {
                            return material_fragment(volume_attenuation, output_alpha);
                        }
                        let interface_fresnel = environment_fresnel;
                        let transmitted_weight = transmission *
                            (1.0 - max(interface_fresnel.r, max(interface_fresnel.g, interface_fresnel.b)));
                        return vec4f(
                            mix(opaque_lighting, transmitted, transmitted_weight),
                            1.0);
                    }
                    return material_fragment(opaque_lighting, output_alpha);
                }
                """;
            GraphicsShaderModule? shader = null;
            GraphicsBindGroupLayout? frameLayout = null;
            GraphicsBindGroupLayout? drawLayout = null;
            GraphicsBindGroupLayout? environmentLayout = null;
            GraphicsBindGroupLayout? shadowLayout = null;
            GraphicsPipelineLayout? pipelineLayout = null;
            GraphicsPipelineLayout? shadowPipelineLayout = null;
            GraphicsBuffer? frameBuffer = null;
            GraphicsBindGroup? frameBindGroup = null;
            GraphicsBuffer? backgroundFrameBuffer = null;
            GraphicsBindGroup? backgroundFrameBindGroup = null;
            EnvironmentCubeGpuResources? fallbackDiffuseEnvironment = null;
            EnvironmentCubeGpuResources? fallbackSpecularEnvironment = null;
            BrdfLutGpuResources? brdfLut = null;
            GraphicsBindGroup? fallbackEnvironmentBindGroup = null;
            GraphicsTexture? shadowTexture = null;
            GraphicsTextureView? shadowView = null;
            GraphicsSampler? shadowSampler = null;
            GraphicsRenderPipeline? shadowBackCullPipeline = null;
            GraphicsRenderPipeline? shadowDoubleSidedPipeline = null;
            GraphicsRenderPipeline? alphaTestedShadowBackCullPipeline = null;
            GraphicsRenderPipeline? alphaTestedShadowDoubleSidedPipeline = null;
            GraphicsRenderPipeline? depthPrepassPipeline = null;
            GraphicsRenderPipeline? alphaTestedDepthPrepassPipeline = null;
            GraphicsRenderPipeline? unlitBack = null;
            GraphicsRenderPipeline? unlitDouble = null;
            GraphicsRenderPipeline? unlitBlendBack = null;
            GraphicsRenderPipeline? unlitBlendDouble = null;
            GraphicsRenderPipeline? pbrBack = null;
            GraphicsRenderPipeline? pbrDouble = null;
            GraphicsRenderPipeline? pbrBlendBack = null;
            GraphicsRenderPipeline? pbrBlendDouble = null;
            try
            {
                shader = device.CreateShaderModule(
                    new GraphicsShaderModuleDescriptor(shaderCode, "scene material shader"));
                frameLayout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                    [new GraphicsBindGroupLayoutEntry(
                        0,
                        GraphicsShaderStage.Vertex | GraphicsShaderStage.Fragment,
                        GraphicsBufferBindingType.Uniform,
                        FrameUniformSize)],
                    "scene frame bind-group layout"));
                drawLayout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                    [
                        new GraphicsBindGroupLayoutEntry(
                            0,
                            GraphicsShaderStage.Vertex | GraphicsShaderStage.Fragment,
                            GraphicsBufferBindingType.Uniform,
                            DrawUniformSize),
                        new GraphicsBindGroupLayoutEntry(
                            1,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            2,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            3,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            4,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            5,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            6,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            7,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            8,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            9,
                            GraphicsShaderStage.Vertex,
                            GraphicsBufferBindingType.ReadOnlyStorage,
                            128),
                        new GraphicsBindGroupLayoutEntry(
                            10,
                            GraphicsShaderStage.Vertex,
                            GraphicsBufferBindingType.ReadOnlyStorage,
                            48),
                        new GraphicsBindGroupLayoutEntry(
                            11,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            12,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            13,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            14,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            15,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            16,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            17,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            18,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            19,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            20,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            21,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            22,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                    ],
                    "scene draw bind-group layout"));
                environmentLayout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                    [
                        new GraphicsBindGroupLayoutEntry(
                            0,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            1,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.Cube),
                        new GraphicsBindGroupLayoutEntry(
                            2,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.CubeArray),
                        new GraphicsBindGroupLayoutEntry(
                            3,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                    ],
                    "scene environment bind-group layout"));
                shadowLayout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                    [
                        new GraphicsBindGroupLayoutEntry(
                            0,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Comparison),
                        new GraphicsBindGroupLayoutEntry(
                            1,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Depth,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            2,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Depth,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            3,
                            GraphicsShaderStage.Fragment,
                            GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            4,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                    ],
                    "scene shadow and ambient-occlusion bind-group layout"));
                pipelineLayout = device.CreatePipelineLayout(
                    new GraphicsPipelineLayoutDescriptor(
                        [frameLayout, drawLayout, environmentLayout, shadowLayout],
                        "scene pipeline layout"));
                shadowPipelineLayout = device.CreatePipelineLayout(
                    new GraphicsPipelineLayoutDescriptor(
                        [frameLayout, drawLayout],
                        "scene shadow pipeline layout"));
                frameBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
                    FrameUniformSize,
                    GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination,
                    "scene frame uniform"));
                frameBindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
                    frameLayout,
                    [new GraphicsBindGroupEntry(0, frameBuffer, 0, FrameUniformSize)],
                    "scene frame bind group"));
                backgroundFrameBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
                    FrameUniformSize,
                    GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination,
                    "scene transmission-background frame uniform"));
                backgroundFrameBindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
                    frameLayout,
                    [new GraphicsBindGroupEntry(0, backgroundFrameBuffer, 0, FrameUniformSize)],
                    "scene transmission-background frame bind group"));
                HdrEnvironmentCube fallbackEnvironment = new(
                    1,
                    new Vector3[6],
                    StandardColorSpaces.LinearSrgb,
                    "black fallback environment");
                fallbackDiffuseEnvironment = EnvironmentCubeGpuResources.Create(
                    device,
                    fallbackEnvironment);
                fallbackSpecularEnvironment = EnvironmentCubeGpuResources.CreateSpecularArray(
                    device,
                    fallbackEnvironment);
                brdfLut = BrdfLutGpuResources.Create(device, SharedBrdfLut.Value);
                fallbackEnvironmentBindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
                    environmentLayout,
                    [
                        new GraphicsBindGroupEntry(0, fallbackDiffuseEnvironment.Sampler),
                        new GraphicsBindGroupEntry(1, fallbackDiffuseEnvironment.View),
                        new GraphicsBindGroupEntry(2, fallbackSpecularEnvironment.View),
                        new GraphicsBindGroupEntry(3, brdfLut.View),
                    ],
                    "scene fallback environment bind group"));
                shadowTexture = device.CreateTexture(new GraphicsTextureDescriptor(
                    new GraphicsExtent3D(ShadowMapSize, ShadowMapSize),
                    GraphicsTextureFormat.Depth32Float,
                    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
                    label: "scene directional shadow map"));
                shadowView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    shadowTexture,
                    label: "scene directional shadow-map view"));
                shadowSampler = device.CreateSampler(new GraphicsSamplerDescriptor(
                    magFilter: GraphicsFilterMode.Linear,
                    minFilter: GraphicsFilterMode.Linear,
                    label: "scene directional shadow comparison sampler",
                    compare: GraphicsCompareFunction.LessEqual));
                shadowBackCullPipeline = CreateShadowPipeline(
                    device,
                    shader,
                    shadowPipelineLayout,
                    GraphicsCullMode.Back,
                    "scene directional shadow pipeline");
                shadowDoubleSidedPipeline = CreateShadowPipeline(
                    device,
                    shader,
                    shadowPipelineLayout,
                    GraphicsCullMode.None,
                    "scene double-sided directional shadow pipeline");
                alphaTestedShadowBackCullPipeline = CreateAlphaTestedShadowPipeline(
                    device,
                    shader,
                    shadowPipelineLayout,
                    GraphicsCullMode.Back,
                    "scene alpha-tested directional shadow pipeline");
                alphaTestedShadowDoubleSidedPipeline = CreateAlphaTestedShadowPipeline(
                    device,
                    shader,
                    shadowPipelineLayout,
                    GraphicsCullMode.None,
                    "scene double-sided alpha-tested directional shadow pipeline");
                depthPrepassPipeline = CreateDepthPrepassPipeline(
                    device,
                    shader,
                    shadowPipelineLayout);
                alphaTestedDepthPrepassPipeline = CreateAlphaTestedDepthPrepassPipeline(
                    device,
                    shader,
                    shadowPipelineLayout);
                unlitBack = CreatePipeline(
                    device, shader, pipelineLayout, colorFormat, "fs_unlit", GraphicsCullMode.Back,
                    false, "scene unlit back-cull pipeline");
                unlitDouble = CreatePipeline(
                    device, shader, pipelineLayout, colorFormat, "fs_unlit", GraphicsCullMode.None,
                    false, "scene unlit double-sided pipeline");
                unlitBlendBack = CreatePipeline(
                    device, shader, pipelineLayout, colorFormat, "fs_unlit", GraphicsCullMode.Back,
                    true, "scene unlit blended back-cull pipeline");
                unlitBlendDouble = CreatePipeline(
                    device, shader, pipelineLayout, colorFormat, "fs_unlit", GraphicsCullMode.None,
                    true, "scene unlit blended double-sided pipeline");
                pbrBack = CreatePipeline(
                    device, shader, pipelineLayout, colorFormat, "fs_pbr", GraphicsCullMode.Back,
                    false, "scene PBR back-cull pipeline");
                pbrDouble = CreatePipeline(
                    device, shader, pipelineLayout, colorFormat, "fs_pbr", GraphicsCullMode.None,
                    false, "scene PBR double-sided pipeline");
                pbrBlendBack = CreatePipeline(
                    device, shader, pipelineLayout, colorFormat, "fs_pbr", GraphicsCullMode.Back,
                    true, "scene PBR blended back-cull pipeline");
                pbrBlendDouble = CreatePipeline(
                    device, shader, pipelineLayout, colorFormat, "fs_pbr", GraphicsCullMode.None,
                    true, "scene PBR blended double-sided pipeline");
                SharedResources result = new(
                    device,
                    colorFormat,
                    shader,
                    frameLayout,
                    drawLayout,
                    environmentLayout,
                    shadowLayout,
                    pipelineLayout,
                    shadowPipelineLayout,
                    frameBuffer,
                    frameBindGroup,
                    backgroundFrameBuffer,
                    backgroundFrameBindGroup,
                    fallbackDiffuseEnvironment,
                    fallbackSpecularEnvironment,
                    brdfLut,
                    fallbackEnvironmentBindGroup,
                    shadowTexture,
                    shadowView,
                    shadowSampler,
                    shadowBackCullPipeline,
                    shadowDoubleSidedPipeline,
                    alphaTestedShadowBackCullPipeline,
                    alphaTestedShadowDoubleSidedPipeline,
                    depthPrepassPipeline,
                    alphaTestedDepthPrepassPipeline,
                    unlitBack,
                    unlitDouble,
                    unlitBlendBack,
                    unlitBlendDouble,
                    pbrBack,
                    pbrDouble,
                    pbrBlendBack,
                    pbrBlendDouble);
                shader = null;
                frameLayout = null;
                drawLayout = null;
                environmentLayout = null;
                shadowLayout = null;
                pipelineLayout = null;
                shadowPipelineLayout = null;
                frameBuffer = null;
                frameBindGroup = null;
                backgroundFrameBuffer = null;
                backgroundFrameBindGroup = null;
                fallbackDiffuseEnvironment = null;
                fallbackSpecularEnvironment = null;
                brdfLut = null;
                fallbackEnvironmentBindGroup = null;
                shadowTexture = null;
                shadowView = null;
                shadowSampler = null;
                shadowBackCullPipeline = null;
                shadowDoubleSidedPipeline = null;
                alphaTestedShadowBackCullPipeline = null;
                alphaTestedShadowDoubleSidedPipeline = null;
                depthPrepassPipeline = null;
                alphaTestedDepthPrepassPipeline = null;
                unlitBack = null;
                unlitDouble = null;
                unlitBlendBack = null;
                unlitBlendDouble = null;
                pbrBack = null;
                pbrDouble = null;
                pbrBlendBack = null;
                pbrBlendDouble = null;
                return result;
            }
            finally
            {
                alphaTestedDepthPrepassPipeline?.Dispose();
                depthPrepassPipeline?.Dispose();
                alphaTestedShadowDoubleSidedPipeline?.Dispose();
                alphaTestedShadowBackCullPipeline?.Dispose();
                shadowDoubleSidedPipeline?.Dispose();
                shadowBackCullPipeline?.Dispose();
                pbrDouble?.Dispose();
                pbrBack?.Dispose();
                pbrBlendDouble?.Dispose();
                pbrBlendBack?.Dispose();
                unlitDouble?.Dispose();
                unlitBack?.Dispose();
                unlitBlendDouble?.Dispose();
                unlitBlendBack?.Dispose();
                frameBindGroup?.Dispose();
                frameBuffer?.Dispose();
                backgroundFrameBindGroup?.Dispose();
                backgroundFrameBuffer?.Dispose();
                fallbackEnvironmentBindGroup?.Dispose();
                shadowSampler?.Dispose();
                shadowView?.Dispose();
                shadowTexture?.Dispose();
                brdfLut?.Dispose();
                fallbackSpecularEnvironment?.Dispose();
                fallbackDiffuseEnvironment?.Dispose();
                pipelineLayout?.Dispose();
                shadowPipelineLayout?.Dispose();
                shadowLayout?.Dispose();
                environmentLayout?.Dispose();
                drawLayout?.Dispose();
                frameLayout?.Dispose();
                shader?.Dispose();
            }
        }

        internal GraphicsRenderPipeline SelectPipeline(
            MaterialKind kind,
            bool doubleSided,
            bool blended,
            MaterialShaderVariant variant)
        {
            bool specialized = MaterialTextureBindingPlan.RequiresSpecialization(variant);
            if (specialized)
            {
                PrepareMaterialVariant(variant);
                return specializedMaterialPipelines[variant];
            }
            PrepareMaterialVariant(variant);
            return (kind, doubleSided, blended) switch
            {
                (MaterialKind.Unlit, false, false) => UnlitBackCullPipeline,
                (MaterialKind.Unlit, true, false) => UnlitDoubleSidedPipeline,
                (MaterialKind.Unlit, false, true) => UnlitBlendBackCullPipeline,
                (MaterialKind.Unlit, true, true) => UnlitBlendDoubleSidedPipeline,
                (MaterialKind.MetallicRoughness, false, false) => PbrBackCullPipeline,
                (MaterialKind.MetallicRoughness, true, false) => PbrDoubleSidedPipeline,
                (MaterialKind.MetallicRoughness, false, true) => PbrBlendBackCullPipeline,
                (MaterialKind.MetallicRoughness, true, true) => PbrBlendDoubleSidedPipeline,
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown material kind."),
            };
        }

        internal void PrepareMaterialVariant(MaterialShaderVariant variant)
        {
            if (preparedMaterialVariants.Contains(variant))
            {
                materialShaderVariantCacheHits++;
                return;
            }
            ValidateMaterialVariant(variant);
            if (MaterialTextureBindingPlan.RequiresSpecialization(variant))
            {
                GraphicsShaderModule shader = device.CreateShaderModule(
                    new GraphicsShaderModuleDescriptor(
                        GenerateCompactMaterialShader(Shader.Descriptor.Code, variant),
                        $"scene compact material shader {variant}"));
                bool blended = variant.AlphaMode == MaterialAlphaMode.Blend ||
                    variant.Extensions.HasFlag(PbrMaterialExtensions.Transmission);
                specializedMaterialShaders.Add(variant, shader);
                specializedMaterialPipelines.Add(
                    variant,
                    CreatePipeline(
                        device,
                        shader,
                        PipelineLayout,
                        colorFormat,
                        "fs_pbr_compact",
                        variant.IsDoubleSided ? GraphicsCullMode.None : GraphicsCullMode.Back,
                        blended,
                        $"scene material variant {variant}"));
            }
            preparedMaterialVariants.Add(variant);
            materialShaderVariantCacheMisses++;
        }

        private static void ValidateMaterialVariant(MaterialShaderVariant variant)
        {
            if (variant.BaseModel == MaterialBaseModel.OpenPbr)
            {
                throw new NotSupportedException(
                    "OpenPBR material variants require an OpenPBR render pass and cannot be prewarmed " +
                    "by the default SceneRenderer.");
            }
            if (variant.BaseModel == MaterialBaseModel.Custom)
            {
                throw new NotSupportedException("The built-in renderer cannot prewarm a custom material closure.");
            }
            _ = MaterialTextureBindingPlan.Create(variant);
        }

        internal GraphicsRenderPipeline SelectShadowPipeline(
            bool alphaTested,
            bool doubleSided) =>
            (alphaTested, doubleSided) switch
            {
                (false, false) => ShadowBackCullPipeline,
                (false, true) => ShadowDoubleSidedPipeline,
                (true, false) => AlphaTestedShadowBackCullPipeline,
                (true, true) => AlphaTestedShadowDoubleSidedPipeline,
            };

        public void Dispose()
        {
            AlphaTestedDepthPrepassPipeline.Dispose();
            DepthPrepassPipeline.Dispose();
            AlphaTestedShadowDoubleSidedPipeline.Dispose();
            AlphaTestedShadowBackCullPipeline.Dispose();
            ShadowDoubleSidedPipeline.Dispose();
            ShadowBackCullPipeline.Dispose();
            PbrDoubleSidedPipeline.Dispose();
            PbrBackCullPipeline.Dispose();
            PbrBlendDoubleSidedPipeline.Dispose();
            PbrBlendBackCullPipeline.Dispose();
            foreach (GraphicsRenderPipeline pipeline in specializedMaterialPipelines.Values)
            {
                pipeline.Dispose();
            }
            specializedMaterialPipelines.Clear();
            foreach (GraphicsShaderModule shader in specializedMaterialShaders.Values)
            {
                shader.Dispose();
            }
            specializedMaterialShaders.Clear();
            UnlitDoubleSidedPipeline.Dispose();
            UnlitBackCullPipeline.Dispose();
            UnlitBlendDoubleSidedPipeline.Dispose();
            UnlitBlendBackCullPipeline.Dispose();
            FallbackEnvironmentBindGroup.Dispose();
            ShadowSampler.Dispose();
            ShadowView.Dispose();
            ShadowTexture.Dispose();
            BrdfLut.Dispose();
            FallbackSpecularEnvironment.Dispose();
            FallbackDiffuseEnvironment.Dispose();
            BackgroundFrameBindGroup.Dispose();
            BackgroundFrameUniformBuffer.Dispose();
            FrameBindGroup.Dispose();
            FrameUniformBuffer.Dispose();
            PipelineLayout.Dispose();
            ShadowPipelineLayout.Dispose();
            ShadowBindGroupLayout.Dispose();
            EnvironmentBindGroupLayout.Dispose();
            DrawBindGroupLayout.Dispose();
            FrameBindGroupLayout.Dispose();
            Shader.Dispose();
        }

        private static GraphicsRenderPipeline CreatePipeline(
            GraphicsDevice device,
            GraphicsShaderModule shader,
            GraphicsPipelineLayout pipelineLayout,
            GraphicsTextureFormat colorFormat,
            string fragmentEntryPoint,
            GraphicsCullMode cullMode,
            bool blended,
            string label) =>
            device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_main",
                shader,
                fragmentEntryPoint,
                colorFormat,
                label: label,
                vertexBuffers:
                [
                    new GraphicsVertexBufferLayout(
                        100,
                        [
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 0, 0),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 12, 1),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 24, 2),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 36, 3),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 44, 4),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 60, 5),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 76, 6),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 92, 7),
                        ]),
                ],
                depthStencil: new GraphicsDepthStencilState(
                    GraphicsTextureFormat.Depth32Float,
                    depthWriteEnabled: !blended),
                layout: pipelineLayout,
                cullMode: cullMode,
                blend: blended ? GraphicsBlendState.PremultipliedAlpha : null));

        private static string GenerateCompactMaterialShader(
            string source,
            MaterialShaderVariant variant)
        {
            MaterialTextureBindingPlan plan = MaterialTextureBindingPlan.Create(variant);
            const string entryMarker = "@fragment fn fs_pbr(";
            const string colorMarker = "let sheen_color = draw.sheen_color_roughness.rgb;";
            const string roughnessMarker =
                "let sheen_roughness = clamp(draw.sheen_color_roughness.w, 0.07, 1.0);";
            const string iridescenceFactorMarker =
                "let iridescence_factor = clamp(draw.iridescence_parameters.x, 0.0, 1.0);";
            const string iridescenceThicknessMarker =
                "let iridescence_thickness = draw.iridescence_parameters.w;";
            const string specularFactorMarker =
                "let specular_factor = clamp(draw.specular_parameters.x, 0.0, 1.0);";
            const string specularColorMarker =
                "let specular_color = draw.specular_parameters.yzw;";
            const string diffuseTransmissionFactorMarker =
                "let diffuse_transmission_factor = clamp(draw.diffuse_transmission_parameters.x, 0.0, 1.0);";
            const string diffuseTransmissionColorMarker =
                "let diffuse_transmission_color = draw.diffuse_transmission_parameters.yzw;";
            if (CountOccurrences(source, entryMarker) != 1 ||
                CountOccurrences(source, colorMarker) != 1 ||
                CountOccurrences(source, roughnessMarker) != 1 ||
                CountOccurrences(source, iridescenceFactorMarker) != 1 ||
                CountOccurrences(source, iridescenceThicknessMarker) != 1 ||
                CountOccurrences(source, specularFactorMarker) != 1 ||
                CountOccurrences(source, specularColorMarker) != 1 ||
                CountOccurrences(source, diffuseTransmissionFactorMarker) != 1 ||
                CountOccurrences(source, diffuseTransmissionColorMarker) != 1)
            {
                throw new InvalidOperationException(
                    "The scene shader template no longer contains the unique compact-material markers.");
            }
            source = source.Replace(
                entryMarker,
                "@fragment fn fs_pbr_compact(",
                StringComparison.Ordinal);

            Remap(PbrMaterialTextureBindings.Emissive, "material_emissive", "emissive_sampler",
                "emissive_texture_coordinate", "vec4f(1.0)");
            Remap(PbrMaterialTextureBindings.ClearcoatFactor, "material_clearcoat", "clearcoat_sampler",
                "clearcoat_texture_coordinate", "vec4f(1.0)");
            Remap(PbrMaterialTextureBindings.ClearcoatRoughness, "material_clearcoat_roughness",
                "clearcoat_roughness_sampler", "clearcoat_roughness_texture_coordinate", "vec4f(1.0)");
            Remap(PbrMaterialTextureBindings.ClearcoatNormal, "material_clearcoat_normal",
                "clearcoat_normal_sampler", "clearcoat_normal_texture_coordinate",
                "vec4f(0.5, 0.5, 1.0, 1.0)");
            Remap(PbrMaterialTextureBindings.Anisotropy, "material_anisotropy", "anisotropy_sampler",
                "anisotropy_texture_coordinate", "vec4f(1.0, 0.5, 1.0, 1.0)");
            Remap(PbrMaterialTextureBindings.Transmission, "material_transmission", "transmission_sampler",
                "transmission_texture_coordinate", "vec4f(1.0)");
            Remap(PbrMaterialTextureBindings.VolumeThickness, "material_thickness", "thickness_sampler",
                "thickness_texture_coordinate", "vec4f(1.0)");

            if (variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.SheenColor))
            {
                (string texture, string sampler) = PhysicalBinding(
                    plan.GetPhysicalSlot(PbrMaterialTextureBindings.SheenColor));
                source = source.Replace(
                    colorMarker,
                    $"let sheen_color = draw.sheen_color_roughness.rgb * textureSample(\n" +
                    $"                        {texture},\n" +
                    $"                        {sampler},\n" +
                    "                        sheen_color_texture_coordinate).rgb;",
                    StringComparison.Ordinal);
            }
            if (variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.SheenRoughness))
            {
                (string texture, string sampler) = PhysicalBinding(
                    plan.GetPhysicalSlot(PbrMaterialTextureBindings.SheenRoughness));
                source = source.Replace(
                    roughnessMarker,
                    "let sheen_roughness = clamp(\n" +
                    "                        draw.sheen_color_roughness.w * textureSample(\n" +
                    $"                            {texture},\n" +
                    $"                            {sampler},\n" +
                    "                            sheen_roughness_texture_coordinate).a,\n" +
                    "                        0.07,\n" +
                    "                        1.0);",
                    StringComparison.Ordinal);
            }
            if (variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.IridescenceFactor))
            {
                (string texture, string sampler) = PhysicalBinding(
                    plan.GetPhysicalSlot(PbrMaterialTextureBindings.IridescenceFactor));
                source = source.Replace(
                    iridescenceFactorMarker,
                    "let iridescence_factor = clamp(draw.iridescence_parameters.x * " +
                    $"textureSample({texture}, {sampler}, iridescence_texture_coordinate).r, " +
                    "0.0, 1.0);",
                    StringComparison.Ordinal);
            }
            if (variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.IridescenceThickness))
            {
                (string texture, string sampler) = PhysicalBinding(
                    plan.GetPhysicalSlot(PbrMaterialTextureBindings.IridescenceThickness));
                source = source.Replace(
                    iridescenceThicknessMarker,
                    "let iridescence_thickness = mix(draw.iridescence_parameters.z, " +
                    "draw.iridescence_parameters.w, " +
                    $"textureSample({texture}, {sampler}, " +
                    "iridescence_thickness_texture_coordinate).g);",
                    StringComparison.Ordinal);
            }
            if (variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.SpecularFactor))
            {
                (string texture, string sampler) = PhysicalBinding(
                    plan.GetPhysicalSlot(PbrMaterialTextureBindings.SpecularFactor));
                source = source.Replace(
                    specularFactorMarker,
                    "let specular_factor = clamp(draw.specular_parameters.x * " +
                    $"textureSample({texture}, {sampler}, specular_texture_coordinate).a, " +
                    "0.0, 1.0);",
                    StringComparison.Ordinal);
            }
            if (variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.SpecularColor))
            {
                (string texture, string sampler) = PhysicalBinding(
                    plan.GetPhysicalSlot(PbrMaterialTextureBindings.SpecularColor));
                source = source.Replace(
                    specularColorMarker,
                    "let specular_color = draw.specular_parameters.yzw * " +
                    $"textureSample({texture}, {sampler}, specular_color_texture_coordinate).rgb;",
                    StringComparison.Ordinal);
            }
            if (variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.DiffuseTransmissionFactor))
            {
                (string texture, string sampler) = PhysicalBinding(
                    plan.GetPhysicalSlot(PbrMaterialTextureBindings.DiffuseTransmissionFactor));
                source = source.Replace(
                    diffuseTransmissionFactorMarker,
                    "let diffuse_transmission_factor = clamp(\n" +
                    "                        draw.diffuse_transmission_parameters.x * textureSample(\n" +
                    $"                            {texture},\n" +
                    $"                            {sampler},\n" +
                    "                            diffuse_transmission_texture_coordinate).a,\n" +
                    "                        0.0,\n" +
                    "                        1.0);",
                    StringComparison.Ordinal);
            }
            if (variant.TextureBindings.HasFlag(PbrMaterialTextureBindings.DiffuseTransmissionColor))
            {
                (string texture, string sampler) = PhysicalBinding(
                    plan.GetPhysicalSlot(PbrMaterialTextureBindings.DiffuseTransmissionColor));
                source = source.Replace(
                    diffuseTransmissionColorMarker,
                    "let diffuse_transmission_color = draw.diffuse_transmission_parameters.yzw * " +
                    $"textureSample({texture}, {sampler}, " +
                    "diffuse_transmission_color_texture_coordinate).rgb;",
                    StringComparison.Ordinal);
            }
            return source;

            void Remap(
                PbrMaterialTextureBindings semantic,
                string originalTexture,
                string originalSampler,
                string coordinate,
                string fallback)
            {
                int slot = plan.GetPhysicalSlot(semantic);
                string pattern = $@"textureSample\(\s*{Regex.Escape(originalTexture)},\s*" +
                    $@"{Regex.Escape(originalSampler)},\s*{Regex.Escape(coordinate)}\)";
                MatchCollection matches = Regex.Matches(source, pattern, RegexOptions.CultureInvariant);
                if (matches.Count != 1)
                {
                    throw new InvalidOperationException(
                        $"Expected one {semantic} sample marker in the scene shader, found {matches.Count}.");
                }
                string replacement = fallback;
                if (slot >= 0)
                {
                    (string texture, string sampler) = PhysicalBinding(slot);
                    replacement = $"textureSample({texture}, {sampler}, {coordinate})";
                }
                source = Regex.Replace(source, pattern, replacement, RegexOptions.CultureInvariant);
            }
        }

        private static (string Texture, string Sampler) PhysicalBinding(int slot) => slot switch
        {
            0 => ("material_base_color", "base_color_sampler"),
            1 => ("material_normal", "normal_sampler"),
            2 => ("material_orm", "orm_sampler"),
            3 => ("material_emissive", "emissive_sampler"),
            4 => ("material_clearcoat", "clearcoat_sampler"),
            5 => ("material_clearcoat_roughness", "clearcoat_roughness_sampler"),
            6 => ("material_clearcoat_normal", "clearcoat_normal_sampler"),
            7 => ("material_anisotropy", "anisotropy_sampler"),
            8 => ("material_transmission", "transmission_sampler"),
            9 => ("material_thickness", "thickness_sampler"),
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

        private static int CountOccurrences(string source, string value)
        {
            int count = 0;
            int offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }
            return count;
        }

        private static GraphicsRenderPipeline CreateShadowPipeline(
            GraphicsDevice device,
            GraphicsShaderModule shader,
            GraphicsPipelineLayout pipelineLayout,
            GraphicsCullMode cullMode,
            string label) =>
            device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_shadow",
                new GraphicsDepthStencilState(GraphicsTextureFormat.Depth32Float),
                label: label,
                vertexBuffers:
                [
                    new GraphicsVertexBufferLayout(
                        100,
                        [
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 0, 0),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 12, 1),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 24, 2),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 36, 3),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 44, 4),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 60, 5),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 76, 6),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 92, 7),
                        ]),
                ],
                layout: pipelineLayout,
                cullMode: cullMode));

        private static GraphicsRenderPipeline CreateAlphaTestedShadowPipeline(
            GraphicsDevice device,
            GraphicsShaderModule shader,
            GraphicsPipelineLayout pipelineLayout,
            GraphicsCullMode cullMode,
            string label) =>
            device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_shadow",
                shader,
                "fs_alpha_test_depth",
                new GraphicsDepthStencilState(GraphicsTextureFormat.Depth32Float),
                label: label,
                vertexBuffers:
                [
                    new GraphicsVertexBufferLayout(
                        100,
                        [
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 0, 0),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 12, 1),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 24, 2),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 36, 3),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 44, 4),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 60, 5),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 76, 6),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 92, 7),
                        ]),
                ],
                layout: pipelineLayout,
                cullMode: cullMode));

        private static GraphicsRenderPipeline CreateDepthPrepassPipeline(
            GraphicsDevice device,
            GraphicsShaderModule shader,
            GraphicsPipelineLayout pipelineLayout) =>
            device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_depth_prepass",
                new GraphicsDepthStencilState(GraphicsTextureFormat.Depth32Float),
                label: "scene ambient-occlusion depth prepass pipeline",
                vertexBuffers:
                [
                    new GraphicsVertexBufferLayout(
                        100,
                        [
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 0, 0),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 12, 1),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 24, 2),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 36, 3),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 44, 4),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 60, 5),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 76, 6),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 92, 7),
                        ]),
                ],
                layout: pipelineLayout,
                cullMode: GraphicsCullMode.None));

        private static GraphicsRenderPipeline CreateAlphaTestedDepthPrepassPipeline(
            GraphicsDevice device,
            GraphicsShaderModule shader,
            GraphicsPipelineLayout pipelineLayout) =>
            device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_depth_prepass",
                shader,
                "fs_alpha_test_depth",
                new GraphicsDepthStencilState(GraphicsTextureFormat.Depth32Float),
                label: "scene alpha-tested ambient-occlusion depth prepass pipeline",
                vertexBuffers:
                [
                    new GraphicsVertexBufferLayout(
                        100,
                        [
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 0, 0),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 12, 1),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x3, 24, 2),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 36, 3),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 44, 4),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 60, 5),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x4, 76, 6),
                            new GraphicsVertexAttribute(GraphicsVertexFormat.Float32x2, 92, 7),
                        ]),
                ],
                layout: pipelineLayout,
                cullMode: GraphicsCullMode.None));
    }

    private sealed class AmbientOcclusionResources : IDisposable
    {
        private AmbientOcclusionResources(
            GraphicsExtent3D extent,
            GraphicsTexture depthTexture,
            GraphicsTextureView depthView,
            GraphicsBindGroup bindGroup)
        {
            Extent = extent;
            DepthTexture = depthTexture;
            DepthView = depthView;
            BindGroup = bindGroup;
        }

        internal GraphicsExtent3D Extent { get; }

        internal GraphicsTexture DepthTexture { get; }

        internal GraphicsTextureView DepthView { get; }

        internal GraphicsBindGroup BindGroup { get; }

        internal static AmbientOcclusionResources Create(
            GraphicsDevice device,
            GraphicsBindGroupLayout layout,
            GraphicsSampler shadowSampler,
            GraphicsTextureView shadowView,
            GraphicsExtent3D extent,
            GraphicsSampler fallbackBackgroundSampler,
            GraphicsTextureView fallbackBackgroundView)
        {
            GraphicsTexture? depthTexture = null;
            GraphicsTextureView? depthView = null;
            GraphicsBindGroup? bindGroup = null;
            try
            {
                depthTexture = device.CreateTexture(new GraphicsTextureDescriptor(
                    extent,
                    GraphicsTextureFormat.Depth32Float,
                    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
                    label: "scene ambient-occlusion camera depth"));
                depthView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    depthTexture,
                    label: "scene ambient-occlusion camera-depth view"));
                bindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
                    layout,
                    [
                        new GraphicsBindGroupEntry(0, shadowSampler),
                        new GraphicsBindGroupEntry(1, shadowView),
                        new GraphicsBindGroupEntry(2, depthView),
                        new GraphicsBindGroupEntry(3, fallbackBackgroundSampler),
                        new GraphicsBindGroupEntry(4, fallbackBackgroundView),
                    ],
                    "scene shadow and ambient-occlusion bind group"));
                AmbientOcclusionResources result = new(extent, depthTexture, depthView, bindGroup);
                depthTexture = null;
                depthView = null;
                bindGroup = null;
                return result;
            }
            finally
            {
                bindGroup?.Dispose();
                depthView?.Dispose();
                depthTexture?.Dispose();
            }
        }

        public void Dispose()
        {
            BindGroup.Dispose();
            DepthView.Dispose();
            DepthTexture.Dispose();
        }
    }

    private sealed class TransmissionResources : IDisposable
    {
        private readonly GraphicsShaderModule mipShader;
        private readonly GraphicsBindGroupLayout mipBindGroupLayout;
        private readonly GraphicsPipelineLayout mipPipelineLayout;
        private readonly GraphicsRenderPipeline mipPipeline;
        private readonly GraphicsTextureView[] mipViews;
        private readonly GraphicsBindGroup[] mipBindGroups;

        private TransmissionResources(
            GraphicsExtent3D extent,
            GraphicsTexture backgroundTexture,
            GraphicsTextureView backgroundView,
            GraphicsSampler sampler,
            GraphicsBindGroup bindGroup,
            GraphicsShaderModule mipShader,
            GraphicsBindGroupLayout mipBindGroupLayout,
            GraphicsPipelineLayout mipPipelineLayout,
            GraphicsRenderPipeline mipPipeline,
            GraphicsTextureView[] mipViews,
            GraphicsBindGroup[] mipBindGroups)
        {
            Extent = extent;
            BackgroundTexture = backgroundTexture;
            BackgroundView = backgroundView;
            Sampler = sampler;
            BindGroup = bindGroup;
            this.mipShader = mipShader;
            this.mipBindGroupLayout = mipBindGroupLayout;
            this.mipPipelineLayout = mipPipelineLayout;
            this.mipPipeline = mipPipeline;
            this.mipViews = mipViews;
            this.mipBindGroups = mipBindGroups;
        }

        internal GraphicsExtent3D Extent { get; }
        internal GraphicsTexture BackgroundTexture { get; }
        internal GraphicsTextureView BackgroundView { get; }
        internal GraphicsTextureView BackgroundRenderView => mipViews[0];
        internal GraphicsSampler Sampler { get; }
        internal GraphicsBindGroup BindGroup { get; }

        internal void RecordMipPyramid(GraphicsCommandEncoder encoder)
        {
            for (int level = 1; level < mipViews.Length; level++)
            {
                using GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(
                    new GraphicsRenderPassDescriptor(
                        new GraphicsRenderPassColorAttachment(mipViews[level]),
                        $"scene HDR transmission background mip {level}"));
                pass.SetPipeline(mipPipeline);
                pass.SetBindGroup(0, mipBindGroups[level - 1]);
                pass.Draw(3);
            }
        }

        internal static TransmissionResources Create(
            GraphicsDevice device,
            GraphicsBindGroupLayout layout,
            GraphicsSampler shadowSampler,
            GraphicsTextureView shadowView,
            GraphicsTextureView depthView,
            GraphicsExtent3D extent,
            GraphicsTextureFormat colorFormat)
        {
            const string mipCode = """
                @group(0) @binding(0) var source_sampler: sampler;
                @group(0) @binding(1) var source_texture: texture_2d<f32>;

                struct FullscreenVertex {
                    @builtin(position) position: vec4f,
                    @location(0) uv: vec2f,
                };

                @vertex fn vs_fullscreen(
                    @builtin(vertex_index) vertex_index: u32,
                ) -> FullscreenVertex {
                    var positions = array<vec2f, 3>(
                        vec2f(-1.0, -1.0),
                        vec2f(3.0, -1.0),
                        vec2f(-1.0, 3.0));
                    let position = positions[vertex_index];
                    var output: FullscreenVertex;
                    output.position = vec4f(position, 0.0, 1.0);
                    output.uv = vec2f(position.x * 0.5 + 0.5, 0.5 - position.y * 0.5);
                    return output;
                }

                @fragment fn fs_downsample(input: FullscreenVertex) -> @location(0) vec4f {
                    // Linear filtering at the next mip's texel center averages the corresponding
                    // 2x2 source footprint while retaining scene-linear HDR values.
                    return textureSampleLevel(source_texture, source_sampler, input.uv, 0.0);
                }
                """;
            uint mipLevelCount = 1u + (uint)BitOperations.Log2(Math.Max(extent.Width, extent.Height));
            GraphicsTexture? texture = null;
            GraphicsTextureView? view = null;
            GraphicsSampler? sampler = null;
            GraphicsBindGroup? bindGroup = null;
            GraphicsShaderModule? mipShader = null;
            GraphicsBindGroupLayout? mipLayout = null;
            GraphicsPipelineLayout? mipPipelineLayout = null;
            GraphicsRenderPipeline? mipPipeline = null;
            List<GraphicsTextureView> mipViews = [];
            List<GraphicsBindGroup> mipBindGroups = [];
            try
            {
                texture = device.CreateTexture(new GraphicsTextureDescriptor(
                    extent,
                    colorFormat,
                    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
                    mipLevelCount,
                    label: "scene opaque HDR transmission background pyramid"));
                for (uint level = 0; level < mipLevelCount; level++)
                {
                    mipViews.Add(device.CreateTextureView(new GraphicsTextureViewDescriptor(
                        texture,
                        baseMipLevel: level,
                        label: $"scene HDR transmission background mip {level}")));
                }
                view = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    texture,
                    mipLevelCount: mipLevelCount,
                    label: "scene opaque HDR transmission-background pyramid view"));
                sampler = device.CreateSampler(new GraphicsSamplerDescriptor(
                    GraphicsAddressMode.ClampToEdge,
                    GraphicsAddressMode.ClampToEdge,
                    GraphicsAddressMode.ClampToEdge,
                    GraphicsFilterMode.Linear,
                    GraphicsFilterMode.Linear,
                    GraphicsFilterMode.Linear,
                    label: "scene transmission trilinear background sampler"));
                bindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
                    layout,
                    [
                        new GraphicsBindGroupEntry(0, shadowSampler),
                        new GraphicsBindGroupEntry(1, shadowView),
                        new GraphicsBindGroupEntry(2, depthView),
                        new GraphicsBindGroupEntry(3, sampler),
                        new GraphicsBindGroupEntry(4, view),
                    ],
                    "scene transmission background bind group"));
                mipShader = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(
                    mipCode,
                    "scene HDR transmission mip shader"));
                mipLayout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                    [
                        new GraphicsBindGroupLayoutEntry(
                            0, GraphicsShaderStage.Fragment, GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            1,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                    ],
                    "scene HDR transmission mip bind-group layout"));
                mipPipelineLayout = device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor(
                    [mipLayout],
                    "scene HDR transmission mip pipeline layout"));
                mipPipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                    mipShader,
                    "vs_fullscreen",
                    mipShader,
                    "fs_downsample",
                    colorFormat,
                    label: "scene HDR transmission mip pipeline",
                    layout: mipPipelineLayout));
                for (int level = 1; level < mipViews.Count; level++)
                {
                    mipBindGroups.Add(device.CreateBindGroup(new GraphicsBindGroupDescriptor(
                        mipLayout,
                        [
                            new GraphicsBindGroupEntry(0, sampler),
                            new GraphicsBindGroupEntry(1, mipViews[level - 1]),
                        ],
                        $"scene HDR transmission mip {level} bind group")));
                }
                TransmissionResources result = new(
                    extent,
                    texture,
                    view,
                    sampler,
                    bindGroup,
                    mipShader,
                    mipLayout,
                    mipPipelineLayout,
                    mipPipeline,
                    [.. mipViews],
                    [.. mipBindGroups]);
                texture = null;
                view = null;
                sampler = null;
                bindGroup = null;
                mipShader = null;
                mipLayout = null;
                mipPipelineLayout = null;
                mipPipeline = null;
                mipViews.Clear();
                mipBindGroups.Clear();
                return result;
            }
            finally
            {
                foreach (GraphicsBindGroup group in mipBindGroups)
                {
                    group.Dispose();
                }
                foreach (GraphicsTextureView mipView in mipViews)
                {
                    mipView.Dispose();
                }
                mipPipeline?.Dispose();
                mipPipelineLayout?.Dispose();
                mipLayout?.Dispose();
                mipShader?.Dispose();
                bindGroup?.Dispose();
                sampler?.Dispose();
                view?.Dispose();
                texture?.Dispose();
            }
        }

        public void Dispose()
        {
            foreach (GraphicsBindGroup group in mipBindGroups)
            {
                group.Dispose();
            }
            foreach (GraphicsTextureView mipView in mipViews)
            {
                mipView.Dispose();
            }
            mipPipeline.Dispose();
            mipPipelineLayout.Dispose();
            mipBindGroupLayout.Dispose();
            mipShader.Dispose();
            BindGroup.Dispose();
            Sampler.Dispose();
            BackgroundView.Dispose();
            BackgroundTexture.Dispose();
        }
    }

    private sealed class EnvironmentResources : IDisposable
    {
        private EnvironmentResources(
            EnvironmentCubeGpuResources diffuseCube,
            EnvironmentCubeGpuResources specularCubeArray,
            GraphicsBindGroup bindGroup)
        {
            DiffuseCube = diffuseCube;
            SpecularCubeArray = specularCubeArray;
            BindGroup = bindGroup;
        }

        internal EnvironmentCubeGpuResources DiffuseCube { get; }

        internal EnvironmentCubeGpuResources SpecularCubeArray { get; }

        internal GraphicsBindGroup BindGroup { get; }

        internal static EnvironmentResources Create(
            GraphicsDevice device,
            GraphicsBindGroupLayout layout,
            GraphicsTextureView brdfLutView,
            EquirectangularHdrEnvironment environment)
        {
            PreparedEnvironmentData prepared = GetPreparedEnvironment(environment);
            EnvironmentCubeGpuResources? diffuseCube = null;
            EnvironmentCubeGpuResources? specularCubeArray = null;
            GraphicsBindGroup? bindGroup = null;
            try
            {
                diffuseCube = EnvironmentCubeGpuResources.Create(device, prepared.DiffuseCube);
                specularCubeArray = EnvironmentCubeGpuResources.CreateSpecularArray(
                    device,
                    prepared.SpecularCube,
                    prepared.SheenCube);
                bindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
                    layout,
                    [
                        new GraphicsBindGroupEntry(0, diffuseCube.Sampler),
                        new GraphicsBindGroupEntry(1, diffuseCube.View),
                        new GraphicsBindGroupEntry(2, specularCubeArray.View),
                        new GraphicsBindGroupEntry(3, brdfLutView),
                    ],
                    $"{environment.Name ?? "environment"} diffuse bind group"));
                EnvironmentResources result = new(diffuseCube, specularCubeArray, bindGroup);
                diffuseCube = null;
                specularCubeArray = null;
                bindGroup = null;
                return result;
            }
            finally
            {
                bindGroup?.Dispose();
                specularCubeArray?.Dispose();
                diffuseCube?.Dispose();
            }
        }

        public void Dispose()
        {
            BindGroup.Dispose();
            SpecularCubeArray.Dispose();
            DiffuseCube.Dispose();
        }
    }

    private static PreparedEnvironmentData GetPreparedEnvironment(
        EquirectangularHdrEnvironment environment) =>
        PreparedEnvironmentCache.GetValue(
            environment,
            static source => new Lazy<PreparedEnvironmentData>(
                () => new PreparedEnvironmentData(
                    HdrEnvironmentConverter.CreateDiffuseIrradianceCube(
                        source,
                        DiffuseEnvironmentFaceSize,
                        StandardColorSpaces.LinearSrgb),
                    HdrEnvironmentConverter.CreateSpecularPrefilteredCube(
                        source,
                        SpecularEnvironmentFaceSize,
                        SpecularEnvironmentMipCount,
                        SpecularEnvironmentSampleCount,
                        StandardColorSpaces.LinearSrgb),
                    HdrEnvironmentConverter.CreateSheenPrefilteredCube(
                        source,
                        SpecularEnvironmentFaceSize,
                        SpecularEnvironmentMipCount,
                        SpecularEnvironmentSampleCount,
                        StandardColorSpaces.LinearSrgb)),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private sealed record PreparedEnvironmentData(
        HdrEnvironmentCube DiffuseCube,
        PrefilteredEnvironmentCube SpecularCube,
        PrefilteredEnvironmentCube SheenCube);

    private sealed class MaterialTextureResources : IDisposable
    {
        private MaterialTextureResources(GraphicsTexture texture, GraphicsTextureView view)
        {
            Texture = texture;
            View = view;
        }

        internal GraphicsTexture Texture { get; }

        internal GraphicsTextureView View { get; }

        internal static MaterialTextureResources Create(GraphicsDevice device, LinearRgbaImage image)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(image);
            Vector4[] level = ReferenceEquals(image.ColorSpace, StandardColorSpaces.LinearSrgb)
                ? image.PixelStorage
                : new Vector4[image.Pixels.Count];
            void ConvertPixel(int index)
            {
                Vector4 pixel = image.Pixels[index];
                LinearRgba converted = StandardLinearRgbConverter.Convert(
                    new LinearRgba(pixel.X, pixel.Y, pixel.Z, pixel.W, image.ColorSpace),
                    StandardColorSpaces.LinearSrgb);
                level[index] = new Vector4(converted.Red, converted.Green, converted.Blue, converted.Alpha);
            }
            if (!ReferenceEquals(image.ColorSpace, StandardColorSpaces.LinearSrgb) &&
                level.Length >= ParallelTextureWorkThreshold)
            {
                Parallel.For(0, level.Length, ConvertPixel);
            }
            else if (!ReferenceEquals(image.ColorSpace, StandardColorSpaces.LinearSrgb))
            {
                for (int index = 0; index < level.Length; index++)
                {
                    ConvertPixel(index);
                }
            }

            GraphicsTexture? texture = null;
            GraphicsTextureView? view = null;
            try
            {
                uint mipCount = GetMipLevelCount(image.Width, image.Height);
                texture = device.CreateTexture(new GraphicsTextureDescriptor(
                    new GraphicsExtent3D(image.Width, image.Height),
                    GraphicsTextureFormat.Rgba16Float,
                    GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                    mipLevelCount: mipCount,
                    label: image.Name ?? "material color texture"));
                uint width = image.Width;
                uint height = image.Height;
                for (uint mip = 0; mip < mipCount; mip++)
                {
                    Half[] texels = EncodeHalf(level);
                    device.Queue.WriteTexture(
                        texture,
                        mip,
                        new GraphicsOrigin3D(),
                        new GraphicsExtent3D(width, height),
                        MemoryMarshal.AsBytes(texels.AsSpan()),
                        checked(width * 8),
                        height);
                    if (mip + 1 < mipCount)
                    {
                        level = DownsampleBox(level, width, height, normalizeNormals: false);
                        width = Math.Max(1, width / 2);
                        height = Math.Max(1, height / 2);
                    }
                }
                view = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    texture,
                    mipLevelCount: mipCount,
                    label: "material color view"));
                MaterialTextureResources result = new(texture, view);
                texture = null;
                view = null;
                return result;
            }
            finally
            {
                view?.Dispose();
                texture?.Dispose();
            }
        }

        internal static MaterialTextureResources Create(
            GraphicsDevice device,
            CompressedMaterialTexture image)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(image);
            if (image.Content != CompressedMaterialTextureContent.Color ||
                image.ColorSpace != StandardColorSpaces.LinearSrgb)
            {
                throw new NotSupportedException(
                    "Compressed material color textures require Linear sRGB content.");
            }
            GraphicsTextureBlockLayout block = GraphicsTextureFormatInfo.GetBlockLayout(image.Format);
            GraphicsTexture? texture = null;
            GraphicsTextureView? view = null;
            try
            {
                uint mipCount = checked((uint)image.MipLevels.Count);
                texture = device.CreateTexture(new GraphicsTextureDescriptor(
                    new GraphicsExtent3D(image.Width, image.Height),
                    image.Format,
                    GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                    mipLevelCount: mipCount,
                    label: image.Name ?? "compressed material color texture"));
                for (uint mip = 0; mip < mipCount; mip++)
                {
                    CompressedTextureMipLevel level = image.MipLevels[checked((int)mip)];
                    uint blocksWide = checked((uint)(
                        ((ulong)level.Width + block.BlockWidth - 1) / block.BlockWidth));
                    uint blockRows = checked((uint)(
                        ((ulong)level.Height + block.BlockHeight - 1) / block.BlockHeight));
                    device.Queue.WriteTexture(
                        texture,
                        mip,
                        default,
                        new GraphicsExtent3D(
                            checked(blocksWide * block.BlockWidth),
                            checked(blockRows * block.BlockHeight)),
                        level.Data.Span,
                        checked(blocksWide * block.BytesPerBlock),
                        blockRows);
                }
                view = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    texture,
                    mipLevelCount: mipCount,
                    label: "compressed material color view"));
                MaterialTextureResources result = new(texture, view);
                texture = null;
                view = null;
                return result;
            }
            finally
            {
                view?.Dispose();
                texture?.Dispose();
            }
        }

        public void Dispose()
        {
            View.Dispose();
            Texture.Dispose();
        }

        private static Half[] EncodeHalf(Vector4[] values)
        {
            Half[] result = new Half[checked(values.Length * 4)];
            void EncodePixel(int index)
            {
                int texel = index * 4;
                result[texel] = ToFiniteHalf(values[index].X);
                result[texel + 1] = ToFiniteHalf(values[index].Y);
                result[texel + 2] = ToFiniteHalf(values[index].Z);
                result[texel + 3] = ToFiniteHalf(values[index].W);
            }
            if (values.Length >= ParallelTextureWorkThreshold)
            {
                Parallel.For(0, values.Length, EncodePixel);
            }
            else
            {
                for (int index = 0; index < values.Length; index++)
                {
                    EncodePixel(index);
                }
            }
            return result;
        }

        private static Half ToFiniteHalf(float value)
        {
            Half converted = (Half)value;
            if (Half.IsInfinity(converted))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "A material image component exceeds finite FP16 storage.");
            }
            return converted;
        }
    }

    private sealed class DataTextureResources : IDisposable
    {
        private DataTextureResources(GraphicsTexture texture, GraphicsTextureView view)
        {
            Texture = texture;
            View = view;
        }

        internal GraphicsTexture Texture { get; }

        internal GraphicsTextureView View { get; }

        internal static DataTextureResources Create(
            GraphicsDevice device,
            NormalizedRgbaDataImage image,
            DataTextureSemantic semantic)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(image);
            Vector4[] level = image.TexelStorage;

            GraphicsTexture? texture = null;
            GraphicsTextureView? view = null;
            try
            {
                uint mipCount = GetMipLevelCount(image.Width, image.Height);
                texture = device.CreateTexture(new GraphicsTextureDescriptor(
                    new GraphicsExtent3D(image.Width, image.Height),
                    GraphicsTextureFormat.Rgba8Unorm,
                    GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                    mipLevelCount: mipCount,
                    label: image.Name ?? "material data texture"));
                uint width = image.Width;
                uint height = image.Height;
                for (uint mip = 0; mip < mipCount; mip++)
                {
                    byte[] texels = EncodeUnorm8(level);
                    device.Queue.WriteTexture(
                        texture,
                        mip,
                        new GraphicsOrigin3D(),
                        new GraphicsExtent3D(width, height),
                        texels,
                        checked(width * 4),
                        height);
                    if (mip + 1 < mipCount)
                    {
                        level = DownsampleBox(
                            level,
                            width,
                            height,
                            normalizeNormals: semantic == DataTextureSemantic.Normal);
                        width = Math.Max(1, width / 2);
                        height = Math.Max(1, height / 2);
                    }
                }
                view = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    texture,
                    mipLevelCount: mipCount,
                    label: "material data view"));
                DataTextureResources result = new(texture, view);
                texture = null;
                view = null;
                return result;
            }
            finally
            {
                view?.Dispose();
                texture?.Dispose();
            }
        }

        internal static DataTextureResources Create(
            GraphicsDevice device,
            CompressedMaterialTexture image,
            DataTextureSemantic semantic)
        {
            ArgumentNullException.ThrowIfNull(device);
            ArgumentNullException.ThrowIfNull(image);
            if (image.Content != CompressedMaterialTextureContent.Data)
            {
                throw new ArgumentException(
                    "A compressed material data texture must be tagged as non-color data.",
                    nameof(image));
            }
            _ = semantic;
            GraphicsTextureBlockLayout block = GraphicsTextureFormatInfo.GetBlockLayout(image.Format);
            GraphicsTexture? texture = null;
            GraphicsTextureView? view = null;
            try
            {
                uint mipCount = checked((uint)image.MipLevels.Count);
                texture = device.CreateTexture(new GraphicsTextureDescriptor(
                    new GraphicsExtent3D(image.Width, image.Height),
                    image.Format,
                    GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                    mipLevelCount: mipCount,
                    label: image.Name ?? "compressed material data texture"));
                for (uint mip = 0; mip < mipCount; mip++)
                {
                    CompressedTextureMipLevel level = image.MipLevels[checked((int)mip)];
                    uint blocksWide = checked((uint)(
                        ((ulong)level.Width + block.BlockWidth - 1) / block.BlockWidth));
                    uint blockRows = checked((uint)(
                        ((ulong)level.Height + block.BlockHeight - 1) / block.BlockHeight));
                    device.Queue.WriteTexture(
                        texture,
                        mip,
                        default,
                        new GraphicsExtent3D(
                            checked(blocksWide * block.BlockWidth),
                            checked(blockRows * block.BlockHeight)),
                        level.Data.Span,
                        checked(blocksWide * block.BytesPerBlock),
                        blockRows);
                }
                view = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    texture,
                    mipLevelCount: mipCount,
                    label: "compressed material data view"));
                DataTextureResources result = new(texture, view);
                texture = null;
                view = null;
                return result;
            }
            finally
            {
                view?.Dispose();
                texture?.Dispose();
            }
        }

        public void Dispose()
        {
            View.Dispose();
            Texture.Dispose();
        }

        private static byte ToUnorm8(float value) =>
            checked((byte)Math.Clamp((int)MathF.Round(value * byte.MaxValue), 0, byte.MaxValue));

        private static byte[] EncodeUnorm8(Vector4[] values)
        {
            byte[] result = new byte[checked(values.Length * 4)];
            void EncodeTexel(int index)
            {
                int texel = index * 4;
                result[texel] = ToUnorm8(values[index].X);
                result[texel + 1] = ToUnorm8(values[index].Y);
                result[texel + 2] = ToUnorm8(values[index].Z);
                result[texel + 3] = ToUnorm8(values[index].W);
            }
            if (values.Length >= ParallelTextureWorkThreshold)
            {
                Parallel.For(0, values.Length, EncodeTexel);
            }
            else
            {
                for (int index = 0; index < values.Length; index++)
                {
                    EncodeTexel(index);
                }
            }
            return result;
        }
    }

    private sealed class BloomResources : IDisposable
    {
        private const int SettingsSize = 32;
        private readonly GraphicsDevice device;
        private readonly GraphicsShaderModule shader;
        private readonly GraphicsBindGroupLayout bindGroupLayout;
        private readonly GraphicsPipelineLayout pipelineLayout;
        private readonly GraphicsSampler sampler;
        private readonly GraphicsBuffer settingsBuffer;
        private readonly GraphicsTextureView sceneView;
        private readonly GraphicsTexture blurTextureA;
        private readonly GraphicsTextureView blurViewA;
        private readonly GraphicsTexture blurTextureB;
        private readonly GraphicsTextureView blurViewB;
        private readonly GraphicsBindGroup extractBindGroup;
        private readonly GraphicsBindGroup blurBindGroup;
        private readonly GraphicsBindGroup horizontalBlurBindGroup;
        private readonly GraphicsBindGroup compositeBindGroup;
        private readonly GraphicsRenderPipeline extractPipeline;
        private readonly GraphicsRenderPipeline blurPipeline;
        private readonly GraphicsRenderPipeline horizontalBlurPipeline;
        private readonly GraphicsRenderPipeline compositePipeline;

        private BloomResources(
            GraphicsDevice device,
            GraphicsExtent3D extent,
            uint downsampleFactor,
            GraphicsShaderModule shader,
            GraphicsBindGroupLayout bindGroupLayout,
            GraphicsPipelineLayout pipelineLayout,
            GraphicsSampler sampler,
            GraphicsBuffer settingsBuffer,
            GraphicsTexture sceneTexture,
            GraphicsTextureView sceneView,
            GraphicsTexture blurTextureA,
            GraphicsTextureView blurViewA,
            GraphicsTexture blurTextureB,
            GraphicsTextureView blurViewB,
            GraphicsBindGroup extractBindGroup,
            GraphicsBindGroup blurBindGroup,
            GraphicsBindGroup horizontalBlurBindGroup,
            GraphicsBindGroup compositeBindGroup,
            GraphicsRenderPipeline extractPipeline,
            GraphicsRenderPipeline blurPipeline,
            GraphicsRenderPipeline horizontalBlurPipeline,
            GraphicsRenderPipeline compositePipeline)
        {
            this.device = device;
            Extent = extent;
            DownsampleFactor = downsampleFactor;
            this.shader = shader;
            this.bindGroupLayout = bindGroupLayout;
            this.pipelineLayout = pipelineLayout;
            this.sampler = sampler;
            this.settingsBuffer = settingsBuffer;
            SceneTexture = sceneTexture;
            this.sceneView = sceneView;
            this.blurTextureA = blurTextureA;
            this.blurViewA = blurViewA;
            this.blurTextureB = blurTextureB;
            this.blurViewB = blurViewB;
            this.extractBindGroup = extractBindGroup;
            this.blurBindGroup = blurBindGroup;
            this.horizontalBlurBindGroup = horizontalBlurBindGroup;
            this.compositeBindGroup = compositeBindGroup;
            this.extractPipeline = extractPipeline;
            this.blurPipeline = blurPipeline;
            this.horizontalBlurPipeline = horizontalBlurPipeline;
            this.compositePipeline = compositePipeline;
        }

        internal GraphicsExtent3D Extent { get; }

        internal uint DownsampleFactor { get; }

        internal GraphicsTexture SceneTexture { get; }

        internal void WriteSettings(
            float threshold,
            float softKnee,
            float intensity,
            float radius,
            BloomCompositeMode compositeMode)
        {
            Span<float> settings = stackalloc float[SettingsSize / sizeof(float)];
            settings.Clear();
            settings[0] = threshold;
            settings[1] = softKnee;
            settings[2] = intensity;
            settings[3] = radius / DownsampleFactor;
            Span<uint> unsignedSettings = MemoryMarshal.Cast<float, uint>(settings);
            unsignedSettings[4] = (uint)compositeMode;
            unsignedSettings[5] = DownsampleFactor;
            device.Queue.WriteBuffer(settingsBuffer, 0, MemoryMarshal.AsBytes(settings));
        }

        internal void RecordPostProcess(
            GraphicsCommandEncoder encoder,
            GraphicsTexture presentationTarget)
        {
            using (GraphicsRenderPassEncoder extract = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(blurTextureA),
                    "scene bloom energy-preserving highlight extraction")))
            {
                extract.SetPipeline(extractPipeline);
                extract.SetBindGroup(0, extractBindGroup);
                extract.Draw(3);
            }
            // Three horizontal/vertical pairs greatly reduce the box-like footprint visible from
            // one sparse separable pass. Each pass uses a radius scaled by sqrt(pass count), so the
            // composed variance tracks the requested output-pixel radius.
            for (int iteration = 0; iteration < 3; iteration++)
            {
                using (GraphicsRenderPassEncoder horizontal = encoder.BeginRenderPass(
                    new GraphicsRenderPassDescriptor(
                        new GraphicsRenderPassColorAttachment(blurTextureB),
                        "scene bloom iterative horizontal blur")))
                {
                    horizontal.SetPipeline(horizontalBlurPipeline);
                    horizontal.SetBindGroup(0, blurBindGroup);
                    horizontal.Draw(3);
                }
                using GraphicsRenderPassEncoder vertical = encoder.BeginRenderPass(
                    new GraphicsRenderPassDescriptor(
                        new GraphicsRenderPassColorAttachment(blurTextureA),
                        "scene bloom iterative vertical blur"));
                vertical.SetPipeline(blurPipeline);
                vertical.SetBindGroup(0, horizontalBlurBindGroup);
                vertical.Draw(3);
            }
            using GraphicsRenderPassEncoder composite = encoder.BeginRenderPass(
                new GraphicsRenderPassDescriptor(
                    new GraphicsRenderPassColorAttachment(presentationTarget),
                    "scene bloom HDR composition"));
            composite.SetPipeline(compositePipeline);
            composite.SetBindGroup(0, compositeBindGroup);
            composite.Draw(3);
        }

        internal static BloomResources Create(
            GraphicsDevice device,
            GraphicsTextureFormat presentationFormat,
            GraphicsExtent3D extent,
            uint downsampleFactor)
        {
            const string code = """
                struct BloomSettings {
                    threshold: f32,
                    soft_knee: f32,
                    intensity: f32,
                    radius_texels: f32,
                    composite_mode: u32,
                    downsample_factor: u32,
                    padding_1: u32,
                    padding_2: u32,
                };

                @group(0) @binding(0) var bloom_sampler: sampler;
                @group(0) @binding(1) var primary_texture: texture_2d<f32>;
                @group(0) @binding(2) var secondary_texture: texture_2d<f32>;
                @group(0) @binding(3) var<uniform> settings: BloomSettings;

                struct FullscreenVertex {
                    @builtin(position) position: vec4f,
                    @location(0) uv: vec2f,
                };

                @vertex
                fn vs_fullscreen(@builtin(vertex_index) vertex_index: u32) -> FullscreenVertex {
                    var positions = array<vec2f, 3>(
                        vec2f(-1.0, -1.0),
                        vec2f(3.0, -1.0),
                        vec2f(-1.0, 3.0));
                    let position = positions[vertex_index];
                    var output: FullscreenVertex;
                    output.position = vec4f(position, 0.0, 1.0);
                    output.uv = vec2f(position.x * 0.5 + 0.5, 0.5 - position.y * 0.5);
                    return output;
                }

                fn extract_highlight(color: vec3f) -> vec3f {
                    let brightness = max(max(color.r, color.g), color.b);
                    let knee = max(settings.soft_knee, 0.00001);
                    var soft = clamp(brightness - settings.threshold + knee, 0.0, 2.0 * knee);
                    soft = soft * soft / (4.0 * knee + 0.00001);
                    let contribution = max(brightness - settings.threshold, soft) /
                        max(brightness, 0.00001);
                    return max(color, vec3f(0.0)) * contribution;
                }

                fn blur_step(dimensions: vec2f) -> vec2f {
                    // CPU code converts the requested output-pixel radius to the selected Bloom
                    // pyramid level. Three blur pairs accumulate the requested variance.
                    let tap_spacing = settings.radius_texels / (3.230769 * 1.732051);
                    return vec2f(tap_spacing) / dimensions;
                }

                @fragment
                fn fs_extract_horizontal(input: FullscreenVertex) -> @location(0) vec4f {
                    _ = input.uv;
                    let source_dimensions = textureDimensions(primary_texture);
                    let factor = max(settings.downsample_factor, 1u);
                    let destination_coordinate = vec2u(input.position.xy);
                    let source_origin = destination_coordinate * factor;
                    var result = vec3f(0.0);
                    var sample_count = 0u;
                    for (var y = 0u; y < 8u; y += 1u) {
                        if (y >= factor) {
                            break;
                        }
                        for (var x = 0u; x < 8u; x += 1u) {
                            if (x >= factor) {
                                break;
                            }
                            let coordinate = source_origin + vec2u(x, y);
                            if (all(coordinate < source_dimensions)) {
                                result += extract_highlight(textureLoad(
                                    primary_texture,
                                    vec2i(coordinate),
                                    0).rgb);
                                sample_count += 1u;
                            }
                        }
                    }
                    return vec4f(result / f32(max(sample_count, 1u)), 1.0);
                }

                @fragment
                fn fs_blur_horizontal(input: FullscreenVertex) -> @location(0) vec4f {
                    let dimensions = vec2f(textureDimensions(primary_texture));
                    let step = vec2f(blur_step(dimensions).x, 0.0);
                    var result = textureSample(primary_texture, bloom_sampler, input.uv).rgb * 0.227027;
                    result += textureSample(
                        primary_texture, bloom_sampler, input.uv + step * 1.384615).rgb * 0.316216;
                    result += textureSample(
                        primary_texture, bloom_sampler, input.uv - step * 1.384615).rgb * 0.316216;
                    result += textureSample(
                        primary_texture, bloom_sampler, input.uv + step * 3.230769).rgb * 0.070270;
                    result += textureSample(
                        primary_texture, bloom_sampler, input.uv - step * 3.230769).rgb * 0.070270;
                    return vec4f(result, 1.0);
                }

                @fragment
                fn fs_blur_vertical(input: FullscreenVertex) -> @location(0) vec4f {
                    let dimensions = vec2f(textureDimensions(primary_texture));
                    let step = vec2f(0.0, blur_step(dimensions).y);
                    var result = textureSample(primary_texture, bloom_sampler, input.uv).rgb * 0.227027;
                    result += textureSample(
                        primary_texture, bloom_sampler, input.uv + step * 1.384615).rgb * 0.316216;
                    result += textureSample(
                        primary_texture, bloom_sampler, input.uv - step * 1.384615).rgb * 0.316216;
                    result += textureSample(
                        primary_texture, bloom_sampler, input.uv + step * 3.230769).rgb * 0.070270;
                    result += textureSample(
                        primary_texture, bloom_sampler, input.uv - step * 3.230769).rgb * 0.070270;
                    return vec4f(result, 1.0);
                }

                @fragment
                fn fs_composite(input: FullscreenVertex) -> @location(0) vec4f {
                    let scene = textureSample(primary_texture, bloom_sampler, input.uv);
                    let bloom = textureSample(secondary_texture, bloom_sampler, input.uv).rgb;
                    if (settings.composite_mode == 0u) {
                        let extracted = extract_highlight(scene.rgb);
                        let redistributed = scene.rgb - extracted + bloom;
                        return vec4f(
                            mix(scene.rgb, redistributed, clamp(settings.intensity, 0.0, 1.0)),
                            scene.a);
                    }
                    return vec4f(scene.rgb + bloom * settings.intensity, scene.a);
                }
                """;
            GraphicsShaderModule? shader = null;
            GraphicsBindGroupLayout? layout = null;
            GraphicsPipelineLayout? pipelineLayout = null;
            GraphicsSampler? sampler = null;
            GraphicsBuffer? settingsBuffer = null;
            GraphicsTexture? sceneTexture = null;
            GraphicsTextureView? sceneView = null;
            GraphicsTexture? blurA = null;
            GraphicsTextureView? blurViewA = null;
            GraphicsTexture? blurB = null;
            GraphicsTextureView? blurViewB = null;
            GraphicsBindGroup? extractBindGroup = null;
            GraphicsBindGroup? blurBindGroup = null;
            GraphicsBindGroup? horizontalBlurBindGroup = null;
            GraphicsBindGroup? compositeBindGroup = null;
            GraphicsRenderPipeline? extractPipeline = null;
            GraphicsRenderPipeline? blurPipeline = null;
            GraphicsRenderPipeline? horizontalBlurPipeline = null;
            GraphicsRenderPipeline? compositePipeline = null;
            try
            {
                shader = device.CreateShaderModule(
                    new GraphicsShaderModuleDescriptor(code, "scene HDR bloom shader"));
                layout = device.CreateBindGroupLayout(new GraphicsBindGroupLayoutDescriptor(
                    [
                        new GraphicsBindGroupLayoutEntry(
                            0, GraphicsShaderStage.Fragment, GraphicsSamplerBindingType.Filtering),
                        new GraphicsBindGroupLayoutEntry(
                            1,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            2,
                            GraphicsShaderStage.Fragment,
                            GraphicsTextureSampleType.Float,
                            GraphicsTextureViewDimension.TwoD),
                        new GraphicsBindGroupLayoutEntry(
                            3,
                            GraphicsShaderStage.Fragment,
                            GraphicsBufferBindingType.Uniform,
                            SettingsSize),
                    ],
                    "scene HDR bloom bind-group layout"));
                pipelineLayout = device.CreatePipelineLayout(new GraphicsPipelineLayoutDescriptor(
                    [layout],
                    "scene HDR bloom pipeline layout"));
                sampler = device.CreateSampler(new GraphicsSamplerDescriptor(
                    magFilter: GraphicsFilterMode.Linear,
                    minFilter: GraphicsFilterMode.Linear,
                    label: "scene HDR bloom linear clamp sampler"));
                settingsBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
                    SettingsSize,
                    GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination,
                    "scene HDR bloom settings"));
                sceneTexture = device.CreateTexture(new GraphicsTextureDescriptor(
                    extent,
                    presentationFormat,
                    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
                    label: "scene HDR bloom source"));
                sceneView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    sceneTexture,
                    label: "scene HDR bloom source view"));
                GraphicsExtent3D blurExtent = new(
                    Math.Max(1u, checked((extent.Width + downsampleFactor - 1) / downsampleFactor)),
                    Math.Max(1u, checked((extent.Height + downsampleFactor - 1) / downsampleFactor)));
                blurA = device.CreateTexture(new GraphicsTextureDescriptor(
                    blurExtent,
                    GraphicsTextureFormat.Rgba16Float,
                    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
                    label: "scene HDR bloom horizontal texture"));
                blurViewA = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    blurA,
                    label: "scene HDR bloom horizontal view"));
                blurB = device.CreateTexture(new GraphicsTextureDescriptor(
                    blurExtent,
                    GraphicsTextureFormat.Rgba16Float,
                    GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
                    label: "scene HDR bloom vertical texture"));
                blurViewB = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                    blurB,
                    label: "scene HDR bloom vertical view"));
                extractBindGroup = CreateBindGroup(
                    device, layout, sampler, sceneView, sceneView, settingsBuffer, "scene bloom extract bind group");
                blurBindGroup = CreateBindGroup(
                    device, layout, sampler, blurViewA, blurViewA, settingsBuffer, "scene bloom blur bind group");
                horizontalBlurBindGroup = CreateBindGroup(
                    device, layout, sampler, blurViewB, blurViewB, settingsBuffer, "scene bloom iterative horizontal bind group");
                compositeBindGroup = CreateBindGroup(
                    device, layout, sampler, sceneView, blurViewA, settingsBuffer, "scene bloom composite bind group");
                extractPipeline = CreatePipeline(
                    device,
                    shader,
                    pipelineLayout,
                    GraphicsTextureFormat.Rgba16Float,
                    "fs_extract_horizontal",
                    "scene bloom highlight extraction pipeline");
                blurPipeline = CreatePipeline(
                    device,
                    shader,
                    pipelineLayout,
                    GraphicsTextureFormat.Rgba16Float,
                    "fs_blur_vertical",
                    "scene bloom vertical blur pipeline");
                horizontalBlurPipeline = CreatePipeline(
                    device,
                    shader,
                    pipelineLayout,
                    GraphicsTextureFormat.Rgba16Float,
                    "fs_blur_horizontal",
                    "scene bloom iterative horizontal blur pipeline");
                compositePipeline = CreatePipeline(
                    device,
                    shader,
                    pipelineLayout,
                    presentationFormat,
                    "fs_composite",
                    "scene bloom HDR composite pipeline");
                BloomResources result = new(
                    device,
                    extent,
                    downsampleFactor,
                    shader,
                    layout,
                    pipelineLayout,
                    sampler,
                    settingsBuffer,
                    sceneTexture,
                    sceneView,
                    blurA,
                    blurViewA,
                    blurB,
                    blurViewB,
                    extractBindGroup,
                    blurBindGroup,
                    horizontalBlurBindGroup,
                    compositeBindGroup,
                    extractPipeline,
                    blurPipeline,
                    horizontalBlurPipeline,
                    compositePipeline);
                shader = null;
                layout = null;
                pipelineLayout = null;
                sampler = null;
                settingsBuffer = null;
                sceneTexture = null;
                sceneView = null;
                blurA = null;
                blurViewA = null;
                blurB = null;
                blurViewB = null;
                extractBindGroup = null;
                blurBindGroup = null;
                horizontalBlurBindGroup = null;
                compositeBindGroup = null;
                extractPipeline = null;
                blurPipeline = null;
                horizontalBlurPipeline = null;
                compositePipeline = null;
                return result;
            }
            finally
            {
                compositePipeline?.Dispose();
                horizontalBlurPipeline?.Dispose();
                blurPipeline?.Dispose();
                extractPipeline?.Dispose();
                compositeBindGroup?.Dispose();
                horizontalBlurBindGroup?.Dispose();
                blurBindGroup?.Dispose();
                extractBindGroup?.Dispose();
                blurViewB?.Dispose();
                blurB?.Dispose();
                blurViewA?.Dispose();
                blurA?.Dispose();
                sceneView?.Dispose();
                sceneTexture?.Dispose();
                settingsBuffer?.Dispose();
                sampler?.Dispose();
                pipelineLayout?.Dispose();
                layout?.Dispose();
                shader?.Dispose();
            }
        }

        internal static uint SelectDownsampleFactor(float radiusPixels) => radiusPixels switch
        {
            <= 32f => 2u,
            <= 64f => 4u,
            _ => 8u,
        };

        public void Dispose()
        {
            compositePipeline.Dispose();
            horizontalBlurPipeline.Dispose();
            blurPipeline.Dispose();
            extractPipeline.Dispose();
            compositeBindGroup.Dispose();
            horizontalBlurBindGroup.Dispose();
            blurBindGroup.Dispose();
            extractBindGroup.Dispose();
            blurViewB.Dispose();
            blurTextureB.Dispose();
            blurViewA.Dispose();
            blurTextureA.Dispose();
            sceneView.Dispose();
            SceneTexture.Dispose();
            settingsBuffer.Dispose();
            sampler.Dispose();
            pipelineLayout.Dispose();
            bindGroupLayout.Dispose();
            shader.Dispose();
        }

        private static GraphicsBindGroup CreateBindGroup(
            GraphicsDevice device,
            GraphicsBindGroupLayout layout,
            GraphicsSampler sampler,
            GraphicsTextureView primary,
            GraphicsTextureView secondary,
            GraphicsBuffer settings,
            string label) =>
            device.CreateBindGroup(new GraphicsBindGroupDescriptor(
                layout,
                [
                    new GraphicsBindGroupEntry(0, sampler),
                    new GraphicsBindGroupEntry(1, primary),
                    new GraphicsBindGroupEntry(2, secondary),
                    new GraphicsBindGroupEntry(3, settings, 0, SettingsSize),
                ],
                label));

        private static GraphicsRenderPipeline CreatePipeline(
            GraphicsDevice device,
            GraphicsShaderModule shader,
            GraphicsPipelineLayout layout,
            GraphicsTextureFormat format,
            string fragmentEntryPoint,
            string label) =>
            device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                shader,
                "vs_fullscreen",
                shader,
                fragmentEntryPoint,
                format,
                label: label,
                layout: layout));
    }

    private sealed class MeshResources : IDisposable
    {
        private MeshResources(
            GraphicsBuffer uniformBuffer,
            GraphicsBuffer skinBuffer,
            GraphicsBindGroup bindGroup,
            GeometryResources geometry,
            GraphicsBuffer morphBuffer,
            IReadOnlyList<MaterialPhysicalTextureBinding> textureBindings,
            Material material,
            ulong materialTextureBindingRevision,
            MaterialShaderVariant shaderKey,
            Skin? skin)
        {
            UniformBuffer = uniformBuffer;
            SkinBuffer = skinBuffer;
            BindGroup = bindGroup;
            Geometry = geometry;
            MorphBuffer = morphBuffer;
            TextureBindings = [.. textureBindings];
            Material = material;
            MaterialTextureBindingRevision = materialTextureBindingRevision;
            ShaderKey = shaderKey;
            Skin = skin;
            SkinPaletteValues = new float[checked((skin?.Joints.Count ?? 1) * 32)];
        }

        internal GraphicsBuffer UniformBuffer { get; }

        internal GraphicsBuffer SkinBuffer { get; }

        internal GraphicsBindGroup BindGroup { get; }

        internal GeometryResources Geometry { get; private set; }

        internal GraphicsBuffer MorphBuffer { get; }

        internal IReadOnlyList<MaterialPhysicalTextureBinding> TextureBindings { get; }

        internal Material Material { get; private set; }

        internal ulong MaterialTextureBindingRevision { get; private set; }

        internal MaterialShaderVariant ShaderKey { get; private set; }


        internal Skin? Skin { get; }

        internal float[] SkinPaletteValues { get; }

        internal bool SkinPaletteInitialized { get; set; }

        internal bool Matches(
            Material material,
            ulong materialTextureBindingRevision,
            MaterialShaderVariant shaderKey,
            MeshGeometry geometry,
            Skin? skin) =>
            ReferenceEquals(Material, material) &&
            MaterialTextureBindingRevision == materialTextureBindingRevision &&
            ShaderKey == shaderKey &&
            ReferenceEquals(Geometry.Source, geometry) &&
            ReferenceEquals(Skin, skin);

        internal bool Matches(IReadOnlyList<MaterialPhysicalTextureBinding> textureBindings)
        {
            if (textureBindings.Count != TextureBindings.Count)
            {
                return false;
            }
            for (int index = 0; index < textureBindings.Count; index++)
            {
                if (!ReferenceEquals(TextureBindings[index].Sampler, textureBindings[index].Sampler) ||
                    !ReferenceEquals(TextureBindings[index].Resource, textureBindings[index].Resource))
                {
                    return false;
                }
            }
            return true;
        }

        internal void UpdateIdentity(
            GeometryResources geometry,
            Material material,
            ulong materialTextureBindingRevision,
            MaterialShaderVariant shaderKey)
        {
            Geometry = geometry;
            Material = material;
            MaterialTextureBindingRevision = materialTextureBindingRevision;
            ShaderKey = shaderKey;
        }

        internal static MeshResources Create(
            GraphicsDevice device,
            GraphicsBindGroupLayout layout,
            GeometryResources geometry,
            IReadOnlyList<MaterialPhysicalTextureBinding> textureBindings,
            Material material,
            ulong materialTextureBindingRevision,
            MaterialShaderVariant shaderKey,
            Skin? skin)
        {
            if (textureBindings.Count != MaterialTextureBindingPlan.PhysicalSlotCount)
            {
                throw new ArgumentException("Exactly ten physical material texture slots are required.",
                    nameof(textureBindings));
            }
            GraphicsBuffer? uniformBuffer = null;
            GraphicsBuffer? skinBuffer = null;
            GraphicsBindGroup? bindGroup = null;
            try
            {
                uniformBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
                    DrawUniformSize,
                    GraphicsBufferUsage.Uniform | GraphicsBufferUsage.CopyDestination,
                    "scene mesh draw uniform"));
                ulong skinBufferSize = checked((ulong)(skin?.Joints.Count ?? 1) * 128);
                skinBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
                    skinBufferSize,
                    GraphicsBufferUsage.Storage | GraphicsBufferUsage.CopyDestination,
                    "scene mesh FP32 skin palette"));
                bindGroup = device.CreateBindGroup(new GraphicsBindGroupDescriptor(
                    layout,
                    [
                        new GraphicsBindGroupEntry(0, uniformBuffer, 0, DrawUniformSize),
                        new GraphicsBindGroupEntry(1, textureBindings[0].Sampler),
                        new GraphicsBindGroupEntry(2, textureBindings[0].View),
                        new GraphicsBindGroupEntry(3, textureBindings[1].Sampler),
                        new GraphicsBindGroupEntry(4, textureBindings[1].View),
                        new GraphicsBindGroupEntry(5, textureBindings[2].Sampler),
                        new GraphicsBindGroupEntry(6, textureBindings[2].View),
                        new GraphicsBindGroupEntry(7, textureBindings[3].Sampler),
                        new GraphicsBindGroupEntry(8, textureBindings[3].View),
                        new GraphicsBindGroupEntry(9, skinBuffer, 0, skinBufferSize),
                        new GraphicsBindGroupEntry(
                            10,
                            geometry.MorphBuffer,
                            0,
                            geometry.MorphBuffer.Size),
                        new GraphicsBindGroupEntry(11, textureBindings[4].Sampler),
                        new GraphicsBindGroupEntry(12, textureBindings[4].View),
                        new GraphicsBindGroupEntry(13, textureBindings[5].Sampler),
                        new GraphicsBindGroupEntry(14, textureBindings[5].View),
                        new GraphicsBindGroupEntry(15, textureBindings[6].Sampler),
                        new GraphicsBindGroupEntry(16, textureBindings[6].View),
                        new GraphicsBindGroupEntry(17, textureBindings[7].Sampler),
                        new GraphicsBindGroupEntry(18, textureBindings[7].View),
                        new GraphicsBindGroupEntry(19, textureBindings[8].Sampler),
                        new GraphicsBindGroupEntry(20, textureBindings[8].View),
                        new GraphicsBindGroupEntry(21, textureBindings[9].Sampler),
                        new GraphicsBindGroupEntry(22, textureBindings[9].View),
                    ],
                    "scene mesh bind group"));
                MeshResources result = new(
                    uniformBuffer,
                    skinBuffer,
                    bindGroup,
                    geometry,
                    geometry.MorphBuffer,
                    textureBindings,
                    material,
                    materialTextureBindingRevision,
                    shaderKey,
                    skin);
                uniformBuffer = null;
                skinBuffer = null;
                bindGroup = null;
                return result;
            }
            finally
            {
                bindGroup?.Dispose();
                skinBuffer?.Dispose();
                uniformBuffer?.Dispose();
            }
        }

        public void Dispose()
        {
            BindGroup.Dispose();
            SkinBuffer.Dispose();
            UniformBuffer.Dispose();
        }
    }

    private sealed class GeometryResources : IDisposable
    {
        private GeometryResources(
            MeshGeometry source,
            GraphicsBuffer vertexBuffer,
            GraphicsBuffer indexBuffer,
            GraphicsBuffer morphBuffer,
            GraphicsIndexFormat indexFormat,
            uint indexCount)
        {
            Source = source;
            VertexBuffer = vertexBuffer;
            IndexBuffer = indexBuffer;
            MorphBuffer = morphBuffer;
            IndexFormat = indexFormat;
            IndexCount = indexCount;
        }

        internal MeshGeometry Source { get; }

        internal GraphicsBuffer VertexBuffer { get; }

        internal GraphicsBuffer IndexBuffer { get; }

        internal GraphicsBuffer MorphBuffer { get; }

        internal GraphicsIndexFormat IndexFormat { get; }

        internal uint IndexCount { get; }

        internal static GeometryResources Create(GraphicsDevice device, MeshGeometry geometry)
        {
            float[] vertices = new float[checked(geometry.Positions.Count * 25)];
            for (int index = 0; index < geometry.Positions.Count; index++)
            {
                Vector3 position = geometry.Positions[index];
                Vector3 normal = geometry.Normals.Count == 0 ? Vector3.UnitZ : geometry.Normals[index];
                Vector3 bentNormal = geometry.BentNormals.Count == 0
                    ? normal
                    : geometry.BentNormals[index];
                Vector2 textureCoordinate = geometry.TextureCoordinates.Count == 0
                    ? Vector2.Zero
                    : geometry.TextureCoordinates[index];
                Vector2 textureCoordinate1 = geometry.TextureCoordinates1.Count == 0
                    ? Vector2.Zero
                    : geometry.TextureCoordinates1[index];
                JointIndices4 jointIndices = geometry.JointIndices.Count == 0
                    ? new JointIndices4(0)
                    : geometry.JointIndices[index];
                Vector4 jointWeights = geometry.JointWeights.Count == 0
                    ? new Vector4(1f, 0f, 0f, 0f)
                    : geometry.JointWeights[index];
                Vector4 tangent = geometry.Tangents.Count == 0
                    ? Vector4.Zero
                    : geometry.Tangents[index];
                int vertex = index * 25;
                vertices[vertex] = position.X;
                vertices[vertex + 1] = position.Y;
                vertices[vertex + 2] = position.Z;
                vertices[vertex + 3] = normal.X;
                vertices[vertex + 4] = normal.Y;
                vertices[vertex + 5] = normal.Z;
                vertices[vertex + 6] = bentNormal.X;
                vertices[vertex + 7] = bentNormal.Y;
                vertices[vertex + 8] = bentNormal.Z;
                vertices[vertex + 9] = textureCoordinate.X;
                vertices[vertex + 10] = textureCoordinate.Y;
                vertices[vertex + 11] = jointIndices.X;
                vertices[vertex + 12] = jointIndices.Y;
                vertices[vertex + 13] = jointIndices.Z;
                vertices[vertex + 14] = jointIndices.W;
                vertices[vertex + 15] = jointWeights.X;
                vertices[vertex + 16] = jointWeights.Y;
                vertices[vertex + 17] = jointWeights.Z;
                vertices[vertex + 18] = jointWeights.W;
                vertices[vertex + 19] = tangent.X;
                vertices[vertex + 20] = tangent.Y;
                vertices[vertex + 21] = tangent.Z;
                vertices[vertex + 22] = tangent.W;
                vertices[vertex + 23] = textureCoordinate1.X;
                vertices[vertex + 24] = textureCoordinate1.Y;
            }

            GraphicsIndexFormat indexFormat;
            byte[] indexBytes;
            uint maximumIndex = geometry.Indices.Max();
            if (maximumIndex <= ushort.MaxValue)
            {
                indexFormat = GraphicsIndexFormat.Uint16;
                ushort[] indices = new ushort[(geometry.Indices.Count + 1) & ~1];
                for (int index = 0; index < geometry.Indices.Count; index++)
                {
                    indices[index] = checked((ushort)geometry.Indices[index]);
                }
                indexBytes = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray();
            }
            else
            {
                indexFormat = GraphicsIndexFormat.Uint32;
                uint[] indices = [.. geometry.Indices];
                indexBytes = MemoryMarshal.AsBytes(indices.AsSpan()).ToArray();
            }

            GraphicsBuffer? vertexBuffer = null;
            GraphicsBuffer? indexBuffer = null;
            GraphicsBuffer? morphBuffer = null;
            try
            {
                ReadOnlySpan<byte> vertexBytes = MemoryMarshal.AsBytes(vertices.AsSpan());
                vertexBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
                    checked((ulong)vertexBytes.Length),
                    GraphicsBufferUsage.Vertex | GraphicsBufferUsage.CopyDestination,
                    "scene geometry positions, normals, bent normals, UVs, joints, weights, and tangents"));
                indexBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
                    checked((ulong)indexBytes.Length),
                    GraphicsBufferUsage.Index | GraphicsBufferUsage.CopyDestination,
                    "scene geometry indices"));
                float[] morphValues = CreateMorphValues(geometry);
                ReadOnlySpan<byte> morphBytes = MemoryMarshal.AsBytes(morphValues.AsSpan());
                morphBuffer = device.CreateBuffer(new GraphicsBufferDescriptor(
                    checked((ulong)morphBytes.Length),
                    GraphicsBufferUsage.Storage | GraphicsBufferUsage.CopyDestination,
                    "scene geometry FP32 morph deltas"));
                device.Queue.WriteBuffer(vertexBuffer, 0, vertexBytes);
                device.Queue.WriteBuffer(indexBuffer, 0, indexBytes);
                device.Queue.WriteBuffer(morphBuffer, 0, morphBytes);
                GeometryResources result = new(
                    geometry,
                    vertexBuffer,
                    indexBuffer,
                    morphBuffer,
                    indexFormat,
                    checked((uint)geometry.Indices.Count));
                vertexBuffer = null;
                indexBuffer = null;
                morphBuffer = null;
                return result;
            }
            finally
            {
                morphBuffer?.Dispose();
                indexBuffer?.Dispose();
                vertexBuffer?.Dispose();
            }
        }

        public void Dispose()
        {
            MorphBuffer.Dispose();
            IndexBuffer.Dispose();
            VertexBuffer.Dispose();
        }

        private static float[] CreateMorphValues(MeshGeometry geometry)
        {
            if (geometry.MorphTargets.Count == 0)
            {
                return new float[12];
            }
            float[] values = new float[checked(geometry.MorphTargets.Count * geometry.Positions.Count * 12)];
            for (int targetIndex = 0; targetIndex < geometry.MorphTargets.Count; targetIndex++)
            {
                MorphTarget target = geometry.MorphTargets[targetIndex];
                for (int vertexIndex = 0; vertexIndex < geometry.Positions.Count; vertexIndex++)
                {
                    Vector3 position = target.PositionDeltas[vertexIndex];
                    Vector3 normal = target.NormalDeltas.Count == 0
                        ? Vector3.Zero
                        : target.NormalDeltas[vertexIndex];
                    Vector3 tangent = target.TangentDeltas.Count == 0
                        ? Vector3.Zero
                        : target.TangentDeltas[vertexIndex];
                    int offset = ((targetIndex * geometry.Positions.Count) + vertexIndex) * 12;
                    values[offset] = position.X;
                    values[offset + 1] = position.Y;
                    values[offset + 2] = position.Z;
                    values[offset + 4] = normal.X;
                    values[offset + 5] = normal.Y;
                    values[offset + 6] = normal.Z;
                    values[offset + 8] = tangent.X;
                    values[offset + 9] = tangent.Y;
                    values[offset + 10] = tangent.Z;
                }
            }
            return values;
        }
    }
}

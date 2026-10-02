using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

/// <summary>Renders OpenPBR surfaces with explicit raster, hybrid, interactive or reference transport.</summary>
/// <remarks>
/// All modes evaluate the pinned Adobe OpenPBR BSDF. Reference and Hybrid retain FP32 accumulation;
/// interactive mode starts an independent estimate each frame. The application owns scheduling,
/// presentation and this disposable pass. No tone mapping, display encoding or denoising is implicit.
/// Surfaces accept constants and bounded typed texture graphs. Transport uses fixed RGB wavelengths, a finite bounce
/// budget and up to eight nested, consistently oriented closed media. Only perspective cameras are
/// supported. A scene may contain OpenPBR meshes, punctual lights and one HDR environment light.
/// Ray transport starts at the camera origin so near-plane clipping does not skip medium boundaries;
/// the primary ray ends at the camera's far plane.
/// Raster uses ordinary near/far clipping, punctual lights with optional directional shadows and fixed BSDF environment
/// quadrature; it rejects transmission, subsurface scattering and partial opacity. Hybrid uses
/// raster pixel-center visibility with bounded secondary transport and may still show sampling noise.
/// Fast is the one explicitly approximate mode: pinned-closure direct lighting, split-sum
/// image-based lighting and graph inputs baked to hardware-filtered mipmapped textures, with every
/// lobe approximation reported through <see cref="FastApproximations"/> and transmission/partial
/// opacity still rejected.
/// Instances are not thread-safe. See the package README for upstream approximation boundaries.
/// </remarks>
public sealed class OpenPbrRenderPass : IRenderPass, IDisposable
{
    private readonly object preparationGate = new();
    private OpenPbrGpuGraph? graph;
    private OpenPbrGpuResources? gpu;
    private OpenPbrSceneSnapshot? snapshot;
    private OpenPbrFastBake? fastBake;
    private Vector4[]? previousParameters;
    private Vector4[]? previousMaterials;
    private Vector4[]? previousLights;
    private EquirectangularHdrEnvironment? previousEnvironment;
    private OpenPbrSceneSnapshot? mediumSnapshot;
    private OpenPbrGpuGraph? mediumGraph;
    private Vector4[]? mediumMaterials;
    private Vector3 mediumOrigin;
    private Vector4[] initialMedia = [];
    private bool sceneUploadPending;
    private int compiledTriangleBudget;
    private long compiledMemoryBudget;
    private bool disposed;
    private uint sequence;

    /// <summary>Creates a pass whose output uses extended linear sRGB.</summary>
    public OpenPbrRenderPass() : this(StandardColorSpaces.LinearSrgb) { }

    /// <summary>Creates a pass with an explicit standard linear RGB output space.</summary>
    /// <param name="workingColorSpace">The output space shared with the surrounding render pipeline.</param>
    public OpenPbrRenderPass(StandardRgbColorSpaceReference workingColorSpace)
    {
        ArgumentNullException.ThrowIfNull(workingColorSpace);
        Descriptor = new("OpenPBR", workingColorSpace, new RenderPassColorAttachmentPolicy(
            GraphicsLoadOperation.Clear, new LinearRgba(0, 0, 0, 0, workingColorSpace)));
        Pipeline = new RenderPassPipeline([this]);
    }

    /// <inheritdoc />
    public RenderPassDescriptor Descriptor { get; }
    /// <summary>Gets a reusable single-pass pipeline for a scene view.</summary>
    public RenderPassPipeline Pipeline { get; }
    /// <summary>Gets or sets the rendering mode. Changing mode resets history on the next frame.</summary>
    public OpenPbrRenderMode Mode { get; set; } = OpenPbrRenderMode.Interactive;
    /// <summary>Gets or sets the interactive path-event budget, from one to 128.</summary>
    public int InteractiveMaxBounces { get; set; } = 4;
    /// <summary>Gets or sets the reference path-event budget, from one to 128.</summary>
    public int ReferenceMaxBounces { get; set; } = 32;
    /// <summary>Gets or sets the hybrid path-event budget including the primary surface, from one to 128.</summary>
    public int HybridMaxBounces { get; set; } = 4;
    /// <summary>Gets or sets deterministic BSDF environment samples per raster pixel, from one to 64.</summary>
    /// <remarks>Default is 16. Fixed quadrature avoids temporal noise but can miss small bright environment features.</remarks>
    public int RasterEnvironmentSamples { get; set; } = 16;
    /// <summary>Gets or sets directional shadow-map evaluation in Raster/Fast. Default true; the light must also opt in with CastsShadows.</summary>
    /// <remarks>Supports one shadow-casting directional light. Other lights and environment lighting remain unshadowed.
    /// Hybrid, Interactive and Reference retain their independent ray visibility. Maps cover scene bounds, without cascades.</remarks>
    public bool DirectionalShadowsEnabled { get; set; } = true;
    /// <summary>Gets or sets the square directional depth-map resolution, a power of two from 64 to 4096. Default 1024.</summary>
    public uint DirectionalShadowMapSize { get; set; } = 1024;
    /// <summary>Gets or sets the nonnegative receiver depth offset in normalized shadow depth, at most one. Default 0.001.</summary>
    public float DirectionalShadowDepthBias { get; set; } = .001f;
    /// <summary>Gets or sets the finite nonnegative, slope-scaled geometric-normal offset in scene units. Default 0.002.</summary>
    public float DirectionalShadowNormalBias { get; set; } = .002f;
    /// <summary>Gets or sets the PCF radius in texels, from zero (hard) to two (5x5). Default one (3x3).</summary>
    /// <remarks>This filter softens map edges; it does not model distance-dependent area-light penumbrae.</remarks>
    public int DirectionalShadowPcfRadius { get; set; } = 1;
    /// <summary>Gets or sets the interactive linear resolution fraction, in (0, 1]. Reference always uses full resolution.</summary>
    public float InteractiveResolutionScale { get; set; } = 0.5f;
    /// <summary>Gets or sets physical metres represented by one scene-coordinate unit.</summary>
    public float MetersPerSceneUnit { get; set; } = 1;
    /// <summary>Gets or sets the maximum source triangle count, validated before compilation.</summary>
    public int MaximumTriangles { get; set; } = 500_000;
    /// <summary>Gets or sets the scene compilation memory budget in bytes.</summary>
    public long MaximumSceneBytes { get; set; } = 512L * 1024 * 1024;
    /// <summary>Gets or sets the attachment budget in bytes: FP32 histories, raster visibility/depth and the active directional shadow map.</summary>
    public long MaximumHistoryBytes { get; set; } = 512L * 1024 * 1024;
    /// <summary>Gets or sets the combined graph and FP32 texture budget, default 64 MiB.</summary>
    public long MaximumTextureBytes { get; set; } = 64L * 1024 * 1024;
    /// <summary>Gets or sets a constant environment radiance added to image-based lighting.</summary>
    public LinearRgba EnvironmentRadiance { get; set; } = new(0, 0, 0, 1, StandardColorSpaces.AcesCg);
    /// <summary>Gets or sets primary-ray background coverage in [0, 1]; indirect illumination remains independent.</summary>
    public float BackgroundAlpha { get; set; }
    /// <summary>Gets or sets a reproducible seed from zero to 16,777,215.</summary>
    public uint Seed { get; set; } = 1;
    /// <summary>Gets the reference samples per pixel accumulated since the latest reset.</summary>
    public long ReferenceSamples => Mode == OpenPbrRenderMode.Reference ? AccumulatedSamples : 0;
    /// <summary>Gets retained samples per pixel in Reference or Hybrid since the latest reset; otherwise zero.</summary>
    public long AccumulatedSamples { get; private set; }
    /// <summary>Gets whether the most recently executed Hybrid frame required primary ray traversal.</summary>
    /// <remarks>Conservatively enabled when the camera is in a medium or scene bounds reach before its near plane.
    /// This preserves pre-near-plane medium boundaries. Other modes report false.</remarks>
    public bool UsesPrimaryRayFallback { get; private set; }
    /// <summary>Gets the per-surface approximations and baked-texture footprint of the compiled Fast scene.</summary>
    /// <remarks>Empty unless <see cref="Mode"/> is <see cref="OpenPbrRenderMode.Fast"/>; updated when the scene or a
    /// material graph recompiles. Reference modes never approximate and never report entries here.</remarks>
    public IReadOnlyList<OpenPbrFastApproximation> FastApproximations { get; private set; } = [];

    /// <summary>Discards history on the next execution without releasing reusable GPU resources.</summary>
    public void ResetAccumulation() => AccumulatedSamples = 0;

    /// <inheritdoc />
    public void Execute(RenderPassContext context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (context.Camera is not PerspectiveCamera camera)
            throw new NotSupportedException("OpenPBR currently requires a perspective camera for consistent initial media.");
        if (context.WorkingColorSpace != Descriptor.WorkingColorSpace)
            throw new ArgumentException("OpenPBR and pipeline working spaces must agree.", nameof(context));
        if (context.ColorTarget.Descriptor.Format is not (GraphicsTextureFormat.Rgba16Float or GraphicsTextureFormat.Rgba32Float))
            throw new NotSupportedException("OpenPBR requires an HDR Float16 or Float32 output attachment.");
        ValidateSettings();
        float scale = Mode == OpenPbrRenderMode.Interactive ? InteractiveResolutionScale : 1;
        uint width = Math.Max(1, (uint)MathF.Ceiling(context.OutputExtent.Width * scale));
        uint height = Math.Max(1, (uint)MathF.Ceiling(context.OutputExtent.Height * scale));
        bool fast = Mode == OpenPbrRenderMode.Fast;
        if (!fast)
        {
            fastBake = null;
            FastApproximations = [];
        }
        bool raster = Mode is OpenPbrRenderMode.Raster or OpenPbrRenderMode.Hybrid || fast;
        bool accumulate = Mode is OpenPbrRenderMode.Reference or OpenPbrRenderMode.Hybrid;
        if (checked((long)width * height * (raster ? 52 : 32)) > MaximumHistoryBytes)
            throw new InvalidOperationException("OpenPBR history exceeds MaximumHistoryBytes.");
        bool geometryChanged = snapshot is null || !snapshot.IsCurrent(context.Scene, camera.VisibilityMask) ||
            compiledTriangleBudget != MaximumTriangles || compiledMemoryBudget != MaximumSceneBytes;
        if (geometryChanged)
        {
            snapshot = OpenPbrSceneCompiler.Compile(context.Scene, camera.VisibilityMask, MaximumTriangles, MaximumSceneBytes);
            compiledTriangleBudget = MaximumTriangles; compiledMemoryBudget = MaximumSceneBytes;
            sceneUploadPending = true;
            ResetAccumulation();
        }
        bool graphChanged = graph is null || !graph.IsCurrent(snapshot!.SourceMaterials, MetersPerSceneUnit, MaximumTextureBytes);
        if (graphChanged)
        {
            graph = new(snapshot!.SourceMaterials, MetersPerSceneUnit, MaximumTextureBytes);
            sceneUploadPending = true;
        }
        if (graphChanged || geometryChanged || sceneUploadPending)
            foreach (Mesh mesh in context.Scene.EnumerateVisible(camera.VisibilityMask).OfType<Mesh>())
                if (mesh.Material is OpenPbrMaterial { Surface.Graph: { } expressions })
                    foreach (var node in expressions.Nodes.Where(n => n.Operation == OpenPbrNodeOperation.Texcoord))
                        if ((node.Value.X == 0 ? mesh.Geometry.TextureCoordinates : mesh.Geometry.TextureCoordinates1).Count == 0)
                            throw new NotSupportedException($"Mesh '{mesh.Name}' has no UV{node.Value.X} required by its OpenPBR graph.");
        Vector4[] materials = snapshot!.SourceMaterials.SelectMany(m => OpenPbrGpuMaterial.Pack(m, MetersPerSceneUnit)).ToArray();
        graph!.SetHeaders(materials);
        if (Mode == OpenPbrRenderMode.Raster && snapshot.SourceMaterials.Any(m =>
            (m.Surface.Graph?.Maximum(OpenPbrInput.TransmissionWeight, m.Surface.TransmissionWeight) ?? m.Surface.TransmissionWeight) > 0 ||
            (m.Surface.Graph?.Maximum(OpenPbrInput.SubsurfaceWeight, m.Surface.SubsurfaceWeight) ?? m.Surface.SubsurfaceWeight) > 0 ||
            (m.Surface.Graph?.Minimum(OpenPbrInput.GeometryOpacity, m.Surface.GeometryOpacity) ?? m.Surface.GeometryOpacity) < 1))
            throw new NotSupportedException("OpenPBR Raster requires opacity 1, transmission 0 and subsurface 0. Select Hybrid or a path-tracing mode for these surfaces.");
        if (fast && snapshot.SourceMaterials.Any(m =>
            (m.Surface.Graph?.Maximum(OpenPbrInput.TransmissionWeight, m.Surface.TransmissionWeight) ?? m.Surface.TransmissionWeight) > 0 ||
            (m.Surface.Graph?.Minimum(OpenPbrInput.GeometryOpacity, m.Surface.GeometryOpacity) ?? m.Surface.GeometryOpacity) < 1))
            throw new NotSupportedException("OpenPBR Fast requires opacity 1 and transmission 0. Select Hybrid or a path-tracing mode for these surfaces.");
        if (fast)
        {
            if (fastBake is null || geometryChanged || graphChanged)
            {
                // Graphs and textures are immutable, so identity change is the only invalidation.
                fastBake = OpenPbrFastBake.Bake(snapshot.SourceMaterials, MaximumTextureBytes);
                sceneUploadPending = true;
            }
            else
            {
                fastBake.RefreshReports(snapshot.SourceMaterials);
            }
            FastApproximations = fastBake.Approximations;
            fastBake.ApplyFolds(materials);
        }
        SceneNode[] visible = context.Scene.EnumerateVisible(camera.VisibilityMask).ToArray();
        PunctualLight[] sceneLights = visible.OfType<PunctualLight>().ToArray();
        Vector4[] lights = sceneLights.SelectMany(PackLight).ToArray();
        Vector4[] shadowFrame = OpenPbrDirectionalShadow.CreateFrame(snapshot, sceneLights, this);
        long shadowBytes = shadowFrame[4].X >= 0 ? checked((long)DirectionalShadowMapSize * DirectionalShadowMapSize * 4) : 0;
        if (checked((long)width * height * (raster ? 52 : 32) + shadowBytes) > MaximumHistoryBytes)
            throw new InvalidOperationException("OpenPBR attachments including the directional shadow map exceed MaximumHistoryBytes.");
        ImageBasedLight[] environments = visible.OfType<ImageBasedLight>().ToArray();
        if (environments.Length > 1) throw new NotSupportedException("OpenPBR currently supports one image-based environment per scene.");
        ImageBasedLight? environment = environments.FirstOrDefault();
        Vector4[] frame = CreateFrame(context, camera, width, height, lights.Length / 4, environment);
        Vector3 origin = new(frame[0].X, frame[0].Y, frame[0].Z);
        if (Mode is not (OpenPbrRenderMode.Raster or OpenPbrRenderMode.Fast) &&
            (!ReferenceEquals(mediumGraph, graph) || !ReferenceEquals(mediumSnapshot, snapshot) || mediumOrigin != origin || !Equal(materials, mediumMaterials)))
        {
            Vector4[] resolved = OpenPbrInitialMedia.Resolve(snapshot, origin);
            initialMedia = resolved; mediumGraph = graph; mediumSnapshot = snapshot; mediumOrigin = origin; mediumMaterials = materials;
        }
        frame[15].Y = Mode is OpenPbrRenderMode.Raster or OpenPbrRenderMode.Fast ? 0 : initialMedia.Length;
        UsesPrimaryRayFallback = Mode == OpenPbrRenderMode.Hybrid &&
            (initialMedia.Length > 0 || BoundsReachNearPlane(snapshot, origin, new(frame[5].X, frame[5].Y, frame[5].Z), camera.NearClip));
        frame[15].Z = UsesPrimaryRayFallback ? 1 : 0;
        bool contentsChanged = graphChanged || sceneUploadPending || !Equal(materials, previousMaterials) || !Equal(lights, previousLights) ||
            !ReferenceEquals(environment?.Environment, previousEnvironment);
        bool recreate = gpu is null || gpu.Device != context.Device ||
            gpu.RasterOnly != (Mode == OpenPbrRenderMode.Raster) ||
            gpu.OutputFormat != context.ColorTarget.Descriptor.Format || gpu.HasRasterVisibility != raster || gpu.Fast != fast;
        if (recreate)
        {
            gpu?.Dispose(); gpu = null;
            gpu = new(context.Device, width, height, context.ColorTarget.Descriptor.Format,
                (StandardRgbColorSpaceReference)Descriptor.WorkingColorSpace, raster, fast, Mode == OpenPbrRenderMode.Raster);
            sceneUploadPending = true;
            contentsChanged = true;
        }
        if (gpu!.Width != width || gpu.Height != height)
        {
            gpu.Resize(width, height);
            sceneUploadPending = true;
            contentsChanged = true;
        }
        if (contentsChanged)
        {
            gpu!.SetScene(snapshot, materials, lights, environment?.Environment, graph, fast ? fastBake : null);
            sceneUploadPending = false;
            previousMaterials = materials; previousLights = lights; previousEnvironment = environment?.Environment;
            ResetAccumulation();
        }
        if (!Equal(frame, previousParameters)) ResetAccumulation();
        previousParameters = (Vector4[])frame.Clone();
        if (accumulate && AccumulatedSamples >= 16_777_215)
        {
            // A new presentation attachment still needs the last completed estimate.
            gpu!.Present(context.ColorTarget);
            return;
        }
        frame[6].Z = accumulate ? AccumulatedSamples : Mode is OpenPbrRenderMode.Raster or OpenPbrRenderMode.Fast ? 0 : sequence++ % 16_777_216;
        gpu!.Draw(frame, initialMedia, context.ColorTarget, shadowFrame, DirectionalShadowMapSize);
        if (accumulate) AccumulatedSamples++;
    }

    /// <summary>Releases this pass's resources. Caller-owned scene, device and attachments remain usable.</summary>
    public void Dispose()
    {
        lock (preparationGate)
        {
        if (disposed) return;
        disposed = true; graph = null; gpu?.Dispose(); gpu = null; snapshot = null; mediumSnapshot = null;
        fastBake = null; FastApproximations = [];
        previousMaterials = null; previousLights = null; mediumMaterials = null; initialMedia = [];
        previousEnvironment = null; previousParameters = null;
        }
    }

    /// <summary>Compiles the selected mode on a worker before the first draw, using minimal temporary attachments.</summary>
    /// <param name="device">Device kept alive by the caller until the returned task completes.</param>
    /// <param name="outputFormat">Actual render target format, including any floating display-view intermediate.</param>
    /// <param name="cancellationToken">Cancels publication; native compilation already in progress must finish before cleanup.</param>
    /// <remarks>Do not call Execute, change settings or prepare again until completion. Disposal may abandon preparation.
    /// Use the automatic MAUI PrepareGraphicsAsync hook to coordinate device lifetime across navigation.</remarks>
    public async Task PrepareAsync(GraphicsDevice device, GraphicsTextureFormat outputFormat,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(device);
        ValidateSettings();
        cancellationToken.ThrowIfCancellationRequested();
        OpenPbrRenderMode mode = Mode;
        bool raster = mode is OpenPbrRenderMode.Raster or OpenPbrRenderMode.Hybrid or OpenPbrRenderMode.Fast;
        OpenPbrGpuResources? prepared = await Task.Run(() => new OpenPbrGpuResources(device, 1, 1,
            outputFormat, (StandardRgbColorSpaceReference)Descriptor.WorkingColorSpace, raster,
            mode == OpenPbrRenderMode.Fast, mode == OpenPbrRenderMode.Raster), cancellationToken);
        try
        {
            lock (preparationGate)
            {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            if (Mode != mode) throw new InvalidOperationException("OpenPBR mode changed during preparation.");
            gpu?.Dispose(); gpu = prepared; prepared = null;
            sceneUploadPending = true;
            ResetAccumulation();
            }
        }
        finally { prepared?.Dispose(); }
    }

    private void ValidateSettings()
    {
        if (!Enum.IsDefined(Mode) || InteractiveMaxBounces is < 1 or > 128 || ReferenceMaxBounces is < 1 or > 128 ||
            HybridMaxBounces is < 1 or > 128 || RasterEnvironmentSamples is < 1 or > 64 ||
            DirectionalShadowMapSize is < 64 or > 4096 || !BitOperations.IsPow2(DirectionalShadowMapSize) ||
            !float.IsFinite(DirectionalShadowDepthBias) || DirectionalShadowDepthBias is < 0 or > 1 ||
            !float.IsFinite(DirectionalShadowNormalBias) || DirectionalShadowNormalBias < 0 || DirectionalShadowPcfRadius is < 0 or > 2 ||
            !float.IsFinite(InteractiveResolutionScale) || InteractiveResolutionScale is <= 0 or > 1 ||
            !float.IsFinite(MetersPerSceneUnit) || MetersPerSceneUnit <= 0 ||
            !float.IsFinite(BackgroundAlpha) || BackgroundAlpha is < 0 or > 1 || Seed > 16_777_215 ||
            MaximumTriangles is < 1 or > 8_388_608 || MaximumSceneBytes <= 0 || MaximumHistoryBytes <= 0 || MaximumTextureBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(Mode), "OpenPBR rendering settings are outside their documented ranges.");
    }

    private Vector4[] CreateFrame(RenderPassContext context, PerspectiveCamera camera, uint width, uint height,
        int lightCount, ImageBasedLight? environment)
    {
        if (!Matrix4x4.Invert(camera.ViewProjectionMatrix, out Matrix4x4 inverse))
            throw new InvalidOperationException("The camera projection must be invertible.");
        Vector4[] frame = new Vector4[20];
        frame[0] = new(Vector3.Transform(Vector3.Zero, camera.WorldMatrix), 1);
        frame[1] = new(inverse.M11, inverse.M12, inverse.M13, inverse.M14);
        frame[2] = new(inverse.M21, inverse.M22, inverse.M23, inverse.M24);
        frame[3] = new(inverse.M31, inverse.M32, inverse.M33, inverse.M34);
        frame[4] = new(inverse.M41, inverse.M42, inverse.M43, inverse.M44);
        Matrix4x4 view = camera.ViewMatrix;
        frame[5] = new(-view.M13, -view.M23, -view.M33, camera.FarClip);
        frame[6] = new(width, height, 0, 0);
        frame[7] = new(snapshot!.TriangleCount, snapshot.NodeCount, lightCount, Mode is OpenPbrRenderMode.Reference or OpenPbrRenderMode.Hybrid ? 1 : 0);
        frame[8] = new(Mode switch { OpenPbrRenderMode.Reference => ReferenceMaxBounces,
            OpenPbrRenderMode.Hybrid => HybridMaxBounces, _ => InteractiveMaxBounces }, 4, (int)Mode, RasterEnvironmentSamples);
        frame[9] = new(Radiance(EnvironmentRadiance), BackgroundAlpha);
        if (environment is not null)
        {
            if (!Matrix4x4.Decompose(environment.WorldMatrix, out _, out Quaternion rotation, out _))
                throw new InvalidOperationException("Environment rotation is not decomposable.");
            Matrix4x4 matrix = Matrix4x4.CreateFromQuaternion(Quaternion.Conjugate(rotation));
            frame[10] = new(matrix.M11, matrix.M21, matrix.M31, 0);
            frame[11] = new(matrix.M12, matrix.M22, matrix.M32, 0);
            frame[12] = new(matrix.M13, matrix.M23, matrix.M33, 0);
            frame[13] = new(environment.Environment.Width, environment.Environment.Height, environment.Intensity, 1);
        }
        frame[14] = new(MetersPerSceneUnit, MaximumTriangles,
            Mode == OpenPbrRenderMode.Fast ? OpenPbrFastEnvironment.SpecularMipCount - 1 : 0, 0);
        frame[15] = new(Seed, 0, 0, 0);
        Matrix4x4 projection = camera.ViewProjectionMatrix;
        frame[16] = new(projection.M11, projection.M12, projection.M13, projection.M14);
        frame[17] = new(projection.M21, projection.M22, projection.M23, projection.M24);
        frame[18] = new(projection.M31, projection.M32, projection.M33, projection.M34);
        frame[19] = new(projection.M41, projection.M42, projection.M43, projection.M44);
        foreach (Vector4 v in frame)
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z) || !float.IsFinite(v.W))
                throw new InvalidOperationException("OpenPBR frame conversion exceeded finite FP32.");
        return frame;
    }

    private static bool BoundsReachNearPlane(OpenPbrSceneSnapshot scene, Vector3 origin, Vector3 forward, float near)
    {
        if (scene.NodeCount == 0) return false;
        Vector4 lo = scene.BvhNodes[0], hi = scene.BvhNodes[1];
        Vector3 nearest = new(forward.X >= 0 ? lo.X : hi.X, forward.Y >= 0 ? lo.Y : hi.Y, forward.Z >= 0 ? lo.Z : hi.Z);
        return Vector3.Dot(nearest - origin, forward) <= near;
    }

    internal static Vector3 Radiance(LinearRgba value)
    {
        LinearRgba c = StandardLinearRgbConverter.Convert(value, StandardColorSpaces.AcesCg);
        if (c.Red < 0 || c.Green < 0 || c.Blue < 0)
            throw new ArgumentOutOfRangeException(nameof(value), "Physical OpenPBR illumination requires nonnegative ACEScg radiance.");
        return new(c.Red, c.Green, c.Blue);
    }

    private static Vector4[] PackLight(PunctualLight light)
    {
        Vector3 color = Radiance(light.Color) * light.Intensity;
        if (!float.IsFinite(color.X) || !float.IsFinite(color.Y) || !float.IsFinite(color.Z))
            throw new InvalidOperationException("Light intensity exceeds finite FP32.");
        return light switch
        {
            DirectionalLight d => [new(-d.WorldDirection, 0), new(color, 0), new(0, 0, 0, MathF.Cos(d.AngularDiameterRadians / 2)), Vector4.Zero],
            PointLight p => [new(p.WorldPosition, 1), new(color, float.IsPositiveInfinity(p.Range) ? 1e30f : p.Range), Vector4.Zero, Vector4.Zero],
            SpotLight s => [new(s.WorldPosition, 2), new(color, float.IsPositiveInfinity(s.Range) ? 1e30f : s.Range), new(s.WorldDirection, MathF.Cos(s.InnerConeAngle)), new(MathF.Cos(s.OuterConeAngle), 0, 0, 0)],
            _ => throw new NotSupportedException($"Unsupported OpenPBR light: {light.GetType().Name}."),
        };
    }

    private static bool Equal(Vector4[] a, Vector4[]? b) => b is not null && a.AsSpan().SequenceEqual(b);
}

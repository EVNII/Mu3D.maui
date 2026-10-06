using System.Numerics;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>
/// Presents a backend-independent scene and camera through a control-owned <see cref="SceneRenderer"/>.
/// </summary>
/// <remarks>
/// The control composes the low-level <see cref="Mu3DView"/>, owns its automatic presentation
/// session, follows the physical output extent, and releases renderer/depth resources before the
/// session is invalidated. Scene objects remain application-owned and mutable; call
/// <see cref="InvalidateScene"/> after mutating them. An explicitly assigned
/// <see cref="RenderPipeline"/> is borrowed and replaces the default one-pass scene pipeline. Use
/// <see cref="Mu3DView"/> directly for custom draw commands or manual swapchain ownership.
/// </remarks>
[ContentProperty(nameof(SceneContent))]
public sealed class Mu3DSceneView : Grid
{
    /// <summary>Gets or sets asynchronous GPU preparation before the automatic surface is published.</summary>
    /// <remarks>See Mu3DView.PrepareGraphicsAsync for device lifetime and cancellation rules.</remarks>
    public Func<GraphicsDevice, GraphicsTextureFormat, CancellationToken, Task>? PrepareGraphicsAsync
    {
        get => surfaceView.PrepareGraphicsAsync;
        set => surfaceView.PrepareGraphicsAsync = value;
    }

    /// <summary>Identifies the Windows-only SDR white matching switch.</summary>
    public static readonly BindableProperty WindowsMatchSdrWhiteProperty = BindableProperty.Create(
        nameof(WindowsMatchSdrWhite), typeof(bool), typeof(Mu3DSceneView), true,
        propertyChanged: static (owner, _, value) => ((Mu3DSceneView)owner).surfaceView.WindowsMatchSdrWhite = (bool)value);

    /// <summary>Gets or sets Windows system SDR white matching. Defaults to true; ignored on Android and Apple.</summary>
    /// <remarks>Only affects System white mode; changing it does not recreate the scene or presentation session.</remarks>
    public bool WindowsMatchSdrWhite
    {
        get => (bool)GetValue(WindowsMatchSdrWhiteProperty);
        set => SetValue(WindowsMatchSdrWhiteProperty, value);
    }

    private static readonly LinearRgba OpaqueBlack = new(
        0f,
        0f,
        0f,
        1f,
        StandardColorSpaces.LinearSrgb);
    private readonly Mu3DView surfaceView;
    private SceneRenderer? renderer;
    private IRenderPass? defaultScenePass;
    private RenderPassPipeline? defaultRenderPipeline;
    private readonly List<IRenderPass> ownedDefaultPasses = [];
    private readonly List<AttachedFeature> attachedFeatures = [];
    private readonly List<FeatureRenderPassRegistration> featureRenderPasses = [];
    private SceneRenderer? defaultPassRenderer;
    private RenderOutputId defaultPassOutput;
    private ulong defaultPassRegistryRevision;
    private ulong defaultPassFeatureRevision;
    private ulong featureRenderPassRevision;
    private long featureRenderPassSequence;
    private GraphicsTexture? displaySceneTexture;
    private ColorViewGpuTransform? displayGpuTransform;
    private PresentationWhitePass? sceneLinearExposurePass;
    private ColorView3D? subscribedDisplayTransform;
    private GraphicsTexture? depthTexture;
    private GraphicsExtent3D depthExtent;
    private ViewportFrameSnapshot? pendingFrameSnapshot;
    private ViewportFrameSnapshot? latestFrameSnapshot;
    private ulong successfulFrameId;
    private bool refreshingFeatures;
    private bool featureRefreshRequested;

    /// <summary>Identifies the <see cref="Scene"/> bindable property.</summary>
    public static readonly BindableProperty SceneProperty = BindableProperty.Create(
        nameof(Scene),
        typeof(Scene),
        typeof(Mu3DSceneView),
        default(Scene),
        propertyChanged: static (bindable, _, _) => ((Mu3DSceneView)bindable).OnFrameSourceChanged());

    /// <summary>Identifies the <see cref="SceneContent"/> bindable property.</summary>
    public static readonly BindableProperty SceneContentProperty = BindableProperty.Create(
        nameof(SceneContent),
        typeof(Scene3D),
        typeof(Mu3DSceneView),
        default(Scene3D),
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((Mu3DSceneView)bindable).OnSceneContentChanged(
                (Scene3D?)oldValue,
                (Scene3D?)newValue));

    /// <summary>Identifies the <see cref="Camera"/> bindable property.</summary>
    public static readonly BindableProperty CameraProperty = BindableProperty.Create(
        nameof(Camera),
        typeof(Camera),
        typeof(Mu3DSceneView),
        default(Camera),
        propertyChanged: static (bindable, _, _) => ((Mu3DSceneView)bindable).OnFrameSourceChanged());

    /// <summary>Identifies the <see cref="ClearColor"/> bindable property.</summary>
    public static readonly BindableProperty ClearColorProperty = BindableProperty.Create(
        nameof(ClearColor),
        typeof(LinearRgba),
        typeof(Mu3DSceneView),
        OpaqueBlack,
        validateValue: static (_, value) =>
            value is LinearRgba color && color.ColorSpace is not null,
        propertyChanged: static (bindable, _, _) => ((Mu3DSceneView)bindable).InvalidateScene());

    /// <summary>Identifies the <see cref="OutputSettings"/> bindable property.</summary>
    public static readonly BindableProperty OutputSettingsProperty = BindableProperty.Create(
        nameof(OutputSettings),
        typeof(OutputSettings),
        typeof(Mu3DSceneView),
        OutputSettings.Default,
        propertyChanged: static (bindable, _, value) =>
            ((Mu3DSceneView)bindable).surfaceView.OutputSettings = (OutputSettings)value);

    /// <summary>Identifies the <see cref="RenderPipeline"/> bindable property.</summary>
    public static readonly BindableProperty RenderPipelineProperty = BindableProperty.Create(
        nameof(RenderPipeline),
        typeof(RenderPassPipeline),
        typeof(Mu3DSceneView),
        default(RenderPassPipeline),
        propertyChanged: static (bindable, _, _) =>
        {
            Mu3DSceneView view = (Mu3DSceneView)bindable;
            view.DisposeDefaultScenePass();
            view.InvalidateScene();
        });

    /// <summary>Identifies the optional final display-view component.</summary>
    public static readonly BindableProperty DisplayTransformProperty = BindableProperty.Create(
        nameof(DisplayTransform), typeof(ColorView3D), typeof(Mu3DSceneView), null,
        propertyChanged: static (owner, _, _) => ((Mu3DSceneView)owner).OnDisplayTransformChanged());

    /// <summary>Identifies the <see cref="SceneLinearExposureStops"/> bindable property.</summary>
    public static readonly BindableProperty SceneLinearExposureStopsProperty = BindableProperty.Create(
        nameof(SceneLinearExposureStops), typeof(float), typeof(Mu3DSceneView), 0f,
        validateValue: static (_, value) => value is float stops && float.IsFinite(stops) && stops is >= -32 and <= 32,
        propertyChanged: static (owner, _, _) => ((Mu3DSceneView)owner).InvalidateScene());

    /// <summary>Identifies the <see cref="RenderOutput"/> bindable property.</summary>
    public static readonly BindableProperty RenderOutputProperty = BindableProperty.Create(
        nameof(RenderOutput),
        typeof(RenderOutputId),
        typeof(Mu3DSceneView),
        RenderOutputIds.Beauty,
        validateValue: static (_, value) =>
            value is RenderOutputId output && output.IsValid,
        propertyChanged: static (bindable, _, _) => ((Mu3DSceneView)bindable).InvalidateScene());

    /// <summary>
    /// Identifies the <see cref="AutomaticallyUpdateCameraAspectRatio"/> bindable property.
    /// </summary>
    public static readonly BindableProperty AutomaticallyUpdateCameraAspectRatioProperty =
        BindableProperty.Create(
            nameof(AutomaticallyUpdateCameraAspectRatio),
            typeof(bool),
            typeof(Mu3DSceneView),
            true,
            propertyChanged: static (bindable, _, _) =>
                ((Mu3DSceneView)bindable).InvalidateScene());

    /// <summary>Initializes an empty high-level scene view.</summary>
    public Mu3DSceneView()
    {
        Features.CollectionChanged += OnFeaturesChanged;
        HandlerChanged += OnSceneViewHandlerChanged;
        surfaceView = new Mu3DView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
        };
        surfaceView.Draw += OnSurfaceDraw;
        surfaceView.PresentationSessionChanged += OnPresentationSessionChanged;
        surfaceView.FramePresented += OnFramePresented;
        surfaceView.SurfaceError += OnSurfaceError;
        surfaceView.SurfaceSizeChanged += OnSurfaceSizeChanged;
        Children.Add(surfaceView);
    }

    /// <summary>Gets or sets an explicit AgX or ACES 2 view applied after the scene render pipeline.</summary>
    /// <remarks>
    /// Null preserves scene-linear presentation, optionally scaled by <see cref="SceneLinearExposureStops"/>.
    /// Selecting a view uses an owned FP16
    /// intermediate, or FP32 when the output is FP32. HDR presets require HDR extended-linear sRGB
    /// presentation. Custom passes must produce premultiplied scene-linear RGB, as the default scene
    /// and OpenPBR passes do. Display changes do not reset application-owned progressive accumulation.
    /// </remarks>
    public ColorView3D? DisplayTransform
    {
        get => (ColorView3D?)GetValue(DisplayTransformProperty);
        set => SetValue(DisplayTransformProperty, value);
    }

    /// <summary>Gets or sets scene-linear exposure from -32 through +32 stops. Defaults to zero.</summary>
    /// <remarks>
    /// Applies only when <see cref="DisplayTransform"/> is null. Non-zero exposure requires an
    /// extended-linear HDR Float16 or Float32 presentation target; SDR output is rejected explicitly.
    /// The final RGB is multiplied by 2 raised to this value, without tone mapping or changing alpha.
    /// Zero retains the direct presentation path without an extra pass. Exposure changes request a
    /// frame without changing scene lighting or resetting application-owned progressive accumulation.
    /// When a display view is attached, use <see cref="ColorView3D.ExposureStops"/> instead.
    /// </remarks>
    public float SceneLinearExposureStops
    {
        get => (float)GetValue(SceneLinearExposureStopsProperty);
        set => SetValue(SceneLinearExposureStopsProperty, value);
    }

    private void OnDisplayTransformChanged()
    {
        if (subscribedDisplayTransform is not null) subscribedDisplayTransform.Changed -= OnDisplayParametersChanged;
        subscribedDisplayTransform = DisplayTransform;
        if (subscribedDisplayTransform is not null)
        {
            subscribedDisplayTransform.Changed += OnDisplayParametersChanged;
            SetInheritedBindingContext(subscribedDisplayTransform, BindingContext);
        }
        InvalidateScene();
    }

    private void OnDisplayParametersChanged(object? sender, EventArgs e) => InvalidateScene();

    /// <inheritdoc />
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (DisplayTransform is not null) SetInheritedBindingContext(DisplayTransform, BindingContext);
    }

    /// <summary>Gets or sets the application-owned scene to render.</summary>
    public Scene? Scene
    {
        get => (Scene?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional declarative scene facade populated by nested XAML content.
    /// </summary>
    /// <remarks>
    /// While assigned, the facade supplies the lower-level <see cref="Scene"/> and
    /// <see cref="Camera"/> values and invalidates this view when its bindable nodes or materials
    /// change. Set this property to null before independently assigning those lower-level values.
    /// </remarks>
    public Scene3D? SceneContent
    {
        get => (Scene3D?)GetValue(SceneContentProperty);
        set => SetValue(SceneContentProperty, value);
    }

    /// <summary>Gets or sets the application-owned camera used to render <see cref="Scene"/>.</summary>
    public Camera? Camera
    {
        get => (Camera?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    /// <summary>
    /// Gets or sets the explicitly tagged scene-linear clear color. The default is opaque linear-sRGB
    /// black; no tone mapping, gamut mapping, or display encoding is applied implicitly.
    /// </summary>
    public LinearRgba ClearColor
    {
        get => (LinearRgba)GetValue(ClearColorProperty);
        set => SetValue(ClearColorProperty, value);
    }

    /// <summary>Gets or sets the HDR-first presentation policy used by the owned surface.</summary>
    public OutputSettings OutputSettings
    {
        get => (OutputSettings)GetValue(OutputSettingsProperty);
        set => SetValue(OutputSettingsProperty, value);
    }

    /// <summary>
    /// Gets or sets an application-owned ordered HDR-linear pass pipeline, or null to render the
    /// scene through the control's default clearing pass.
    /// </summary>
    /// <remarks>
    /// The control never disposes this pipeline or its passes. A custom pipeline must target the
    /// current <see cref="Renderer"/> device, format, and working color space. Replace renderer-bound
    /// passes when <see cref="RendererChanged"/> reports a new renderer.
    /// </remarks>
    public RenderPassPipeline? RenderPipeline
    {
        get => (RenderPassPipeline?)GetValue(RenderPipelineProperty);
        set => SetValue(RenderPipelineProperty, value);
    }

    /// <summary>
    /// Gets or sets the stable output selected for the control's default one-pass pipeline.
    /// </summary>
    /// <remarks>
    /// The default is <see cref="RenderOutputIds.Beauty"/>. This value is ignored when
    /// <see cref="RenderPipeline"/> is non-null. Register extension factories in
    /// <see cref="RenderOutputRegistry"/> before selecting their identifiers.
    /// </remarks>
    public RenderOutputId RenderOutput
    {
        get => (RenderOutputId)GetValue(RenderOutputProperty);
        set => SetValue(RenderOutputProperty, value);
    }

    /// <summary>Gets the explicit pass-factory registry used by the default pipeline.</summary>
    /// <remarks>
    /// The per-view registry initially contains all built-in outputs. After changing registrations,
    /// call <see cref="InvalidateScene"/>; the next frame observes the registry revision.
    /// </remarks>
    public RenderOutputRegistry RenderOutputRegistry { get; } =
        RenderOutputRegistry.CreateDefault();

    /// <summary>Gets the ordered, application-owned features attached with the native handler.</summary>
    /// <remarks>
    /// Changes detach current features in reverse order and reattach the resulting collection in
    /// forward order. Feature objects are borrowed and are never disposed by the view.
    /// </remarks>
    public ObservableCollection<ISceneViewFeature> Features { get; } = [];

    /// <summary>
    /// Gets or sets whether a <see cref="PerspectiveCamera"/>'s aspect ratio follows the current
    /// physical output extent. The default is true.
    /// </summary>
    public bool AutomaticallyUpdateCameraAspectRatio
    {
        get => (bool)GetValue(AutomaticallyUpdateCameraAspectRatioProperty);
        set => SetValue(AutomaticallyUpdateCameraAspectRatioProperty, value);
    }

    /// <summary>Gets the control-owned backend-independent presentation session.</summary>
    /// <remarks>Application code must not dispose this object.</remarks>
    public IPresentationSurfaceSession? PresentationSession => surfaceView.PresentationSession;

    /// <summary>Gets the control-owned scene renderer after its first renderable frame.</summary>
    /// <remarks>
    /// Applications may configure the renderer when <see cref="RendererChanged"/> reports a non-null
    /// value, but must not dispose it. It becomes null before its presentation session is released.
    /// </remarks>
    public SceneRenderer? Renderer => renderer;

    /// <summary>Gets the current non-zero physical output width.</summary>
    public uint PixelWidth => surfaceView.GetSurfaceConfigurationSize().Width;

    /// <summary>Gets the current non-zero physical output height.</summary>
    public uint PixelHeight => surfaceView.GetSurfaceConfigurationSize().Height;

    /// <summary>
    /// Gets the exact camera and viewport state of the latest successfully presented scene frame.
    /// </summary>
    /// <remarks>
    /// The value remains the last visible successful frame across a timeout or failed attempt and
    /// becomes null when the presentation session is invalidated, <see cref="Scene"/> or
    /// <see cref="Camera"/> is replaced, or a successful empty frame replaces the scene.
    /// </remarks>
    public ViewportFrameSnapshot? LatestFrameSnapshot => latestFrameSnapshot;

    /// <summary>Occurs when the control-owned scene renderer is created or released.</summary>
    public event EventHandler<SceneRendererChangedEventArgs>? RendererChanged;

    /// <summary>Occurs when the automatic presentation session becomes ready or is invalidated.</summary>
    public event EventHandler<PresentationSessionChangedEventArgs>? PresentationSessionChanged;

    /// <summary>Occurs after an on-demand frame attempt finishes.</summary>
    public event EventHandler<SurfaceFramePresentedEventArgs>? FramePresented;

    /// <summary>Occurs when automatic session creation, resize, or scene drawing fails.</summary>
    public event EventHandler<SurfaceErrorEventArgs>? SurfaceError;

    /// <summary>Occurs after the authoritative physical-pixel output extent changes.</summary>
    public event EventHandler<SurfaceSizeChangedEventArgs>? SurfaceSizeChanged;

    /// <summary>
    /// Occurs after <see cref="LatestFrameSnapshot"/> changes following successful presentation or
    /// session invalidation or scene/camera replacement.
    /// </summary>
    public event EventHandler<ViewportFrameSnapshotChangedEventArgs>? FrameSnapshotChanged;

    /// <summary>Occurs when a feature attach, detach, or render-pass creation operation fails.</summary>
    public event EventHandler<SceneViewFeatureErrorEventArgs>? FeatureError;

    /// <summary>Coalesces and requests one scene frame after application-owned state changes.</summary>
    public void InvalidateScene() => surfaceView.InvalidateSurface();

    private void OnSceneContentChanged(Scene3D? oldScene, Scene3D? newScene)
    {
        oldScene?.DetachView(this);
        newScene?.AttachView(this);
        InvalidateScene();
    }

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        pendingFrameSnapshot = null;
        Scene? scene = Scene;
        Camera? camera = Camera;
        ColorViewTransform? display = DisplayTransform?.ToTransform(StandardColorSpaces.LinearSrgb);
        float linearExposureStops = display is null ? SceneLinearExposureStops : 0f;
        bool applyLinearExposure = linearExposureStops != 0f;
        if (applyLinearExposure &&
            (e.OutputPlan.Output.DynamicRange != OutputDynamicRange.Hdr ||
             e.OutputPlan.Output.Encoding != ColorEncoding.ExtendedSrgbLinear ||
             e.Target.Descriptor.Format is not (GraphicsTextureFormat.Rgba16Float or GraphicsTextureFormat.Rgba32Float)))
            throw new NotSupportedException("Scene-linear exposure requires an extended-linear HDR Float16 or Float32 surface. Select HDR output or reset SceneLinearExposureStops to zero.");
        if (display is not null && OutputSettings.WhiteMode == OutputWhiteMode.FixedAbsolute &&
            display.ReferenceWhiteNits != OutputSettings.ReferenceWhiteNits)
            throw new InvalidOperationException("Fixed absolute presentation requires OutputSettings.ReferenceWhiteNits to match DisplayTransform.ReferenceWhiteNits.");
        if (display is null && !applyLinearExposure && (scene is null || camera is null))
        {
            ClearTarget(e.Device, e.Target, ClearColor);
            return;
        }
        if (display is not null && display.IsHdr &&
            (e.OutputPlan.Output.DynamicRange != OutputDynamicRange.Hdr ||
             e.OutputPlan.Output.Encoding != ColorEncoding.ExtendedSrgbLinear))
            throw new NotSupportedException("The selected HDR display view requires extended-linear HDR presentation. Select an SDR view explicitly for this surface.");
        GraphicsTextureFormat sceneFormat = display is null ? e.Target.Descriptor.Format :
            e.Target.Descriptor.Format == GraphicsTextureFormat.Rgba32Float ? GraphicsTextureFormat.Rgba32Float : GraphicsTextureFormat.Rgba16Float;
        SceneRenderer currentRenderer = EnsureRenderer(e.Device, sceneFormat);
        GraphicsTexture sceneTarget = display is not null
            ? EnsureDisplaySceneTexture(e.Device, e.Width, e.Height, sceneFormat)
            : applyLinearExposure ? EnsureSceneLinearExposurePass(e.Device, sceneFormat).GetInput(e.Target) : e.Target;
        LinearRgba clear = ClearColor;
        // Scene attachments are premultiplied. Keep the default path's legacy clear behavior intact.
        if (display is not null)
            clear = new LinearRgba(clear.Red * clear.Alpha, clear.Green * clear.Alpha, clear.Blue * clear.Alpha, clear.Alpha, clear.ColorSpace);
        if (scene is null || camera is null)
        {
            ClearTarget(e.Device, sceneTarget, clear);
            if (display is not null) ApplyDisplayTransform(e, sceneTarget, display);
            else if (applyLinearExposure) sceneLinearExposurePass!.Apply(e.Target, MathF.Pow(2, linearExposureStops));
            return;
        }

        if (AutomaticallyUpdateCameraAspectRatio && camera is PerspectiveCamera perspectiveCamera)
        {
            perspectiveCamera.AspectRatio = (float)e.Width / e.Height;
        }

        Matrix4x4 viewMatrix = camera.ViewMatrix;
        Matrix4x4 projectionMatrix = camera.ProjectionMatrix;

        RenderPassPipeline pipeline = RenderPipeline ??
            EnsureDefaultRenderPipeline(currentRenderer, clear);
        RenderPassExecutionResult result = pipeline.Execute(new RenderPassContext(
            scene,
            camera,
            sceneTarget,
            EnsureDepthTexture(e.Device, e.Width, e.Height),
            currentRenderer.WorkingColorSpace));
        if (!result.ColorTargetInitialized)
        {
            throw new InvalidOperationException(
                "The Mu3DSceneView render pipeline did not produce a preserved color output.");
        }

        if (display is not null) ApplyDisplayTransform(e, sceneTarget, display);
        else if (applyLinearExposure) sceneLinearExposurePass!.Apply(e.Target, MathF.Pow(2, linearExposureStops));

        pendingFrameSnapshot = new ViewportFrameSnapshot(
            successfulFrameId == ulong.MaxValue ? ulong.MaxValue : successfulFrameId + 1,
            viewMatrix,
            projectionMatrix,
            e.Width,
            e.Height,
            ResolveLogicalExtent(Width, e.Width),
            ResolveLogicalExtent(Height, e.Height));
    }

    private PresentationWhitePass EnsureSceneLinearExposurePass(GraphicsDevice device, GraphicsTextureFormat format)
    {
        if (sceneLinearExposurePass is null || sceneLinearExposurePass.Format != format)
        {
            PresentationWhitePass replacement = new(device, format);
            sceneLinearExposurePass?.Dispose();
            sceneLinearExposurePass = replacement;
        }
        return sceneLinearExposurePass;
    }

    private GraphicsTexture EnsureDisplaySceneTexture(GraphicsDevice device, uint width, uint height, GraphicsTextureFormat format)
    {
        GraphicsExtent3D extent = new(width, height);
        if (displaySceneTexture is not null && displaySceneTexture.Descriptor.Size == extent &&
            displaySceneTexture.Descriptor.Format == format) return displaySceneTexture;
        GraphicsTexture replacement = device.CreateTexture(new GraphicsTextureDescriptor(extent, format,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: "Mu3DSceneView scene-linear display input"));
        displaySceneTexture?.Dispose();
        return displaySceneTexture = replacement;
    }

    private void ApplyDisplayTransform(SurfaceDrawEventArgs e, GraphicsTexture source, ColorViewTransform transform)
    {
        bool outputPremultiplied = PresentationSession?.AlphaMode switch
        {
            SurfaceAlphaMode.Opaque or SurfaceAlphaMode.Premultiplied => true,
            SurfaceAlphaMode.Unpremultiplied => false,
            _ => throw new NotSupportedException("Display views require a presentation session reporting its negotiated alpha association. Select a supported explicit alpha mode when inheritance cannot be resolved."),
        };
        if (displayGpuTransform is null || displayGpuTransform.Transform.Preset != transform.Preset ||
            displayGpuTransform.OutputFormat != e.Target.Descriptor.Format ||
            displayGpuTransform.OutputEncoding != e.OutputPlan.Output.Encoding ||
            displayGpuTransform.OutputPremultiplied != outputPremultiplied)
        {
            ColorViewGpuTransform replacement = new(e.Device, transform, e.Target.Descriptor.Format,
                e.OutputPlan.Output.Encoding, inputPremultiplied: true, outputPremultiplied: outputPremultiplied);
            displayGpuTransform?.Dispose();
            displayGpuTransform = replacement;
        }
        else if (!ReferenceEquals(displayGpuTransform.Transform, transform)) displayGpuTransform.UpdateTransform(transform);
        displayGpuTransform.Apply(source, transform.SourceSpace, e.Target, transform.DestinationSpace);
    }

    private SceneRenderer EnsureRenderer(GraphicsDevice device, GraphicsTextureFormat colorFormat)
    {
        if (renderer is not null &&
            ReferenceEquals(renderer.Device, device) &&
            renderer.ColorFormat == colorFormat)
        {
            return renderer;
        }

        DisposeRenderResources();
        renderer = new SceneRenderer(device, colorFormat);
        RendererChanged?.Invoke(this, new SceneRendererChangedEventArgs(renderer));
        return renderer;
    }

    private RenderPassPipeline EnsureDefaultRenderPipeline(
        SceneRenderer currentRenderer,
        LinearRgba clearColor)
    {
        if (defaultScenePass is not null &&
            defaultRenderPipeline is not null &&
            ReferenceEquals(defaultPassRenderer, currentRenderer) &&
            defaultPassOutput == RenderOutput &&
            defaultPassRegistryRevision == RenderOutputRegistry.Revision &&
            defaultPassFeatureRevision == featureRenderPassRevision &&
            defaultScenePass.Descriptor.ColorAttachment?.ClearColor == clearColor)
        {
            return defaultRenderPipeline;
        }

        DisposeDefaultScenePass();
        SceneRenderPassOptions options = new(GraphicsLoadOperation.Clear, clearColor);
        List<IRenderPass> createdPasses = [];
        try
        {
            foreach (FeatureRenderPassRegistration registration in OrderedFeaturePasses(
                SceneViewRenderPassPlacement.BeforeScene))
            {
                createdPasses.Add(CreateFeaturePass(registration, currentRenderer));
            }

            IRenderPass createdScenePass = RenderOutputRegistry.CreatePass(
                RenderOutput,
                currentRenderer,
                options,
                "Mu3DSceneView scene");
            createdPasses.Add(createdScenePass);

            foreach (FeatureRenderPassRegistration registration in OrderedFeaturePasses(
                SceneViewRenderPassPlacement.AfterScene))
            {
                createdPasses.Add(CreateFeaturePass(registration, currentRenderer));
            }

            defaultRenderPipeline = new RenderPassPipeline(createdPasses);
            defaultScenePass = createdScenePass;
            ownedDefaultPasses.AddRange(createdPasses);
        }
        catch
        {
            for (int index = createdPasses.Count - 1; index >= 0; index--)
            {
                (createdPasses[index] as IDisposable)?.Dispose();
            }
            throw;
        }
        defaultPassRenderer = currentRenderer;
        defaultPassOutput = RenderOutput;
        defaultPassRegistryRevision = RenderOutputRegistry.Revision;
        defaultPassFeatureRevision = featureRenderPassRevision;
        return defaultRenderPipeline;
    }

    internal IDisposable RegisterFeatureRenderPass(
        ISceneViewFeature feature,
        SceneViewRenderPassFactory factory,
        SceneViewRenderPassPlacement placement,
        int order)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(factory);
        if (!Enum.IsDefined(placement))
        {
            throw new ArgumentOutOfRangeException(nameof(placement));
        }

        FeatureRenderPassRegistration registration = new(
            feature,
            factory,
            placement,
            order,
            featureRenderPassSequence++);
        featureRenderPasses.Add(registration);
        AdvanceFeatureRenderPassRevision();
        DisposeDefaultScenePass();
        InvalidateScene();
        return new FeatureRenderPassToken(this, registration);
    }

    private IEnumerable<FeatureRenderPassRegistration> OrderedFeaturePasses(
        SceneViewRenderPassPlacement placement) =>
        featureRenderPasses
            .Where(registration => registration.Placement == placement)
            .OrderBy(registration => registration.Order)
            .ThenBy(registration => registration.Sequence);

    private IRenderPass CreateFeaturePass(
        FeatureRenderPassRegistration registration,
        SceneRenderer currentRenderer)
    {
        try
        {
            return registration.Factory(currentRenderer) ??
                throw new InvalidOperationException("A scene-view feature render-pass factory returned null.");
        }
        catch (Exception exception)
        {
            FeatureError?.Invoke(
                this,
                new SceneViewFeatureErrorEventArgs(
                    registration.Feature,
                    SceneViewFeatureOperation.CreateRenderPass,
                    exception));
            throw;
        }
    }

    private void RemoveFeatureRenderPass(FeatureRenderPassRegistration registration)
    {
        if (!featureRenderPasses.Remove(registration))
        {
            return;
        }

        AdvanceFeatureRenderPassRevision();
        DisposeDefaultScenePass();
        InvalidateScene();
    }

    private void AdvanceFeatureRenderPassRevision() =>
        featureRenderPassRevision = featureRenderPassRevision == ulong.MaxValue
            ? 0
            : featureRenderPassRevision + 1;

    private GraphicsTexture EnsureDepthTexture(GraphicsDevice device, uint width, uint height)
    {
        GraphicsExtent3D requiredExtent = new(width, height);
        if (depthTexture is not null &&
            ReferenceEquals(depthTexture.Device, device) &&
            depthExtent == requiredExtent)
        {
            return depthTexture;
        }

        depthTexture?.Dispose();
        depthTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            requiredExtent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: "Mu3DSceneView depth"));
        depthExtent = requiredExtent;
        return depthTexture;
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        DisposeRenderResources();
        ClearFrameSnapshot();
        PresentationSessionChanged?.Invoke(this, e);
    }

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        _ = sender;
        if (e.Status is PresentationSurfaceFrameStatus.PresentedOptimal or
            PresentationSurfaceFrameStatus.PresentedSuboptimal)
        {
            if (pendingFrameSnapshot is ViewportFrameSnapshot snapshot)
            {
                successfulFrameId = snapshot.FrameId;
                latestFrameSnapshot = snapshot;
                FrameSnapshotChanged?.Invoke(
                    this,
                    new ViewportFrameSnapshotChangedEventArgs(snapshot));
            }
            else
            {
                ClearFrameSnapshot();
            }
        }
        pendingFrameSnapshot = null;
        FramePresented?.Invoke(this, e);
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e) =>
        SurfaceError?.Invoke(this, e);

    private void OnSurfaceSizeChanged(object? sender, SurfaceSizeChangedEventArgs e) =>
        SurfaceSizeChanged?.Invoke(this, e);

    private void DisposeRenderResources()
    {
        DisposeDefaultScenePass();
        displayGpuTransform?.Dispose();
        displayGpuTransform = null;
        sceneLinearExposurePass?.Dispose();
        sceneLinearExposurePass = null;
        displaySceneTexture?.Dispose();
        displaySceneTexture = null;
        depthTexture?.Dispose();
        depthTexture = null;
        depthExtent = default;
        SceneRenderer? released = renderer;
        renderer = null;
        released?.Dispose();
        if (released is not null)
        {
            RendererChanged?.Invoke(this, new SceneRendererChangedEventArgs(null));
        }
    }

    private void DisposeDefaultScenePass()
    {
        defaultRenderPipeline = null;
        defaultScenePass = null;
        defaultPassRenderer = null;
        defaultPassOutput = default;
        defaultPassRegistryRevision = 0;
        defaultPassFeatureRevision = 0;
        for (int index = ownedDefaultPasses.Count - 1; index >= 0; index--)
        {
            (ownedDefaultPasses[index] as IDisposable)?.Dispose();
        }
        ownedDefaultPasses.Clear();
    }

    private void OnFeaturesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshFeatureAttachments();
    }

    private void OnSceneViewHandlerChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshFeatureAttachments();
    }

    private void RefreshFeatureAttachments()
    {
        if (refreshingFeatures)
        {
            featureRefreshRequested = true;
            return;
        }

        refreshingFeatures = true;
        try
        {
            do
            {
                featureRefreshRequested = false;
                DetachFeatures();
                if (Handler is not null)
                {
                    AttachFeatures();
                }
            }
            while (featureRefreshRequested);
        }
        finally
        {
            refreshingFeatures = false;
        }
    }

    private void AttachFeatures()
    {
        HashSet<ISceneViewFeature> seen = new(ReferenceEqualityComparer.Instance);
        foreach (ISceneViewFeature feature in Features)
        {
            try
            {
                if (feature is null)
                {
                    throw new InvalidOperationException("A scene-view feature collection cannot contain null.");
                }
                if (!seen.Add(feature))
                {
                    throw new InvalidOperationException(
                        "The same scene-view feature instance cannot be attached twice to one view.");
                }

                SceneViewFeatureContext context = new(this, feature);
                IDisposable attachment;
                try
                {
                    attachment = feature.Attach(context) ??
                        throw new InvalidOperationException("A scene-view feature returned a null attachment lease.");
                }
                catch
                {
                    context.Detach();
                    throw;
                }
                attachedFeatures.Add(new AttachedFeature(feature, context, attachment));
            }
            catch (Exception exception)
            {
                FeatureError?.Invoke(
                    this,
                    new SceneViewFeatureErrorEventArgs(
                        feature,
                        SceneViewFeatureOperation.Attach,
                        exception));
                DetachFeatures();
                break;
            }
        }
    }

    private void DetachFeatures()
    {
        for (int index = attachedFeatures.Count - 1; index >= 0; index--)
        {
            AttachedFeature attached = attachedFeatures[index];
            try
            {
                attached.Detach();
            }
            catch (Exception exception)
            {
                FeatureError?.Invoke(
                    this,
                    new SceneViewFeatureErrorEventArgs(
                        attached.Feature,
                        SceneViewFeatureOperation.Detach,
                        exception));
            }
        }
        attachedFeatures.Clear();
    }

    private void OnFrameSourceChanged()
    {
        // A newly attached consumer must not pair the previous frame with a replacement source.
        try
        {
            ClearFrameSnapshot();
        }
        finally
        {
            InvalidateScene();
        }
    }

    private void ClearFrameSnapshot()
    {
        pendingFrameSnapshot = null;
        if (latestFrameSnapshot is null)
        {
            return;
        }
        latestFrameSnapshot = null;
        FrameSnapshotChanged?.Invoke(
            this,
            new ViewportFrameSnapshotChangedEventArgs(null));
    }

    private static double ResolveLogicalExtent(double logicalExtent, uint pixelExtent) =>
        double.IsFinite(logicalExtent) && logicalExtent > 0d ? logicalExtent : pixelExtent;

    private static void ClearTarget(
        GraphicsDevice device,
        GraphicsTexture target,
        LinearRgba clearColor)
    {
        LinearRgba converted = StandardLinearRgbConverter.Convert(
            clearColor,
            StandardColorSpaces.LinearSrgb);
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("Mu3DSceneView empty frame");
        using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(
                target,
                clearColor: new GraphicsClearColor(
                    converted.Red,
                    converted.Green,
                    converted.Blue,
                    converted.Alpha)),
            "Mu3DSceneView empty scene clear")))
        {
        }
        using GraphicsCommandBuffer commands = encoder.Finish("Mu3DSceneView empty frame commands");
        device.Queue.Submit(commands);
    }

    private sealed record FeatureRenderPassRegistration(
        ISceneViewFeature Feature,
        SceneViewRenderPassFactory Factory,
        SceneViewRenderPassPlacement Placement,
        int Order,
        long Sequence);

    private sealed class FeatureRenderPassToken(
        Mu3DSceneView owner,
        FeatureRenderPassRegistration registration) : IDisposable
    {
        private Mu3DSceneView? owner = owner;

        public void Dispose()
        {
            Mu3DSceneView? releasedOwner = Interlocked.Exchange(ref owner, null);
            releasedOwner?.RemoveFeatureRenderPass(registration);
        }
    }

    private sealed class AttachedFeature(
        ISceneViewFeature feature,
        SceneViewFeatureContext context,
        IDisposable attachment)
    {
        internal ISceneViewFeature Feature { get; } = feature;

        internal void Detach()
        {
            List<Exception>? failures = null;
            try
            {
                attachment.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            try
            {
                context.Detach();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            if (failures is not null)
            {
                throw new AggregateException("A scene-view feature failed to detach cleanly.", failures);
            }
        }
    }
}

/// <summary>Reports creation or release of a control-owned <see cref="SceneRenderer"/>.</summary>
/// <param name="Renderer">The ready renderer, or null after it is released.</param>
public sealed class SceneRendererChangedEventArgs(SceneRenderer? Renderer) : EventArgs
{
    /// <summary>Gets the ready renderer, or null after it is released.</summary>
    public SceneRenderer? Renderer { get; } = Renderer;
}

/// <summary>Reports a new successful viewport-frame snapshot or its invalidation.</summary>
/// <param name="Snapshot">The latest successful snapshot, or null after invalidation.</param>
public sealed class ViewportFrameSnapshotChangedEventArgs(ViewportFrameSnapshot? Snapshot)
    : EventArgs
{
    /// <summary>Gets the latest successful snapshot, or null after invalidation.</summary>
    public ViewportFrameSnapshot? Snapshot { get; } = Snapshot;
}

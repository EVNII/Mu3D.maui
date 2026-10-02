using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>
/// Describes one explicit logical region rendered by a shared <see cref="SceneViewportHost"/>.
/// </summary>
public sealed class SceneViewportSlot
{
    /// <summary>Initializes a logical shared-surface scene region.</summary>
    /// <param name="scene">The borrowed scene rendered in the region.</param>
    /// <param name="camera">The borrowed camera used for the region.</param>
    /// <param name="bounds">The region in host-relative logical MAUI units.</param>
    public SceneViewportSlot(Scene scene, Camera camera, Rect bounds)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        if (!double.IsFinite(bounds.X) || !double.IsFinite(bounds.Y) ||
            !double.IsFinite(bounds.Width) || !double.IsFinite(bounds.Height) ||
            bounds.Width <= 0d || bounds.Height <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bounds),
                "A viewport slot must have finite coordinates and positive dimensions.");
        }
        Bounds = bounds;
    }

    /// <summary>Gets the borrowed scene rendered in the region.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the borrowed camera used for the region.</summary>
    public Camera Camera { get; }

    /// <summary>Gets the region in host-relative logical MAUI units.</summary>
    public Rect Bounds { get; }
}

/// <summary>
/// Owns one MAUI presentation surface and renders multiple lightweight logical scene slots into it.
/// </summary>
/// <remarks>
/// This shared-surface path is intended for explicitly coordinated live regions. Image-backed
/// collection cells should use <see cref="SceneViewProxyHost"/> so MAUI owns final placement and
/// composition. This host borrows scenes and cameras, owns one renderer/depth target, and never
/// creates or reparents a native surface per item.
/// </remarks>
public sealed class SceneViewportHost : Grid
{
    private static readonly LinearRgba DefaultClearColor = new(
        0.035f,
        0.055f,
        0.095f,
        1f,
        StandardColorSpaces.LinearSrgb);
    private readonly Mu3DView surfaceView;
    private IReadOnlyList<SceneViewportSlot> slots = [];
    private IReadOnlyList<Scene> retainedScenes = [];
    private SceneRenderer? renderer;
    private GraphicsTexture? depthTexture;
    private GraphicsExtent3D depthExtent;

    /// <summary>Identifies the <see cref="ClearColor"/> bindable property.</summary>
    public static readonly BindableProperty ClearColorProperty = BindableProperty.Create(
        nameof(ClearColor),
        typeof(LinearRgba),
        typeof(SceneViewportHost),
        DefaultClearColor,
        validateValue: static (_, value) =>
            value is LinearRgba color && color.ColorSpace is not null,
        propertyChanged: static (bindable, _, _) =>
            ((SceneViewportHost)bindable).InvalidateViewports());

    /// <summary>Identifies the <see cref="IsRenderingEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsRenderingEnabledProperty = BindableProperty.Create(
        nameof(IsRenderingEnabled),
        typeof(bool),
        typeof(SceneViewportHost),
        true,
        propertyChanged: static (bindable, _, value) =>
        {
            if ((bool)value)
            {
                ((SceneViewportHost)bindable).InvalidateViewports();
            }
        });

    /// <summary>Initializes one shared presentation-surface host.</summary>
    public SceneViewportHost()
    {
        surfaceView = new Mu3DView
        {
            InputTransparent = true,
        };
        surfaceView.Draw += OnSurfaceDraw;
        surfaceView.PresentationSessionChanged += OnPresentationSessionChanged;
        surfaceView.FramePresented += OnFramePresented;
        surfaceView.SurfaceError += OnSurfaceError;
        Children.Add(surfaceView);
    }

    /// <summary>Gets or sets the explicitly tagged scene-linear background clear color.</summary>
    public LinearRgba ClearColor
    {
        get => (LinearRgba)GetValue(ClearColorProperty);
        set => SetValue(ClearColorProperty, value);
    }

    /// <summary>Gets or sets whether slot invalidations may request presentation frames.</summary>
    public bool IsRenderingEnabled
    {
        get => (bool)GetValue(IsRenderingEnabledProperty);
        set => SetValue(IsRenderingEnabledProperty, value);
    }

    /// <summary>Gets the currently retained explicit logical slot count.</summary>
    public int SlotCount => slots.Count;

    /// <summary>Gets the control-owned renderer after its first draw, or null.</summary>
    public SceneRenderer? Renderer => renderer;

    /// <summary>Occurs when the control-owned renderer is created or released.</summary>
    public event EventHandler<SceneRendererChangedEventArgs>? RendererChanged;

    /// <summary>Forwards errors from the single control-owned presentation surface.</summary>
    public event EventHandler<SurfaceErrorEventArgs>? SurfaceError;

    /// <summary>Occurs after the shared presentation surface attempts one frame.</summary>
    public event EventHandler<SurfaceFramePresentedEventArgs>? FramePresented;

    /// <summary>
    /// Replaces the borrowed logical slots with an immutable snapshot and coalesces one frame.
    /// </summary>
    public void SetViewports(
        IEnumerable<SceneViewportSlot> viewports,
        IEnumerable<Scene>? warmScenes = null)
    {
        ArgumentNullException.ThrowIfNull(viewports);
        SceneViewportSlot[] snapshot = viewports.ToArray();
        if (snapshot.Any(static viewport => viewport is null))
        {
            throw new ArgumentException("A viewport snapshot cannot contain null.", nameof(viewports));
        }
        slots = snapshot;
        Scene[] retainedSnapshot = warmScenes?.ToArray() ?? [];
        if (retainedSnapshot.Any(static scene => scene is null))
        {
            throw new ArgumentException("A warm scene snapshot cannot contain null.", nameof(warmScenes));
        }
        retainedScenes = retainedSnapshot;
        InvalidateViewports();
    }

    /// <summary>
    /// Replaces the borrowed non-rendered warm scene set without changing explicit or proxy slots.
    /// </summary>
    /// <param name="warmScenes">Scenes whose renderer cache entries should remain resident.</param>
    public void SetRetainedScenes(IEnumerable<Scene>? warmScenes)
    {
        Scene[] snapshot = warmScenes?.ToArray() ?? [];
        if (snapshot.Any(static scene => scene is null))
        {
            throw new ArgumentException("A warm scene snapshot cannot contain null.", nameof(warmScenes));
        }
        retainedScenes = snapshot;
        InvalidateViewports();
    }

    /// <summary>Coalesces and requests one shared-surface frame.</summary>
    public void InvalidateViewports()
    {
        if (IsRenderingEnabled)
        {
            surfaceView.InvalidateSurface();
        }
    }

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        SceneRenderer currentRenderer = EnsureRenderer(e.Device, e.Target.Descriptor.Format);
        GraphicsTexture currentDepth = EnsureDepthTexture(e.Device, e.Width, e.Height);
        IReadOnlyList<SceneRenderViewport> physicalViewports = IsRenderingEnabled
            ? CreatePhysicalViewports(e.Width, e.Height)
            : [];
        currentRenderer.RenderViewports(
            physicalViewports,
            e.Target,
            currentDepth,
            ClearColor,
            retainedScenes);
    }

    private IReadOnlyList<SceneRenderViewport> CreatePhysicalViewports(
        uint pixelWidth,
        uint pixelHeight)
    {
        double logicalWidth = double.IsFinite(Width) && Width > 0d ? Width : pixelWidth;
        double logicalHeight = double.IsFinite(Height) && Height > 0d ? Height : pixelHeight;
        double scaleX = pixelWidth / logicalWidth;
        double scaleY = pixelHeight / logicalHeight;
        List<SceneRenderViewport> result = new(slots.Count);
        foreach (SceneViewportSlot slot in slots)
        {
            AppendPhysicalViewport(
                result,
                slot.Scene,
                slot.Camera,
                slot.Bounds,
                automaticallyUpdateCameraAspectRatio: true,
                logicalWidth,
                logicalHeight,
                pixelWidth,
                pixelHeight,
                scaleX,
                scaleY);
        }

        return result;
    }

    private static void AppendPhysicalViewport(
        ICollection<SceneRenderViewport> result,
        Scene scene,
        Camera camera,
        Rect bounds,
        bool automaticallyUpdateCameraAspectRatio,
        double logicalWidth,
        double logicalHeight,
        uint pixelWidth,
        uint pixelHeight,
        double scaleX,
        double scaleY)
    {
        double left = Math.Clamp(bounds.Left, 0d, logicalWidth);
        double top = Math.Clamp(bounds.Top, 0d, logicalHeight);
        double right = Math.Clamp(bounds.Right, 0d, logicalWidth);
        double bottom = Math.Clamp(bounds.Bottom, 0d, logicalHeight);
        if (right <= left || bottom <= top)
        {
            return;
        }

        uint physicalLeft = Math.Min(pixelWidth, checked((uint)Math.Floor(left * scaleX)));
        uint physicalTop = Math.Min(pixelHeight, checked((uint)Math.Floor(top * scaleY)));
        uint physicalRight = Math.Min(pixelWidth, checked((uint)Math.Ceiling(right * scaleX)));
        uint physicalBottom = Math.Min(pixelHeight, checked((uint)Math.Ceiling(bottom * scaleY)));
        if (physicalRight <= physicalLeft || physicalBottom <= physicalTop)
        {
            return;
        }

        result.Add(new SceneRenderViewport(
            scene,
            camera,
            physicalLeft,
            physicalTop,
            physicalRight - physicalLeft,
            physicalBottom - physicalTop,
            automaticallyUpdateCameraAspectRatio));
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
            GraphicsTextureUsage.RenderAttachment,
            label: "shared scene viewport depth"));
        depthExtent = requiredExtent;
        return depthTexture;
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        DisposeRenderResources();
    }

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        _ = sender;
        FramePresented?.Invoke(this, e);
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        SurfaceError?.Invoke(this, e);
    }

    private void DisposeRenderResources()
    {
        depthTexture?.Dispose();
        depthTexture = null;
        depthExtent = default;
        if (renderer is not null)
        {
            renderer.Dispose();
            renderer = null;
            RendererChanged?.Invoke(this, new SceneRendererChangedEventArgs(null));
        }
    }

}

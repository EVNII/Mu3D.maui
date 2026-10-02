using System.ComponentModel;
using System.Numerics;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Helpers;
using MauiPoint = Microsoft.Maui.Graphics.Point;

namespace Mu3D.Maui.Toolkit.Overlays;

/// <summary>Defines how one MAUI viewport overlay participates in pointer input.</summary>
public enum ViewportOverlayInputMode
{
    /// <summary>The overlay and its descendants do not receive input.</summary>
    PassThrough,

    /// <summary>The overlay receives input and temporarily excludes sibling 3D controllers.</summary>
    Interactive,
}

/// <summary>Defines a scene-node point used to position one MAUI viewport overlay.</summary>
/// <remarks>
/// Assign either <see cref="Node"/> for a Core scene or <see cref="Target"/> for declarative
/// <see cref="Scene3D"/> content. The two target properties are mutually exclusive. Coordinates are
/// node-local and the logical <see cref="Offset"/> is applied after projection.
/// </remarks>
public sealed class SceneNodeAnchor : BindableObject
{
    /// <summary>Identifies the <see cref="Node"/> bindable property.</summary>
    public static readonly BindableProperty NodeProperty = BindableProperty.Create(
        nameof(Node),
        typeof(SceneNode),
        typeof(SceneNodeAnchor),
        default(SceneNode),
        validateValue: static (_, value) => value is null or SceneNode);

    /// <summary>Identifies the <see cref="Target"/> bindable property.</summary>
    public static readonly BindableProperty TargetProperty = BindableProperty.Create(
        nameof(Target),
        typeof(SceneNode3D),
        typeof(SceneNodeAnchor),
        default(SceneNode3D),
        validateValue: static (_, value) => value is null or SceneNode3D);

    /// <summary>Identifies the <see cref="X"/> bindable property.</summary>
    public static readonly BindableProperty XProperty = CreateCoordinateProperty(nameof(X));

    /// <summary>Identifies the <see cref="Y"/> bindable property.</summary>
    public static readonly BindableProperty YProperty = CreateCoordinateProperty(nameof(Y));

    /// <summary>Identifies the <see cref="Z"/> bindable property.</summary>
    public static readonly BindableProperty ZProperty = CreateCoordinateProperty(nameof(Z));

    /// <summary>Identifies the <see cref="Offset"/> bindable property.</summary>
    public static readonly BindableProperty OffsetProperty = BindableProperty.Create(
        nameof(Offset),
        typeof(MauiPoint),
        typeof(SceneNodeAnchor),
        new MauiPoint(8d, -28d),
        validateValue: static (_, value) =>
            value is MauiPoint point && double.IsFinite(point.X) && double.IsFinite(point.Y));

    /// <summary>Identifies the <see cref="HideWhenOutsideViewport"/> bindable property.</summary>
    public static readonly BindableProperty HideWhenOutsideViewportProperty =
        BindableProperty.Create(
            nameof(HideWhenOutsideViewport),
            typeof(bool),
            typeof(SceneNodeAnchor),
            true);

    /// <summary>Gets or sets the optional low-level Core scene node.</summary>
    public SceneNode? Node
    {
        get => (SceneNode?)GetValue(NodeProperty);
        set => SetValue(NodeProperty, value);
    }

    /// <summary>Gets or sets the optional declarative scene-node facade.</summary>
    public SceneNode3D? Target
    {
        get => (SceneNode3D?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Gets or sets the node-local X coordinate.</summary>
    public float X
    {
        get => (float)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    /// <summary>Gets or sets the node-local Y coordinate.</summary>
    public float Y
    {
        get => (float)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    /// <summary>Gets or sets the node-local Z coordinate.</summary>
    public float Z
    {
        get => (float)GetValue(ZProperty);
        set => SetValue(ZProperty, value);
    }

    /// <summary>Gets or sets the logical MAUI offset applied after projection.</summary>
    public MauiPoint Offset
    {
        get => (MauiPoint)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    /// <summary>Gets or sets whether the overlay is hidden outside the captured camera frustum.</summary>
    public bool HideWhenOutsideViewport
    {
        get => (bool)GetValue(HideWhenOutsideViewportProperty);
        set => SetValue(HideWhenOutsideViewportProperty, value);
    }

    internal Vector3 LocalPosition => new(X, Y, Z);

    internal SceneNode? ResolveNode()
    {
        if (Node is not null && Target is not null)
        {
            throw new InvalidOperationException(
                "A SceneNodeAnchor cannot assign both Node and Target.");
        }
        return Target?.CoreNode ?? Node;
    }

    private static BindableProperty CreateCoordinateProperty(string name) =>
        BindableProperty.Create(
            name,
            typeof(float),
            typeof(SceneNodeAnchor),
            0f,
            validateValue: static (_, value) => value is float number && float.IsFinite(number));
}

/// <summary>
/// Hosts arbitrary real MAUI content over a SceneView at a fixed viewport position or scene anchor.
/// </summary>
/// <remarks>
/// Declare this control inside <see cref="ViewportTools"/>. Without <see cref="Anchor"/>,
/// <see cref="Placement"/> selects one of nine viewport alignments. With an anchor, the shared
/// per-viewport overlay manager projects the node from the latest successfully presented frame.
/// Content stays in the native MAUI visual tree and therefore retains binding, styling,
/// accessibility and MAUI-animation behavior. The overlay owns no scene node or GPU resource.
/// </remarks>
[ContentProperty(nameof(Content))]
public sealed class ViewportOverlay : ContentView, IViewportTool
{
    private OverlayAttachment? attachment;

    /// <summary>Identifies the <see cref="Placement"/> bindable property.</summary>
    public static readonly BindableProperty PlacementProperty = BindableProperty.Create(
        nameof(Placement),
        typeof(ViewportOverlayPlacement),
        typeof(ViewportOverlay),
        ViewportOverlayPlacement.TopLeft,
        validateValue: static (_, value) =>
            value is ViewportOverlayPlacement placement && Enum.IsDefined(placement));

    /// <summary>Identifies the <see cref="Anchor"/> bindable property.</summary>
    public static readonly BindableProperty AnchorProperty = BindableProperty.Create(
        nameof(Anchor),
        typeof(SceneNodeAnchor),
        typeof(ViewportOverlay),
        default(SceneNodeAnchor),
        validateValue: static (_, value) => value is null or SceneNodeAnchor);

    /// <summary>Identifies the <see cref="InputMode"/> bindable property.</summary>
    public static readonly BindableProperty InputModeProperty = BindableProperty.Create(
        nameof(InputMode),
        typeof(ViewportOverlayInputMode),
        typeof(ViewportOverlay),
        ViewportOverlayInputMode.Interactive,
        validateValue: static (_, value) =>
            value is ViewportOverlayInputMode mode && Enum.IsDefined(mode));

    /// <summary>Gets or sets the fixed viewport placement used when <see cref="Anchor"/> is null.</summary>
    public ViewportOverlayPlacement Placement
    {
        get => (ViewportOverlayPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>Gets or sets the optional scene-node positioning policy.</summary>
    public SceneNodeAnchor? Anchor
    {
        get => (SceneNodeAnchor?)GetValue(AnchorProperty);
        set => SetValue(AnchorProperty, value);
    }

    /// <summary>Gets or sets whether overlay content receives input or passes it to the viewport.</summary>
    public ViewportOverlayInputMode InputMode
    {
        get => (ViewportOverlayInputMode)GetValue(InputModeProperty);
        set => SetValue(InputModeProperty, value);
    }

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("A ViewportOverlay can attach only once at a time.");
        }

        OverlayAttachment created = new(this, context.OverlayManager.Register(this));
        attachment = created;
        return created;
    }

    private sealed class OverlayAttachment(
        ViewportOverlay owner,
        ViewportOverlayRegistration registration) : IDisposable
    {
        private ViewportOverlay? owner = owner;
        private ViewportOverlayRegistration? registration = registration;

        public void Dispose()
        {
            IDisposable? currentRegistration = Interlocked.Exchange(ref registration, null);
            try
            {
                currentRegistration?.Dispose();
            }
            finally
            {
                ViewportOverlay? currentOwner = Interlocked.Exchange(ref owner, null);
                if (currentOwner is not null && ReferenceEquals(currentOwner.attachment, this))
                {
                    currentOwner.attachment = null;
                }
            }
        }
    }
}

internal sealed class ViewportOverlayManager : IDisposable
{
    private const int OverlayRootZIndex = 10_000;
    private readonly Mu3DSceneView view;
    private readonly ViewportControlArbiter controlArbiter;
    private readonly Grid root;
    private readonly List<IViewportOverlayRegistration> registrations = [];
    private ViewportFrameSnapshot? latestSnapshot;
    private ulong snapshotRevision;
    private bool disposed;

    internal ViewportOverlayManager(
        Mu3DSceneView view,
        ViewportControlArbiter controlArbiter)
    {
        this.view = view;
        this.controlArbiter = controlArbiter;
        root = new Grid
        {
            CascadeInputTransparent = false,
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            IsClippedToBounds = true,
            VerticalOptions = LayoutOptions.Fill,
            ZIndex = OverlayRootZIndex,
        };
        view.Children.Add(root);
        latestSnapshot = view.LatestFrameSnapshot;
        view.FrameSnapshotChanged += OnFrameSnapshotChanged;
        view.PropertyChanged += OnViewPropertyChanged;
    }

    internal Mu3DSceneView SceneView => view;

    internal bool IsCurrentSnapshot(ViewportFrameSnapshot? snapshot) =>
        !disposed && ReferenceEquals(latestSnapshot, snapshot);

    internal ViewportOverlayRegistration Register(ViewportOverlay overlay)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(overlay);
        ViewportOverlayRegistration registration = new(
            this,
            overlay,
            controlArbiter,
            root);
        registrations.Add(registration);
        try
        {
            registration.Attach(latestSnapshot);
        }
        catch
        {
            registrations.Remove(registration);
            registration.Dispose();
            throw;
        }
        return registration;
    }

    internal ViewportOverlayLayerRegistration Register(SceneNodeAnchorLayer layer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(layer);
        ViewportOverlayLayerRegistration registration = new(this, layer, root);
        registrations.Add(registration);
        try
        {
            registration.Attach(latestSnapshot);
        }
        catch
        {
            registrations.Remove(registration);
            registration.Dispose();
            throw;
        }
        return registration;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        snapshotRevision++;
        view.FrameSnapshotChanged -= OnFrameSnapshotChanged;
        view.PropertyChanged -= OnViewPropertyChanged;
        latestSnapshot = null;
        for (int index = registrations.Count - 1; index >= 0; index--)
        {
            registrations[index].Dispose();
        }
        registrations.Clear();
        view.Children.Remove(root);
    }

    internal void Remove(IViewportOverlayRegistration registration) =>
        registrations.Remove(registration);

    private void OnFrameSnapshotChanged(
        object? sender,
        ViewportFrameSnapshotChangedEventArgs e)
    {
        if (disposed || !ReferenceEquals(sender, view))
        {
            return;
        }
        latestSnapshot = e.Snapshot;
        ulong revision = ++snapshotRevision;
        foreach (IViewportOverlayRegistration registration in registrations.ToArray())
        {
            if (disposed || snapshotRevision != revision)
            {
                break;
            }
            registration.UpdateAnchor(e.Snapshot);
        }
    }

    private void OnViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (disposed || !ReferenceEquals(sender, view) ||
            e.PropertyName is not (nameof(Mu3DSceneView.Scene) or nameof(Mu3DSceneView.Camera)))
        {
            return;
        }
        // A replacement scene/camera cannot reuse projection from the previous successful frame.
        latestSnapshot = null;
        ulong revision = ++snapshotRevision;
        foreach (IViewportOverlayRegistration registration in registrations.ToArray())
        {
            if (disposed || snapshotRevision != revision)
            {
                break;
            }
            registration.UpdateAnchor(null);
        }
    }
}

internal interface IViewportOverlayRegistration : IDisposable
{
    void UpdateAnchor(ViewportFrameSnapshot? snapshot);
}

internal sealed class ViewportOverlayLayerRegistration : IViewportOverlayRegistration
{
    private readonly ViewportOverlayManager manager;
    private readonly SceneNodeAnchorLayer layer;
    private readonly Grid root;
    private readonly Grid presenter;
    private bool disposed;

    internal ViewportOverlayLayerRegistration(
        ViewportOverlayManager manager,
        SceneNodeAnchorLayer layer,
        Grid root)
    {
        this.manager = manager;
        this.layer = layer;
        this.root = root;
        presenter = new Grid
        {
            CascadeInputTransparent = false,
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            VerticalOptions = LayoutOptions.Fill,
            ZIndex = layer.ZIndex,
        };
        presenter.Children.Add(layer);
    }

    internal void Attach(ViewportFrameSnapshot? snapshot)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        root.Children.Add(presenter);
        UpdateAnchor(snapshot);
    }

    public void UpdateAnchor(ViewportFrameSnapshot? snapshot)
    {
        if (!disposed)
        {
            layer.UpdateFromOverlayManager(snapshot);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        layer.UpdateFromOverlayManager(null);
        presenter.Children.Remove(layer);
        root.Children.Remove(presenter);
        manager.Remove(this);
    }
}

internal sealed class ViewportOverlayRegistration : IViewportOverlayRegistration
{
    private readonly ViewportOverlayManager manager;
    private readonly ViewportOverlay overlay;
    private readonly ViewportControlArbiter controlArbiter;
    private readonly Grid root;
    private readonly Grid presenter;
    private readonly PointerGestureRecognizer pointerRecognizer = new();
    private SceneNodeAnchor? subscribedAnchor;
    private SceneNode? resolvedNode;
    private ViewportFrameSnapshot? snapshot;
    private ViewportControlLease? inputLease;
    private ulong updateRevision;
    private bool inputAttached;
    private bool disposed;

    internal ViewportOverlayRegistration(
        ViewportOverlayManager manager,
        ViewportOverlay overlay,
        ViewportControlArbiter controlArbiter,
        Grid root)
    {
        this.manager = manager;
        this.overlay = overlay;
        this.controlArbiter = controlArbiter;
        this.root = root;
        presenter = new Grid
        {
            CascadeInputTransparent = false,
        };
        presenter.Children.Add(overlay);
    }

    internal void Attach(ViewportFrameSnapshot? latestSnapshot)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        overlay.PropertyChanged += OnOverlayPropertyChanged;
        presenter.Unloaded += OnPresenterUnloaded;
        root.Children.Add(presenter);
        snapshot = latestSnapshot;
        Refresh();
    }

    internal void Refresh()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ReconnectAnchor();
        presenter.ZIndex = overlay.ZIndex;
        RefreshInput();
        UpdateAnchor(snapshot);
    }

    public void UpdateAnchor(ViewportFrameSnapshot? currentSnapshot)
    {
        if (disposed)
        {
            return;
        }
        ulong revision = ++updateRevision;
        snapshot = currentSnapshot;
        SceneNodeAnchor? anchor = subscribedAnchor;
        if (anchor is null)
        {
            ApplyFixedPlacement();
            presenter.IsVisible = true;
            return;
        }

        presenter.HorizontalOptions = LayoutOptions.Start;
        if (!IsCurrentUpdate(revision, currentSnapshot)) return;
        presenter.VerticalOptions = LayoutOptions.Start;
        if (!IsCurrentUpdate(revision, currentSnapshot)) return;
        SceneNode? node = resolvedNode;
        Mu3DSceneView view = manager.SceneView;
        if (node is null || currentSnapshot is null || view.Scene is not Scene scene ||
            view.Camera is not Camera camera ||
            !SceneTargetEligibility.IsVisible(scene, node, camera.VisibilityMask))
        {
            HideAnchor();
            return;
        }

        Vector3 worldPosition = Vector3.Transform(anchor.LocalPosition, node.WorldMatrix);
        if (!currentSnapshot.TryProject(worldPosition, out ViewportProjection projection) ||
            !double.IsFinite(projection.LogicalPosition.X) ||
            !double.IsFinite(projection.LogicalPosition.Y))
        {
            HideAnchor();
            return;
        }

        MauiPoint offset = anchor.Offset;
        double x = projection.LogicalPosition.X + offset.X;
        double y = projection.LogicalPosition.Y + offset.Y;
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            (anchor.HideWhenOutsideViewport && !projection.IsInsideViewport))
        {
            HideAnchor();
            return;
        }
        presenter.TranslationX = x;
        if (!IsCurrentUpdate(revision, currentSnapshot)) return;
        presenter.TranslationY = y;
        if (!IsCurrentUpdate(revision, currentSnapshot)) return;
        presenter.IsVisible = true;
    }

    private bool IsCurrentUpdate(ulong revision, ViewportFrameSnapshot? currentSnapshot) =>
        !disposed && revision == updateRevision && ReferenceEquals(snapshot, currentSnapshot) &&
        manager.IsCurrentSnapshot(currentSnapshot);

    private void HideAnchor()
    {
        ReleaseInputLease();
        presenter.IsVisible = false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        overlay.PropertyChanged -= OnOverlayPropertyChanged;
        presenter.Unloaded -= OnPresenterUnloaded;
        DisconnectAnchor();
        DetachInput();
        presenter.Children.Remove(overlay);
        root.Children.Remove(presenter);
        manager.Remove(this);
    }

    private void ReconnectAnchor()
    {
        SceneNodeAnchor? next = overlay.Anchor;
        if (ReferenceEquals(subscribedAnchor, next))
        {
            return;
        }
        DisconnectAnchor();
        subscribedAnchor = next;
        if (subscribedAnchor is not null)
        {
            resolvedNode = subscribedAnchor.ResolveNode();
            subscribedAnchor.PropertyChanged += OnAnchorPropertyChanged;
        }
    }

    private void DisconnectAnchor()
    {
        if (subscribedAnchor is not null)
        {
            subscribedAnchor.PropertyChanged -= OnAnchorPropertyChanged;
            subscribedAnchor = null;
        }
        resolvedNode = null;
    }

    private void RefreshInput()
    {
        bool interactive = overlay.InputMode == ViewportOverlayInputMode.Interactive;
        presenter.InputTransparent = !interactive;
        presenter.CascadeInputTransparent = !interactive;
        if (interactive && !inputAttached)
        {
            pointerRecognizer.PointerPressed += OnPointerPressed;
            pointerRecognizer.PointerReleased += OnPointerReleased;
            pointerRecognizer.PointerExited += OnPointerExited;
            presenter.GestureRecognizers.Add(pointerRecognizer);
            inputAttached = true;
        }
        else if (!interactive && inputAttached)
        {
            DetachInput();
        }
    }

    private void DetachInput()
    {
        if (inputAttached)
        {
            pointerRecognizer.PointerPressed -= OnPointerPressed;
            pointerRecognizer.PointerReleased -= OnPointerReleased;
            pointerRecognizer.PointerExited -= OnPointerExited;
            presenter.GestureRecognizers.Remove(pointerRecognizer);
            inputAttached = false;
        }
        ReleaseInputLease();
    }

    private void ApplyFixedPlacement()
    {
        presenter.TranslationX = 0d;
        presenter.TranslationY = 0d;
        presenter.HorizontalOptions = overlay.Placement switch
        {
            ViewportOverlayPlacement.TopLeft or
            ViewportOverlayPlacement.CenterLeft or
            ViewportOverlayPlacement.BottomLeft => LayoutOptions.Start,
            ViewportOverlayPlacement.TopCenter or
            ViewportOverlayPlacement.Center or
            ViewportOverlayPlacement.BottomCenter => LayoutOptions.Center,
            _ => LayoutOptions.End,
        };
        presenter.VerticalOptions = overlay.Placement switch
        {
            ViewportOverlayPlacement.TopLeft or
            ViewportOverlayPlacement.TopCenter or
            ViewportOverlayPlacement.TopRight => LayoutOptions.Start,
            ViewportOverlayPlacement.CenterLeft or
            ViewportOverlayPlacement.Center or
            ViewportOverlayPlacement.CenterRight => LayoutOptions.Center,
            _ => LayoutOptions.End,
        };
    }

    private void OnOverlayPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (disposed)
        {
            return;
        }
        if (e.PropertyName is nameof(ViewportOverlay.Anchor) or
            nameof(ViewportOverlay.Placement) or
            nameof(ViewportOverlay.InputMode) or
            nameof(VisualElement.ZIndex))
        {
            Refresh();
        }
    }

    private void OnAnchorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (disposed)
        {
            return;
        }
        resolvedNode = subscribedAnchor?.ResolveNode();
        UpdateAnchor(snapshot);
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        _ = sender;
        _ = e;
        if (disposed || !presenter.IsVisible || overlay.InputMode != ViewportOverlayInputMode.Interactive)
        {
            return;
        }
        ReleaseInputLease();
        inputLease = controlArbiter.TryAcquire(
            "Viewport overlay input",
            ViewportControlPriorities.OverlayUi);
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        _ = sender;
        _ = e;
        ReleaseInputLease();
    }

    private void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _ = sender;
        _ = e;
        ReleaseInputLease();
    }

    private void OnPresenterUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ReleaseInputLease();
    }

    private void ReleaseInputLease()
    {
        inputLease?.Dispose();
        inputLease = null;
    }
}

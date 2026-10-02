using System.ComponentModel;
using System.Numerics;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Helpers;

namespace Mu3D.Maui.Toolkit.Overlays;

/// <summary>
/// Batches placement of real MAUI child views at scene-node positions from successfully presented
/// <see cref="Mu3DSceneView"/> frame snapshots.
/// </summary>
/// <remarks>
/// Place this transparent <see cref="AbsoluteLayout"/> over the referenced scene view with the same
/// logical bounds, or declare it directly inside <see cref="ViewportTools"/> to use the common
/// overlay manager. Attach a <see cref="NodeProperty"/> to each child. The layer borrows the view
/// and nodes, subscribes only while loaded when standalone and performs no depth readback; clipping
/// reflects the captured camera frustum, not geometry occlusion.
/// </remarks>
public sealed class SceneNodeAnchorLayer : AbsoluteLayout, IViewportTool
{
    private Mu3DSceneView? subscribedView;
    private ManagerAttachment? managerAttachment;
    private ViewportFrameSnapshot? managedSnapshot;
    private ViewportFrameSnapshot? observedSnapshot;
    private ulong updateRevision;
    private bool isLoaded;

    /// <summary>Identifies the <see cref="SceneView"/> bindable property.</summary>
    public static readonly BindableProperty SceneViewProperty = BindableProperty.Create(
        nameof(SceneView),
        typeof(Mu3DSceneView),
        typeof(SceneNodeAnchorLayer),
        default(Mu3DSceneView),
        propertyChanged: static (bindable, _, _) =>
            ((SceneNodeAnchorLayer)bindable).Reconnect());

    /// <summary>Identifies the scene node attached to one anchor child.</summary>
    public static readonly BindableProperty NodeProperty = BindableProperty.CreateAttached(
        "Node",
        typeof(SceneNode),
        typeof(SceneNodeAnchorLayer),
        default(SceneNode),
        propertyChanged: OnAnchorPropertyChanged);

    /// <summary>Identifies the node-local position attached to one anchor child.</summary>
    public static readonly BindableProperty LocalPositionProperty = BindableProperty.CreateAttached(
        "LocalPosition",
        typeof(Vector3),
        typeof(SceneNodeAnchorLayer),
        Vector3.Zero,
        propertyChanged: OnAnchorPropertyChanged);

    /// <summary>Identifies the logical UI offset attached to one anchor child.</summary>
    public static readonly BindableProperty OffsetProperty = BindableProperty.CreateAttached(
        "Offset",
        typeof(Microsoft.Maui.Graphics.Point),
        typeof(SceneNodeAnchorLayer),
        new Microsoft.Maui.Graphics.Point(8d, -28d),
        propertyChanged: OnAnchorPropertyChanged);

    /// <summary>
    /// Identifies whether one anchor child is hidden outside the captured camera frustum.
    /// </summary>
    public static readonly BindableProperty HideWhenOutsideViewportProperty =
        BindableProperty.CreateAttached(
            "HideWhenOutsideViewport",
            typeof(bool),
            typeof(SceneNodeAnchorLayer),
            true,
            propertyChanged: OnAnchorPropertyChanged);

    /// <summary>Initializes a transparent, input-transparent scene-node anchor layer.</summary>
    public SceneNodeAnchorLayer()
    {
        InputTransparent = true;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Gets or sets the scene view whose successful frames drive this layer.</summary>
    public Mu3DSceneView? SceneView
    {
        get => (Mu3DSceneView?)GetValue(SceneViewProperty);
        set => SetValue(SceneViewProperty, value);
    }

    /// <summary>Gets the scene node attached to an anchor child.</summary>
    public static SceneNode? GetNode(BindableObject child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return (SceneNode?)child.GetValue(NodeProperty);
    }

    /// <summary>Sets the scene node attached to an anchor child.</summary>
    public static void SetNode(BindableObject child, SceneNode? value)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.SetValue(NodeProperty, value);
    }

    /// <summary>Gets the node-local point projected for an anchor child.</summary>
    public static Vector3 GetLocalPosition(BindableObject child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return (Vector3)child.GetValue(LocalPositionProperty);
    }

    /// <summary>Sets the node-local point projected for an anchor child.</summary>
    public static void SetLocalPosition(BindableObject child, Vector3 value)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.SetValue(LocalPositionProperty, value);
    }

    /// <summary>Gets the logical offset applied after projection.</summary>
    public static Microsoft.Maui.Graphics.Point GetOffset(BindableObject child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return (Microsoft.Maui.Graphics.Point)child.GetValue(OffsetProperty);
    }

    /// <summary>Sets the logical offset applied after projection.</summary>
    public static void SetOffset(
        BindableObject child,
        Microsoft.Maui.Graphics.Point value)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.SetValue(OffsetProperty, value);
    }

    /// <summary>Gets whether an anchor child is hidden outside the captured camera frustum.</summary>
    public static bool GetHideWhenOutsideViewport(BindableObject child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return (bool)child.GetValue(HideWhenOutsideViewportProperty);
    }

    /// <summary>Sets whether an anchor child is hidden outside the captured camera frustum.</summary>
    public static void SetHideWhenOutsideViewport(BindableObject child, bool value)
    {
        ArgumentNullException.ThrowIfNull(child);
        child.SetValue(HideWhenOutsideViewportProperty, value);
    }

    /// <summary>Reprojects every child from the latest successful scene-view frame.</summary>
    public void Refresh() => UpdateAnchors(
        managerAttachment is null ? observedSnapshot : managedSnapshot);

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (managerAttachment is not null)
        {
            throw new InvalidOperationException(
                "A SceneNodeAnchorLayer can attach only once at a time.");
        }
        if (SceneView is not null && !ReferenceEquals(SceneView, context.View))
        {
            throw new InvalidOperationException(
                "A managed SceneNodeAnchorLayer cannot reference a different SceneView.");
        }

        Mu3DSceneView? previousView = SceneView;
        ManagerAttachment created = new(this, context.View, previousView);
        managerAttachment = created;
        // The same SceneView assignment may emit no property change; still stop a standalone
        // subscription before the manager becomes the sole successful-frame source.
        Disconnect();
        SceneView = context.View;
        try
        {
            created.SetRegistration(context.OverlayManager.Register(this));
        }
        catch
        {
            managerAttachment = null;
            SceneView = previousView;
            Reconnect();
            throw;
        }
        return created;
    }

    internal void UpdateFromOverlayManager(ViewportFrameSnapshot? snapshot)
    {
        managedSnapshot = snapshot;
        UpdateAnchors(snapshot);
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        isLoaded = true;
        if (managerAttachment is null)
        {
            Reconnect();
        }
        else
        {
            UpdateAnchors(managedSnapshot);
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        isLoaded = false;
        Disconnect();
    }

    private void Reconnect()
    {
        Disconnect();
        if (managerAttachment is not null || !isLoaded || SceneView is not Mu3DSceneView view)
        {
            return;
        }
        subscribedView = view;
        subscribedView.FrameSnapshotChanged += OnFrameSnapshotChanged;
        subscribedView.PropertyChanged += OnViewPropertyChanged;
        observedSnapshot = subscribedView.LatestFrameSnapshot;
        UpdateAnchors(observedSnapshot);
    }

    private void Disconnect()
    {
        if (subscribedView is not null)
        {
            subscribedView.FrameSnapshotChanged -= OnFrameSnapshotChanged;
            subscribedView.PropertyChanged -= OnViewPropertyChanged;
            subscribedView = null;
        }
        observedSnapshot = null;
        UpdateAnchors(null);
    }

    private void OnFrameSnapshotChanged(
        object? sender,
        ViewportFrameSnapshotChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, subscribedView))
        {
            return;
        }
        observedSnapshot = e.Snapshot;
        UpdateAnchors(observedSnapshot);
    }

    private void OnViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, subscribedView) ||
            e.PropertyName is not (nameof(Mu3DSceneView.Scene) or nameof(Mu3DSceneView.Camera)))
        {
            return;
        }
        observedSnapshot = null;
        UpdateAnchors(null);
    }

    private void UpdateAnchors(ViewportFrameSnapshot? snapshot)
    {
        ulong revision = ++updateRevision;
        Mu3DSceneView? view = managerAttachment?.View ?? subscribedView;
        foreach (IView child in Children)
        {
            if (revision != updateRevision)
            {
                break;
            }
            if (child is VisualElement visual)
            {
                UpdateAnchor(visual, snapshot, view?.Scene, view?.Camera, revision);
            }
        }
    }

    private void UpdateAnchor(
        VisualElement child,
        ViewportFrameSnapshot? snapshot,
        Scene? scene,
        Camera? camera,
        ulong revision)
    {
        SceneNode? node = GetNode(child);
        bool hideOutside = GetHideWhenOutsideViewport(child);
        bool projected = false;
        ViewportProjection result = default;
        if (node is not null && snapshot is not null && scene is not null && camera is not null &&
            SceneTargetEligibility.IsVisible(scene, node, camera.VisibilityMask))
        {
            Vector3 worldPosition = Vector3.Transform(GetLocalPosition(child), node.WorldMatrix);
            projected = snapshot.TryProject(worldPosition, out result) &&
                double.IsFinite(result.LogicalPosition.X) && double.IsFinite(result.LogicalPosition.Y);
        }

        if (!projected)
        {
            child.IsVisible = false;
            return;
        }

        Microsoft.Maui.Graphics.Point offset = GetOffset(child);
        double x = result.LogicalPosition.X + offset.X;
        double y = result.LogicalPosition.Y + offset.Y;
        bool visible = (!hideOutside || result.IsInsideViewport) &&
            double.IsFinite(x) && double.IsFinite(y);
        child.IsVisible = visible;
        if (!visible || revision != updateRevision)
        {
            return;
        }
        AbsoluteLayout.SetLayoutFlags(child, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        if (revision != updateRevision)
        {
            return;
        }
        AbsoluteLayout.SetLayoutBounds(
            child,
            new Microsoft.Maui.Graphics.Rect(
                x,
                y,
                AbsoluteLayout.AutoSize,
                AbsoluteLayout.AutoSize));
    }

    private static void OnAnchorPropertyChanged(
        BindableObject bindable,
        object oldValue,
        object newValue)
    {
        _ = oldValue;
        _ = newValue;
        if (bindable is Element { Parent: SceneNodeAnchorLayer layer })
        {
            layer.Refresh();
        }
    }

    private sealed class ManagerAttachment(
        SceneNodeAnchorLayer owner,
        Mu3DSceneView attachedView,
        Mu3DSceneView? previousView) : IDisposable
    {
        private SceneNodeAnchorLayer? owner = owner;
        private IDisposable? registration;

        internal Mu3DSceneView View => attachedView;

        internal void SetRegistration(IDisposable value) => registration = value;

        public void Dispose()
        {
            IDisposable? currentRegistration = Interlocked.Exchange(ref registration, null);
            try
            {
                currentRegistration?.Dispose();
            }
            finally
            {
                SceneNodeAnchorLayer? currentOwner = Interlocked.Exchange(ref owner, null);
                if (currentOwner is not null)
                {
                    currentOwner.managedSnapshot = null;
                    if (ReferenceEquals(currentOwner.managerAttachment, this))
                    {
                        currentOwner.managerAttachment = null;
                    }
                    if (ReferenceEquals(currentOwner.SceneView, attachedView))
                    {
                        currentOwner.SceneView = previousView;
                    }
                    currentOwner.Reconnect();
                }
            }
        }
    }
}

using System.Collections.ObjectModel;
using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>Declares one backend-independent scene hierarchy for a <see cref="Mu3DSceneView"/>.</summary>
/// <remarks>
/// The facade owns ordinary managed Core objects only. It does not own a renderer, presentation
/// surface, native device, input policy, or application selection state. Assign the same instance
/// to more than one view when both viewports should observe the same scene state.
/// </remarks>
[ContentProperty(nameof(Children))]
public sealed class Scene3D : BindableObject
{
    private readonly SceneNodeCollection children;
    private readonly List<WeakReference<Mu3DSceneView>> attachedViews = [];
    private readonly HashSet<SceneNode3D> observedNodes = new(ReferenceEqualityComparer.Instance);

    /// <summary>Identifies the <see cref="Name"/> bindable property.</summary>
    public static readonly BindableProperty NameProperty = BindableProperty.Create(
        nameof(Name),
        typeof(string),
        typeof(Scene3D),
        default(string),
        propertyChanged: static (bindable, _, value) =>
        {
            Scene3D scene = (Scene3D)bindable;
            scene.Scene.Name = (string?)value;
            scene.InvalidateViews();
        });

    /// <summary>Identifies the <see cref="Camera"/> bindable property.</summary>
    public static readonly BindableProperty CameraProperty = BindableProperty.Create(
        nameof(Camera),
        typeof(Camera3D),
        typeof(Scene3D),
        default(Camera3D),
        propertyChanged: static (bindable, _, _) => ((Scene3D)bindable).OnCameraChanged());

    /// <summary>Initializes an empty declarative scene.</summary>
    public Scene3D()
    {
        Scene = new Scene();
        children = new SceneNodeCollection(this);
    }

    /// <summary>Gets or sets the optional application-facing scene name.</summary>
    public string? Name
    {
        get => (string?)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    /// <summary>Gets or sets the active declarative camera.</summary>
    /// <remarks>The camera is not implicitly inserted into <see cref="Children"/>.</remarks>
    public Camera3D? Camera
    {
        get => (Camera3D?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    /// <summary>Gets the ordered root nodes populated by nested XAML elements.</summary>
    public IList<SceneNode3D> Children => children;

    /// <summary>Gets the underlying Core scene for imperative interoperation.</summary>
    /// <remarks>
    /// Prefer the declarative children for ordinary composition. After mutating the Core scene
    /// directly, call the owning view's <see cref="Mu3DSceneView.InvalidateScene"/> method.
    /// </remarks>
    public Scene Scene { get; }

    internal void AttachView(Mu3DSceneView view)
    {
        RemoveView(view);
        attachedViews.Add(new WeakReference<Mu3DSceneView>(view));
        view.Scene = Scene;
        view.Camera = Camera?.Camera;
        view.InvalidateScene();
    }

    internal void DetachView(Mu3DSceneView view)
    {
        RemoveView(view);
        if (ReferenceEquals(view.Scene, Scene))
        {
            view.Scene = null;
        }
        if (ReferenceEquals(view.Camera, Camera?.Camera))
        {
            view.Camera = null;
        }
    }

    private void AddNode(SceneNode3D node)
    {
        node.AttachToScene(this);
        Scene.Add(node.CoreNode);
        RefreshNodeObservations();
        InvalidateViews();
    }

    private void RemoveNode(SceneNode3D node)
    {
        _ = Scene.Remove(node.CoreNode);
        node.DetachFromScene(this);
        RefreshNodeObservations();
        InvalidateViews();
    }

    private void ReplaceNode(SceneNode3D oldNode, SceneNode3D newNode)
    {
        _ = Scene.Remove(oldNode.CoreNode);
        oldNode.DetachFromScene(this);
        newNode.AttachToScene(this);
        RebuildRootOrder();
        RefreshNodeObservations();
        InvalidateViews();
    }

    private void ClearNodes(IReadOnlyList<SceneNode3D> removedNodes)
    {
        Scene.Root.ClearChildren();
        foreach (SceneNode3D node in removedNodes)
        {
            node.DetachFromScene(this);
        }
        RefreshNodeObservations();
        InvalidateViews();
    }

    private void RebuildRootOrder()
    {
        Scene.Root.ClearChildren();
        foreach (SceneNode3D node in children)
        {
            Scene.Add(node.CoreNode);
        }
    }

    private void OnCameraChanged()
    {
        RefreshNodeObservations();
        VisitLiveViews(view =>
        {
            if (ReferenceEquals(view.SceneContent, this))
            {
                view.Camera = Camera?.Camera;
            }
        });
        InvalidateViews();
    }

    private void RefreshNodeObservations()
    {
        foreach (SceneNode3D node in observedNodes)
        {
            node.Changed -= OnNodeChanged;
        }
        observedNodes.Clear();

        foreach (SceneNode3D node in children)
        {
            if (observedNodes.Add(node))
            {
                node.Changed += OnNodeChanged;
            }
        }
        if (Camera is { } camera && observedNodes.Add(camera))
        {
            camera.Changed += OnNodeChanged;
        }
    }

    private void OnNodeChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        InvalidateViews();
    }

    private void InvalidateViews() => VisitLiveViews(static view => view.InvalidateScene());

    private void VisitLiveViews(Action<Mu3DSceneView> action)
    {
        for (int index = attachedViews.Count - 1; index >= 0; index--)
        {
            if (!attachedViews[index].TryGetTarget(out Mu3DSceneView? view) ||
                !ReferenceEquals(view.SceneContent, this))
            {
                attachedViews.RemoveAt(index);
                continue;
            }
            action(view);
        }
    }

    private void RemoveView(Mu3DSceneView view)
    {
        for (int index = attachedViews.Count - 1; index >= 0; index--)
        {
            if (!attachedViews[index].TryGetTarget(out Mu3DSceneView? candidate) ||
                ReferenceEquals(candidate, view))
            {
                attachedViews.RemoveAt(index);
            }
        }
    }

    private sealed class SceneNodeCollection(Scene3D owner) : Collection<SceneNode3D>
    {
        protected override void InsertItem(int index, SceneNode3D item)
        {
            ArgumentNullException.ThrowIfNull(item);
            item.ThrowIfOwned();
            base.InsertItem(index, item);
            owner.AddNode(item);
            if (index != Count - 1)
            {
                owner.RebuildRootOrder();
            }
        }

        protected override void SetItem(int index, SceneNode3D item)
        {
            ArgumentNullException.ThrowIfNull(item);
            item.ThrowIfOwned();
            SceneNode3D oldItem = this[index];
            base.SetItem(index, item);
            owner.ReplaceNode(oldItem, item);
        }

        protected override void RemoveItem(int index)
        {
            SceneNode3D item = this[index];
            base.RemoveItem(index);
            owner.RemoveNode(item);
        }

        protected override void ClearItems()
        {
            SceneNode3D[] removedNodes = [.. this];
            base.ClearItems();
            owner.ClearNodes(removedNodes);
        }
    }
}

/// <summary>Provides bindable transform and visibility state for one declarative scene node.</summary>
public abstract class SceneNode3D : BindableObject
{
    private Scene3D? owner;

    /// <summary>Identifies the <see cref="Name"/> bindable property.</summary>
    public static readonly BindableProperty NameProperty = BindableProperty.Create(
        nameof(Name), typeof(string), typeof(SceneNode3D), default(string),
        propertyChanged: static (bindable, _, value) =>
        {
            SceneNode3D node = (SceneNode3D)bindable;
            node.CoreNode.Name = (string?)value;
            node.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="IsVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible), typeof(bool), typeof(SceneNode3D), true,
        propertyChanged: static (bindable, _, value) =>
        {
            SceneNode3D node = (SceneNode3D)bindable;
            node.CoreNode.IsVisible = (bool)value;
            node.NotifyChanged();
        });

    /// <summary>Identifies the <see cref="X"/> bindable property.</summary>
    public static readonly BindableProperty XProperty = CreateTransformProperty(nameof(X), 0f);

    /// <summary>Identifies the <see cref="Y"/> bindable property.</summary>
    public static readonly BindableProperty YProperty = CreateTransformProperty(nameof(Y), 0f);

    /// <summary>Identifies the <see cref="Z"/> bindable property.</summary>
    public static readonly BindableProperty ZProperty = CreateTransformProperty(nameof(Z), 0f);

    /// <summary>Identifies the <see cref="RotationX"/> bindable property.</summary>
    public static readonly BindableProperty RotationXProperty =
        CreateTransformProperty(nameof(RotationX), 0f);

    /// <summary>Identifies the <see cref="RotationY"/> bindable property.</summary>
    public static readonly BindableProperty RotationYProperty =
        CreateTransformProperty(nameof(RotationY), 0f);

    /// <summary>Identifies the <see cref="RotationZ"/> bindable property.</summary>
    public static readonly BindableProperty RotationZProperty =
        CreateTransformProperty(nameof(RotationZ), 0f);

    /// <summary>Identifies the <see cref="ScaleX"/> bindable property.</summary>
    public static readonly BindableProperty ScaleXProperty = CreateTransformProperty(nameof(ScaleX), 1f);

    /// <summary>Identifies the <see cref="ScaleY"/> bindable property.</summary>
    public static readonly BindableProperty ScaleYProperty = CreateTransformProperty(nameof(ScaleY), 1f);

    /// <summary>Identifies the <see cref="ScaleZ"/> bindable property.</summary>
    public static readonly BindableProperty ScaleZProperty = CreateTransformProperty(nameof(ScaleZ), 1f);

    /// <summary>Initializes a declarative wrapper over the supplied Core node.</summary>
    protected SceneNode3D(SceneNode coreNode) =>
        CoreNode = coreNode ?? throw new ArgumentNullException(nameof(coreNode));

    /// <summary>Gets or sets the optional application-facing node name.</summary>
    public string? Name
    {
        get => (string?)GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    /// <summary>Gets or sets whether this node and its subtree participate in rendering.</summary>
    public bool IsVisible
    {
        get => (bool)GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>Gets or sets local translation on the X axis.</summary>
    public float X { get => (float)GetValue(XProperty); set => SetValue(XProperty, value); }

    /// <summary>Gets or sets local translation on the Y axis.</summary>
    public float Y { get => (float)GetValue(YProperty); set => SetValue(YProperty, value); }

    /// <summary>Gets or sets local translation on the Z axis.</summary>
    public float Z { get => (float)GetValue(ZProperty); set => SetValue(ZProperty, value); }

    /// <summary>Gets or sets local X rotation in degrees.</summary>
    public float RotationX
    {
        get => (float)GetValue(RotationXProperty);
        set => SetValue(RotationXProperty, value);
    }

    /// <summary>Gets or sets local Y rotation in degrees.</summary>
    public float RotationY
    {
        get => (float)GetValue(RotationYProperty);
        set => SetValue(RotationYProperty, value);
    }

    /// <summary>Gets or sets local Z rotation in degrees.</summary>
    public float RotationZ
    {
        get => (float)GetValue(RotationZProperty);
        set => SetValue(RotationZProperty, value);
    }

    /// <summary>Gets or sets local scale on the X axis.</summary>
    public float ScaleX
    {
        get => (float)GetValue(ScaleXProperty);
        set => SetValue(ScaleXProperty, value);
    }

    /// <summary>Gets or sets local scale on the Y axis.</summary>
    public float ScaleY
    {
        get => (float)GetValue(ScaleYProperty);
        set => SetValue(ScaleYProperty, value);
    }

    /// <summary>Gets or sets local scale on the Z axis.</summary>
    public float ScaleZ
    {
        get => (float)GetValue(ScaleZProperty);
        set => SetValue(ScaleZProperty, value);
    }

    /// <summary>Gets the underlying Core node for controllers, selection and advanced mutation.</summary>
    public SceneNode CoreNode { get; }

    internal event EventHandler? Changed;

    internal void ThrowIfOwned()
    {
        if (owner is not null)
        {
            throw new InvalidOperationException(
                "A declarative scene node can belong to only one Scene3D child collection.");
        }
    }

    internal void AttachToScene(Scene3D scene)
    {
        ThrowIfOwned();
        owner = scene;
    }

    internal void DetachFromScene(Scene3D scene)
    {
        if (ReferenceEquals(owner, scene))
        {
            owner = null;
        }
    }

    internal void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static BindableProperty CreateTransformProperty(string name, float defaultValue) =>
        BindableProperty.Create(
            name,
            typeof(float),
            typeof(SceneNode3D),
            defaultValue,
            validateValue: static (_, value) => value is float number && float.IsFinite(number),
            propertyChanged: static (bindable, _, _) => ((SceneNode3D)bindable).ApplyTransform());

    private void ApplyTransform()
    {
        CoreNode.Transform.Position = new Vector3(X, Y, Z);
        CoreNode.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(
            DegreesToRadians(RotationY),
            DegreesToRadians(RotationX),
            DegreesToRadians(RotationZ));
        CoreNode.Transform.Scale = new Vector3(ScaleX, ScaleY, ScaleZ);
        NotifyChanged();
    }

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
}

/// <summary>Base class for declarative camera wrappers.</summary>
public abstract class Camera3D : SceneNode3D
{
    /// <summary>Initializes a wrapper over a Core camera.</summary>
    protected Camera3D(Camera camera)
        : base(camera) => Camera = camera;

    /// <summary>Gets the underlying Core camera.</summary>
    public Camera Camera { get; }
}

using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Rendering;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Attaches transform-gizmo pointer input and an ordered HDR-linear overlay pass.</summary>
/// <remarks>
/// The feature borrows its gizmo, hit tester and arbiter. It does not choose a target, perform scene
/// selection, create undo records or own editor tool state. Its pass registration follows the
/// default scene pass and is removed deterministically with the native scene-view handler.
/// </remarks>
public sealed class TransformGizmoSceneViewFeature : BindableObject, ISceneViewFeature
{
    private readonly TransformGizmoPointerBehavior behavior = new();
    private SceneViewFeatureContext? context;
    private IDisposable? renderPassRegistration;
    private Mu3DSceneView? attachedView;

    /// <summary>Identifies the <see cref="Gizmo"/> bindable property.</summary>
    public static readonly BindableProperty GizmoProperty = BindableProperty.Create(
        nameof(Gizmo),
        typeof(TransformGizmo),
        typeof(TransformGizmoSceneViewFeature),
        default(TransformGizmo),
        propertyChanged: static (bindable, _, value) =>
        {
            TransformGizmoSceneViewFeature feature = (TransformGizmoSceneViewFeature)bindable;
            feature.behavior.Gizmo = (TransformGizmo?)value;
            feature.RefreshRenderPass();
        });

    /// <summary>Identifies the <see cref="HitTester"/> bindable property.</summary>
    public static readonly BindableProperty HitTesterProperty = BindableProperty.Create(
        nameof(HitTester),
        typeof(TransformGizmoHitTester),
        typeof(TransformGizmoSceneViewFeature),
        default(TransformGizmoHitTester),
        propertyChanged: static (bindable, _, value) =>
            ((TransformGizmoSceneViewFeature)bindable).behavior.HitTester =
                (TransformGizmoHitTester?)value);

    /// <summary>Identifies the <see cref="ControlArbiter"/> bindable property.</summary>
    public static readonly BindableProperty ControlArbiterProperty = BindableProperty.Create(
        nameof(ControlArbiter),
        typeof(ViewportControlArbiter),
        typeof(TransformGizmoSceneViewFeature),
        default(ViewportControlArbiter),
        propertyChanged: static (bindable, _, value) =>
            ((TransformGizmoSceneViewFeature)bindable).behavior.ControlArbiter =
                (ViewportControlArbiter?)value);

    /// <summary>Identifies the <see cref="RenderStyle"/> bindable property.</summary>
    public static readonly BindableProperty RenderStyleProperty = BindableProperty.Create(
        nameof(RenderStyle),
        typeof(TransformGizmoRenderStyle),
        typeof(TransformGizmoSceneViewFeature),
        TransformGizmoRenderStyle.Default,
        validateValue: static (_, value) => value is TransformGizmoRenderStyle,
        propertyChanged: static (bindable, _, _) =>
            ((TransformGizmoSceneViewFeature)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(TransformGizmoSceneViewFeature),
        true,
        propertyChanged: static (bindable, _, value) =>
        {
            TransformGizmoSceneViewFeature feature = (TransformGizmoSceneViewFeature)bindable;
            feature.behavior.IsEnabled = (bool)value;
            feature.RefreshRenderPass();
        });

    /// <summary>Identifies the <see cref="PassOrder"/> bindable property.</summary>
    public static readonly BindableProperty PassOrderProperty = BindableProperty.Create(
        nameof(PassOrder),
        typeof(int),
        typeof(TransformGizmoSceneViewFeature),
        0,
        propertyChanged: static (bindable, _, _) =>
            ((TransformGizmoSceneViewFeature)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="ScaleFactorPerGizmoLength"/> bindable property.</summary>
    public static readonly BindableProperty ScaleFactorPerGizmoLengthProperty =
        BindableProperty.Create(
            nameof(ScaleFactorPerGizmoLength),
            typeof(float),
            typeof(TransformGizmoSceneViewFeature),
            2f,
            validateValue: static (_, value) =>
                value is float number && float.IsFinite(number) && number > 1f,
            propertyChanged: static (bindable, _, value) =>
                ((TransformGizmoSceneViewFeature)bindable).behavior.ScaleFactorPerGizmoLength =
                    (float)value);

    /// <summary>Gets or sets the borrowed transform gizmo to render and manipulate.</summary>
    public TransformGizmo? Gizmo
    {
        get => (TransformGizmo?)GetValue(GizmoProperty);
        set => SetValue(GizmoProperty, value);
    }

    /// <summary>Gets or sets an optional borrowed handle hit tester.</summary>
    public TransformGizmoHitTester? HitTester
    {
        get => (TransformGizmoHitTester?)GetValue(HitTesterProperty);
        set => SetValue(HitTesterProperty, value);
    }

    /// <summary>Gets or sets the optional borrowed viewport-control arbiter.</summary>
    public ViewportControlArbiter? ControlArbiter
    {
        get => (ViewportControlArbiter?)GetValue(ControlArbiterProperty);
        set => SetValue(ControlArbiterProperty, value);
    }

    /// <summary>Gets or sets the immutable physical-pixel and HDR-linear handle style.</summary>
    public TransformGizmoRenderStyle RenderStyle
    {
        get => (TransformGizmoRenderStyle)GetValue(RenderStyleProperty);
        set => SetValue(RenderStyleProperty, value);
    }

    /// <summary>Gets or sets whether pointer input and handle rendering are enabled.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets or sets the order among passes registered after the default scene pass.</summary>
    public int PassOrder
    {
        get => (int)GetValue(PassOrderProperty);
        set => SetValue(PassOrderProperty, value);
    }

    /// <summary>Gets or sets the scale factor produced by one full projected gizmo-length drag.</summary>
    public float ScaleFactorPerGizmoLength
    {
        get => (float)GetValue(ScaleFactorPerGizmoLengthProperty);
        set => SetValue(ScaleFactorPerGizmoLengthProperty, value);
    }

    /// <summary>Gets the reusable pointer behavior for status and interaction events.</summary>
    /// <remarks>Do not add this behavior to another visual while the feature is attached.</remarks>
    public TransformGizmoPointerBehavior Behavior => behavior;

    /// <summary>Gets whether the current target supplies native pointer/capture input.</summary>
    public bool IsPointerInputAvailable => behavior.IsPointerInputAvailable;

    /// <inheritdoc />
    public IDisposable Attach(SceneViewFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachedView is not null)
        {
            throw new InvalidOperationException("A transform-gizmo feature can attach to only one scene view at a time.");
        }

        Mu3DSceneView view = context.View;
        attachedView = view;
        this.context = context;
        view.Behaviors.Add(behavior);
        try
        {
            RefreshRenderPass();
        }
        catch
        {
            view.Behaviors.Remove(behavior);
            attachedView = null;
            this.context = null;
            throw;
        }
        return new Attachment(this, view);
    }

    private void RefreshRenderPass()
    {
        renderPassRegistration?.Dispose();
        renderPassRegistration = null;
        TransformGizmo? currentGizmo = Gizmo;
        SceneViewFeatureContext? currentContext = context;
        if (!IsEnabled || currentGizmo is null || currentContext is null)
        {
            return;
        }

        TransformGizmoRenderStyle style = RenderStyle;
        renderPassRegistration = currentContext.RegisterRenderPass(
            renderer =>
            {
                if (renderer.WorkingColorSpace is not StandardRgbColorSpaceReference workingSpace)
                {
                    throw new InvalidOperationException(
                        "Transform-gizmo rendering requires a standard linear RGB working space.");
                }
                return new TransformGizmoRenderPass(
                    currentGizmo,
                    style,
                    workingSpace,
                    "SceneView transform gizmo");
            },
            SceneViewRenderPassPlacement.AfterScene,
            PassOrder);
    }

    private void Detach(Mu3DSceneView view)
    {
        if (!ReferenceEquals(attachedView, view))
        {
            return;
        }

        renderPassRegistration?.Dispose();
        renderPassRegistration = null;
        view.Behaviors.Remove(behavior);
        context = null;
        attachedView = null;
    }

    private sealed class Attachment(
        TransformGizmoSceneViewFeature owner,
        Mu3DSceneView view) : IDisposable
    {
        private TransformGizmoSceneViewFeature? owner = owner;

        public void Dispose()
        {
            TransformGizmoSceneViewFeature? released = Interlocked.Exchange(ref owner, null);
            released?.Detach(view);
        }
    }
}

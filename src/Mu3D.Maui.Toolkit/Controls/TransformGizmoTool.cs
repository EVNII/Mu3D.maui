using Mu3D.Maui.Controls;
using Mu3D.Toolkit.Gizmos;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Creates an interactive mixed transform gizmo for an optional declarative target.</summary>
/// <remarks>
/// The tool owns its gizmo, pointer adapter, and overlay-pass registration only while attached.
/// A null target keeps the tool attached but creates no gizmo, which supports binding the target to
/// <see cref="SceneSelectionTool.SelectedNode"/>. Target selection, undo/redo, and editor-mode
/// policy remain application-owned.
/// </remarks>
public sealed class TransformGizmoTool : BindableObject, IViewportTool
{
    private readonly TransformGizmoSceneViewFeature feature = new();
    private ToolAttachment? attachment;

    /// <summary>Identifies the <see cref="Target"/> bindable property.</summary>
    public static readonly BindableProperty TargetProperty = BindableProperty.Create(
        nameof(Target),
        typeof(SceneNode3D),
        typeof(TransformGizmoTool),
        default(SceneNode3D),
        validateValue: static (_, value) => value is null or SceneNode3D,
        propertyChanged: static (bindable, _, _) =>
            ((TransformGizmoTool)bindable).attachment?.Rebuild());

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(TransformGizmoTool),
        true,
        propertyChanged: static (bindable, _, value) =>
            ((TransformGizmoTool)bindable).ApplyEnabled((bool)value));

    /// <summary>Identifies the <see cref="IsTranslateEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsTranslateEnabledProperty = CreateBooleanGizmoProperty(
        nameof(IsTranslateEnabled),
        true,
        static (gizmo, value) => gizmo.IsTranslateEnabled = value);

    /// <summary>Identifies the <see cref="IsRotateEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsRotateEnabledProperty = CreateBooleanGizmoProperty(
        nameof(IsRotateEnabled),
        false,
        static (gizmo, value) => gizmo.IsRotateEnabled = value);

    /// <summary>Identifies the <see cref="IsScaleEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsScaleEnabledProperty = CreateBooleanGizmoProperty(
        nameof(IsScaleEnabled),
        false,
        static (gizmo, value) => gizmo.IsScaleEnabled = value);

    /// <summary>Identifies the <see cref="Space"/> bindable property.</summary>
    public static readonly BindableProperty SpaceProperty = BindableProperty.Create(
        nameof(Space),
        typeof(TransformGizmoSpace),
        typeof(TransformGizmoTool),
        TransformGizmoSpace.World,
        validateValue: static (_, value) =>
            value is TransformGizmoSpace space && Enum.IsDefined(space),
        propertyChanged: static (bindable, _, value) =>
            ((TransformGizmoTool)bindable).Apply(gizmo => gizmo.Space = (TransformGizmoSpace)value));

    /// <summary>Identifies the <see cref="TranslationStep"/> bindable property.</summary>
    public static readonly BindableProperty TranslationStepProperty = CreateStepProperty(
        nameof(TranslationStep),
        static (gizmo, value) => gizmo.TranslationSnap = value);

    /// <summary>Identifies the <see cref="RotationStep"/> bindable property.</summary>
    public static readonly BindableProperty RotationStepProperty = CreateStepProperty(
        nameof(RotationStep),
        static (gizmo, value) => gizmo.RotationSnapRadians = value * (MathF.PI / 180f));

    /// <summary>Identifies the <see cref="ScaleStep"/> bindable property.</summary>
    public static readonly BindableProperty ScaleStepProperty = CreateStepProperty(
        nameof(ScaleStep),
        static (gizmo, value) => gizmo.ScaleSnap = value);

    /// <summary>Identifies the <see cref="ScreenSize"/> bindable property.</summary>
    public static readonly BindableProperty ScreenSizeProperty = BindableProperty.Create(
        nameof(ScreenSize),
        typeof(float),
        typeof(TransformGizmoTool),
        96f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, value) =>
            ((TransformGizmoTool)bindable).Apply(gizmo => gizmo.ScreenSizePixels = (float)value));

    /// <summary>Identifies the <see cref="ScaleFactorPerGizmoLength"/> bindable property.</summary>
    public static readonly BindableProperty ScaleFactorPerGizmoLengthProperty =
        BindableProperty.Create(
            nameof(ScaleFactorPerGizmoLength),
            typeof(float),
            typeof(TransformGizmoTool),
            2f,
            validateValue: static (_, value) =>
                value is float number && float.IsFinite(number) && number > 1f,
            propertyChanged: static (bindable, _, value) =>
            {
                TransformGizmoTool tool = (TransformGizmoTool)bindable;
                tool.feature.ScaleFactorPerGizmoLength = (float)value;
            });

    /// <summary>Identifies the <see cref="PassOrder"/> bindable property.</summary>
    public static readonly BindableProperty PassOrderProperty = BindableProperty.Create(
        nameof(PassOrder),
        typeof(int),
        typeof(TransformGizmoTool),
        0,
        propertyChanged: static (bindable, _, value) =>
            ((TransformGizmoTool)bindable).feature.PassOrder = (int)value);

    /// <summary>Gets or sets the optional application-selected declarative target.</summary>
    public SceneNode3D? Target
    {
        get => (SceneNode3D?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Gets or sets whether pointer input and overlay rendering are enabled.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets or sets whether translation handles are displayed and interactive.</summary>
    public bool IsTranslateEnabled
    {
        get => (bool)GetValue(IsTranslateEnabledProperty);
        set => SetValue(IsTranslateEnabledProperty, value);
    }

    /// <summary>Gets or sets whether rotation handles are displayed and interactive.</summary>
    public bool IsRotateEnabled
    {
        get => (bool)GetValue(IsRotateEnabledProperty);
        set => SetValue(IsRotateEnabledProperty, value);
    }

    /// <summary>Gets or sets whether local-axis and uniform scale handles are displayed.</summary>
    public bool IsScaleEnabled
    {
        get => (bool)GetValue(IsScaleEnabledProperty);
        set => SetValue(IsScaleEnabledProperty, value);
    }

    /// <summary>Gets or sets world or target-local orientation for translation and rotation.</summary>
    public TransformGizmoSpace Space
    {
        get => (TransformGizmoSpace)GetValue(SpaceProperty);
        set => SetValue(SpaceProperty, value);
    }

    /// <summary>Gets or sets the non-negative translation snapping distance.</summary>
    public float TranslationStep
    {
        get => (float)GetValue(TranslationStepProperty);
        set => SetValue(TranslationStepProperty, value);
    }

    /// <summary>Gets or sets the non-negative rotation snapping angle in degrees.</summary>
    public float RotationStep
    {
        get => (float)GetValue(RotationStepProperty);
        set => SetValue(RotationStepProperty, value);
    }

    /// <summary>Gets or sets the non-negative scale-factor snapping interval around one.</summary>
    public float ScaleStep
    {
        get => (float)GetValue(ScaleStepProperty);
        set => SetValue(ScaleStepProperty, value);
    }

    /// <summary>Gets or sets the positive desired physical-pixel handle size.</summary>
    public float ScreenSize
    {
        get => (float)GetValue(ScreenSizeProperty);
        set => SetValue(ScreenSizeProperty, value);
    }

    /// <summary>Gets or sets the scale factor produced by one projected gizmo-length drag.</summary>
    public float ScaleFactorPerGizmoLength
    {
        get => (float)GetValue(ScaleFactorPerGizmoLengthProperty);
        set => SetValue(ScaleFactorPerGizmoLengthProperty, value);
    }

    /// <summary>Gets or sets ordering among passes registered after the scene.</summary>
    public int PassOrder
    {
        get => (int)GetValue(PassOrderProperty);
        set => SetValue(PassOrderProperty, value);
    }

    /// <summary>Gets the attachment-owned gizmo, or null while detached.</summary>
    /// <remarks>Application code may subscribe to events but must not dispose or retain it.</remarks>
    public TransformGizmo? Gizmo => attachment?.Gizmo;

    /// <summary>Gets the reusable pointer behavior for availability and interaction-failure events.</summary>
    public TransformGizmoPointerBehavior Behavior => feature.Behavior;

    /// <summary>Gets whether native pointer/capture input is available on the attached target.</summary>
    public bool IsPointerInputAvailable => feature.IsPointerInputAvailable;

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("A TransformGizmoTool can attach only once at a time.");
        }
        ToolAttachment created = new(this, context, feature);
        attachment = created;
        try
        {
            created.Rebuild();
        }
        catch
        {
            attachment = null;
            created.Dispose();
            throw;
        }
        return created;
    }

    private void ApplyEnabled(bool value)
    {
        feature.IsEnabled = value;
        attachment?.Context.InvalidateScene();
    }

    private void Apply(Action<TransformGizmo> update)
    {
        TransformGizmo? gizmo = attachment?.Gizmo;
        if (gizmo is null)
        {
            return;
        }
        if (gizmo.IsInteracting)
        {
            gizmo.CancelInteraction();
        }
        update(gizmo);
        attachment!.Context.InvalidateScene();
    }

    private static BindableProperty CreateBooleanGizmoProperty(
        string name,
        bool defaultValue,
        Action<TransformGizmo, bool> update) => BindableProperty.Create(
        name,
        typeof(bool),
        typeof(TransformGizmoTool),
        defaultValue,
        propertyChanged: (bindable, _, value) =>
            ((TransformGizmoTool)bindable).Apply(gizmo => update(gizmo, (bool)value)));

    private static BindableProperty CreateStepProperty(
        string name,
        Action<TransformGizmo, float> update) => BindableProperty.Create(
        name,
        typeof(float),
        typeof(TransformGizmoTool),
        0f,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number >= 0f,
        propertyChanged: (bindable, _, value) =>
            ((TransformGizmoTool)bindable).Apply(gizmo => update(gizmo, (float)value)));

    private static bool IsPositiveFinite(object value) =>
        value is float number && float.IsFinite(number) && number > 0f;

    private sealed class ToolAttachment(
        TransformGizmoTool owner,
        ViewportToolContext context,
        TransformGizmoSceneViewFeature feature) : IDisposable
    {
        private IDisposable? featureLease;
        private bool disposed;

        internal ViewportToolContext Context => context;

        internal TransformGizmo? Gizmo { get; private set; }

        internal void Rebuild()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ReleaseOwnedState();
            if (owner.Target is not { } target)
            {
                return;
            }
            TransformGizmo gizmo = new(
                target.CoreNode,
                new ViewportToolFrameRequester(context))
            {
                IsTranslateEnabled = owner.IsTranslateEnabled,
                IsRotateEnabled = owner.IsRotateEnabled,
                IsScaleEnabled = owner.IsScaleEnabled,
                Space = owner.Space,
                TranslationSnap = owner.TranslationStep,
                RotationSnapRadians = owner.RotationStep * (MathF.PI / 180f),
                ScaleSnap = owner.ScaleStep,
                ScreenSizePixels = owner.ScreenSize,
            };
            Gizmo = gizmo;
            feature.Gizmo = gizmo;
            feature.ControlArbiter = context.ControlArbiter;
            feature.IsEnabled = owner.IsEnabled;
            feature.ScaleFactorPerGizmoLength = owner.ScaleFactorPerGizmoLength;
            feature.PassOrder = owner.PassOrder;
            try
            {
                featureLease = feature.Attach(context.SceneViewContext);
            }
            catch
            {
                ReleaseOwnedState();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            ReleaseOwnedState();
            if (ReferenceEquals(owner.attachment, this))
            {
                owner.attachment = null;
            }
        }

        private void ReleaseOwnedState()
        {
            featureLease?.Dispose();
            featureLease = null;
            feature.Gizmo = null;
            feature.ControlArbiter = null;
            Gizmo?.Dispose();
            Gizmo = null;
        }
    }
}

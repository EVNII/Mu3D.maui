using System.Numerics;
using System.Windows.Input;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Provides declarative first-person camera navigation for an explicit XAML camera.</summary>
/// <remarks>
/// Fly uses the same physical-device <see cref="ViewportInput"/> and normalized Rotate, Pan and
/// Dolly routes as Orbit and Map. It interprets them as look, strafe/lift and forward/backward
/// movement. The tool owns one <see cref="FlyController"/> only while attached to
/// <see cref="ViewportTools"/>.
/// </remarks>
public sealed class FlyTool : BindableObject, IViewportTool
{
    private readonly Command<FlyNavigationAction> navigationCommand;
    private readonly ViewportNavigationBehavior behavior = new();
    private ToolAttachment? attachment;

    /// <summary>Initializes a Fly tool with first-person device-route defaults.</summary>
    public FlyTool()
    {
        navigationCommand = new Command<FlyNavigationAction>(
            action => _ = TryNavigate(action),
            CanNavigate);

        Input = behavior.Input;
        Input.Mouse.LeftButtonDragAction = ViewportDragAction.Rotate;
        Input.Mouse.MiddleButtonDragAction = ViewportDragAction.None;
        Input.Mouse.RightButtonDragAction = ViewportDragAction.Pan;
        Input.Mouse.WheelAction = ViewportScalarAction.Dolly;
        Input.Trackpad.TwoFingerDragAction = ViewportDragAction.Pan;
        Input.Trackpad.PinchAction = ViewportScalarAction.Dolly;
        Input.Touchscreen.OneFingerDragAction = ViewportDragAction.Rotate;
        Input.Touchscreen.TwoFingerDragAction = ViewportDragAction.Pan;
        Input.Touchscreen.PinchAction = ViewportScalarAction.Dolly;

        Input.Keyboard.RotateLeftKey = ViewportKey.LeftArrow;
        Input.Keyboard.RotateRightKey = ViewportKey.RightArrow;
        Input.Keyboard.RotateUpKey = ViewportKey.UpArrow;
        Input.Keyboard.RotateDownKey = ViewportKey.DownArrow;
        Input.Keyboard.PanLeftKey = ViewportKey.A;
        Input.Keyboard.PanRightKey = ViewportKey.D;
        Input.Keyboard.PanUpKey = ViewportKey.E;
        Input.Keyboard.PanDownKey = ViewportKey.Q;
        Input.Keyboard.DollyInKey = ViewportKey.W;
        Input.Keyboard.DollyOutKey = ViewportKey.S;
        Input.Keyboard.AlternateDollyInKey = ViewportKey.None;
        Input.Keyboard.AlternateDollyOutKey = ViewportKey.None;

        ApplyBehaviorSettings();
    }

    /// <summary>Identifies the <see cref="Camera"/> bindable property.</summary>
    public static readonly BindableProperty CameraProperty = BindableProperty.Create(
        nameof(Camera),
        typeof(PerspectiveCamera3D),
        typeof(FlyTool),
        default(PerspectiveCamera3D),
        propertyChanged: static (bindable, _, _) => ((FlyTool)bindable).attachment?.Rebuild());

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(FlyTool),
        true,
        propertyChanged: static (bindable, _, value) => ((FlyTool)bindable).ApplyEnabled((bool)value));

    /// <summary>Identifies the <see cref="Input"/> bindable property.</summary>
    public static readonly BindableProperty InputProperty = BindableProperty.Create(
        nameof(Input),
        typeof(ViewportInput),
        typeof(FlyTool),
        defaultValue: null,
        validateValue: static (_, value) => value is ViewportInput,
        propertyChanged: static (bindable, _, value) =>
            ((FlyTool)bindable).behavior.Input = (ViewportInput)value);

    /// <summary>Identifies the <see cref="DampingEnabled"/> bindable property.</summary>
    public static readonly BindableProperty DampingEnabledProperty = BindableProperty.Create(
        nameof(DampingEnabled),
        typeof(bool),
        typeof(FlyTool),
        true,
        propertyChanged: static (bindable, _, value) =>
        {
            if (((FlyTool)bindable).attachment?.Controller is FlyController controller)
            {
                controller.DampingEnabled = (bool)value;
            }
        });

    /// <summary>Identifies the <see cref="DampingTime"/> bindable property.</summary>
    public static readonly BindableProperty DampingTimeProperty = BindableProperty.Create(
        nameof(DampingTime),
        typeof(float),
        typeof(FlyTool),
        0.1f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, value) =>
        {
            if (((FlyTool)bindable).attachment?.Controller is FlyController controller)
            {
                controller.DampingTime = (float)value;
            }
        });

    /// <summary>Identifies the <see cref="MovementStep"/> bindable property.</summary>
    public static readonly BindableProperty MovementStepProperty = BindableProperty.Create(
        nameof(MovementStep),
        typeof(float),
        typeof(FlyTool),
        0.5f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, _) => ((FlyTool)bindable).ApplyBehaviorSettings());

    /// <summary>Identifies the <see cref="LookStepRadians"/> bindable property.</summary>
    public static readonly BindableProperty LookStepRadiansProperty = BindableProperty.Create(
        nameof(LookStepRadians),
        typeof(float),
        typeof(FlyTool),
        MathF.PI / 36f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, _) => ((FlyTool)bindable).ApplyBehaviorSettings());

    /// <summary>Identifies the <see cref="RotationSensitivity"/> bindable property.</summary>
    public static readonly BindableProperty RotationSensitivityProperty = CreateNonNegativeProperty(
        nameof(RotationSensitivity),
        1f,
        static (controller, value) => controller.RotationSensitivity = value);

    /// <summary>Identifies the <see cref="PanSensitivity"/> bindable property.</summary>
    public static readonly BindableProperty PanSensitivityProperty = CreateNonNegativeProperty(
        nameof(PanSensitivity),
        1f,
        static (controller, value) => controller.PanSensitivity = value);

    /// <summary>Identifies the <see cref="DollySensitivity"/> bindable property.</summary>
    public static readonly BindableProperty DollySensitivityProperty = CreateNonNegativeProperty(
        nameof(DollySensitivity),
        1f,
        static (controller, value) => controller.DollySensitivity = value);

    /// <summary>Identifies the <see cref="RotationRadiansPerViewport"/> bindable property.</summary>
    public static readonly BindableProperty RotationRadiansPerViewportProperty =
        CreateBehaviorScaleProperty(nameof(RotationRadiansPerViewport), MathF.PI);

    /// <summary>Identifies the <see cref="PanUnitsPerViewport"/> bindable property.</summary>
    public static readonly BindableProperty PanUnitsPerViewportProperty =
        CreateBehaviorScaleProperty(nameof(PanUnitsPerViewport), 4f);

    /// <summary>Identifies the <see cref="PinchDollyMultiplier"/> bindable property.</summary>
    public static readonly BindableProperty PinchDollyMultiplierProperty =
        CreateBehaviorScaleProperty(nameof(PinchDollyMultiplier), 6f);

    /// <summary>Identifies the <see cref="WheelDollyStep"/> bindable property.</summary>
    public static readonly BindableProperty WheelDollyStepProperty =
        CreateBehaviorScaleProperty(nameof(WheelDollyStep), 0.75f);

    /// <summary>Gets or sets the explicit declarative camera to control.</summary>
    public PerspectiveCamera3D? Camera
    {
        get => (PerspectiveCamera3D?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    /// <summary>Gets or sets whether commands are accepted.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets or sets all physical-device input routes for this viewport tool.</summary>
    public ViewportInput Input
    {
        get => (ViewportInput)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    /// <summary>Gets or sets whether accepted movement and look commands are VSync-smoothed.</summary>
    public bool DampingEnabled
    {
        get => (bool)GetValue(DampingEnabledProperty);
        set => SetValue(DampingEnabledProperty, value);
    }

    /// <summary>Gets or sets the positive exponential damping time in seconds.</summary>
    public float DampingTime
    {
        get => (float)GetValue(DampingTimeProperty);
        set => SetValue(DampingTimeProperty, value);
    }

    /// <summary>Gets or sets the positive world-unit distance of one discrete movement command.</summary>
    public float MovementStep
    {
        get => (float)GetValue(MovementStepProperty);
        set => SetValue(MovementStepProperty, value);
    }

    /// <summary>Gets or sets the positive radian amount of one discrete look command.</summary>
    public float LookStepRadians
    {
        get => (float)GetValue(LookStepRadiansProperty);
        set => SetValue(LookStepRadiansProperty, value);
    }

    /// <summary>Gets or sets the non-negative Rotate multiplier.</summary>
    public float RotationSensitivity
    {
        get => (float)GetValue(RotationSensitivityProperty);
        set => SetValue(RotationSensitivityProperty, value);
    }

    /// <summary>Gets or sets the non-negative Pan/local-X-Y multiplier.</summary>
    public float PanSensitivity
    {
        get => (float)GetValue(PanSensitivityProperty);
        set => SetValue(PanSensitivityProperty, value);
    }

    /// <summary>Gets or sets the non-negative Dolly multiplier.</summary>
    public float DollySensitivity
    {
        get => (float)GetValue(DollySensitivityProperty);
        set => SetValue(DollySensitivityProperty, value);
    }

    /// <summary>Gets or sets Rotate radians represented by one short-side viewport drag.</summary>
    public float RotationRadiansPerViewport
    {
        get => (float)GetValue(RotationRadiansPerViewportProperty);
        set => SetValue(RotationRadiansPerViewportProperty, value);
    }

    /// <summary>Gets or sets Pan world units represented by one short-side viewport drag.</summary>
    public float PanUnitsPerViewport
    {
        get => (float)GetValue(PanUnitsPerViewportProperty);
        set => SetValue(PanUnitsPerViewportProperty, value);
    }

    /// <summary>Gets or sets forward units represented by one natural-log pinch scale.</summary>
    public float PinchDollyMultiplier
    {
        get => (float)GetValue(PinchDollyMultiplierProperty);
        set => SetValue(PinchDollyMultiplierProperty, value);
    }

    /// <summary>Gets or sets forward units represented by one wheel detent.</summary>
    public float WheelDollyStep
    {
        get => (float)GetValue(WheelDollyStepProperty);
        set => SetValue(WheelDollyStepProperty, value);
    }

    /// <summary>Gets the stable command that accepts a <see cref="FlyNavigationAction"/>.</summary>
    public ICommand NavigationCommand => navigationCommand;

    /// <summary>Gets the attachment-owned controller, or null while detached.</summary>
    public FlyController? Controller => attachment?.Controller;

    /// <summary>Gets the shared MAUI device-input navigation behavior.</summary>
    /// <remarks>Do not add this behavior to another visual while the tool is attached.</remarks>
    public ViewportNavigationBehavior Behavior => behavior;

    /// <summary>Attempts one discrete first-person command.</summary>
    /// <param name="action">The normalized action to submit.</param>
    /// <returns><see langword="true"/> when the attached controller accepted the command.</returns>
    public bool TryNavigate(FlyNavigationAction action)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
        ToolAttachment? current = attachment;
        FlyController? controller = current?.Controller;
        if (!IsEnabled || current is null || controller is null)
        {
            return false;
        }

        return FlyNavigationState.TryApply(
            controller,
            current.Context.ControlArbiter,
            action,
            MovementStep,
            LookStepRadians);
    }

    /// <summary>Attempts one atomic application-supplied movement and look delta.</summary>
    /// <param name="localMovement">Local world-unit movement: X right, Y up and Z forward.</param>
    /// <param name="lookDeltaRadians">Yaw/pitch radians: positive X right and positive Y up.</param>
    /// <returns><see langword="true"/> when the attached controller accepted a component.</returns>
    public bool TryNavigate(Vector3 localMovement, Vector2 lookDeltaRadians)
    {
        FlyNavigationState.ValidateDelta(localMovement, lookDeltaRadians);
        ToolAttachment? current = attachment;
        FlyController? controller = current?.Controller;
        return IsEnabled && current is not null && controller is not null &&
            FlyNavigationState.TryApplyDelta(
                controller,
                current.Context.ControlArbiter,
                localMovement,
                lookDeltaRadians);
    }

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("A FlyTool can attach only once at a time.");
        }
        if (Camera is null)
        {
            throw new InvalidOperationException("FlyTool requires an explicit Camera.");
        }

        ToolAttachment created = new(this, context);
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
        behavior.IsEnabled = value;
        if (attachment?.Controller is FlyController controller)
        {
            controller.IsEnabled = value;
        }
        RefreshCommandState();
    }

    private void ApplyBehaviorSettings()
    {
        behavior.RotationRadiansPerViewport = RotationRadiansPerViewport;
        behavior.PanUnitsPerViewport = PanUnitsPerViewport;
        behavior.PinchDollyMultiplier = PinchDollyMultiplier;
        behavior.WheelDollyStep = WheelDollyStep;
        behavior.KeyboardRotationStepRadians = LookStepRadians;
        behavior.KeyboardPanStep = MovementStep;
        behavior.KeyboardDollyStep = MovementStep;
        behavior.UseIsotropicDragNormalization = true;
        behavior.InvertHorizontalRotation = true;
        behavior.IsEnabled = IsEnabled;
    }

    private bool CanNavigate(FlyNavigationAction action) =>
        Enum.IsDefined(action) && IsEnabled && attachment?.Controller is not null;

    private void RefreshCommandState() => navigationCommand.ChangeCanExecute();

    private static BindableProperty CreateBehaviorScaleProperty(string name, float defaultValue) =>
        BindableProperty.Create(
            name,
            typeof(float),
            typeof(FlyTool),
            defaultValue,
            validateValue: static (_, value) => IsPositiveFinite(value),
            propertyChanged: static (bindable, _, _) =>
                ((FlyTool)bindable).ApplyBehaviorSettings());

    private static BindableProperty CreateNonNegativeProperty(
        string name,
        float defaultValue,
        Action<FlyController, float> update) => BindableProperty.Create(
            name,
            typeof(float),
            typeof(FlyTool),
            defaultValue,
            validateValue: static (_, value) =>
                value is float number && float.IsFinite(number) && number >= 0f,
            propertyChanged: (bindable, _, value) =>
            {
                if (((FlyTool)bindable).attachment?.Controller is FlyController controller)
                {
                    update(controller, (float)value);
                }
            });

    private static bool IsPositiveFinite(object value) =>
        value is float number && float.IsFinite(number) && number > 0f;

    private sealed class ToolAttachment : IDisposable
    {
        private readonly FlyTool owner;
        private readonly Mu3DSceneView view;
        private bool behaviorAttached;
        private bool disposed;

        internal ToolAttachment(FlyTool owner, ViewportToolContext context)
        {
            this.owner = owner;
            Context = context;
            view = context.View;
        }

        internal ViewportToolContext Context { get; }

        internal FlyController? Controller { get; private set; }

        internal void Rebuild()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ReleaseOwnedState();
            if (owner.Camera is not { } camera)
            {
                owner.RefreshCommandState();
                return;
            }

            Controller = new FlyController(
                camera.PerspectiveCamera,
                new ViewportToolFrameRequester(Context))
            {
                IsEnabled = owner.IsEnabled,
                DampingEnabled = owner.DampingEnabled,
                DampingTime = owner.DampingTime,
                RotationSensitivity = owner.RotationSensitivity,
                PanSensitivity = owner.PanSensitivity,
                DollySensitivity = owner.DollySensitivity,
            };
            owner.behavior.Controller = Controller;
            owner.behavior.ControlArbiter = Context.ControlArbiter;
            owner.ApplyBehaviorSettings();
            view.Behaviors.Add(owner.behavior);
            behaviorAttached = true;
            owner.RefreshCommandState();
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
            owner.RefreshCommandState();
        }

        private void ReleaseOwnedState()
        {
            if (behaviorAttached)
            {
                view.Behaviors.Remove(owner.behavior);
                behaviorAttached = false;
            }
            owner.behavior.Controller = null;
            owner.behavior.ControlArbiter = null;
            Controller?.Dispose();
            Controller = null;
        }
    }
}

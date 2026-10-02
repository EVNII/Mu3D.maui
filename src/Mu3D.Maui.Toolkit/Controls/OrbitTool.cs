using System.Numerics;
using System.Windows.Input;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Viewports;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Creates and attaches an orbit controller for an explicit declarative camera.</summary>
/// <remarks>
/// The tool owns its controller only for the active <see cref="ViewportTools"/> attachment. Camera
/// state remains owned by the application, and no timer, selection, or navigation policy is added.
/// </remarks>
public class OrbitTool : BindableObject, IViewportTool
{
    private const float PolarEpsilon = 0.0001f;
    private readonly OrbitSceneViewFeature feature = new();
    private readonly Command<OrbitNavigationAction> navigationCommand;
    private ToolAttachment? attachment;

    /// <summary>Initializes a declarative orbit tool and its stable MAUI navigation command.</summary>
    public OrbitTool()
    {
        navigationCommand = new Command<OrbitNavigationAction>(
            execute: action => _ = TryNavigate(action),
            canExecute: CanNavigate);
        Input = feature.Behavior.Input;
    }

    /// <summary>Identifies the <see cref="Camera"/> bindable property.</summary>
    public static readonly BindableProperty CameraProperty = BindableProperty.Create(
        nameof(Camera),
        typeof(PerspectiveCamera3D),
        typeof(OrbitTool),
        default(PerspectiveCamera3D),
        validateValue: static (_, value) => value is null or PerspectiveCamera3D,
        propertyChanged: static (bindable, _, _) => ((OrbitTool)bindable).attachment?.Rebuild());

    /// <summary>Identifies the <see cref="TargetX"/> bindable property.</summary>
    public static readonly BindableProperty TargetXProperty = CreateTargetProperty(nameof(TargetX));

    /// <summary>Identifies the <see cref="TargetY"/> bindable property.</summary>
    public static readonly BindableProperty TargetYProperty = CreateTargetProperty(nameof(TargetY));

    /// <summary>Identifies the <see cref="TargetZ"/> bindable property.</summary>
    public static readonly BindableProperty TargetZProperty = CreateTargetProperty(nameof(TargetZ));

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(OrbitTool),
        true,
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).ApplyEnabled((bool)value));

    /// <summary>Identifies the <see cref="Input"/> bindable property.</summary>
    public static readonly BindableProperty InputProperty = BindableProperty.Create(
        nameof(Input),
        typeof(ViewportInput),
        typeof(OrbitTool),
        defaultValue: null,
        validateValue: static (_, value) => value is ViewportInput,
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Behavior.Input = (ViewportInput)value);

    /// <summary>Identifies the <see cref="DampingEnabled"/> bindable property.</summary>
    public static readonly BindableProperty DampingEnabledProperty = BindableProperty.Create(
        nameof(DampingEnabled),
        typeof(bool),
        typeof(OrbitTool),
        true,
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Apply(controller => controller.DampingEnabled = (bool)value));

    /// <summary>Identifies the <see cref="DampingTime"/> bindable property.</summary>
    public static readonly BindableProperty DampingTimeProperty = CreatePositiveProperty(
        nameof(DampingTime),
        0.1f,
        static (controller, value) => controller.DampingTime = value);

    /// <summary>Identifies the <see cref="MinimumDistance"/> bindable property.</summary>
    public static readonly BindableProperty MinimumDistanceProperty = BindableProperty.Create(
        nameof(MinimumDistance),
        typeof(float),
        typeof(OrbitTool),
        0.25f,
        validateValue: static (bindable, value) =>
            IsPositiveFinite(value) && (float)value <= ((OrbitTool)bindable).MaximumDistance,
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Apply(controller => controller.MinimumDistance = (float)value));

    /// <summary>Identifies the <see cref="MaximumDistance"/> bindable property.</summary>
    public static readonly BindableProperty MaximumDistanceProperty = BindableProperty.Create(
        nameof(MaximumDistance),
        typeof(float),
        typeof(OrbitTool),
        1000f,
        validateValue: static (bindable, value) =>
            IsPositiveFinite(value) && (float)value >= ((OrbitTool)bindable).MinimumDistance,
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Apply(controller => controller.MaximumDistance = (float)value));

    /// <summary>Identifies the <see cref="PanMode"/> bindable property.</summary>
    public static readonly BindableProperty PanModeProperty = BindableProperty.Create(
        nameof(PanMode),
        typeof(OrbitPanMode),
        typeof(OrbitTool),
        OrbitPanMode.ScreenSpace,
        validateValue: static (_, value) =>
            value is OrbitPanMode mode && Enum.IsDefined(mode),
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Apply(controller => controller.PanMode = (OrbitPanMode)value));

    /// <summary>Identifies the <see cref="MinimumPolarAngle"/> bindable property.</summary>
    public static readonly BindableProperty MinimumPolarAngleProperty = BindableProperty.Create(
        nameof(MinimumPolarAngle),
        typeof(float),
        typeof(OrbitTool),
        PolarEpsilon,
        validateValue: static (bindable, value) =>
            IsPolarAngle(value) && (float)value <= ((OrbitTool)bindable).MaximumPolarAngle,
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Apply(
                controller => controller.MinimumPolarAngle = (float)value));

    /// <summary>Identifies the <see cref="MaximumPolarAngle"/> bindable property.</summary>
    public static readonly BindableProperty MaximumPolarAngleProperty = BindableProperty.Create(
        nameof(MaximumPolarAngle),
        typeof(float),
        typeof(OrbitTool),
        MathF.PI - PolarEpsilon,
        validateValue: static (bindable, value) =>
            IsPolarAngle(value) && (float)value >= ((OrbitTool)bindable).MinimumPolarAngle,
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Apply(
                controller => controller.MaximumPolarAngle = (float)value));

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

    /// <summary>Identifies the <see cref="NavigationRotationStepRadians"/> bindable property.</summary>
    public static readonly BindableProperty NavigationRotationStepRadiansProperty =
        BindableProperty.Create(
            nameof(NavigationRotationStepRadians),
            typeof(float),
            typeof(OrbitTool),
            MathF.PI / 36f,
            validateValue: static (_, value) => IsPositiveFinite(value),
            propertyChanged: static (bindable, _, value) =>
                ((OrbitTool)bindable).Behavior.KeyboardRotationStepRadians = (float)value);

    /// <summary>Identifies the <see cref="NavigationPanStep"/> bindable property.</summary>
    public static readonly BindableProperty NavigationPanStepProperty = BindableProperty.Create(
        nameof(NavigationPanStep),
        typeof(float),
        typeof(OrbitTool),
        0.05f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Behavior.KeyboardPanStep = (float)value);

    /// <summary>Identifies the <see cref="NavigationDollyStep"/> bindable property.</summary>
    public static readonly BindableProperty NavigationDollyStepProperty = BindableProperty.Create(
        nameof(NavigationDollyStep),
        typeof(float),
        typeof(OrbitTool),
        0.12f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Behavior.KeyboardDollyStep = (float)value);

    /// <summary>Identifies the <see cref="PinchDollyMultiplier"/> bindable property.</summary>
    public static readonly BindableProperty PinchDollyMultiplierProperty = BindableProperty.Create(
        nameof(PinchDollyMultiplier),
        typeof(float),
        typeof(OrbitTool),
        1.6f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, value) =>
            ((OrbitTool)bindable).Behavior.PinchDollyMultiplier = (float)value);

    /// <summary>Gets or sets the explicit declarative perspective camera to control.</summary>
    public PerspectiveCamera3D? Camera
    {
        get => (PerspectiveCamera3D?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    /// <summary>Gets or sets the world-space orbit target X coordinate.</summary>
    public float TargetX
    {
        get => (float)GetValue(TargetXProperty);
        set => SetValue(TargetXProperty, value);
    }

    /// <summary>Gets or sets the world-space orbit target Y coordinate.</summary>
    public float TargetY
    {
        get => (float)GetValue(TargetYProperty);
        set => SetValue(TargetYProperty, value);
    }

    /// <summary>Gets or sets the world-space orbit target Z coordinate.</summary>
    public float TargetZ
    {
        get => (float)GetValue(TargetZProperty);
        set => SetValue(TargetZProperty, value);
    }

    /// <summary>Gets or sets whether orbit input is accepted.</summary>
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

    /// <summary>Gets or sets whether accepted motion is consumed over VSync-driven frames.</summary>
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

    /// <summary>Gets or sets the positive minimum camera-to-target distance.</summary>
    public float MinimumDistance
    {
        get => (float)GetValue(MinimumDistanceProperty);
        set => SetValue(MinimumDistanceProperty, value);
    }

    /// <summary>Gets or sets the finite maximum camera-to-target distance.</summary>
    public float MaximumDistance
    {
        get => (float)GetValue(MaximumDistanceProperty);
        set => SetValue(MaximumDistanceProperty, value);
    }

    /// <summary>Gets or sets the world-space basis used by pan commands.</summary>
    public OrbitPanMode PanMode
    {
        get => (OrbitPanMode)GetValue(PanModeProperty);
        set => SetValue(PanModeProperty, value);
    }

    /// <summary>Gets or sets the positive minimum polar angle in radians.</summary>
    public float MinimumPolarAngle
    {
        get => (float)GetValue(MinimumPolarAngleProperty);
        set => SetValue(MinimumPolarAngleProperty, value);
    }

    /// <summary>Gets or sets the maximum polar angle below pi radians.</summary>
    public float MaximumPolarAngle
    {
        get => (float)GetValue(MaximumPolarAngleProperty);
        set => SetValue(MaximumPolarAngleProperty, value);
    }

    /// <summary>Gets or sets the non-negative rotation multiplier.</summary>
    public float RotationSensitivity
    {
        get => (float)GetValue(RotationSensitivityProperty);
        set => SetValue(RotationSensitivityProperty, value);
    }

    /// <summary>Gets or sets the non-negative pan multiplier.</summary>
    public float PanSensitivity
    {
        get => (float)GetValue(PanSensitivityProperty);
        set => SetValue(PanSensitivityProperty, value);
    }

    /// <summary>Gets or sets the non-negative dolly multiplier.</summary>
    public float DollySensitivity
    {
        get => (float)GetValue(DollySensitivityProperty);
        set => SetValue(DollySensitivityProperty, value);
    }

    /// <summary>Gets or sets the positive rotation amount used by discrete navigation commands.</summary>
    public float NavigationRotationStepRadians
    {
        get => (float)GetValue(NavigationRotationStepRadiansProperty);
        set => SetValue(NavigationRotationStepRadiansProperty, value);
    }

    /// <summary>Gets or sets the positive viewport-span pan amount used by navigation commands.</summary>
    public float NavigationPanStep
    {
        get => (float)GetValue(NavigationPanStepProperty);
        set => SetValue(NavigationPanStepProperty, value);
    }

    /// <summary>Gets or sets the positive logarithmic dolly amount used by navigation commands.</summary>
    public float NavigationDollyStep
    {
        get => (float)GetValue(NavigationDollyStepProperty);
        set => SetValue(NavigationDollyStepProperty, value);
    }

    /// <summary>Gets or sets the positive multiplier applied to logarithmic pinch dolly.</summary>
    public float PinchDollyMultiplier
    {
        get => (float)GetValue(PinchDollyMultiplierProperty);
        set => SetValue(PinchDollyMultiplierProperty, value);
    }

    /// <summary>Gets the attachment-owned controller, or null while detached.</summary>
    /// <remarks>Application code may subscribe to events but must not dispose or retain it.</remarks>
    public OrbitController? Controller => attachment?.Controller;

    /// <summary>
    /// Gets the stable MAUI command that accepts an <see cref="OrbitNavigationAction"/> parameter.
    /// </summary>
    /// <remarks>
    /// Bind this command to MAUI buttons, menu items, gestures or keyboard accelerators. It can
    /// execute only while the tool has an enabled attached controller.
    /// </remarks>
    public ICommand NavigationCommand => navigationCommand;

    /// <summary>Gets the reusable MAUI gesture behavior for advanced input settings and events.</summary>
    public ViewportNavigationBehavior Behavior => feature.Behavior;

    /// <summary>Attempts one discrete application-supplied navigation action.</summary>
    /// <param name="action">The normalized action to submit.</param>
    /// <returns>True when the attached controller accepted or queued the command.</returns>
    public bool TryNavigate(OrbitNavigationAction action)
    {
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
        return CanNavigate(action) && Behavior.ProcessNavigationAction(
            action,
            NavigationRotationStepRadians,
            NavigationPanStep,
            NavigationDollyStep);
    }

    /// <summary>Attempts one atomic application-supplied composite navigation delta.</summary>
    /// <param name="rotationRadians">
    /// The signed yaw/pitch delta in radians. Positive X rotates right and positive Y rotates down.
    /// </param>
    /// <param name="panViewportDelta">
    /// The signed viewport-relative pan delta. Positive X pans right and positive Y pans up.
    /// </param>
    /// <param name="dollyDelta">The signed logarithmic dolly delta; positive moves inward.</param>
    /// <returns>True when the attached controller accepted at least one non-zero component.</returns>
    /// <remarks>
    /// Applications that own held-key or gamepad state can normalize simultaneous axes, multiply
    /// them by elapsed time and submit one delta per VSync frame. The complete delta uses one
    /// camera-priority arbitration lease, so diagonal input cannot be split by another tool.
    /// </remarks>
    public bool TryNavigate(
        Vector2 rotationRadians,
        Vector2 panViewportDelta,
        float dollyDelta)
    {
        ViewportNavigationInputState.ValidateNavigationDelta(
            rotationRadians,
            panViewportDelta,
            dollyDelta);
        return IsEnabled && attachment?.Controller is not null &&
            Behavior.ProcessNavigationDelta(
                rotationRadians,
                panViewportDelta,
                dollyDelta);
    }

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException($"A {GetType().Name} can attach only once at a time.");
        }
        if (Camera is null)
        {
            throw new InvalidOperationException($"{GetType().Name} requires an explicit Camera.");
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

    private Vector3 Target => new(TargetX, TargetY, TargetZ);

    private void Apply(Action<OrbitController> update)
    {
        OrbitController? controller = attachment?.Controller;
        if (controller is not null)
        {
            update(controller);
        }
    }

    private void ApplyTarget() => Apply(controller => controller.Target = Target);

    private void ApplyEnabled(bool value)
    {
        feature.IsEnabled = value;
        Apply(controller => controller.IsEnabled = value);
        RefreshNavigationCommandState();
    }

    private bool CanNavigate(OrbitNavigationAction action) =>
        Enum.IsDefined(action) && IsEnabled && attachment?.Controller is not null;

    private void RefreshNavigationCommandState() => navigationCommand.ChangeCanExecute();

    /// <summary>Creates the attachment-owned UI-independent controller.</summary>
    /// <param name="camera">The borrowed scene camera.</param>
    /// <param name="target">The initial world-space target.</param>
    /// <param name="frameRequester">The borrowed frame requester.</param>
    /// <returns>A controller owned by this tool's active attachment.</returns>
    protected virtual OrbitController CreateController(
        PerspectiveCamera camera,
        Vector3 target,
        IViewportFrameRequester frameRequester) => new(camera, target, frameRequester);

    private static BindableProperty CreateTargetProperty(string name) => BindableProperty.Create(
        name,
        typeof(float),
        typeof(OrbitTool),
        0f,
        validateValue: static (_, value) => value is float number && float.IsFinite(number),
        propertyChanged: static (bindable, _, _) => ((OrbitTool)bindable).ApplyTarget());

    private static BindableProperty CreatePositiveProperty(
        string name,
        float defaultValue,
        Action<OrbitController, float> update) => BindableProperty.Create(
        name,
        typeof(float),
        typeof(OrbitTool),
        defaultValue,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: (bindable, _, value) =>
            ((OrbitTool)bindable).Apply(controller => update(controller, (float)value)));

    private static BindableProperty CreateNonNegativeProperty(
        string name,
        float defaultValue,
        Action<OrbitController, float> update) => BindableProperty.Create(
        name,
        typeof(float),
        typeof(OrbitTool),
        defaultValue,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number >= 0f,
        propertyChanged: (bindable, _, value) =>
            ((OrbitTool)bindable).Apply(controller => update(controller, (float)value)));

    private static bool IsPositiveFinite(object value) =>
        value is float number && float.IsFinite(number) && number > 0f;

    private static bool IsPolarAngle(object value) =>
        value is float number && float.IsFinite(number) && number > 0f && number < MathF.PI;

    private sealed class ToolAttachment(
        OrbitTool owner,
        ViewportToolContext context,
        OrbitSceneViewFeature feature) : IDisposable
    {
        private IDisposable? featureLease;
        private bool disposed;

        internal OrbitController? Controller { get; private set; }

        internal void Rebuild()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ReleaseOwnedState();
            if (owner.Camera is not { } camera)
            {
                return;
            }
            OrbitController controller = owner.CreateController(
                camera.PerspectiveCamera,
                owner.Target,
                new ViewportToolFrameRequester(context));
            controller.DampingEnabled = owner.DampingEnabled;
            controller.DampingTime = owner.DampingTime;
            controller.MinimumDistance = owner.MinimumDistance;
            controller.MaximumDistance = owner.MaximumDistance;
            controller.PanMode = owner.PanMode;
            controller.MinimumPolarAngle = owner.MinimumPolarAngle;
            controller.MaximumPolarAngle = owner.MaximumPolarAngle;
            controller.RotationSensitivity = owner.RotationSensitivity;
            controller.PanSensitivity = owner.PanSensitivity;
            controller.DollySensitivity = owner.DollySensitivity;
            controller.IsEnabled = owner.IsEnabled;
            Controller = controller;
            feature.Controller = controller;
            feature.ControlArbiter = context.ControlArbiter;
            feature.IsEnabled = owner.IsEnabled;
            try
            {
                featureLease = feature.Attach(context.SceneViewContext);
                owner.RefreshNavigationCommandState();
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
            feature.Controller = null;
            feature.ControlArbiter = null;
            Controller?.Dispose();
            Controller = null;
            owner.RefreshNavigationCommandState();
        }
    }
}

using System.Diagnostics;
using System.Numerics;
using Microsoft.Maui.Animations;
using MauiAnimation = Microsoft.Maui.Animations.Animation;
using Mu3D.Maui.Controls;
using Mu3D.Toolkit.Controls;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Maps opt-in MAUI device input into normalized viewport navigation actions.</summary>
/// <remarks>
/// The behavior borrows its controller, optional arbiter and scene view. It adds only its own gesture
/// recognizers and platform subscriptions while enabled and removes them when disabled, detached or
/// disposed. Accepted interaction and damping tails use MAUI's shared platform VSync ticker only
/// while motion is active; idle behavior owns no timer or render loop. Mouse, native trackpad,
/// direct touchscreen and focused hardware-key settings remain separate. One-finger drag,
/// simultaneous two-finger drag/pinch, pointer-wheel and focused-key input have adapters on Android,
/// iOS, Mac Catalyst and Windows; callers can query the corresponding capability properties.
/// Apple uses a native pinch recognizer for direct and indirect transform input. The behavior does
/// not own application selection, transform-gizmo hit testing or process-global input policy.
/// </remarks>
public sealed partial class ViewportNavigationBehavior : Behavior<Mu3DSceneView>, IDisposable
{
    private readonly PanGestureRecognizer panGesture = new() { TouchPoints = 1 };
    private readonly PanGestureRecognizer twoFingerPanGesture = new() { TouchPoints = 2 };
    private readonly PinchGestureRecognizer pinchGesture = new();
    private readonly ViewportGestureState gestureState = new();
    private readonly ViewportMultiTouchGestureState multiTouchGestureState = new();
    private readonly ViewportGestureState pointerButtonGestureState = new();
    private Mu3DSceneView? sceneView;
    private IViewportNavigationController? subscribedController;
    private IAnimationManager? frameAnimationManager;
    private MauiAnimation? frameAnimation;
    private long lastFrameTimestamp;
    private bool isViewLoaded;
    private bool frameClockStopQueued;
    private bool inputAttached;
    private bool onePointerDragSuppressed;
    private ViewportDragAction activeOnePointerDragAction;
    private ViewportDragAction activePointerButtonDragAction;
    private bool disposed;

    internal bool InvertHorizontalRotation { get; set; }

    /// <summary>Initializes one device-separated viewport navigation behavior.</summary>
    public ViewportNavigationBehavior()
    {
        Input = new ViewportInput();
    }

    /// <summary>Identifies the <see cref="Controller"/> bindable property.</summary>
    public static readonly BindableProperty ControllerProperty = BindableProperty.Create(
        nameof(Controller),
        typeof(IViewportNavigationController),
        typeof(ViewportNavigationBehavior),
        default(IViewportNavigationController),
        propertyChanged: static (bindable, _, _) =>
            ((ViewportNavigationBehavior)bindable).RefreshAttachment());

    /// <summary>Identifies the <see cref="ControlArbiter"/> bindable property.</summary>
    public static readonly BindableProperty ControlArbiterProperty = BindableProperty.Create(
        nameof(ControlArbiter),
        typeof(ViewportControlArbiter),
        typeof(ViewportNavigationBehavior),
        default(ViewportControlArbiter),
        propertyChanged: static (bindable, _, _) =>
            ((ViewportNavigationBehavior)bindable).EndGesture());

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(ViewportNavigationBehavior),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((ViewportNavigationBehavior)bindable).RefreshAttachment());

    /// <summary>Identifies the <see cref="Input"/> bindable property.</summary>
    public static readonly BindableProperty InputProperty = BindableProperty.Create(
        nameof(Input),
        typeof(ViewportInput),
        typeof(ViewportNavigationBehavior),
        defaultValue: null,
        validateValue: static (_, value) => value is ViewportInput,
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((ViewportNavigationBehavior)bindable).OnInputReplaced(
                (ViewportInput?)oldValue,
                (ViewportInput)newValue));

    /// <summary>Identifies the <see cref="RotationRadiansPerViewport"/> bindable property.</summary>
    public static readonly BindableProperty RotationRadiansPerViewportProperty =
        BindableProperty.Create(
            nameof(RotationRadiansPerViewport),
            typeof(float),
            typeof(ViewportNavigationBehavior),
            MathF.PI * 2f,
            validateValue: static (_, value) => value is float number && IsPositiveFinite(number));

    /// <summary>Identifies the <see cref="PanUnitsPerViewport"/> bindable property.</summary>
    public static readonly BindableProperty PanUnitsPerViewportProperty = BindableProperty.Create(
        nameof(PanUnitsPerViewport),
        typeof(float),
        typeof(ViewportNavigationBehavior),
        1f,
        validateValue: static (_, value) => value is float number && IsPositiveFinite(number));

    /// <summary>Identifies the <see cref="UseIsotropicDragNormalization"/> bindable property.</summary>
    public static readonly BindableProperty UseIsotropicDragNormalizationProperty =
        BindableProperty.Create(
            nameof(UseIsotropicDragNormalization),
            typeof(bool),
            typeof(ViewportNavigationBehavior),
            false);

    /// <summary>Identifies the <see cref="PinchDollyMultiplier"/> bindable property.</summary>
    public static readonly BindableProperty PinchDollyMultiplierProperty = BindableProperty.Create(
        nameof(PinchDollyMultiplier),
        typeof(float),
        typeof(ViewportNavigationBehavior),
        1.6f,
        validateValue: static (_, value) => value is float number && IsPositiveFinite(number));

    /// <summary>Identifies the <see cref="MaximumDampingFrameSeconds"/> bindable property.</summary>
    public static readonly BindableProperty MaximumDampingFrameSecondsProperty =
        BindableProperty.Create(
            nameof(MaximumDampingFrameSeconds),
            typeof(float),
            typeof(ViewportNavigationBehavior),
            0.1f,
            validateValue: static (_, value) => value is float number && IsPositiveFinite(number));

    /// <summary>Identifies the <see cref="WheelDollyStep"/> bindable property.</summary>
    public static readonly BindableProperty WheelDollyStepProperty = BindableProperty.Create(
        nameof(WheelDollyStep),
        typeof(float),
        typeof(ViewportNavigationBehavior),
        0.12f,
        validateValue: static (_, value) => value is float number && IsPositiveFinite(number));

    /// <summary>Identifies the <see cref="KeyboardRotationStepRadians"/> bindable property.</summary>
    public static readonly BindableProperty KeyboardRotationStepRadiansProperty =
        BindableProperty.Create(
            nameof(KeyboardRotationStepRadians),
            typeof(float),
            typeof(ViewportNavigationBehavior),
            MathF.PI / 36f,
            validateValue: static (_, value) => value is float number && IsPositiveFinite(number));

    /// <summary>Identifies the <see cref="KeyboardPanStep"/> bindable property.</summary>
    public static readonly BindableProperty KeyboardPanStepProperty = BindableProperty.Create(
        nameof(KeyboardPanStep),
        typeof(float),
        typeof(ViewportNavigationBehavior),
        0.05f,
        validateValue: static (_, value) => value is float number && IsPositiveFinite(number));

    /// <summary>Identifies the <see cref="KeyboardDollyStep"/> bindable property.</summary>
    public static readonly BindableProperty KeyboardDollyStepProperty = BindableProperty.Create(
        nameof(KeyboardDollyStep),
        typeof(float),
        typeof(ViewportNavigationBehavior),
        0.12f,
        validateValue: static (_, value) => value is float number && IsPositiveFinite(number));

    /// <summary>Occurs when a controller operation rejects an input update.</summary>
    public event EventHandler<ViewportNavigationFailedEventArgs>? InteractionFailed;

    /// <summary>Gets or sets the borrowed UI-independent navigation controller.</summary>
    public IViewportNavigationController? Controller
    {
        get => (IViewportNavigationController?)GetValue(ControllerProperty);
        set => SetValue(ControllerProperty, value);
    }

    /// <summary>Gets or sets the optional borrowed viewport-control arbiter.</summary>
    public ViewportControlArbiter? ControlArbiter
    {
        get => (ViewportControlArbiter?)GetValue(ControlArbiterProperty);
        set => SetValue(ControlArbiterProperty, value);
    }

    /// <summary>Gets or sets whether this behavior installs recognizers and accepts gestures.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets or sets the physical-device input routes.</summary>
    public ViewportInput Input
    {
        get => (ViewportInput)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    private MouseInput MouseInput => Input.Mouse;

    private TrackpadInput TrackpadInput => Input.Trackpad;

    private TouchscreenInput TouchscreenInput => Input.Touchscreen;

    private KeyboardInput KeyboardInput => Input.Keyboard;

    /// <summary>
    /// Gets or sets the positive rotation radians mapped from one complete viewport drag.
    /// </summary>
    public float RotationRadiansPerViewport
    {
        get => (float)GetValue(RotationRadiansPerViewportProperty);
        set => SetValue(RotationRadiansPerViewportProperty, value);
    }

    /// <summary>Gets or sets the positive Pan units mapped from one complete viewport drag.</summary>
    public float PanUnitsPerViewport
    {
        get => (float)GetValue(PanUnitsPerViewportProperty);
        set => SetValue(PanUnitsPerViewportProperty, value);
    }

    /// <summary>
    /// Gets or sets whether both drag axes use the viewport's short side for normalization.
    /// </summary>
    public bool UseIsotropicDragNormalization
    {
        get => (bool)GetValue(UseIsotropicDragNormalizationProperty);
        set => SetValue(UseIsotropicDragNormalizationProperty, value);
    }

    /// <summary>Gets or sets the positive multiplier applied to logarithmic pinch scale changes.</summary>
    public float PinchDollyMultiplier
    {
        get => (float)GetValue(PinchDollyMultiplierProperty);
        set => SetValue(PinchDollyMultiplierProperty, value);
    }

    /// <summary>Gets or sets the positive maximum elapsed seconds consumed by one damping frame.</summary>
    public float MaximumDampingFrameSeconds
    {
        get => (float)GetValue(MaximumDampingFrameSecondsProperty);
        set => SetValue(MaximumDampingFrameSecondsProperty, value);
    }

    /// <summary>Gets or sets the positive logarithmic dolly amount for one wheel detent.</summary>
    public float WheelDollyStep
    {
        get => (float)GetValue(WheelDollyStepProperty);
        set => SetValue(WheelDollyStepProperty, value);
    }

    /// <summary>Gets or sets the positive rotation amount for one accepted rotation-key press.</summary>
    public float KeyboardRotationStepRadians
    {
        get => (float)GetValue(KeyboardRotationStepRadiansProperty);
        set => SetValue(KeyboardRotationStepRadiansProperty, value);
    }

    /// <summary>Gets or sets the positive Pan amount for one accepted Pan key.</summary>
    public float KeyboardPanStep
    {
        get => (float)GetValue(KeyboardPanStepProperty);
        set => SetValue(KeyboardPanStepProperty, value);
    }

    /// <summary>Gets or sets the positive logarithmic dolly amount for one accepted dolly key.</summary>
    public float KeyboardDollyStep
    {
        get => (float)GetValue(KeyboardDollyStepProperty);
        set => SetValue(KeyboardDollyStepProperty, value);
    }

    /// <summary>Gets whether this target currently supplies the behavior's pointer-wheel adapter.</summary>
    public bool IsWheelInputAvailable => GetPlatformWheelInputAvailable();

    /// <summary>Gets whether this target currently supplies the behavior's hardware-key adapter.</summary>
    public bool IsKeyboardInputAvailable => GetPlatformKeyboardInputAvailable();

    /// <summary>Gets whether this target supplies native additional mouse-button drag input.</summary>
    public bool IsMouseButtonInputAvailable => GetPlatformMouseButtonInputAvailable();

    /// <summary>Gets whether this behavior currently owns an accepted gesture.</summary>
    public bool IsGestureActive => IsAnyGestureActive;

    /// <inheritdoc />
    protected override void OnAttachedTo(Mu3DSceneView bindable)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        base.OnAttachedTo(bindable);
        sceneView = bindable;
        sceneView.HandlerChanged += OnHandlerChanged;
        sceneView.Loaded += OnViewLoaded;
        sceneView.Unloaded += OnViewUnloaded;
        isViewLoaded = sceneView.IsLoaded;
        RefreshAttachment();
    }

    /// <inheritdoc />
    protected override void OnDetachingFrom(Mu3DSceneView bindable)
    {
        DetachInput();
        bindable.HandlerChanged -= OnHandlerChanged;
        bindable.Loaded -= OnViewLoaded;
        bindable.Unloaded -= OnViewUnloaded;
        isViewLoaded = false;
        sceneView = null;
        base.OnDetachingFrom(bindable);
    }

    /// <summary>Detaches recognizers and events without disposing borrowed objects.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        DetachInput();
        if (sceneView is not null)
        {
            sceneView.HandlerChanged -= OnHandlerChanged;
            sceneView.Loaded -= OnViewLoaded;
            sceneView.Unloaded -= OnViewUnloaded;
        }
        isViewLoaded = false;
        gestureState.Dispose();
        multiTouchGestureState.Dispose();
        pointerButtonGestureState.Dispose();
        Input.PropertyChanged -= OnInputChanged;
        sceneView = null;
        InteractionFailed = null;
    }

    internal void ProcessPan(GestureStatus status, double totalX, double totalY)
    {
        if (!inputAttached)
        {
            return;
        }
        try
        {
            switch (status)
            {
                case GestureStatus.Started:
                    activeOnePointerDragAction = GetPlatformOnePointerDragAction();
                    onePointerDragSuppressed =
                        activeOnePointerDragAction == ViewportDragAction.None ||
                        GetPlatformSystemNavigationGestureActive();
                    if (onePointerDragSuppressed)
                    {
                        return;
                    }
                    if (!pointerButtonGestureState.IsActive && !multiTouchGestureState.IsActive &&
                        gestureState.BeginPan(totalX, totalY, ControlArbiter))
                    {
                        RefreshFrameClock();
                    }
                    break;
                case GestureStatus.Running:
                    if (onePointerDragSuppressed)
                    {
                        return;
                    }
                    if (!gestureState.TryGetPanDelta(
                        totalX,
                        totalY,
                        out double deltaX,
                        out double deltaY))
                    {
                        return;
                    }
                    ApplyPan(deltaX, deltaY, activeOnePointerDragAction);
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    gestureState.EndPan();
                    onePointerDragSuppressed = false;
                    activeOnePointerDragAction = ViewportDragAction.None;
                    RefreshFrameClock();
                    break;
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    internal void ProcessTwoFingerPan(GestureStatus status, double totalX, double totalY)
    {
        ViewportDragAction action = TouchscreenInput.IsEnabled
            ? TouchscreenInput.TwoFingerDragAction
            : ViewportDragAction.None;
        if (!inputAttached || action == ViewportDragAction.None)
        {
            return;
        }
        try
        {
            switch (status)
            {
                case GestureStatus.Started:
                    gestureState.End();
                    pointerButtonGestureState.End();
                    if (multiTouchGestureState.BeginPan(totalX, totalY, ControlArbiter))
                    {
                        RefreshFrameClock();
                    }
                    break;
                case GestureStatus.Running:
                    if (!multiTouchGestureState.TryGetPanDelta(
                        totalX,
                        totalY,
                        out double deltaX,
                        out double deltaY))
                    {
                        return;
                    }
                    ApplyPan(deltaX, deltaY, action);
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    multiTouchGestureState.EndPan();
                    RefreshFrameClock();
                    break;
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    internal void ProcessPinch(GestureStatus status, double scale, bool isTrackpad = false)
    {
        bool enabled = isTrackpad
            ? TrackpadInput.IsEnabled &&
                TrackpadInput.PinchAction == ViewportScalarAction.Dolly
            : TouchscreenInput.IsEnabled &&
                TouchscreenInput.PinchAction == ViewportScalarAction.Dolly;
        if (!inputAttached || !enabled)
        {
            return;
        }
        try
        {
            switch (status)
            {
                case GestureStatus.Started:
                    gestureState.End();
                    pointerButtonGestureState.End();
                    if (multiTouchGestureState.BeginPinch(ControlArbiter))
                    {
                        RefreshFrameClock();
                    }
                    break;
                case GestureStatus.Running:
                    if (!multiTouchGestureState.TryGetPinchRatio(scale, out double ratio))
                    {
                        return;
                    }
                    ApplyPinch(ratio);
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    multiTouchGestureState.EndPinch();
                    RefreshFrameClock();
                    break;
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    internal bool ProcessActivePointerButtonDrag(
        GestureStatus status,
        double positionX,
        double positionY) => ProcessPointerButtonDrag(
            status,
            positionX,
            positionY,
            MouseInput.IsEnabled ? MouseInput.RightButtonDragAction : ViewportDragAction.None);

    internal bool ProcessPointerButtonDrag(
        GestureStatus status,
        double positionX,
        double positionY,
        ViewportDragAction requestedAction)
    {
        if (!inputAttached ||
            (status == GestureStatus.Started && requestedAction == ViewportDragAction.None))
        {
            return false;
        }
        try
        {
            switch (status)
            {
                case GestureStatus.Started:
                    activePointerButtonDragAction = requestedAction;
                    gestureState.End();
                    multiTouchGestureState.End();
                    if (!pointerButtonGestureState.BeginPan(
                        positionX,
                        positionY,
                        ControlArbiter))
                    {
                        return false;
                    }
                    RefreshFrameClock();
                    return true;
                case GestureStatus.Running:
                    if (!pointerButtonGestureState.TryGetPanDelta(
                        positionX,
                        positionY,
                        out double deltaX,
                        out double deltaY))
                    {
                        return false;
                    }
                    ApplyPan(deltaX, deltaY, activePointerButtonDragAction);
                    return true;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    pointerButtonGestureState.EndPan();
                    activePointerButtonDragAction = ViewportDragAction.None;
                    RefreshFrameClock();
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(status));
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return false;
        }
    }

    internal void ProcessFrame()
    {
        IViewportNavigationController? controller = subscribedController;
        Mu3DSceneView? currentView = sceneView;
        if (!inputAttached || controller is null || currentView is null)
        {
            QueueFrameClockStopIfIdle();
            return;
        }
        long now = Stopwatch.GetTimestamp();
        float elapsedSeconds = lastFrameTimestamp == 0
            ? 0f
            : (float)Math.Min(
                Stopwatch.GetElapsedTime(lastFrameTimestamp, now).TotalSeconds,
                MaximumDampingFrameSeconds);
        lastFrameTimestamp = now;
        if (!controller.HasPendingMotion)
        {
            if (IsAnyGestureActive)
            {
                currentView.InvalidateScene();
            }
            else
            {
                QueueFrameClockStopIfIdle();
            }
            return;
        }
        try
        {
            controller.Update(elapsedSeconds);
            if (IsAnyGestureActive || controller.HasPendingMotion)
            {
                currentView.InvalidateScene();
            }
            else
            {
                QueueFrameClockStopIfIdle();
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    internal bool ProcessWheel(float detents)
    {
        if (!inputAttached || !MouseInput.IsEnabled ||
            MouseInput.WheelAction != ViewportScalarAction.Dolly)
        {
            return false;
        }
        try
        {
            bool accepted = ViewportNavigationInputState.TryApplyWheel(
                subscribedController!,
                ControlArbiter,
                detents,
                WheelDollyStep);
            if (accepted)
            {
                sceneView!.InvalidateScene();
                RefreshFrameClock();
            }
            return accepted;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return false;
        }
    }

    internal bool ProcessKeyboardAction(ViewportKeyboardAction action)
    {
        if (!inputAttached || !KeyboardInput.IsEnabled)
        {
            return false;
        }
        try
        {
            bool accepted = ViewportNavigationInputState.TryApplyKeyboard(
                subscribedController!,
                ControlArbiter,
                action,
                KeyboardRotationStepRadians,
                KeyboardPanStep,
                KeyboardDollyStep);
            if (accepted)
            {
                sceneView!.InvalidateScene();
                RefreshFrameClock();
            }
            return accepted;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return false;
        }
    }

    internal bool ProcessNavigationAction(
        OrbitNavigationAction action,
        float rotationStepRadians,
        float panStep,
        float dollyStep)
    {
        if (!inputAttached)
        {
            return false;
        }
        try
        {
            bool accepted = ViewportNavigationInputState.TryApplyNavigation(
                subscribedController!,
                ControlArbiter,
                action,
                rotationStepRadians,
                panStep,
                dollyStep);
            if (accepted)
            {
                sceneView!.InvalidateScene();
                RefreshFrameClock();
            }
            return accepted;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return false;
        }
    }

    internal bool ProcessNavigationDelta(
        Vector2 rotationRadians,
        Vector2 panViewportDelta,
        float dollyDelta)
    {
        if (!inputAttached)
        {
            return false;
        }
        try
        {
            bool accepted = ViewportNavigationInputState.TryApplyNavigationDelta(
                subscribedController!,
                ControlArbiter,
                rotationRadians,
                panViewportDelta,
                dollyDelta);
            if (accepted)
            {
                sceneView!.InvalidateScene();
                RefreshFrameClock();
            }
            return accepted;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return false;
        }
    }

    internal bool ProcessKeyboardKey(ViewportKey key)
    {
        KeyboardInput keyboard = KeyboardInput;
        ViewportKeyboardAction? action = ViewportKeyboardBindingState.Resolve(
            key,
            keyboard.RotateLeftKey,
            keyboard.RotateRightKey,
            keyboard.RotateUpKey,
            keyboard.RotateDownKey,
            keyboard.PanLeftKey,
            keyboard.PanRightKey,
            keyboard.PanUpKey,
            keyboard.PanDownKey,
            keyboard.DollyInKey,
            keyboard.DollyOutKey,
            keyboard.AlternateDollyInKey,
            keyboard.AlternateDollyOutKey);
        return action is ViewportKeyboardAction command && ProcessKeyboardAction(command);
    }

    private void RefreshAttachment()
    {
        DetachInput();
        if (disposed || !IsEnabled || sceneView is null || Controller is null)
        {
            return;
        }

        subscribedController = Controller;
        subscribedController.Changed += OnControllerChanged;
        bool acceptsOnePointerDrag =
            (MouseInput.IsEnabled &&
                MouseInput.LeftButtonDragAction != ViewportDragAction.None) ||
            (TouchscreenInput.IsEnabled &&
                TouchscreenInput.OneFingerDragAction != ViewportDragAction.None);
        if (acceptsOnePointerDrag)
        {
            panGesture.PanUpdated += OnPanUpdated;
            sceneView.GestureRecognizers.Add(panGesture);
        }
        if (TouchscreenInput.IsEnabled &&
            TouchscreenInput.TwoFingerDragAction != ViewportDragAction.None)
        {
            twoFingerPanGesture.PanUpdated += OnTwoFingerPanUpdated;
            sceneView.GestureRecognizers.Add(twoFingerPanGesture);
        }
        if (!GetPlatformUsesNativePinchInput() &&
            TouchscreenInput.IsEnabled &&
            TouchscreenInput.PinchAction == ViewportScalarAction.Dolly)
        {
            pinchGesture.PinchUpdated += OnPinchUpdated;
            sceneView.GestureRecognizers.Add(pinchGesture);
        }
        sceneView.FramePresented += OnFramePresented;
        inputAttached = true;
        if (isViewLoaded)
        {
            AttachPlatformInput();
        }
        lastFrameTimestamp = Stopwatch.GetTimestamp();
    }

    private void DetachInput()
    {
        StopFrameClock();
        DetachPlatformInput();
        gestureState.End();
        multiTouchGestureState.End();
        pointerButtonGestureState.End();
        onePointerDragSuppressed = false;
        activeOnePointerDragAction = ViewportDragAction.None;
        activePointerButtonDragAction = ViewportDragAction.None;
        if (subscribedController is not null)
        {
            subscribedController.Changed -= OnControllerChanged;
            subscribedController = null;
        }
        panGesture.PanUpdated -= OnPanUpdated;
        twoFingerPanGesture.PanUpdated -= OnTwoFingerPanUpdated;
        pinchGesture.PinchUpdated -= OnPinchUpdated;
        if (sceneView is not null)
        {
            sceneView.FramePresented -= OnFramePresented;
            sceneView.GestureRecognizers.Remove(panGesture);
            sceneView.GestureRecognizers.Remove(twoFingerPanGesture);
            sceneView.GestureRecognizers.Remove(pinchGesture);
        }
        inputAttached = false;
        lastFrameTimestamp = 0;
    }

    private void RefreshPlatformInput()
    {
        DetachPlatformInput();
        if (inputAttached && isViewLoaded)
        {
            AttachPlatformInput();
        }
    }

    private void EndGesture()
    {
        gestureState.End();
        multiTouchGestureState.End();
        pointerButtonGestureState.End();
        onePointerDragSuppressed = false;
        activeOnePointerDragAction = ViewportDragAction.None;
        activePointerButtonDragAction = ViewportDragAction.None;
        RefreshFrameClock();
    }

    private void ApplyPan(double deltaX, double deltaY, ViewportDragAction action)
    {
        if (action == ViewportDragAction.None)
        {
            return;
        }

        Mu3DSceneView currentView = sceneView!;
        IViewportNavigationController controller = subscribedController!;
        if (!double.IsFinite(currentView.Width) || !double.IsFinite(currentView.Height) ||
            currentView.Width <= 0d || currentView.Height <= 0d)
        {
            return;
        }

        Vector2 command = ViewportGestureState.MapDragDelta(
            deltaX,
            deltaY,
            currentView.Width,
            currentView.Height,
            action,
            RotationRadiansPerViewport,
            PanUnitsPerViewport,
            UseIsotropicDragNormalization);
        if (action == ViewportDragAction.Rotate && InvertHorizontalRotation)
        {
            command.X = -command.X;
        }
        bool accepted = action == ViewportDragAction.Rotate
            ? controller.Rotate(command)
            : controller.Pan(command);
        if (accepted)
        {
            currentView.InvalidateScene();
            RefreshFrameClock();
        }
    }

    private void ApplyPinch(double ratio)
    {
        float logarithmicDelta = (float)(Math.Log(ratio) * PinchDollyMultiplier);
        if (!float.IsFinite(logarithmicDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio), "Pinch scale change is too large.");
        }
        if (subscribedController!.Dolly(logarithmicDelta))
        {
            sceneView!.InvalidateScene();
            RefreshFrameClock();
        }
    }

    private void ReportFailure(Exception exception)
    {
        EndGesture();
        StopFrameClock();
        InteractionFailed?.Invoke(this, new ViewportNavigationFailedEventArgs(exception));
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e) =>
        ProcessPan(e.StatusType, e.TotalX, e.TotalY);

    private void OnTwoFingerPanUpdated(object? sender, PanUpdatedEventArgs e) =>
        ProcessTwoFingerPan(e.StatusType, e.TotalX, e.TotalY);

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e) =>
        ProcessPinch(e.Status, e.Scale);

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        _ = e;
        if (frameAnimation is null)
        {
            ProcessFrame();
        }
    }

    private void OnControllerChanged(object? sender, EventArgs e)
    {
        sceneView?.InvalidateScene();
        RefreshFrameClock();
    }

    private void OnHandlerChanged(object? sender, EventArgs e) => RefreshPlatformInput();

    private void OnViewLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        isViewLoaded = true;
        RefreshPlatformInput();
        lastFrameTimestamp = Stopwatch.GetTimestamp();
        RefreshFrameClock();
    }

    private void OnViewUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        isViewLoaded = false;
        gestureState.End();
        multiTouchGestureState.End();
        pointerButtonGestureState.End();
        DetachPlatformInput();
        StopFrameClock();
    }

    private void RefreshFrameClock()
    {
        if (IsAnyGestureActive || subscribedController?.HasPendingMotion == true)
        {
            StartFrameClock();
            return;
        }
        QueueFrameClockStopIfIdle();
    }

    private void StartFrameClock()
    {
        if (frameAnimation is not null || !inputAttached || !isViewLoaded || sceneView is null)
        {
            return;
        }
        if (sceneView.Handler?.MauiContext?.Services.GetService(typeof(IAnimationManager))
            is not IAnimationManager manager)
        {
            return;
        }

        var animation = new MauiAnimation(
            _ => ProcessFrame(),
            start: 0d,
            duration: 1d,
            easing: Easing.Linear,
            finished: null)
        {
            Name = "Mu3D viewport navigation VSync loop",
            Repeats = true,
        };
        frameClockStopQueued = false;
        frameAnimationManager = manager;
        frameAnimation = animation;
        lastFrameTimestamp = Stopwatch.GetTimestamp();
        animation.Commit(manager);
    }

    private void QueueFrameClockStopIfIdle()
    {
        if (frameAnimation is null || frameClockStopQueued || IsAnyGestureActive ||
            subscribedController?.HasPendingMotion == true)
        {
            return;
        }

        Mu3DSceneView? currentView = sceneView;
        if (currentView is null)
        {
            StopFrameClock();
            return;
        }
        frameClockStopQueued = true;
        if (!currentView.Dispatcher.Dispatch(() =>
            {
                frameClockStopQueued = false;
                if (!IsAnyGestureActive && subscribedController?.HasPendingMotion != true)
                {
                    StopFrameClock();
                }
            }))
        {
            frameClockStopQueued = false;
            StopFrameClock();
        }
    }

    private void StopFrameClock()
    {
        MauiAnimation? animation = frameAnimation;
        IAnimationManager? manager = frameAnimationManager;
        frameAnimation = null;
        frameAnimationManager = null;
        frameClockStopQueued = false;
        lastFrameTimestamp = 0;
        if (animation is not null)
        {
            manager?.Remove(animation);
            animation.Dispose();
        }
    }

    internal static Vector2 MapDragDelta(
        double deltaX,
        double deltaY,
        double width,
        double height,
        ViewportDragAction action,
        float rotationRadiansPerViewport,
        float panUnitsPerViewport,
        bool useIsotropicNormalization)
        => ViewportGestureState.MapDragDelta(
            deltaX,
            deltaY,
            width,
            height,
            action,
            rotationRadiansPerViewport,
            panUnitsPerViewport,
            useIsotropicNormalization);

    private static bool IsPositiveFinite(float value) => float.IsFinite(value) && value > 0f;

    private void OnInputReplaced(ViewportInput? oldInput, ViewportInput newInput)
    {
        if (oldInput is not null)
        {
            oldInput.PropertyChanged -= OnInputChanged;
        }
        newInput.PropertyChanged += OnInputChanged;
        EndGesture();
        RefreshAttachment();
    }

    private void OnInputChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        EndGesture();
        RefreshAttachment();
    }

    private bool IsAnyGestureActive =>
        gestureState.IsActive || multiTouchGestureState.IsActive || pointerButtonGestureState.IsActive;

    private partial void AttachPlatformInput();

    private partial void DetachPlatformInput();

    private static partial bool GetPlatformWheelInputAvailable();

    private static partial bool GetPlatformKeyboardInputAvailable();

    private static partial bool GetPlatformMouseButtonInputAvailable();

    private partial bool GetPlatformSystemNavigationGestureActive();

    private partial ViewportDragAction GetPlatformOnePointerDragAction();

    private static partial bool GetPlatformUsesNativePinchInput();

}

/// <summary>Reports a viewport navigation update rejected by the controller.</summary>
/// <param name="Exception">The controller or mapping exception.</param>
public sealed class ViewportNavigationFailedEventArgs(Exception Exception) : EventArgs
{
    /// <summary>Gets the controller or mapping exception.</summary>
    public Exception Exception { get; } = Exception;
}

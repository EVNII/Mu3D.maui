using System.ComponentModel;
using System.Numerics;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Helpers;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Maps supported-platform pointers into an application-owned transform gizmo.</summary>
/// <remarks>
/// The behavior hit-tests only the assigned gizmo's own handles, acquires an optional gizmo-priority
/// control lease, captures one accepted native pointer, and maps cumulative drags to the gizmo's
/// translate, rotate, or scale contract. It borrows the scene view, gizmo, optional hit tester, and
/// optional arbiter. It does not choose scene targets, rank scene objects, create undo commands,
/// define pen semantics, or render handles. Presses are ignored unless the attached scene view has a
/// perspective camera, a positive physical surface size, and a visible target belonging to that
/// scene whose own layers intersect the camera layers. Invalidating that eligibility cancels an
/// active edit and releases capture without clearing the borrowed target. Scene mutations remain
/// application-owned; invalidate the scene to observe them at the next presented-frame boundary.
/// </remarks>
public sealed partial class TransformGizmoPointerBehavior : Behavior<Mu3DSceneView>, IDisposable
{
    private readonly TransformGizmoHitTester defaultHitTester = new();
    private readonly TransformGizmoPointerState pointerState = new();
    private Mu3DSceneView? sceneView;
    private Scene? interactionScene;
    private PerspectiveCamera? interactionCamera;
    private TransformGizmo? interactionGizmo;
    private SceneNode? interactionTarget;
    private uint interactionWidth;
    private uint interactionHeight;
    private bool inputAttached;
    private bool contextSubscribed;
    private bool cancelingPointerInteraction;
    private bool processingPointerInput;
    private bool disposed;

    /// <summary>Initializes a disposable transform-gizmo pointer behavior.</summary>
    public TransformGizmoPointerBehavior()
    {
        pointerState.InteractionFailed += OnPointerStateFailed;
        pointerState.InteractionRevoked += OnPointerStateRevoked;
    }

    /// <summary>Identifies the <see cref="Gizmo"/> bindable property.</summary>
    public static readonly BindableProperty GizmoProperty = BindableProperty.Create(
        nameof(Gizmo),
        typeof(TransformGizmo),
        typeof(TransformGizmoPointerBehavior),
        default(TransformGizmo),
        propertyChanged: static (bindable, _, _) =>
            ((TransformGizmoPointerBehavior)bindable).RefreshAttachment());

    /// <summary>Identifies the <see cref="HitTester"/> bindable property.</summary>
    public static readonly BindableProperty HitTesterProperty = BindableProperty.Create(
        nameof(HitTester),
        typeof(TransformGizmoHitTester),
        typeof(TransformGizmoPointerBehavior),
        default(TransformGizmoHitTester),
        propertyChanged: static (bindable, _, _) =>
            ((TransformGizmoPointerBehavior)bindable).CancelPointerInteraction());

    /// <summary>Identifies the <see cref="ControlArbiter"/> bindable property.</summary>
    public static readonly BindableProperty ControlArbiterProperty = BindableProperty.Create(
        nameof(ControlArbiter),
        typeof(ViewportControlArbiter),
        typeof(TransformGizmoPointerBehavior),
        default(ViewportControlArbiter),
        propertyChanged: static (bindable, _, _) =>
            ((TransformGizmoPointerBehavior)bindable).CancelPointerInteraction());

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(TransformGizmoPointerBehavior),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((TransformGizmoPointerBehavior)bindable).RefreshAttachment());

    /// <summary>Identifies the <see cref="ScaleFactorPerGizmoLength"/> bindable property.</summary>
    public static readonly BindableProperty ScaleFactorPerGizmoLengthProperty =
        BindableProperty.Create(
            nameof(ScaleFactorPerGizmoLength),
            typeof(float),
            typeof(TransformGizmoPointerBehavior),
            2f,
            validateValue: static (_, value) =>
                value is float number && float.IsFinite(number) && number > 1f,
            propertyChanged: static (bindable, _, _) =>
                ((TransformGizmoPointerBehavior)bindable).CancelPointerInteraction());

    /// <summary>Occurs when hit testing, drag mapping, or the gizmo rejects pointer input.</summary>
    public event EventHandler<TransformGizmoPointerFailedEventArgs>? InteractionFailed;

    /// <summary>Gets or sets the borrowed UI-independent transform gizmo.</summary>
    public TransformGizmo? Gizmo
    {
        get => (TransformGizmo?)GetValue(GizmoProperty);
        set => SetValue(GizmoProperty, value);
    }

    /// <summary>
    /// Gets or sets an optional borrowed hit tester; null uses a behavior-local default tester.
    /// </summary>
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

    /// <summary>Gets or sets whether the behavior installs native pointer input.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the factor produced by dragging one full projected gizmo length while scaling.
    /// </summary>
    public float ScaleFactorPerGizmoLength
    {
        get => (float)GetValue(ScaleFactorPerGizmoLengthProperty);
        set => SetValue(ScaleFactorPerGizmoLengthProperty, value);
    }

    /// <summary>Gets whether this target supplies a native pointer/capture adapter.</summary>
    public bool IsPointerInputAvailable => GetPlatformPointerInputAvailable();

    /// <summary>Gets whether this behavior currently owns an accepted pointer interaction.</summary>
    public bool IsPointerActive => pointerState.IsActive;

    /// <inheritdoc />
    protected override void OnAttachedTo(Mu3DSceneView bindable)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        base.OnAttachedTo(bindable);
        sceneView = bindable;
        sceneView.HandlerChanged += OnHandlerChanged;
        RefreshAttachment();
    }

    /// <inheritdoc />
    protected override void OnDetachingFrom(Mu3DSceneView bindable)
    {
        DetachInput();
        bindable.HandlerChanged -= OnHandlerChanged;
        sceneView = null;
        base.OnDetachingFrom(bindable);
    }

    /// <summary>Detaches native input and cancels an active drag without disposing borrowed objects.</summary>
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
        }
        pointerState.InteractionFailed -= OnPointerStateFailed;
        pointerState.InteractionRevoked -= OnPointerStateRevoked;
        pointerState.Dispose();
        sceneView = null;
        InteractionFailed = null;
    }

    internal bool ProcessPointerPressed(Vector2 viewportPositionPixels)
    {
        if (cancelingPointerInteraction || processingPointerInput)
        {
            return false;
        }
        if (pointerState.IsActive)
        {
            if (!IsInteractionEligible())
            {
                CancelPointerInteraction();
            }
            return false;
        }
        Mu3DSceneView? currentView = sceneView;
        TransformGizmo? currentGizmo = Gizmo;
        if (!inputAttached || currentView is null || currentGizmo is null || currentGizmo.IsInteracting ||
            currentView.Scene is not Scene scene || currentView.Camera is not PerspectiveCamera camera ||
            currentGizmo.Target is not SceneNode target ||
            !SceneTargetEligibility.IsVisible(scene, target, camera.VisibilityMask))
        {
            return false;
        }

        uint pixelWidth = currentView.PixelWidth;
        uint pixelHeight = currentView.PixelHeight;
        if (pixelWidth == 0 || pixelHeight == 0 ||
            viewportPositionPixels.X < 0f || viewportPositionPixels.Y < 0f ||
            viewportPositionPixels.X > pixelWidth || viewportPositionPixels.Y > pixelHeight)
        {
            return false;
        }
        bool attemptedOwnedBegin = false;
        processingPointerInput = true;
        try
        {
            TransformGizmoHit? hit = (HitTester ?? defaultHitTester).HitTest(
                currentGizmo,
                camera,
                pixelWidth,
                pixelHeight,
                viewportPositionPixels);
            if (hit is null)
            {
                return false;
            }
            interactionScene = scene;
            interactionCamera = camera;
            interactionGizmo = currentGizmo;
            interactionTarget = target;
            interactionWidth = pixelWidth;
            interactionHeight = pixelHeight;
            attemptedOwnedBegin = !currentGizmo.IsInteracting;
            bool accepted = pointerState.Begin(
                currentGizmo,
                hit,
                camera,
                pixelWidth,
                pixelHeight,
                viewportPositionPixels,
                ControlArbiter,
                ScaleFactorPerGizmoLength);
            if (accepted && !IsInteractionEligible())
            {
                CancelPointerInteraction();
                return false;
            }
            if (accepted)
            {
                currentView.InvalidateScene();
                if (!IsInteractionEligible())
                {
                    CancelPointerInteraction();
                    return false;
                }
            }
            else
            {
                ClearInteractionContext();
            }
            return accepted;
        }
        catch (Exception exception)
        {
            Exception reported = exception;
            // A throwing application Started callback can leave the gizmo interacting after
            // the generic drag controller has already reset its state. Recover only the idle
            // gizmo/target whose begin this press attempted; never cancel an external edit.
            if (attemptedOwnedBegin && !pointerState.IsActive && currentGizmo.IsInteracting &&
                ReferenceEquals(currentGizmo.Target, target))
            {
                try
                {
                    currentGizmo.CancelInteraction();
                }
                catch (Exception cleanupException)
                {
                    reported = CombineFailures(reported, cleanupException);
                }
            }
            ReportFailure(reported);
            return false;
        }
        finally
        {
            processingPointerInput = false;
        }
    }

    internal bool ProcessPointerMoved(Vector2 viewportPositionPixels)
    {
        if (processingPointerInput || cancelingPointerInteraction || !inputAttached || !pointerState.IsActive)
        {
            return false;
        }
        if (!IsInteractionEligible())
        {
            CancelPointerInteraction();
            return false;
        }
        processingPointerInput = true;
        try
        {
            bool changed = pointerState.Update(viewportPositionPixels);
            if (!IsInteractionEligible())
            {
                CancelPointerInteraction();
                return false;
            }
            if (changed)
            {
                sceneView?.InvalidateScene();
                if (!IsInteractionEligible())
                {
                    CancelPointerInteraction();
                    return false;
                }
            }
            return true;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return false;
        }
        finally
        {
            processingPointerInput = false;
        }
    }

    internal bool ProcessPointerReleased(Vector2 viewportPositionPixels)
    {
        if (processingPointerInput || cancelingPointerInteraction || !inputAttached || !pointerState.IsActive)
        {
            return false;
        }
        if (!IsInteractionEligible())
        {
            CancelPointerInteraction();
            return false;
        }
        processingPointerInput = true;
        try
        {
            pointerState.Update(viewportPositionPixels);
            if (!IsInteractionEligible())
            {
                CancelPointerInteraction();
                return false;
            }
            ClearInteractionContext();
            pointerState.Complete();
            sceneView?.InvalidateScene();
            return true;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return false;
        }
        finally
        {
            processingPointerInput = false;
        }
    }

    internal void ProcessPointerCanceled()
    {
        CancelPointerInteraction();
    }

    private void RefreshAttachment()
    {
        DetachInput();
        if (disposed || !IsEnabled || sceneView is null || Gizmo is null)
        {
            return;
        }
        inputAttached = true;
        AttachContextSubscriptions();
        try
        {
            AttachPlatformInput();
        }
        catch
        {
            DetachInput();
            throw;
        }
    }

    private void DetachInput()
    {
        inputAttached = false;
        DetachContextSubscriptions();
        CancelPointerInteraction();
        DetachPlatformInput();
    }

    private void AttachContextSubscriptions()
    {
        if (contextSubscribed || sceneView is null)
        {
            return;
        }
        sceneView.PropertyChanged += OnSceneViewPropertyChanged;
        sceneView.FrameSnapshotChanged += OnFrameSnapshotChanged;
        contextSubscribed = true;
    }

    private void DetachContextSubscriptions()
    {
        if (!contextSubscribed || sceneView is null)
        {
            return;
        }
        sceneView.PropertyChanged -= OnSceneViewPropertyChanged;
        sceneView.FrameSnapshotChanged -= OnFrameSnapshotChanged;
        contextSubscribed = false;
    }

    private bool IsInteractionEligible()
    {
        Mu3DSceneView? currentView = sceneView;
        TransformGizmo? currentGizmo = interactionGizmo;
        SceneNode? target = interactionTarget;
        return inputAttached && pointerState.IsActive && currentView is not null &&
            currentGizmo is not null && currentGizmo.IsInteracting && target is not null &&
            ReferenceEquals(Gizmo, currentGizmo) && ReferenceEquals(currentGizmo.Target, target) &&
            interactionWidth > 0 && interactionHeight > 0 &&
            currentView.PixelWidth == interactionWidth && currentView.PixelHeight == interactionHeight &&
            interactionScene is Scene scene && ReferenceEquals(currentView.Scene, scene) &&
            interactionCamera is PerspectiveCamera camera && ReferenceEquals(currentView.Camera, camera) &&
            SceneTargetEligibility.IsVisible(scene, target, camera.VisibilityMask);
    }

    private void ClearInteractionContext()
    {
        interactionScene = null;
        interactionCamera = null;
        interactionGizmo = null;
        interactionTarget = null;
        interactionWidth = 0;
        interactionHeight = 0;
    }

    private void CancelPointerInteraction(Exception? reported = null)
    {
        if (cancelingPointerInteraction)
        {
            return;
        }
        bool wasActive = pointerState.IsActive;
        // Cancel callbacks can invalidate frames or change the attached view. End the observed
        // mapping first, and let nested notifications see a cancellation already in progress.
        ClearInteractionContext();
        cancelingPointerInteraction = true;
        try
        {
            pointerState.Cancel();
        }
        catch (Exception cleanupException)
        {
            reported = CombineFailures(reported, cleanupException);
        }
        finally
        {
            try
            {
                ReleasePlatformPointerCapture();
            }
            catch (Exception captureException)
            {
                reported = CombineFailures(reported, captureException);
            }
            finally
            {
                cancelingPointerInteraction = false;
            }
        }
        if (wasActive)
        {
            try
            {
                sceneView?.InvalidateScene();
            }
            catch (Exception invalidateException)
            {
                reported = CombineFailures(reported, invalidateException);
            }
        }
        if (reported is not null)
        {
            InteractionFailed?.Invoke(this, new TransformGizmoPointerFailedEventArgs(reported));
        }
    }

    private static Exception CombineFailures(Exception? previous, Exception current) => previous is null
        ? current
        : new AggregateException("Transform-gizmo pointer input or cleanup failed.", previous, current);

    private void ReportFailure(Exception exception) => CancelPointerInteraction(exception);

    private void OnSceneViewPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, sceneView) && pointerState.IsActive &&
            (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(Mu3DSceneView.Scene) or nameof(Mu3DSceneView.Camera)) &&
            !IsInteractionEligible())
        {
            CancelPointerInteraction();
        }
    }

    private void OnFrameSnapshotChanged(object? sender, ViewportFrameSnapshotChangedEventArgs e)
    {
        if (ReferenceEquals(sender, sceneView) && pointerState.IsActive &&
            (e.Snapshot is null || !IsInteractionEligible()))
        {
            CancelPointerInteraction();
        }
    }

    private void OnPointerStateFailed(Exception exception) => ReportFailure(exception);

    private void OnPointerStateRevoked()
    {
        CancelPointerInteraction();
        sceneView?.InvalidateScene();
    }

    private void OnHandlerChanged(object? sender, EventArgs e) => RefreshAttachment();

    private partial void AttachPlatformInput();

    private partial void DetachPlatformInput();

    private partial void ReleasePlatformPointerCapture();

    private static partial bool GetPlatformPointerInputAvailable();
}

/// <summary>Reports a transform-gizmo pointer update rejected by mapping or controller state.</summary>
/// <param name="Exception">The hit-test, mapping, arbitration, or gizmo exception.</param>
public sealed class TransformGizmoPointerFailedEventArgs(Exception Exception) : EventArgs
{
    /// <summary>Gets the hit-test, mapping, arbitration, or gizmo exception.</summary>
    public Exception Exception { get; } = Exception;
}

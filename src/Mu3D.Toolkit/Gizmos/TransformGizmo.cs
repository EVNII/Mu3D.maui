using System.Numerics;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Internal;
using Mu3D.Toolkit.Viewports;

namespace Mu3D.Toolkit.Gizmos;

/// <summary>
/// Applies host-normalized translate, rotate and scale interactions to an explicit scene node.
/// </summary>
/// <remarks>
/// The host owns target selection, undo commands and rendering. Raw input, pointer capture and handle
/// hit testing remain outside this controller and may be supplied by an optional adapter. Update
/// values are cumulative from the beginning of the active interaction, allowing snapping without
/// incremental drift. This controller is not thread-safe and never owns its target or optional frame
/// requester.
/// </remarks>
public sealed class TransformGizmo : IDisposable
{
    private readonly IViewportFrameRequester? frameRequester;
    private SceneNode? target;
    private InteractionState? interaction;
    private TransformGizmoMode mode;
    private bool isTranslateEnabled = true;
    private bool isRotateEnabled;
    private bool isScaleEnabled;
    private TransformGizmoSpace space;
    private float translationSnap;
    private float rotationSnapRadians;
    private float scaleSnap;
    private float minimumScaleFactor = 0.001f;
    private float maximumScaleFactor = 1000f;
    private float screenSizePixels = 96f;
    private bool disposed;

    /// <summary>Initializes a transform gizmo over an optional application-owned target.</summary>
    public TransformGizmo(
        SceneNode? target = null,
        IViewportFrameRequester? frameRequester = null)
    {
        this.target = target;
        this.frameRequester = frameRequester;
    }

    /// <summary>Occurs after a valid interaction captures its initial pose.</summary>
    public event EventHandler<TransformGizmoInteractionEventArgs>? InteractionStarted;

    /// <summary>Occurs after an update changes the target pose.</summary>
    public event EventHandler<TransformGizmoInteractionEventArgs>? InteractionChanged;

    /// <summary>Occurs when the current pose is accepted as a completed interaction.</summary>
    public event EventHandler<TransformGizmoInteractionEventArgs>? InteractionCompleted;

    /// <summary>Occurs after cancellation restores the initial pose.</summary>
    public event EventHandler<TransformGizmoInteractionEventArgs>? InteractionCanceled;

    /// <summary>Gets the optional borrowed one-shot frame requester.</summary>
    public IViewportFrameRequester? FrameRequester => frameRequester;

    /// <summary>Gets or sets the application-selected target while no interaction is active.</summary>
    public SceneNode? Target
    {
        get => target;
        set
        {
            ThrowIfDisposed();
            ThrowIfInteracting(nameof(Target));
            target = value;
            frameRequester?.RequestFrame();
        }
    }

    /// <summary>
    /// Gets or sets the primary operation used by <see cref="BeginInteraction(TransformGizmoAxis)"/>.
    /// </summary>
    /// <remarks>
    /// Assigning this compatibility property enables only the selected operation. Use
    /// <see cref="IsTranslateEnabled"/>, <see cref="IsRotateEnabled"/> and
    /// <see cref="IsScaleEnabled"/> to display and hit-test a mixed gizmo.
    /// </remarks>
    public TransformGizmoMode Mode
    {
        get => mode;
        set
        {
            ThrowIfDisposed();
            ThrowIfInteracting(nameof(Mode));
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            mode = value;
            isTranslateEnabled = value == TransformGizmoMode.Translate;
            isRotateEnabled = value == TransformGizmoMode.Rotate;
            isScaleEnabled = value == TransformGizmoMode.Scale;
            frameRequester?.RequestFrame();
        }
    }

    /// <summary>Gets or sets whether translation handles are displayed and interactive.</summary>
    public bool IsTranslateEnabled
    {
        get => isTranslateEnabled;
        set => SetModeEnabled(TransformGizmoMode.Translate, value, nameof(IsTranslateEnabled));
    }

    /// <summary>Gets or sets whether rotation handles are displayed and interactive.</summary>
    public bool IsRotateEnabled
    {
        get => isRotateEnabled;
        set => SetModeEnabled(TransformGizmoMode.Rotate, value, nameof(IsRotateEnabled));
    }

    /// <summary>Gets or sets whether axis and uniform scale handles are displayed and interactive.</summary>
    public bool IsScaleEnabled
    {
        get => isScaleEnabled;
        set => SetModeEnabled(TransformGizmoMode.Scale, value, nameof(IsScaleEnabled));
    }

    /// <summary>Returns whether the specified operation is currently displayed and interactive.</summary>
    public bool IsModeEnabled(TransformGizmoMode value)
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        return IsModeEnabledCore(value);
    }

    /// <summary>
    /// Gets or sets the axis orientation used by translate and rotate interactions.
    /// Scale handles always follow the target-local axes.
    /// </summary>
    public TransformGizmoSpace Space
    {
        get => space;
        set
        {
            ThrowIfDisposed();
            ThrowIfInteracting(nameof(Space));
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            space = value;
            frameRequester?.RequestFrame();
        }
    }

    /// <summary>Gets or sets the non-negative translation snap distance; zero disables snapping.</summary>
    public float TranslationSnap
    {
        get => translationSnap;
        set
        {
            ThrowIfDisposed();
            ThrowIfNonNegativeFinite(value, nameof(value));
            translationSnap = value;
        }
    }

    /// <summary>Gets or sets the non-negative rotation snap in radians; zero disables snapping.</summary>
    public float RotationSnapRadians
    {
        get => rotationSnapRadians;
        set
        {
            ThrowIfDisposed();
            ThrowIfNonNegativeFinite(value, nameof(value));
            rotationSnapRadians = value;
        }
    }

    /// <summary>
    /// Gets or sets the non-negative scale-factor snap interval around one; zero disables snapping.
    /// </summary>
    public float ScaleSnap
    {
        get => scaleSnap;
        set
        {
            ThrowIfDisposed();
            ThrowIfNonNegativeFinite(value, nameof(value));
            scaleSnap = value;
        }
    }

    /// <summary>Gets or sets the positive minimum cumulative scale factor.</summary>
    public float MinimumScaleFactor
    {
        get => minimumScaleFactor;
        set
        {
            ThrowIfDisposed();
            ThrowIfPositiveFinite(value, nameof(value));
            if (value > maximumScaleFactor)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Minimum scale factor cannot exceed maximum scale factor.");
            }
            minimumScaleFactor = value;
        }
    }

    /// <summary>Gets or sets the finite maximum cumulative scale factor.</summary>
    public float MaximumScaleFactor
    {
        get => maximumScaleFactor;
        set
        {
            ThrowIfDisposed();
            ThrowIfPositiveFinite(value, nameof(value));
            if (value < minimumScaleFactor)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Maximum scale factor cannot be below minimum scale factor.");
            }
            maximumScaleFactor = value;
        }
    }

    /// <summary>Gets or sets the positive desired on-screen handle size. The default is 96 pixels.</summary>
    public float ScreenSizePixels
    {
        get => screenSizePixels;
        set
        {
            ThrowIfDisposed();
            ThrowIfPositiveFinite(value, nameof(value));
            screenSizePixels = value;
            frameRequester?.RequestFrame();
        }
    }

    /// <summary>Gets whether an interaction currently owns the target transform.</summary>
    public bool IsInteracting => interaction is not null;

    /// <summary>Gets the active operation, or null while idle.</summary>
    public TransformGizmoMode? ActiveMode => interaction?.Mode;

    /// <summary>Gets the active axis or null while idle.</summary>
    public TransformGizmoAxis? ActiveAxis => interaction?.Axis;

    /// <summary>Captures the target pose and begins one primary-mode axis interaction.</summary>
    public void BeginInteraction(TransformGizmoAxis axis) => BeginInteraction(mode, axis);

    /// <summary>Captures the target pose and begins one enabled operation and axis interaction.</summary>
    /// <param name="interactionMode">The enabled translate, rotate or scale operation.</param>
    /// <param name="axis">The selected axis, or uniform handle for scale.</param>
    public void BeginInteraction(TransformGizmoMode interactionMode, TransformGizmoAxis axis)
    {
        ThrowIfDisposed();
        if (interaction is not null)
        {
            throw new InvalidOperationException("A transform-gizmo interaction is already active.");
        }
        if (!Enum.IsDefined(axis))
        {
            throw new ArgumentOutOfRangeException(nameof(axis));
        }
        if (!Enum.IsDefined(interactionMode))
        {
            throw new ArgumentOutOfRangeException(nameof(interactionMode));
        }
        if (!IsModeEnabledCore(interactionMode))
        {
            throw new InvalidOperationException(
                $"The {interactionMode} transform-gizmo operation is disabled.");
        }
        if (interactionMode != TransformGizmoMode.Scale && axis == TransformGizmoAxis.Uniform)
        {
            throw new ArgumentException(
                "The uniform handle is available only in scale mode.",
                nameof(axis));
        }
        SceneNode currentTarget = target ??
            throw new InvalidOperationException("A transform gizmo requires an explicit target.");
        SceneNode? parent = currentTarget.Parent;
        Matrix4x4 parentWorld = parent?.WorldMatrix ?? Matrix4x4.Identity;
        if (!Matrix4x4.Invert(parentWorld, out Matrix4x4 inverseParentWorld))
        {
            throw new InvalidOperationException("The transform-gizmo target parent is not invertible.");
        }

        TransformGizmoPose initialPose = TransformGizmoPose.Capture(currentTarget);
        TransformGizmoSpace interactionSpace = interactionMode == TransformGizmoMode.Scale
            ? TransformGizmoSpace.Local
            : space;
        interaction = new InteractionState(
            currentTarget,
            parent,
            parentWorld,
            inverseParentWorld,
            initialPose,
            currentTarget.WorldMatrix,
            interactionMode,
            interactionSpace,
            axis);
        InteractionStarted?.Invoke(this, CreateEventArgs(interaction, initialPose));
        frameRequester?.RequestFrame();
    }

    /// <summary>Updates a translate interaction with total signed world distance from its start.</summary>
    public bool UpdateTranslation(float totalDistance)
    {
        ThrowIfFinite(totalDistance, nameof(totalDistance));
        InteractionState state = GetInteraction(TransformGizmoMode.Translate);
        float snappedDistance = SnapSigned(totalDistance, translationSnap);
        Vector3 direction = GetAxisDirection(state);
        Vector3 worldPosition = state.InitialWorldPosition + direction * snappedDistance;
        TransformMatrixHelper.ThrowIfNotFinite(worldPosition, nameof(totalDistance));
        Vector3 localPosition = Vector3.Transform(worldPosition, state.InverseParentWorld);
        TransformMatrixHelper.ThrowIfNotFinite(localPosition, nameof(totalDistance));
        TransformGizmoPose next = new(
            localPosition,
            state.InitialPose.Rotation,
            state.InitialPose.Scale);
        return ApplyLocalPose(state, next);
    }

    /// <summary>Updates a rotate interaction with total signed radians from its start.</summary>
    public bool UpdateRotation(float totalRadians)
    {
        ThrowIfFinite(totalRadians, nameof(totalRadians));
        InteractionState state = GetInteraction(TransformGizmoMode.Rotate);
        float snappedRadians = SnapSigned(totalRadians, rotationSnapRadians);
        Matrix4x4 delta;
        if (state.Space == TransformGizmoSpace.Local)
        {
            delta = Matrix4x4.CreateFromAxisAngle(GetAxisVector(state.Axis), snappedRadians);
            Matrix4x4 desiredLocal =
                Matrix4x4.CreateScale(state.InitialPose.Scale) *
                delta *
                Matrix4x4.CreateFromQuaternion(state.InitialPose.Rotation) *
                Matrix4x4.CreateTranslation(state.InitialPose.Position);
            TransformMatrixHelper.DecomposeRepresentable(
                desiredLocal,
                "The local gizmo rotation cannot be represented by Transform3D.",
                out Vector3 scale,
                out Quaternion rotation,
                out Vector3 position);
            return ApplyLocalPose(state, new TransformGizmoPose(position, rotation, scale));
        }

        Vector3 worldAxis = GetAxisVector(state.Axis);
        TransformMatrixHelper.DecomposeRepresentable(
            state.InitialWorld,
            "The target world pose contains shear and cannot be rotated in world space.",
            out Vector3 worldScale,
            out Quaternion worldRotation,
            out Vector3 worldPosition);
        delta = Matrix4x4.CreateFromAxisAngle(worldAxis, snappedRadians);
        Matrix4x4 desiredWorld =
            Matrix4x4.CreateScale(worldScale) *
            Matrix4x4.CreateFromQuaternion(worldRotation) *
            delta *
            Matrix4x4.CreateTranslation(worldPosition);
        return ApplyWorldMatrix(state, desiredWorld);
    }

    /// <summary>Updates a scale interaction with a total positive factor from its start.</summary>
    /// <remarks>
    /// Scale handles always use the target-local axes, matching the scale components stored by
    /// <see cref="Transform3D"/>. This keeps the visible handle, pointer direction and changed scale
    /// component aligned without introducing shear.
    /// </remarks>
    public bool UpdateScale(float totalFactor)
    {
        ThrowIfPositiveFinite(totalFactor, nameof(totalFactor));
        InteractionState state = GetInteraction(TransformGizmoMode.Scale);
        float snappedFactor = SnapScale(totalFactor);
        Vector3 factors = GetScaleFactors(state.Axis, snappedFactor);
        TransformGizmoPose next = new(
            state.InitialPose.Position,
            state.InitialPose.Rotation,
            state.InitialPose.Scale * factors);
        return ApplyLocalPose(state, next);
    }

    /// <summary>Accepts the current pose and completes the active interaction.</summary>
    public void CompleteInteraction()
    {
        ThrowIfDisposed();
        InteractionState state = GetInteraction();
        TransformGizmoPose current = TransformGizmoPose.Capture(state.Target);
        interaction = null;
        InteractionCompleted?.Invoke(this, CreateEventArgs(state, current));
        frameRequester?.RequestFrame();
    }

    /// <summary>Restores the initial pose and cancels the active interaction.</summary>
    public void CancelInteraction()
    {
        ThrowIfDisposed();
        InteractionState state = GetInteraction();
        state.InitialPose.ApplyTo(state.Target);
        state.LastPose = state.InitialPose;
        interaction = null;
        InteractionCanceled?.Invoke(this, CreateEventArgs(state, state.InitialPose));
        frameRequester?.RequestFrame();
    }

    /// <summary>
    /// Calculates the world size required to keep handles at <see cref="ScreenSizePixels"/>.
    /// Returns zero when the target is not in front of the camera.
    /// </summary>
    public float CalculateWorldSize(PerspectiveCamera camera, uint viewportHeightPixels)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfZero(viewportHeightPixels);
        SceneNode currentTarget = target ??
            throw new InvalidOperationException("A transform gizmo requires an explicit target.");
        Vector3 worldPosition = TransformMatrixHelper.GetWorldPosition(currentTarget);
        Vector3 viewPosition = Vector3.Transform(worldPosition, camera.ViewMatrix);
        TransformMatrixHelper.ThrowIfNotFinite(viewPosition, nameof(camera));
        float viewDepth = -viewPosition.Z;
        if (viewDepth <= 0f)
        {
            return 0f;
        }

        double worldSize =
            screenSizePixels *
            (2d * viewDepth * Math.Tan(camera.FieldOfViewRadians * 0.5d)) /
            viewportHeightPixels;
        if (!double.IsFinite(worldSize) || worldSize > float.MaxValue)
        {
            throw new InvalidOperationException("The requested fixed-screen gizmo size is not finite.");
        }
        return (float)worldSize;
    }

    /// <summary>
    /// Cancels an active interaction when its hierarchy is unchanged, then releases subscribers.
    /// The target and frame requester remain application-owned.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        if (interaction is InteractionState state &&
            IsHierarchyUnchanged(state) &&
            ApproximatelyEqual(TransformGizmoPose.Capture(state.Target), state.LastPose))
        {
            state.InitialPose.ApplyTo(state.Target);
            frameRequester?.RequestFrame();
        }
        interaction = null;
        disposed = true;
        InteractionStarted = null;
        InteractionChanged = null;
        InteractionCompleted = null;
        InteractionCanceled = null;
    }

    private bool ApplyLocalPose(InteractionState state, TransformGizmoPose next)
    {
        if (ApproximatelyEqual(state.LastPose, next))
        {
            return false;
        }
        next.ApplyTo(state.Target);
        return PublishChange(state);
    }

    private bool ApplyWorldMatrix(InteractionState state, Matrix4x4 desiredWorld)
    {
        TransformGizmoPose before = state.LastPose;
        TransformMatrixHelper.SetWorldMatrix(
            state.Target,
            desiredWorld,
            "The requested gizmo transform requires local shear that Transform3D cannot represent.");
        TransformGizmoPose current = TransformGizmoPose.Capture(state.Target);
        if (ApproximatelyEqual(before, current))
        {
            return false;
        }
        return PublishChange(state, current);
    }

    private bool PublishChange(InteractionState state) =>
        PublishChange(state, TransformGizmoPose.Capture(state.Target));

    private bool PublishChange(InteractionState state, TransformGizmoPose current)
    {
        state.LastPose = current;
        InteractionChanged?.Invoke(this, CreateEventArgs(state, current));
        frameRequester?.RequestFrame();
        return true;
    }

    private InteractionState GetInteraction(TransformGizmoMode expectedMode)
    {
        InteractionState state = GetInteraction();
        if (state.Mode != expectedMode)
        {
            throw new InvalidOperationException(
                $"The active transform-gizmo interaction is {state.Mode}, not {expectedMode}.");
        }
        return state;
    }

    private InteractionState GetInteraction()
    {
        InteractionState state = interaction ??
            throw new InvalidOperationException("No transform-gizmo interaction is active.");
        if (!IsHierarchyUnchanged(state))
        {
            throw new InvalidOperationException(
                "The transform-gizmo target hierarchy changed during interaction.");
        }
        TransformGizmoPose current = TransformGizmoPose.Capture(state.Target);
        if (!ApproximatelyEqual(current, state.LastPose))
        {
            throw new InvalidOperationException(
                "The transform-gizmo target was changed externally during interaction.");
        }
        return state;
    }

    private bool IsHierarchyUnchanged(InteractionState state) =>
        ReferenceEquals(target, state.Target) &&
        ReferenceEquals(state.Target.Parent, state.Parent) &&
        TransformMatrixHelper.ApproximatelyEqual(
            state.Parent?.WorldMatrix ?? Matrix4x4.Identity,
            state.ParentWorld);

    private static Vector3 GetAxisDirection(InteractionState state)
    {
        Vector3 axis = GetAxisVector(state.Axis);
        if (state.Space == TransformGizmoSpace.World)
        {
            return axis;
        }
        Vector3 transformed = Vector3.TransformNormal(axis, state.InitialWorld);
        if (!float.IsFinite(transformed.X) || !float.IsFinite(transformed.Y) ||
            !float.IsFinite(transformed.Z) || transformed.LengthSquared() <= float.Epsilon)
        {
            throw new InvalidOperationException("The selected local gizmo axis is degenerate.");
        }
        return Vector3.Normalize(transformed);
    }

    private static Vector3 GetAxisVector(TransformGizmoAxis axis) => axis switch
    {
        TransformGizmoAxis.X => Vector3.UnitX,
        TransformGizmoAxis.Y => Vector3.UnitY,
        TransformGizmoAxis.Z => Vector3.UnitZ,
        _ => throw new InvalidOperationException("The uniform handle does not identify one axis."),
    };

    private static Vector3 GetScaleFactors(TransformGizmoAxis axis, float factor) => axis switch
    {
        TransformGizmoAxis.X => new Vector3(factor, 1f, 1f),
        TransformGizmoAxis.Y => new Vector3(1f, factor, 1f),
        TransformGizmoAxis.Z => new Vector3(1f, 1f, factor),
        TransformGizmoAxis.Uniform => new Vector3(factor),
        _ => throw new ArgumentOutOfRangeException(nameof(axis)),
    };

    private float SnapScale(float factor)
    {
        float result = factor;
        if (scaleSnap > 0f)
        {
            result = 1f + MathF.Round(
                (factor - 1f) / scaleSnap,
                MidpointRounding.AwayFromZero) * scaleSnap;
        }
        return Math.Clamp(result, minimumScaleFactor, maximumScaleFactor);
    }

    private static float SnapSigned(float value, float interval) => interval > 0f
        ? MathF.Round(value / interval, MidpointRounding.AwayFromZero) * interval
        : value;

    private static bool ApproximatelyEqual(TransformGizmoPose left, TransformGizmoPose right) =>
        ApproximatelyEqual(left.Position, right.Position) &&
        ApproximatelyEqual(left.Scale, right.Scale) &&
        MathF.Abs(Quaternion.Dot(left.Rotation, right.Rotation)) >= 0.999999f;

    private static bool ApproximatelyEqual(Vector3 left, Vector3 right) =>
        TransformMatrixHelper.ApproximatelyEqual(left.X, right.X) &&
        TransformMatrixHelper.ApproximatelyEqual(left.Y, right.Y) &&
        TransformMatrixHelper.ApproximatelyEqual(left.Z, right.Z);

    private static TransformGizmoInteractionEventArgs CreateEventArgs(
        InteractionState state,
        TransformGizmoPose current) => new(
            state.Target,
            state.Mode,
            state.Space,
            state.Axis,
            state.InitialPose,
            current);

    private static void ThrowIfFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value must be finite.");
        }
    }

    private static void ThrowIfNonNegativeFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The value must be non-negative and finite.");
        }
    }

    private static void ThrowIfPositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The value must be positive and finite.");
        }
    }

    private void ThrowIfInteracting(string propertyName)
    {
        if (interaction is not null)
        {
            throw new InvalidOperationException(
                $"{propertyName} cannot change during a transform-gizmo interaction.");
        }
    }

    private void SetModeEnabled(
        TransformGizmoMode value,
        bool enabled,
        string propertyName)
    {
        ThrowIfDisposed();
        ThrowIfInteracting(propertyName);
        switch (value)
        {
            case TransformGizmoMode.Translate:
                isTranslateEnabled = enabled;
                break;
            case TransformGizmoMode.Rotate:
                isRotateEnabled = enabled;
                break;
            case TransformGizmoMode.Scale:
                isScaleEnabled = enabled;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value));
        }

        if (enabled && !IsModeEnabledCore(mode))
        {
            mode = value;
        }
        else if (!enabled && mode == value)
        {
            if (isTranslateEnabled)
            {
                mode = TransformGizmoMode.Translate;
            }
            else if (isRotateEnabled)
            {
                mode = TransformGizmoMode.Rotate;
            }
            else if (isScaleEnabled)
            {
                mode = TransformGizmoMode.Scale;
            }
        }
        frameRequester?.RequestFrame();
    }

    private bool IsModeEnabledCore(TransformGizmoMode value) => value switch
    {
        TransformGizmoMode.Translate => isTranslateEnabled,
        TransformGizmoMode.Rotate => isRotateEnabled,
        TransformGizmoMode.Scale => isScaleEnabled,
        _ => false,
    };

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private sealed class InteractionState
    {
        internal InteractionState(
            SceneNode target,
            SceneNode? parent,
            Matrix4x4 parentWorld,
            Matrix4x4 inverseParentWorld,
            TransformGizmoPose initialPose,
            Matrix4x4 initialWorld,
            TransformGizmoMode mode,
            TransformGizmoSpace space,
            TransformGizmoAxis axis)
        {
            Target = target;
            Parent = parent;
            ParentWorld = parentWorld;
            InverseParentWorld = inverseParentWorld;
            InitialPose = initialPose;
            LastPose = initialPose;
            InitialWorld = initialWorld;
            InitialWorldPosition = Vector3.Transform(Vector3.Zero, initialWorld);
            Mode = mode;
            Space = space;
            Axis = axis;
        }

        internal SceneNode Target { get; }

        internal SceneNode? Parent { get; }

        internal Matrix4x4 ParentWorld { get; }

        internal Matrix4x4 InverseParentWorld { get; }

        internal TransformGizmoPose InitialPose { get; }

        internal TransformGizmoPose LastPose { get; set; }

        internal Matrix4x4 InitialWorld { get; }

        internal Vector3 InitialWorldPosition { get; }

        internal TransformGizmoMode Mode { get; }

        internal TransformGizmoSpace Space { get; }

        internal TransformGizmoAxis Axis { get; }
    }
}

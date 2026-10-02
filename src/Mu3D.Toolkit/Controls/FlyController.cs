using System.Numerics;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Internal;
using Mu3D.Toolkit.Viewports;

namespace Mu3D.Toolkit.Controls;

/// <summary>Applies UI-independent first-person movement and look commands to one camera.</summary>
/// <remarks>
/// Movement uses world units: local X strafes right, local Y follows <see cref="WorldUp"/>, and
/// local Z moves along the camera's forward direction. Look deltas are yaw and pitch radians. The
/// controller borrows its camera and frame requester, acquires no raw input, owns no timer, and is
/// not thread-safe.
/// </remarks>
public sealed class FlyController : IViewportNavigationController, IDisposable
{
    private const float PitchEpsilon = 0.0001f;
    private const float ResidualEpsilon = 0.000001f;
    private readonly PerspectiveCamera camera;
    private readonly IViewportFrameRequester? frameRequester;
    private readonly Vector3 worldUp;
    private readonly Vector3 yawReference;
    private readonly Vector3 yawRight;
    private float yawAngle;
    private float pitchAngle;
    private float minimumPitchAngle = -MathF.PI / 2f + PitchEpsilon;
    private float maximumPitchAngle = MathF.PI / 2f - PitchEpsilon;
    private float rotationSensitivity = 1f;
    private float panSensitivity = 1f;
    private float dollySensitivity = 1f;
    private Vector3 pendingMovement;
    private Vector2 pendingLook;
    private bool dampingEnabled;
    private float dampingTime = 0.12f;
    private bool enabled = true;
    private bool disposed;

    /// <summary>Initializes a first-person controller for one borrowed camera.</summary>
    /// <param name="camera">The borrowed camera whose complete world pose is controlled.</param>
    /// <param name="frameRequester">An optional borrowed one-shot frame requester.</param>
    /// <param name="worldUp">The finite non-zero world-up direction.</param>
    public FlyController(
        PerspectiveCamera camera,
        IViewportFrameRequester? frameRequester = null,
        Vector3? worldUp = null)
    {
        ArgumentNullException.ThrowIfNull(camera);
        Vector3 requestedUp = worldUp ?? Vector3.UnitY;
        TransformMatrixHelper.ThrowIfNotFinite(requestedUp, nameof(worldUp));
        if (requestedUp.LengthSquared() <= float.Epsilon)
        {
            throw new ArgumentOutOfRangeException(nameof(worldUp), "World up must be non-zero.");
        }

        this.camera = camera;
        this.frameRequester = frameRequester;
        this.worldUp = Vector3.Normalize(requestedUp);
        yawReference = CreateYawReference(this.worldUp);
        yawRight = Vector3.Normalize(Vector3.Cross(yawReference, this.worldUp));
        ReadAnglesFromCamera();
        ApplyPose(Position, yawAngle, pitchAngle);
    }

    /// <summary>Occurs after a command changes the camera pose.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the borrowed camera.</summary>
    public PerspectiveCamera Camera => camera;

    /// <summary>Gets the optional borrowed one-shot frame requester.</summary>
    public IViewportFrameRequester? FrameRequester => frameRequester;

    /// <summary>Gets the normalized immutable world-up direction.</summary>
    public Vector3 WorldUp => worldUp;

    /// <summary>Gets the current finite world-space camera position.</summary>
    public Vector3 Position => TransformMatrixHelper.GetWorldPosition(camera);

    /// <summary>Gets the current normalized world-space forward direction.</summary>
    public Vector3 ForwardDirection => CalculateForward(yawAngle, pitchAngle);

    /// <summary>Gets the current normalized world-space right direction.</summary>
    public Vector3 RightDirection => Vector3.Normalize(Vector3.Cross(ForwardDirection, worldUp));

    /// <summary>Gets the current yaw angle in radians around <see cref="WorldUp"/>.</summary>
    public float YawAngle => yawAngle;

    /// <summary>Gets the current pitch angle in radians above the world-up plane.</summary>
    public float PitchAngle => pitchAngle;

    /// <summary>Gets or sets whether movement and look commands are accepted.</summary>
    public bool IsEnabled
    {
        get => enabled;
        set
        {
            ThrowIfDisposed();
            if (enabled == value)
            {
                return;
            }
            enabled = value;
            if (!enabled)
            {
                ClearPendingMotion();
            }
        }
    }

    /// <summary>Gets or sets whether commands are consumed over subsequent frame updates.</summary>
    public bool DampingEnabled
    {
        get => dampingEnabled;
        set
        {
            ThrowIfDisposed();
            if (dampingEnabled == value)
            {
                return;
            }
            if (!value && HasPendingMotion)
            {
                Vector3 movement = pendingMovement;
                Vector2 look = pendingLook;
                bool changed = ApplyDeltas(movement, look);
                ClearPendingMotion();
                dampingEnabled = false;
                if (changed)
                {
                    NotifyChangedAndRequestFrame();
                }
                return;
            }
            dampingEnabled = value;
        }
    }

    /// <summary>
    /// Gets or sets the positive exponential damping time in seconds. The default is 0.12 seconds.
    /// </summary>
    public float DampingTime
    {
        get => dampingTime;
        set
        {
            ThrowIfDisposed();
            ThrowIfPositiveFinite(value, nameof(value));
            dampingTime = value;
        }
    }

    /// <summary>Gets whether a damped movement or look command remains to be consumed.</summary>
    public bool HasPendingMotion => pendingMovement != Vector3.Zero || pendingLook != Vector2.Zero;

    /// <summary>Gets or sets the non-negative Rotate multiplier. The default is one.</summary>
    public float RotationSensitivity
    {
        get => rotationSensitivity;
        set
        {
            ThrowIfDisposed();
            ThrowIfNonNegativeFinite(value, nameof(value));
            rotationSensitivity = value;
        }
    }

    /// <summary>Gets or sets the non-negative Pan/local-X-Y multiplier. The default is one.</summary>
    public float PanSensitivity
    {
        get => panSensitivity;
        set
        {
            ThrowIfDisposed();
            ThrowIfNonNegativeFinite(value, nameof(value));
            panSensitivity = value;
        }
    }

    /// <summary>Gets or sets the non-negative Dolly multiplier. The default is one.</summary>
    public float DollySensitivity
    {
        get => dollySensitivity;
        set
        {
            ThrowIfDisposed();
            ThrowIfNonNegativeFinite(value, nameof(value));
            dollySensitivity = value;
        }
    }

    /// <summary>Gets or sets the minimum pitch angle above negative pi/2.</summary>
    public float MinimumPitchAngle
    {
        get => minimumPitchAngle;
        set
        {
            ThrowIfDisposed();
            ThrowIfPitch(value, nameof(value));
            if (value > maximumPitchAngle)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            minimumPitchAngle = value;
            ApplyPitchConstraint();
        }
    }

    /// <summary>Gets or sets the maximum pitch angle below positive pi/2.</summary>
    public float MaximumPitchAngle
    {
        get => maximumPitchAngle;
        set
        {
            ThrowIfDisposed();
            ThrowIfPitch(value, nameof(value));
            if (value < minimumPitchAngle)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            maximumPitchAngle = value;
            ApplyPitchConstraint();
        }
    }

    /// <summary>Moves the camera in its local first-person basis.</summary>
    /// <param name="localDelta">X strafes right, Y follows world up, and Z moves forward.</param>
    /// <returns><see langword="true"/> when an enabled non-zero command was accepted.</returns>
    public bool Move(Vector3 localDelta)
    {
        return Navigate(localDelta, Vector2.Zero);
    }

    /// <summary>Changes yaw and pitch in radians.</summary>
    /// <param name="deltaRadians">Positive X turns right; positive Y looks up.</param>
    /// <returns><see langword="true"/> when an enabled non-zero command was accepted.</returns>
    public bool Look(Vector2 deltaRadians)
    {
        return Navigate(Vector3.Zero, deltaRadians);
    }

    /// <inheritdoc />
    bool IViewportNavigationController.Rotate(Vector2 delta) =>
        Look(delta);

    /// <inheritdoc />
    bool IViewportNavigationController.Pan(Vector2 delta) =>
        Move(new Vector3(delta.X, delta.Y, 0f));

    /// <inheritdoc />
    bool IViewportNavigationController.Dolly(float delta) =>
        Move(new Vector3(0f, 0f, delta));

    /// <summary>Applies one atomic local movement and yaw/pitch update.</summary>
    /// <param name="localMovement">
    /// X strafes right, Y follows world up, and Z moves forward in the updated orientation.
    /// </param>
    /// <param name="lookDeltaRadians">Positive X turns right; positive Y looks up.</param>
    /// <returns><see langword="true"/> when an enabled non-zero component was accepted.</returns>
    /// <remarks>
    /// Both components are validated before mutation and produce at most one pose assignment,
    /// change notification and frame request. Movement follows the orientation after applying the
    /// supplied look delta, which keeps simultaneous held-key movement and look deterministic.
    /// With damping enabled, the validated components are queued and consumed by <see cref="Update"/>.
    /// </remarks>
    public bool Navigate(Vector3 localMovement, Vector2 lookDeltaRadians)
    {
        ThrowIfDisposed();
        TransformMatrixHelper.ThrowIfNotFinite(localMovement, nameof(localMovement));
        ThrowIfFinite(lookDeltaRadians, nameof(lookDeltaRadians));
        if (!enabled)
        {
            return false;
        }

        Vector3 scaledMovement = new(
            localMovement.X * panSensitivity,
            localMovement.Y * panSensitivity,
            localMovement.Z * dollySensitivity);
        Vector2 scaledLook = lookDeltaRadians * rotationSensitivity;
        TransformMatrixHelper.ThrowIfNotFinite(scaledMovement, nameof(localMovement));
        ThrowIfFinite(scaledLook, nameof(lookDeltaRadians));
        if (scaledMovement == Vector3.Zero && scaledLook == Vector2.Zero)
        {
            return false;
        }

        if (dampingEnabled)
        {
            Vector3 nextMovement = pendingMovement + scaledMovement;
            Vector2 nextLook = pendingLook + scaledLook;
            TransformMatrixHelper.ThrowIfNotFinite(nextMovement, nameof(localMovement));
            ThrowIfFinite(nextLook, nameof(lookDeltaRadians));
            pendingMovement = nextMovement;
            pendingLook = nextLook;
            frameRequester?.RequestFrame();
            return true;
        }

        bool changed = ApplyDeltas(scaledMovement, scaledLook);
        if (changed)
        {
            NotifyChangedAndRequestFrame();
        }
        return changed;
    }

    /// <summary>Consumes pending damped movement for one host-supplied frame interval.</summary>
    /// <param name="elapsedSeconds">A finite non-negative elapsed duration.</param>
    /// <returns><see langword="true"/> when this update changed the camera pose.</returns>
    public bool Update(float elapsedSeconds)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }
        if (!enabled || !dampingEnabled || !HasPendingMotion)
        {
            return false;
        }
        if (elapsedSeconds == 0f)
        {
            frameRequester?.RequestFrame();
            return false;
        }

        float amount = 1f - MathF.Exp(-elapsedSeconds / dampingTime);
        Vector3 movementStep = CalculateStep(pendingMovement, amount);
        Vector2 lookStep = CalculateStep(pendingLook, amount);
        bool changed = ApplyDeltas(movementStep, lookStep);
        pendingMovement -= movementStep;
        pendingLook -= lookStep;
        NormalizeResiduals();
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        if (changed || HasPendingMotion)
        {
            frameRequester?.RequestFrame();
        }
        return changed;
    }

    private bool ApplyDeltas(Vector3 localMovement, Vector2 lookDeltaRadians)
    {
        float nextYaw = yawAngle;
        float nextPitch = pitchAngle;
        if (lookDeltaRadians != Vector2.Zero)
        {
            nextYaw = NormalizeAngle((double)yawAngle + lookDeltaRadians.X);
            nextPitch = (float)Math.Clamp(
                (double)pitchAngle + lookDeltaRadians.Y,
                minimumPitchAngle,
                maximumPitchAngle);
        }

        Vector3 currentPosition = Position;
        Vector3 nextPosition = currentPosition;
        if (localMovement != Vector3.Zero)
        {
            Vector3 nextForward = CalculateForward(nextYaw, nextPitch);
            Vector3 nextRight = Vector3.Normalize(Vector3.Cross(nextForward, worldUp));
            Vector3 translation =
                nextRight * localMovement.X +
                worldUp * localMovement.Y +
                nextForward * localMovement.Z;
            TransformMatrixHelper.ThrowIfNotFinite(translation, nameof(localMovement));
            nextPosition += translation;
        }

        if (nextPosition == currentPosition && nextYaw == yawAngle && nextPitch == pitchAngle)
        {
            return false;
        }

        ApplyPose(nextPosition, nextYaw, nextPitch);
        yawAngle = nextYaw;
        pitchAngle = nextPitch;
        return true;
    }

    /// <summary>Re-reads an externally assigned camera pose.</summary>
    public void SynchronizeFromCamera()
    {
        ThrowIfDisposed();
        ReadAnglesFromCamera();
        ApplyPose(Position, yawAngle, pitchAngle);
        ClearPendingMotion();
        NotifyChangedAndRequestFrame();
    }

    /// <summary>Stops accepting commands and releases event subscribers.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        ClearPendingMotion();
        Changed = null;
    }

    private void ReadAnglesFromCamera()
    {
        Vector3 forward = Vector3.TransformNormal(-Vector3.UnitZ, camera.WorldMatrix);
        TransformMatrixHelper.ThrowIfNotFinite(forward, nameof(camera));
        float lengthSquared = forward.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
        {
            throw new ArgumentException("The camera forward direction must be non-zero.", nameof(camera));
        }
        forward /= MathF.Sqrt(lengthSquared);
        pitchAngle = Math.Clamp(
            MathF.Asin(Math.Clamp(Vector3.Dot(forward, worldUp), -1f, 1f)),
            minimumPitchAngle,
            maximumPitchAngle);
        Vector3 horizontal = forward - worldUp * Vector3.Dot(forward, worldUp);
        if (horizontal.LengthSquared() > float.Epsilon)
        {
            horizontal = Vector3.Normalize(horizontal);
            yawAngle = NormalizeAngle(MathF.Atan2(
                Vector3.Dot(horizontal, yawRight),
                Vector3.Dot(horizontal, yawReference)));
        }
    }

    private void ApplyPitchConstraint()
    {
        float next = Math.Clamp(pitchAngle, minimumPitchAngle, maximumPitchAngle);
        if (next == pitchAngle)
        {
            return;
        }
        pitchAngle = next;
        ApplyPose(Position, yawAngle, pitchAngle);
        NotifyChangedAndRequestFrame();
    }

    private void ApplyPose(Vector3 position, float yaw, float pitch)
    {
        TransformMatrixHelper.ThrowIfNotFinite(position, nameof(position));
        Vector3 forward = CalculateForward(yaw, pitch);
        Matrix4x4 view = Matrix4x4.CreateLookAt(position, position + forward, worldUp);
        if (!Matrix4x4.Invert(view, out Matrix4x4 world))
        {
            throw new InvalidOperationException("The fly camera world transform is not invertible.");
        }
        TransformMatrixHelper.SetWorldMatrix(
            camera,
            world,
            "The fly camera parent requires a local shear that Transform3D cannot represent.");
    }

    private Vector3 CalculateForward(float yaw, float pitch)
    {
        float cosPitch = MathF.Cos(pitch);
        return Vector3.Normalize(
            yawReference * (cosPitch * MathF.Cos(yaw)) +
            yawRight * (cosPitch * MathF.Sin(yaw)) +
            worldUp * MathF.Sin(pitch));
    }

    private void NotifyChangedAndRequestFrame()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        frameRequester?.RequestFrame();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private static Vector3 CreateYawReference(Vector3 up)
    {
        Vector3 reference = -Vector3.UnitZ - up * Vector3.Dot(-Vector3.UnitZ, up);
        if (reference.LengthSquared() <= float.Epsilon)
        {
            reference = Vector3.UnitX - up * Vector3.Dot(Vector3.UnitX, up);
        }
        return Vector3.Normalize(reference);
    }

    private static float NormalizeAngle(double value)
    {
        double wrapped = Math.IEEERemainder(value, Math.PI * 2d);
        return (float)(wrapped <= -Math.PI ? wrapped + Math.PI * 2d : wrapped);
    }

    private static void ThrowIfFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ThrowIfNonNegativeFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private static void ThrowIfPositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    private void ClearPendingMotion()
    {
        pendingMovement = Vector3.Zero;
        pendingLook = Vector2.Zero;
    }

    private void NormalizeResiduals()
    {
        if (pendingMovement.LengthSquared() <= ResidualEpsilon * ResidualEpsilon)
        {
            pendingMovement = Vector3.Zero;
        }
        if (pendingLook.LengthSquared() <= ResidualEpsilon * ResidualEpsilon)
        {
            pendingLook = Vector2.Zero;
        }
    }

    private static Vector3 CalculateStep(Vector3 residual, float amount)
    {
        Vector3 step = residual * amount;
        return (residual - step).LengthSquared() <= ResidualEpsilon * ResidualEpsilon
            ? residual
            : step;
    }

    private static Vector2 CalculateStep(Vector2 residual, float amount)
    {
        Vector2 step = residual * amount;
        return (residual - step).LengthSquared() <= ResidualEpsilon * ResidualEpsilon
            ? residual
            : step;
    }

    private static void ThrowIfPitch(float value, string parameterName)
    {
        if (!float.IsFinite(value) ||
            value < -MathF.PI / 2f + PitchEpsilon ||
            value > MathF.PI / 2f - PitchEpsilon)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

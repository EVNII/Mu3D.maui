using System.Numerics;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Internal;
using Mu3D.Toolkit.Viewports;

namespace Mu3D.Toolkit.Controls;

/// <summary>Specifies the world-space basis used by <see cref="OrbitController.Pan"/>.</summary>
public enum OrbitPanMode
{
    /// <summary>Moves along camera right and camera up.</summary>
    ScreenSpace,

    /// <summary>Moves along the plane perpendicular to the controller's world-up direction.</summary>
    WorldUpPlane,
}

/// <summary>
/// Applies UI-independent orbit, pan and dolly commands to one perspective camera.
/// </summary>
/// <remarks>
/// Commands use FP32 world and camera state. Rotation is expressed in radians, pan in fractions of
/// the visible viewport span at the target, and dolly as a logarithmic distance delta. The
/// controller does not acquire raw input, select scene objects, create a timer, or own the camera or
/// optional frame requester. It is not thread-safe.
/// </remarks>
public class OrbitController : IViewportNavigationController, IDisposable
{
    private const float DefaultMinimumDistance = 0.0001f;
    private const float PolarEpsilon = 0.0001f;
    private const float ResidualEpsilon = 0.000001f;
    private readonly PerspectiveCamera camera;
    private readonly IViewportFrameRequester? frameRequester;
    private readonly Vector3 worldUp;
    private readonly Vector3 azimuthReference;
    private readonly Vector3 azimuthRight;
    private Vector3 target;
    private Vector2 pendingRotation;
    private Vector2 pendingPan;
    private float pendingDolly;
    private float azimuthAngle;
    private float polarAngle;
    private float distance;
    private float minimumDistance = DefaultMinimumDistance;
    private float maximumDistance = float.MaxValue;
    private float minimumPolarAngle = PolarEpsilon;
    private float maximumPolarAngle = MathF.PI - PolarEpsilon;
    private float rotationSensitivity = 1f;
    private float panSensitivity = 1f;
    private float dollySensitivity = 1f;
    private float dampingTime = 0.12f;
    private OrbitPanMode panMode;
    private bool enabled = true;
    private bool dampingEnabled;
    private bool disposed;

    /// <summary>Initializes an orbit controller around one world-space target.</summary>
    /// <param name="camera">The borrowed perspective camera whose complete pose is controlled.</param>
    /// <param name="target">The finite world-space orbit target.</param>
    /// <param name="frameRequester">An optional borrowed one-shot frame requester.</param>
    /// <param name="worldUp">The finite non-zero world-up direction.</param>
    public OrbitController(
        PerspectiveCamera camera,
        Vector3 target,
        IViewportFrameRequester? frameRequester = null,
        Vector3? worldUp = null)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ThrowIfNotFinite(target, nameof(target));
        Vector3 requestedUp = worldUp ?? Vector3.UnitY;
        ThrowIfNotFinite(requestedUp, nameof(worldUp));
        if (requestedUp.LengthSquared() <= float.Epsilon)
        {
            throw new ArgumentOutOfRangeException(nameof(worldUp), "World up must be non-zero.");
        }

        this.camera = camera;
        this.frameRequester = frameRequester;
        this.worldUp = Vector3.Normalize(requestedUp);
        azimuthReference = CreateAzimuthReference(this.worldUp);
        azimuthRight = Vector3.Normalize(Vector3.Cross(this.worldUp, azimuthReference));
        this.target = target;

        Vector3 eye = GetCameraWorldPosition();
        OrbitState initial = CalculateState(eye, target, azimuthAngle);
        SetCameraWorldPose(initial.Target, initial.Azimuth, initial.Polar, initial.Distance);
        AssignState(initial);
    }

    /// <summary>Occurs after this controller changes the camera pose.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the borrowed perspective camera.</summary>
    public PerspectiveCamera Camera => camera;

    /// <summary>Gets the optional borrowed frame requester.</summary>
    public IViewportFrameRequester? FrameRequester => frameRequester;

    /// <summary>Gets the normalized immutable world-up direction.</summary>
    public Vector3 WorldUp => worldUp;

    /// <summary>Gets or sets the finite world-space orbit and pan target.</summary>
    public Vector3 Target
    {
        get => target;
        set
        {
            ThrowIfDisposed();
            ThrowIfNotFinite(value, nameof(value));
            if (value == target)
            {
                return;
            }

            OrbitState next = CalculateState(GetCameraWorldPosition(), value, azimuthAngle);
            SetCameraWorldPose(next.Target, next.Azimuth, next.Polar, next.Distance);
            AssignState(next);
            ClearPendingMotion();
            NotifyChangedAndRequestFrame();
        }
    }

    /// <summary>Gets the current azimuth angle in radians around <see cref="WorldUp"/>.</summary>
    public float AzimuthAngle => azimuthAngle;

    /// <summary>Gets the current polar angle in radians measured from <see cref="WorldUp"/>.</summary>
    public float PolarAngle => polarAngle;

    /// <summary>Gets the current positive camera-to-target distance.</summary>
    public float Distance => distance;

    /// <summary>Moves the camera to one target-relative viewing direction at its current distance.</summary>
    /// <param name="targetToCameraDirection">
    /// A finite non-zero world-space direction from <see cref="Target"/> toward the camera.
    /// Cardinal and normalized two-axis directions produce orthogonal and 45-degree views.
    /// </param>
    /// <returns><see langword="true"/> when the camera pose changed.</returns>
    /// <remarks>
    /// The direction is normalized and constrained by the current polar-angle limits. Pending
    /// damped input is discarded so subsequent orbit gestures continue from the selected view.
    /// </remarks>
    public bool SetViewDirection(Vector3 targetToCameraDirection)
    {
        ThrowIfDisposed();
        ThrowIfNotFinite(targetToCameraDirection, nameof(targetToCameraDirection));
        if (!enabled)
        {
            return false;
        }
        float lengthSquared = targetToCameraDirection.LengthSquared();
        if (!float.IsFinite(lengthSquared) || lengthSquared <= float.Epsilon)
        {
            throw new ArgumentOutOfRangeException(
                nameof(targetToCameraDirection),
                "The target-to-camera direction must be non-zero.");
        }

        Vector3 direction = targetToCameraDirection / MathF.Sqrt(lengthSquared);
        float upComponent = Math.Clamp(Vector3.Dot(direction, worldUp), -1f, 1f);
        float nextPolar = Math.Clamp(
            MathF.Acos(upComponent),
            minimumPolarAngle,
            maximumPolarAngle);
        Vector3 horizontal = direction - worldUp * upComponent;
        float nextAzimuth = azimuthAngle;
        if (horizontal.LengthSquared() > float.Epsilon)
        {
            horizontal = Vector3.Normalize(horizontal);
            nextAzimuth = MathF.Atan2(
                Vector3.Dot(horizontal, azimuthRight),
                Vector3.Dot(horizontal, azimuthReference));
        }
        nextAzimuth = NormalizeAngle(nextAzimuth);
        if (nextAzimuth == azimuthAngle && nextPolar == polarAngle && !HasPendingMotion)
        {
            return false;
        }

        SetCameraWorldPose(target, nextAzimuth, nextPolar, distance);
        azimuthAngle = nextAzimuth;
        polarAngle = nextPolar;
        ClearPendingMotion();
        NotifyChangedAndRequestFrame();
        return true;
    }

    /// <summary>Gets or sets whether normalized commands and pending motion are accepted.</summary>
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
                Vector2 rotation = pendingRotation;
                Vector2 pan = pendingPan;
                float dolly = pendingDolly;
                bool changed = ApplyDeltas(rotation, pan, dolly);
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

    /// <summary>Gets whether a damped rotate, pan or dolly command remains to be consumed.</summary>
    public bool HasPendingMotion =>
        pendingRotation != Vector2.Zero || pendingPan != Vector2.Zero || pendingDolly != 0f;

    /// <summary>Gets or sets the non-negative rotation multiplier. The default is one.</summary>
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

    /// <summary>Gets or sets the non-negative viewport-span pan multiplier. The default is one.</summary>
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

    /// <summary>
    /// Gets or sets the world-space basis used for panning. The default is
    /// <see cref="OrbitPanMode.ScreenSpace"/>.
    /// </summary>
    public OrbitPanMode PanMode
    {
        get => panMode;
        set
        {
            ThrowIfDisposed();
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            if (panMode == value)
            {
                return;
            }

            panMode = value;
            pendingPan = Vector2.Zero;
        }
    }

    /// <summary>Gets or sets the non-negative logarithmic dolly multiplier. The default is one.</summary>
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

    /// <summary>Gets or sets the positive minimum camera-to-target distance.</summary>
    public float MinimumDistance
    {
        get => minimumDistance;
        set
        {
            ThrowIfDisposed();
            ThrowIfPositiveFinite(value, nameof(value));
            if (value > maximumDistance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Minimum distance cannot exceed maximum distance.");
            }
            minimumDistance = value;
            ApplyConstraintChanges();
        }
    }

    /// <summary>Gets or sets the finite maximum camera-to-target distance.</summary>
    public float MaximumDistance
    {
        get => maximumDistance;
        set
        {
            ThrowIfDisposed();
            ThrowIfPositiveFinite(value, nameof(value));
            if (value < minimumDistance)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Maximum distance cannot be below minimum distance.");
            }
            maximumDistance = value;
            ApplyConstraintChanges();
        }
    }

    /// <summary>Gets or sets the positive minimum polar angle in radians.</summary>
    public float MinimumPolarAngle
    {
        get => minimumPolarAngle;
        set
        {
            ThrowIfDisposed();
            ThrowIfPolarAngle(value, nameof(value));
            if (value > maximumPolarAngle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Minimum polar angle cannot exceed maximum polar angle.");
            }
            minimumPolarAngle = value;
            ApplyConstraintChanges();
        }
    }

    /// <summary>Gets or sets the maximum polar angle below pi radians.</summary>
    public float MaximumPolarAngle
    {
        get => maximumPolarAngle;
        set
        {
            ThrowIfDisposed();
            ThrowIfPolarAngle(value, nameof(value));
            if (value < minimumPolarAngle)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Maximum polar angle cannot be below minimum polar angle.");
            }
            maximumPolarAngle = value;
            ApplyConstraintChanges();
        }
    }

    /// <summary>
    /// Applies or queues an azimuth/polar rotation delta in radians.
    /// </summary>
    /// <param name="deltaRadians">
    /// X increases azimuth around world up; Y increases the polar angle toward the lower pole.
    /// </param>
    /// <returns>True when a non-zero enabled command was applied or queued.</returns>
    public bool Rotate(Vector2 deltaRadians)
    {
        ThrowIfDisposed();
        ThrowIfNotFinite(deltaRadians, nameof(deltaRadians));
        return Submit(
            deltaRadians * rotationSensitivity,
            Vector2.Zero,
            0f);
    }

    /// <summary>
    /// Applies or queues a pan measured in fractions of the visible target-plane span.
    /// </summary>
    /// <param name="viewportDelta">
    /// Positive X moves along camera right by that fraction of viewport width. Positive Y follows
    /// camera up in <see cref="OrbitPanMode.ScreenSpace"/> or its projection onto the world-up
    /// plane in <see cref="OrbitPanMode.WorldUpPlane"/>.
    /// </param>
    /// <returns>True when a non-zero enabled command was applied or queued.</returns>
    public bool Pan(Vector2 viewportDelta)
    {
        ThrowIfDisposed();
        ThrowIfNotFinite(viewportDelta, nameof(viewportDelta));
        return Submit(
            Vector2.Zero,
            viewportDelta * panSensitivity,
            0f);
    }

    /// <summary>Applies or queues a logarithmic camera-to-target distance change.</summary>
    /// <param name="logarithmicDelta">
    /// Positive values move toward the target; one unit divides distance by e before constraints.
    /// </param>
    /// <returns>True when a non-zero enabled command was applied or queued.</returns>
    public bool Dolly(float logarithmicDelta)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(logarithmicDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(logarithmicDelta));
        }
        return Submit(
            Vector2.Zero,
            Vector2.Zero,
            logarithmicDelta * dollySensitivity);
    }

    /// <summary>Consumes pending damped motion for one host-supplied frame interval.</summary>
    /// <param name="elapsedSeconds">A finite non-negative elapsed duration.</param>
    /// <returns>True when this update changed the camera pose.</returns>
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
        Vector2 rotationStep = CalculateStep(pendingRotation, amount);
        Vector2 panStep = CalculateStep(pendingPan, amount);
        float dollyStep = CalculateStep(pendingDolly, amount);
        bool changed = ApplyDeltas(rotationStep, panStep, dollyStep);
        pendingRotation -= rotationStep;
        pendingPan -= panStep;
        pendingDolly -= dollyStep;
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

    /// <summary>
    /// Re-reads an externally changed camera position, clears pending motion and looks at
    /// <see cref="Target"/> again.
    /// </summary>
    public void SynchronizeFromCamera()
    {
        ThrowIfDisposed();
        OrbitState next = CalculateState(GetCameraWorldPosition(), target, azimuthAngle);
        SetCameraWorldPose(next.Target, next.Azimuth, next.Polar, next.Distance);
        AssignState(next);
        ClearPendingMotion();
        NotifyChangedAndRequestFrame();
    }

    /// <summary>
    /// Stops accepting commands and releases event subscribers. The camera and frame requester are
    /// borrowed and remain alive.
    /// </summary>
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

    private bool Submit(Vector2 rotation, Vector2 pan, float dolly)
    {
        if (!enabled || (rotation == Vector2.Zero && pan == Vector2.Zero && dolly == 0f))
        {
            return false;
        }
        ThrowIfNotFinite(rotation, nameof(rotation));
        ThrowIfNotFinite(pan, nameof(pan));
        if (!float.IsFinite(dolly))
        {
            throw new ArgumentOutOfRangeException(nameof(dolly));
        }

        if (dampingEnabled)
        {
            Vector2 nextRotation = pendingRotation + rotation;
            Vector2 nextPan = pendingPan + pan;
            float nextDolly = pendingDolly + dolly;
            ThrowIfNotFinite(nextRotation, nameof(rotation));
            ThrowIfNotFinite(nextPan, nameof(pan));
            if (!float.IsFinite(nextDolly))
            {
                throw new ArgumentOutOfRangeException(nameof(dolly));
            }
            pendingRotation = nextRotation;
            pendingPan = nextPan;
            pendingDolly = nextDolly;
            frameRequester?.RequestFrame();
            return true;
        }

        bool changed = ApplyDeltas(rotation, pan, dolly);
        if (changed)
        {
            NotifyChangedAndRequestFrame();
        }
        return changed;
    }

    private bool ApplyDeltas(Vector2 rotation, Vector2 pan, float dolly)
    {
        float nextAzimuth = NormalizeAngle((double)azimuthAngle + rotation.X);
        float nextPolar = Math.Clamp(polarAngle + rotation.Y, minimumPolarAngle, maximumPolarAngle);
        float nextDistance = CalculateDollyDistance(distance, dolly);
        Vector3 nextTarget = target;
        if (pan != Vector2.Zero)
        {
            nextTarget += CalculatePanTranslation(
                pan,
                nextAzimuth,
                nextPolar,
                nextDistance);
            ThrowIfNotFinite(nextTarget, nameof(pan));
        }

        if (nextAzimuth == azimuthAngle &&
            nextPolar == polarAngle &&
            nextDistance == distance &&
            nextTarget == target)
        {
            return false;
        }

        SetCameraWorldPose(nextTarget, nextAzimuth, nextPolar, nextDistance);
        target = nextTarget;
        azimuthAngle = nextAzimuth;
        polarAngle = nextPolar;
        distance = nextDistance;
        return true;
    }

    private float CalculateDollyDistance(float currentDistance, float logarithmicDelta)
    {
        double scaled = currentDistance * Math.Exp(-(double)logarithmicDelta);
        if (scaled <= minimumDistance)
        {
            return minimumDistance;
        }
        if (scaled >= maximumDistance)
        {
            return maximumDistance;
        }
        return (float)scaled;
    }

    private Vector3 CalculatePanTranslation(
        Vector2 viewportDelta,
        float azimuth,
        float polar,
        float cameraDistance)
    {
        Vector3 offsetDirection = CalculateOffsetDirection(azimuth, polar);
        Vector3 forward = -offsetDirection;
        Vector3 right = Vector3.Normalize(Vector3.Cross(forward, worldUp));
        Vector3 viewUp = Vector3.Normalize(Vector3.Cross(right, forward));
        Vector3 panUp = viewUp;
        if (panMode == OrbitPanMode.WorldUpPlane)
        {
            panUp -= worldUp * Vector3.Dot(panUp, worldUp);
            if (panUp.LengthSquared() <= float.Epsilon)
            {
                panUp = Vector3.Cross(worldUp, right);
            }
            panUp = Vector3.Normalize(panUp);
        }
        double verticalSpan = 2d * cameraDistance * Math.Tan(camera.FieldOfViewRadians * 0.5d);
        double horizontalSpan = verticalSpan * camera.AspectRatio;
        Vector3 translation =
            right * (float)(viewportDelta.X * horizontalSpan) +
            panUp * (float)(viewportDelta.Y * verticalSpan);
        ThrowIfNotFinite(translation, nameof(viewportDelta));
        return translation;
    }

    private void ApplyConstraintChanges()
    {
        float nextDistance = Math.Clamp(distance, minimumDistance, maximumDistance);
        float nextPolar = Math.Clamp(polarAngle, minimumPolarAngle, maximumPolarAngle);
        if (nextDistance == distance && nextPolar == polarAngle)
        {
            return;
        }

        SetCameraWorldPose(target, azimuthAngle, nextPolar, nextDistance);
        distance = nextDistance;
        polarAngle = nextPolar;
        ClearPendingMotion();
        NotifyChangedAndRequestFrame();
    }

    private OrbitState CalculateState(Vector3 eye, Vector3 nextTarget, float fallbackAzimuth)
    {
        ThrowIfNotFinite(eye, nameof(eye));
        Vector3 offset = eye - nextTarget;
        float rawDistance = offset.Length();
        if (!float.IsFinite(rawDistance) || rawDistance <= float.Epsilon)
        {
            throw new ArgumentException(
                "The orbit camera position must differ from its target.",
                nameof(nextTarget));
        }

        Vector3 direction = offset / rawDistance;
        float upComponent = Math.Clamp(Vector3.Dot(direction, worldUp), -1f, 1f);
        float nextPolar = Math.Clamp(
            MathF.Acos(upComponent),
            minimumPolarAngle,
            maximumPolarAngle);
        Vector3 horizontal = direction - worldUp * upComponent;
        float nextAzimuth = fallbackAzimuth;
        if (horizontal.LengthSquared() > float.Epsilon)
        {
            horizontal = Vector3.Normalize(horizontal);
            nextAzimuth = MathF.Atan2(
                Vector3.Dot(horizontal, azimuthRight),
                Vector3.Dot(horizontal, azimuthReference));
        }

        return new OrbitState(
            nextTarget,
            NormalizeAngle(nextAzimuth),
            nextPolar,
            Math.Clamp(rawDistance, minimumDistance, maximumDistance));
    }

    private Vector3 CalculateOffsetDirection(float azimuth, float polar)
    {
        float sinPolar = MathF.Sin(polar);
        return Vector3.Normalize(
            azimuthReference * (sinPolar * MathF.Cos(azimuth)) +
            azimuthRight * (sinPolar * MathF.Sin(azimuth)) +
            worldUp * MathF.Cos(polar));
    }

    private void SetCameraWorldPose(
        Vector3 nextTarget,
        float azimuth,
        float polar,
        float cameraDistance)
    {
        Vector3 eye = nextTarget + CalculateOffsetDirection(azimuth, polar) * cameraDistance;
        ThrowIfNotFinite(eye, nameof(nextTarget));
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, nextTarget, worldUp);
        if (!Matrix4x4.Invert(view, out Matrix4x4 world))
        {
            throw new InvalidOperationException("The orbit camera world transform is not invertible.");
        }

        TransformMatrixHelper.SetWorldMatrix(
            camera,
            world,
            "The orbit camera parent requires a local shear that Transform3D cannot represent.");
    }

    private Vector3 GetCameraWorldPosition()
    {
        Vector3 position = Vector3.Transform(Vector3.Zero, camera.WorldMatrix);
        ThrowIfNotFinite(position, nameof(camera));
        return position;
    }

    private void AssignState(OrbitState state)
    {
        target = state.Target;
        azimuthAngle = state.Azimuth;
        polarAngle = state.Polar;
        distance = state.Distance;
    }

    private void NotifyChangedAndRequestFrame()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        frameRequester?.RequestFrame();
    }

    private void ClearPendingMotion()
    {
        pendingRotation = Vector2.Zero;
        pendingPan = Vector2.Zero;
        pendingDolly = 0f;
    }

    private void NormalizeResiduals()
    {
        if (pendingRotation.LengthSquared() <= ResidualEpsilon * ResidualEpsilon)
        {
            pendingRotation = Vector2.Zero;
        }
        if (pendingPan.LengthSquared() <= ResidualEpsilon * ResidualEpsilon)
        {
            pendingPan = Vector2.Zero;
        }
        if (MathF.Abs(pendingDolly) <= ResidualEpsilon)
        {
            pendingDolly = 0f;
        }
    }

    private static Vector2 CalculateStep(Vector2 residual, float amount)
    {
        Vector2 step = residual * amount;
        return (residual - step).LengthSquared() <= ResidualEpsilon * ResidualEpsilon
            ? residual
            : step;
    }

    private static float CalculateStep(float residual, float amount)
    {
        float step = residual * amount;
        return MathF.Abs(residual - step) <= ResidualEpsilon ? residual : step;
    }

    private static Vector3 CreateAzimuthReference(Vector3 up)
    {
        Vector3 seed = MathF.Abs(Vector3.Dot(up, Vector3.UnitZ)) < 0.99f
            ? Vector3.UnitZ
            : Vector3.UnitX;
        return Vector3.Normalize(seed - up * Vector3.Dot(seed, up));
    }

    private static float NormalizeAngle(double angle) =>
        (float)Math.IEEERemainder(angle, Math.PI * 2d);

    private static void ThrowIfPositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The value must be positive and finite.");
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

    private static void ThrowIfPolarAngle(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f || value >= MathF.PI)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "A polar angle must be finite and strictly between zero and pi.");
        }
    }

    private static void ThrowIfNotFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
        }
    }

    private static void ThrowIfNotFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private readonly record struct OrbitState(
        Vector3 Target,
        float Azimuth,
        float Polar,
        float Distance);
}

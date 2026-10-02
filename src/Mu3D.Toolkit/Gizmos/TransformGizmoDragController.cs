using System.Numerics;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Gizmos;

namespace Mu3D.Toolkit.Gizmos;

/// <summary>Maps physical viewport drags to the shared transform-gizmo commands.</summary>
/// <remarks>The host supplies hit results, camera/viewport state and capture lifetime. This controller
/// owns no native input, selection or undo. Cancel before changing camera, target or viewport size.
/// Instances are not thread-safe. Update positions may leave the viewport during capture.</remarks>
public sealed class TransformGizmoDragController : IDisposable
{
    private const string LeaseOwnerName = "transform-gizmo drag";
    private const float ProjectionEpsilon = 0.000001f;
    private const float MinimumProjectedDragPixels = 0.25f;
    private const float RotationProbeRadians = 0.01f;
    private ViewportControlLease? controlLease;
    private TransformGizmo? gizmo;
    private Vector2 initialPositionPixels;
    private Vector2 dragDirection;
    private float commandPerPixel;
    private float scaleFactorPerPixel;
    private TransformGizmoMode mode;

    /// <summary>Reports cleanup failures during lease revocation or disposal.</summary>
    public event Action<Exception>? InteractionFailed;

    /// <summary>Signals that higher-priority input revoked the drag lease; release host capture.</summary>
    public event Action? InteractionRevoked;

    /// <summary>Gets whether a drag currently owns a gizmo interaction.</summary>
    public bool IsActive => gizmo is not null;

    /// <summary>Begins a projected drag from an in-bounds physical-pixel position and valid handle hit.</summary>
    /// <remarks>Returns false when already active or arbitration denies capture. The scale factor
    /// per gizmo length must be finite and greater than one. Camera, gizmo and arbiter are borrowed.</remarks>
    public bool Begin(
        TransformGizmo candidateGizmo,
        TransformGizmoHit hit,
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Vector2 viewportPositionPixels,
        ViewportControlArbiter? arbiter,
        float scaleFactorPerGizmoLength)
    {
        ArgumentNullException.ThrowIfNull(candidateGizmo);
        ArgumentNullException.ThrowIfNull(hit);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfZero(viewportWidthPixels);
        ArgumentOutOfRangeException.ThrowIfZero(viewportHeightPixels);
        ThrowIfViewportPositionInvalid(
            viewportPositionPixels,
            viewportWidthPixels,
            viewportHeightPixels);
        if (!float.IsFinite(scaleFactorPerGizmoLength) || scaleFactorPerGizmoLength <= 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleFactorPerGizmoLength),
                "The scale factor per gizmo length must be finite and greater than one.");
        }
        if (IsActive)
        {
            return false;
        }
        if (candidateGizmo.IsInteracting)
        {
            throw new InvalidOperationException(
                "The transform gizmo already has an active interaction.");
        }
        if (!candidateGizmo.IsModeEnabled(hit.Mode))
        {
            throw new ArgumentException(
                "The hit result refers to a transform-gizmo operation that is no longer enabled.",
                nameof(hit));
        }

        DragMapping mapping = CreateMapping(
            candidateGizmo,
            hit,
            camera,
            viewportWidthPixels,
            viewportHeightPixels,
            scaleFactorPerGizmoLength);
        ViewportControlLease? lease = arbiter?.TryAcquire(
            LeaseOwnerName,
            ViewportControlPriorities.Gizmo);
        if (arbiter is not null && lease is null)
        {
            return false;
        }

        controlLease = lease;
        if (controlLease is not null)
        {
            controlLease.Revoked += OnControlLeaseRevoked;
        }
        gizmo = candidateGizmo;
        initialPositionPixels = viewportPositionPixels;
        dragDirection = mapping.Direction;
        commandPerPixel = mapping.CommandPerPixel;
        scaleFactorPerPixel = mapping.ScaleFactorPerPixel;
        mode = hit.Mode;
        try
        {
            candidateGizmo.BeginInteraction(hit.Mode, hit.Axis);
        }
        catch
        {
            ResetInteractionState();
            ReleaseLease();
            throw;
        }
        if (!ReferenceEquals(gizmo, candidateGizmo) || !candidateGizmo.IsInteracting)
        {
            ResetInteractionState();
            ReleaseLease();
            return false;
        }
        return true;
    }

    /// <summary>Updates from cumulative physical-pixel displacement; returns whether the pose changed.</summary>
    public bool Update(Vector2 viewportPositionPixels)
    {
        ThrowIfFinite(viewportPositionPixels, nameof(viewportPositionPixels));
        TransformGizmo? currentGizmo = gizmo;
        if (currentGizmo is null)
        {
            return false;
        }

        float signedPixels = Vector2.Dot(
            viewportPositionPixels - initialPositionPixels,
            dragDirection);
        return mode switch
        {
            TransformGizmoMode.Translate =>
                currentGizmo.UpdateTranslation(signedPixels * commandPerPixel),
            TransformGizmoMode.Rotate =>
                currentGizmo.UpdateRotation(signedPixels * commandPerPixel),
            TransformGizmoMode.Scale =>
                currentGizmo.UpdateScale(CalculateScaleFactor(currentGizmo, signedPixels)),
            _ => throw new InvalidOperationException("The active transform-gizmo mode is invalid."),
        };
    }

    /// <summary>Commits the active interaction and releases its lease; idle calls do nothing.</summary>
    public void Complete()
    {
        TransformGizmo? currentGizmo = gizmo;
        if (currentGizmo is null)
        {
            return;
        }
        ResetInteractionState();
        try
        {
            currentGizmo.CompleteInteraction();
        }
        finally
        {
            ReleaseLease();
        }
    }

    /// <summary>Restores the initial pose and releases the lease; idle calls do nothing.</summary>
    public void Cancel()
    {
        TransformGizmo? currentGizmo = gizmo;
        if (currentGizmo is null)
        {
            ReleaseLease();
            return;
        }
        ResetInteractionState();
        try
        {
            if (currentGizmo.IsInteracting)
            {
                currentGizmo.CancelInteraction();
            }
        }
        finally
        {
            ReleaseLease();
        }
    }

    /// <summary>Cancels the current drag, releases its lease and detaches callbacks.</summary>
    public void Dispose()
    {
        try
        {
            Cancel();
        }
        catch (Exception exception)
        {
            InteractionFailed?.Invoke(exception);
        }
        InteractionFailed = null;
        InteractionRevoked = null;
    }

    /// <summary>Maps finite host-local coordinates to physical viewport pixels without clamping.</summary>
    /// <remarks>Both viewport extents must be positive. Out-of-bounds positions are valid during capture.</remarks>
    public static Vector2 MapViewportPosition(
        double platformX,
        double platformY,
        double platformWidth,
        double platformHeight,
        uint viewportWidthPixels,
        uint viewportHeightPixels)
    {
        if (!double.IsFinite(platformX) || !double.IsFinite(platformY))
        {
            throw new ArgumentOutOfRangeException(
                nameof(platformX),
                "Platform pointer coordinates must be finite.");
        }
        if (!double.IsFinite(platformWidth) || !double.IsFinite(platformHeight) ||
            platformWidth <= 0d || platformHeight <= 0d ||
            viewportWidthPixels == 0 || viewportHeightPixels == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(platformWidth),
                "Platform and physical viewport sizes must be positive and finite.");
        }

        double mappedX = platformX * viewportWidthPixels / platformWidth;
        double mappedY = platformY * viewportHeightPixels / platformHeight;
        if (!double.IsFinite(mappedX) || !double.IsFinite(mappedY) ||
            mappedX < float.MinValue || mappedX > float.MaxValue ||
            mappedY < float.MinValue || mappedY > float.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(platformX),
                "The physical viewport position must be finite and representable as FP32.");
        }
        return new Vector2((float)mappedX, (float)mappedY);
    }

    private static DragMapping CreateMapping(
        TransformGizmo candidateGizmo,
        TransformGizmoHit hit,
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        float scaleFactorPerGizmoLength)
    {
        SceneNode target = candidateGizmo.Target ??
            throw new InvalidOperationException("A transform gizmo requires an explicit target.");
        float worldSize = candidateGizmo.CalculateWorldSize(camera, viewportHeightPixels);
        if (worldSize <= 0f)
        {
            throw new InvalidOperationException(
                "The transform gizmo must be in front of the camera for pointer interaction.");
        }

        if (hit.Axis == TransformGizmoAxis.Uniform)
        {
            if (hit.Mode != TransformGizmoMode.Scale)
            {
                throw new ArgumentException(
                    "The uniform handle is available only in scale mode.",
                    nameof(hit));
            }
            return new DragMapping(
                Vector2.Normalize(new Vector2(1f, -1f)),
                0f,
                CalculateScaleFactorPerPixel(
                    scaleFactorPerGizmoLength,
                    candidateGizmo.ScreenSizePixels));
        }

        Vector3 origin = Vector3.Transform(Vector3.Zero, target.WorldMatrix);
        TransformGizmoSpace interactionSpace = hit.Mode == TransformGizmoMode.Scale
            ? TransformGizmoSpace.Local
            : candidateGizmo.Space;
        Vector3 axis = GetAxisDirection(interactionSpace, target.WorldMatrix, hit.Axis);
        Matrix4x4 viewProjection = camera.ViewProjectionMatrix;
        if (hit.Mode == TransformGizmoMode.Rotate)
        {
            Vector3 radial = hit.WorldPosition - origin;
            float radius = radial.Length();
            if (!IsFinite(radial) || !float.IsFinite(radius) || radius <= float.Epsilon)
            {
                throw new InvalidOperationException("The selected rotation-ring point is degenerate.");
            }
            radial /= radius;
            Vector3 nextRadial = Vector3.TransformNormal(
                radial,
                Matrix4x4.CreateFromAxisAngle(axis, RotationProbeRadians));
            Vector3 ringPoint = origin + radial * radius;
            Vector3 nextRingPoint = origin + nextRadial * radius;
            Vector2 projectedRing = Project(
                ringPoint,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels);
            Vector2 projectedNext = Project(
                nextRingPoint,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels);
            Vector2 rotationDelta = projectedNext - projectedRing;
            float rotationPixels = rotationDelta.Length();
            ThrowIfProjectedDragDegenerate(rotationPixels, hit.Axis);
            return new DragMapping(
                rotationDelta / rotationPixels,
                RotationProbeRadians / rotationPixels,
                0f);
        }

        Vector2 projectedOrigin = Project(
            origin,
            viewProjection,
            viewportWidthPixels,
            viewportHeightPixels);
        Vector2 projectedEnd = Project(
            origin + axis * worldSize,
            viewProjection,
            viewportWidthPixels,
            viewportHeightPixels);
        Vector2 axisDelta = projectedEnd - projectedOrigin;
        float axisPixels = axisDelta.Length();
        ThrowIfProjectedDragDegenerate(axisPixels, hit.Axis);
        return hit.Mode switch
        {
            TransformGizmoMode.Translate => new DragMapping(
                axisDelta / axisPixels,
                worldSize / axisPixels,
                0f),
            TransformGizmoMode.Scale => new DragMapping(
                axisDelta / axisPixels,
                0f,
                CalculateScaleFactorPerPixel(scaleFactorPerGizmoLength, axisPixels)),
            _ => throw new ArgumentOutOfRangeException(nameof(hit)),
        };
    }

    private static float CalculateScaleFactorPerPixel(float scaleFactor, float pixelLength)
    {
        float result = MathF.Log(scaleFactor) / pixelLength;
        if (!float.IsFinite(result) || result <= 0f)
        {
            throw new InvalidOperationException(
                "The transform-gizmo scale drag mapping is not finite and positive.");
        }
        return result;
    }

    private float CalculateScaleFactor(TransformGizmo currentGizmo, float signedPixels)
    {
        double exponent = signedPixels * scaleFactorPerPixel;
        double scaleFactor = Math.Exp(exponent);
        if (double.IsPositiveInfinity(scaleFactor) || scaleFactor > currentGizmo.MaximumScaleFactor)
        {
            return currentGizmo.MaximumScaleFactor;
        }
        if (scaleFactor == 0d || scaleFactor < currentGizmo.MinimumScaleFactor)
        {
            return currentGizmo.MinimumScaleFactor;
        }
        return (float)scaleFactor;
    }

    private void OnControlLeaseRevoked(object? sender, EventArgs e)
    {
        ViewportControlLease? revoked = controlLease;
        if (revoked is null || !ReferenceEquals(sender, revoked))
        {
            return;
        }
        revoked.Revoked -= OnControlLeaseRevoked;
        controlLease = null;
        try
        {
            Cancel();
        }
        catch (Exception exception)
        {
            InteractionFailed?.Invoke(exception);
        }
        InteractionRevoked?.Invoke();
    }

    private void ResetInteractionState()
    {
        gizmo = null;
        initialPositionPixels = default;
        dragDirection = default;
        commandPerPixel = 0f;
        scaleFactorPerPixel = 0f;
        mode = default;
    }

    private void ReleaseLease()
    {
        ViewportControlLease? lease = controlLease;
        controlLease = null;
        if (lease is null)
        {
            return;
        }
        lease.Revoked -= OnControlLeaseRevoked;
        lease.Dispose();
    }

    private static Vector3 GetAxisDirection(
        TransformGizmoSpace space,
        Matrix4x4 targetWorld,
        TransformGizmoAxis axis)
    {
        Vector3 direction = axis switch
        {
            TransformGizmoAxis.X => Vector3.UnitX,
            TransformGizmoAxis.Y => Vector3.UnitY,
            TransformGizmoAxis.Z => Vector3.UnitZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };
        if (space == TransformGizmoSpace.World)
        {
            return direction;
        }
        direction = Vector3.TransformNormal(direction, targetWorld);
        if (!IsFinite(direction) || direction.LengthSquared() <= float.Epsilon)
        {
            throw new InvalidOperationException("A local transform-gizmo axis is degenerate.");
        }
        return Vector3.Normalize(direction);
    }

    private static Vector2 Project(
        Vector3 worldPosition,
        Matrix4x4 viewProjection,
        uint viewportWidthPixels,
        uint viewportHeightPixels)
    {
        Vector4 clip = Vector4.Transform(new Vector4(worldPosition, 1f), viewProjection);
        if (!float.IsFinite(clip.X) || !float.IsFinite(clip.Y) ||
            !float.IsFinite(clip.Z) || !float.IsFinite(clip.W) ||
            clip.W <= ProjectionEpsilon)
        {
            throw new InvalidOperationException(
                "The selected transform-gizmo drag reference cannot be projected.");
        }
        float inverseW = 1f / clip.W;
        float depth = clip.Z * inverseW;
        if (!float.IsFinite(depth) || depth < 0f || depth > 1f)
        {
            throw new InvalidOperationException(
                "The selected transform-gizmo drag reference is outside the camera depth range.");
        }
        Vector2 screen = new(
            (clip.X * inverseW + 1f) * 0.5f * viewportWidthPixels,
            (1f - clip.Y * inverseW) * 0.5f * viewportHeightPixels);
        ThrowIfFinite(screen, nameof(worldPosition));
        return screen;
    }

    private static void ThrowIfProjectedDragDegenerate(
        float projectedPixels,
        TransformGizmoAxis axis)
    {
        if (!float.IsFinite(projectedPixels) || projectedPixels < MinimumProjectedDragPixels)
        {
            throw new InvalidOperationException(
                $"The projected {axis} handle is too small for a stable pointer drag.");
        }
    }

    private static void ThrowIfViewportPositionInvalid(
        Vector2 position,
        uint viewportWidthPixels,
        uint viewportHeightPixels)
    {
        ThrowIfFinite(position, nameof(position));
        if (position.X < 0f || position.Y < 0f ||
            position.X > viewportWidthPixels || position.Y > viewportHeightPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "The viewport position must be inside the viewport bounds.");
        }
    }

    private static void ThrowIfFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Vector components must be finite.");
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private readonly record struct DragMapping(
        Vector2 Direction,
        float CommandPerPixel,
        float ScaleFactorPerPixel);
}

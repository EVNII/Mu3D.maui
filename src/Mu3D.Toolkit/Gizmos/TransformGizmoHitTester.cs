using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Gizmos;

/// <summary>Describes the closest projected transform-gizmo handle accepted by a hit test.</summary>
public sealed class TransformGizmoHit
{
    internal TransformGizmoHit(
        TransformGizmoMode mode,
        TransformGizmoAxis axis,
        Vector3 worldPosition,
        float screenDistancePixels,
        float cameraDistance)
    {
        Mode = mode;
        Axis = axis;
        WorldPosition = worldPosition;
        ScreenDistancePixels = screenDistancePixels;
        CameraDistance = cameraDistance;
    }

    /// <summary>Gets the transform operation active when the handle was tested.</summary>
    public TransformGizmoMode Mode { get; }

    /// <summary>Gets the selected axis or uniform scale handle.</summary>
    public TransformGizmoAxis Axis { get; }

    /// <summary>Gets the closest sampled world position on the selected handle.</summary>
    public Vector3 WorldPosition { get; }

    /// <summary>Gets the non-negative distance from the pointer to the projected handle.</summary>
    public float ScreenDistancePixels { get; }

    /// <summary>Gets the positive world distance from the camera to <see cref="WorldPosition"/>.</summary>
    public float CameraDistance { get; }
}

/// <summary>
/// Hit-tests one transform gizmo's own handles in viewport pixel coordinates.
/// </summary>
/// <remarks>
/// The tester projects every enabled operation's deterministic axis segments, rotation rings, and
/// uniform-scale center through the supplied perspective camera. Scale handles always follow the
/// target-local axes. Endpoint markers rank ahead of
/// overlapping axis lines so mixed translate/scale handles remain selectable. It does not acquire
/// raw input, capture pointers, rank scene objects, begin an interaction, or own the gizmo, target,
/// or camera. Rotation rings use a fixed tessellation shared by every call; this class allocates
/// only the accepted result.
/// </remarks>
public sealed class TransformGizmoHitTester
{
    internal const float AxisStartFraction = 0.18f;
    internal const float TranslationAxisEndFraction = 1f;
    internal const float ScaleAxisEndFraction = 0.72f;
    internal const float RotationRadiusFraction = 0.75f;
    internal const int RotationSegmentCount = 96;
    internal const float UniformRadiusMultiplier = 1.25f;
    private const float ProjectionEpsilon = 0.000001f;
    private const float RankingEpsilon = 0.0001f;
    private float hitTolerancePixels;

    /// <summary>Initializes a reusable hit tester with a positive pixel-space tolerance.</summary>
    /// <param name="hitTolerancePixels">Maximum pointer-to-handle distance in viewport pixels.</param>
    public TransformGizmoHitTester(float hitTolerancePixels = 8f)
    {
        HitTolerancePixels = hitTolerancePixels;
    }

    /// <summary>Gets or sets the positive maximum pointer-to-handle distance in viewport pixels.</summary>
    public float HitTolerancePixels
    {
        get => hitTolerancePixels;
        set
        {
            ThrowIfPositiveFinite(value, nameof(value));
            hitTolerancePixels = value;
        }
    }

    /// <summary>Returns the closest accepted gizmo handle, or null when no handle is hit.</summary>
    /// <param name="gizmo">The borrowed idle transform gizmo whose own handles are tested.</param>
    /// <param name="camera">The borrowed perspective camera used to project the handles.</param>
    /// <param name="viewportWidthPixels">The positive viewport width in physical pixels.</param>
    /// <param name="viewportHeightPixels">The positive viewport height in physical pixels.</param>
    /// <param name="viewportPositionPixels">
    /// The finite top-left-origin pointer position inside the inclusive viewport bounds.
    /// </param>
    public TransformGizmoHit? HitTest(
        TransformGizmo gizmo,
        PerspectiveCamera camera,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Vector2 viewportPositionPixels)
    {
        ArgumentNullException.ThrowIfNull(gizmo);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentOutOfRangeException.ThrowIfZero(viewportWidthPixels);
        ArgumentOutOfRangeException.ThrowIfZero(viewportHeightPixels);
        ThrowIfViewportPositionInvalid(
            viewportPositionPixels,
            viewportWidthPixels,
            viewportHeightPixels);
        if (gizmo.IsInteracting)
        {
            throw new InvalidOperationException(
                "Transform-gizmo handles can be hit-tested only while the gizmo is idle.");
        }

        SceneNode? target = gizmo.Target;
        if (target is null)
        {
            return null;
        }
        float worldSize = gizmo.CalculateWorldSize(camera, viewportHeightPixels);
        if (worldSize == 0f)
        {
            return null;
        }

        Matrix4x4 viewProjection = camera.ViewProjectionMatrix;
        Vector3 cameraPosition = Vector3.Transform(Vector3.Zero, camera.WorldMatrix);
        Vector3 origin = Vector3.Transform(Vector3.Zero, target.WorldMatrix);
        ThrowIfFinite(origin, nameof(gizmo));
        ThrowIfFinite(cameraPosition, nameof(camera));

        Candidate? best = null;
        if (gizmo.IsScaleEnabled &&
            TryProject(
                origin,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                out Vector2 projectedOrigin))
        {
            float centerDistance = Vector2.Distance(viewportPositionPixels, projectedOrigin);
            if (centerDistance <= hitTolerancePixels * UniformRadiusMultiplier)
            {
                float cameraDistance = Vector3.Distance(cameraPosition, origin);
                if (!float.IsFinite(cameraDistance) || cameraDistance <= 0f)
                {
                    throw new InvalidOperationException(
                        "The gizmo handle camera distance is invalid.");
                }
                best = new Candidate(
                    TransformGizmoMode.Scale,
                    TransformGizmoAxis.Uniform,
                    origin,
                    centerDistance,
                    cameraDistance,
                    IsMarkerHit: true);
            }
        }

        if (gizmo.IsRotateEnabled)
        {
            AddRotationRing(
                ref best,
                TransformGizmoMode.Rotate,
                TransformGizmoAxis.X,
                GetAxisDirection(gizmo.Space, target.WorldMatrix, TransformGizmoAxis.X),
                origin,
                worldSize * RotationRadiusFraction,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels);
            AddRotationRing(
                ref best,
                TransformGizmoMode.Rotate,
                TransformGizmoAxis.Y,
                GetAxisDirection(gizmo.Space, target.WorldMatrix, TransformGizmoAxis.Y),
                origin,
                worldSize * RotationRadiusFraction,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels);
            AddRotationRing(
                ref best,
                TransformGizmoMode.Rotate,
                TransformGizmoAxis.Z,
                GetAxisDirection(gizmo.Space, target.WorldMatrix, TransformGizmoAxis.Z),
                origin,
                worldSize * RotationRadiusFraction,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels);
        }
        if (gizmo.IsTranslateEnabled)
        {
            AddAxisSegment(
                ref best,
                TransformGizmoMode.Translate,
                TransformGizmoAxis.X,
                GetAxisDirection(gizmo.Space, target.WorldMatrix, TransformGizmoAxis.X),
                origin,
                worldSize,
                TranslationAxisEndFraction,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels);
            AddAxisSegment(
                ref best,
                TransformGizmoMode.Translate,
                TransformGizmoAxis.Y,
                GetAxisDirection(gizmo.Space, target.WorldMatrix, TransformGizmoAxis.Y),
                origin,
                worldSize,
                TranslationAxisEndFraction,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels);
            AddAxisSegment(
                ref best,
                TransformGizmoMode.Translate,
                TransformGizmoAxis.Z,
                GetAxisDirection(gizmo.Space, target.WorldMatrix, TransformGizmoAxis.Z),
                origin,
                worldSize,
                TranslationAxisEndFraction,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels);
        }
        if (gizmo.IsScaleEnabled)
        {
            AddAxisSegment(
                ref best,
                TransformGizmoMode.Scale,
                TransformGizmoAxis.X,
                GetAxisDirection(TransformGizmoSpace.Local, target.WorldMatrix, TransformGizmoAxis.X),
                origin,
                worldSize,
                ScaleAxisEndFraction,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels);
            AddAxisSegment(
                ref best,
                TransformGizmoMode.Scale,
                TransformGizmoAxis.Y,
                GetAxisDirection(TransformGizmoSpace.Local, target.WorldMatrix, TransformGizmoAxis.Y),
                origin,
                worldSize,
                ScaleAxisEndFraction,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels);
            AddAxisSegment(
                ref best,
                TransformGizmoMode.Scale,
                TransformGizmoAxis.Z,
                GetAxisDirection(TransformGizmoSpace.Local, target.WorldMatrix, TransformGizmoAxis.Z),
                origin,
                worldSize,
                ScaleAxisEndFraction,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels);
        }

        return best is Candidate accepted
            ? new TransformGizmoHit(
                accepted.Mode,
                accepted.Axis,
                accepted.WorldPosition,
                accepted.ScreenDistancePixels,
                accepted.CameraDistance)
            : null;
    }

    private void AddAxisSegment(
        ref Candidate? best,
        TransformGizmoMode mode,
        TransformGizmoAxis axis,
        Vector3 direction,
        Vector3 origin,
        float worldSize,
        float endFraction,
        Vector3 cameraPosition,
        Matrix4x4 viewProjection,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Vector2 viewportPositionPixels)
    {
        Vector3 start = origin + direction * (worldSize * AxisStartFraction);
        Vector3 end = origin + direction * (worldSize * endFraction);
        AddProjectedSegment(
            ref best,
            mode,
            axis,
            start,
            end,
            cameraPosition,
            viewProjection,
            viewportWidthPixels,
            viewportHeightPixels,
            viewportPositionPixels,
            hasEndpointMarker: true);
    }

    private void AddRotationRing(
        ref Candidate? best,
        TransformGizmoMode mode,
        TransformGizmoAxis axis,
        Vector3 normal,
        Vector3 origin,
        float radius,
        Vector3 cameraPosition,
        Matrix4x4 viewProjection,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Vector2 viewportPositionPixels)
    {
        Vector3 seed = MathF.Abs(Vector3.Dot(normal, Vector3.UnitZ)) < 0.9f
            ? Vector3.UnitZ
            : Vector3.UnitY;
        Vector3 firstBasis = Vector3.Normalize(Vector3.Cross(normal, seed));
        Vector3 secondBasis = Vector3.Normalize(Vector3.Cross(normal, firstBasis));
        Vector3 previous = origin + firstBasis * radius;
        for (int index = 1; index <= RotationSegmentCount; index++)
        {
            float angle = index * (MathF.PI * 2f / RotationSegmentCount);
            Vector3 current = origin +
                (firstBasis * MathF.Cos(angle) + secondBasis * MathF.Sin(angle)) * radius;
            AddProjectedSegment(
                ref best,
                mode,
                axis,
                previous,
                current,
                cameraPosition,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                viewportPositionPixels,
                hasEndpointMarker: false);
            previous = current;
        }
    }

    private void AddProjectedSegment(
        ref Candidate? best,
        TransformGizmoMode mode,
        TransformGizmoAxis axis,
        Vector3 worldStart,
        Vector3 worldEnd,
        Vector3 cameraPosition,
        Matrix4x4 viewProjection,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        Vector2 viewportPositionPixels,
        bool hasEndpointMarker)
    {
        if (!TryProject(
                worldStart,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                out Vector2 screenStart) ||
            !TryProject(
                worldEnd,
                viewProjection,
                viewportWidthPixels,
                viewportHeightPixels,
                out Vector2 screenEnd))
        {
            return;
        }

        Vector2 screenSegment = screenEnd - screenStart;
        float screenLengthSquared = screenSegment.LengthSquared();
        if (!float.IsFinite(screenLengthSquared) || screenLengthSquared <= float.Epsilon)
        {
            return;
        }
        float amount = Math.Clamp(
            Vector2.Dot(viewportPositionPixels - screenStart, screenSegment) /
                screenLengthSquared,
            0f,
            1f);
        Vector2 closestScreen = Vector2.Lerp(screenStart, screenEnd, amount);
        float screenDistance = Vector2.Distance(viewportPositionPixels, closestScreen);
        if (screenDistance > hitTolerancePixels)
        {
            return;
        }

        Vector3 closestWorld = Vector3.Lerp(worldStart, worldEnd, amount);
        float cameraDistance = Vector3.Distance(cameraPosition, closestWorld);
        if (!float.IsFinite(cameraDistance) || cameraDistance <= 0f)
        {
            return;
        }

        bool isMarkerHit = hasEndpointMarker &&
            Vector2.Distance(viewportPositionPixels, screenEnd) <=
                hitTolerancePixels * UniformRadiusMultiplier;
        Candidate candidate = new(
            mode,
            axis,
            closestWorld,
            screenDistance,
            cameraDistance,
            isMarkerHit);
        if (best is null || IsBetter(candidate, best.Value))
        {
            best = candidate;
        }
    }

    private static bool IsBetter(Candidate candidate, Candidate current)
    {
        if (candidate.IsMarkerHit != current.IsMarkerHit)
        {
            return candidate.IsMarkerHit;
        }

        float screenDifference = candidate.ScreenDistancePixels - current.ScreenDistancePixels;
        if (screenDifference < -RankingEpsilon)
        {
            return true;
        }
        if (MathF.Abs(screenDifference) > RankingEpsilon)
        {
            return false;
        }

        float cameraDifference = candidate.CameraDistance - current.CameraDistance;
        if (cameraDifference < -RankingEpsilon)
        {
            return true;
        }
        if (MathF.Abs(cameraDifference) > RankingEpsilon)
        {
            return false;
        }
        if (candidate.Mode != current.Mode)
        {
            return candidate.Mode < current.Mode;
        }
        return candidate.Axis < current.Axis;
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
        if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) ||
            !float.IsFinite(direction.Z) || direction.LengthSquared() <= float.Epsilon)
        {
            throw new InvalidOperationException("A local transform-gizmo axis is degenerate.");
        }
        return Vector3.Normalize(direction);
    }

    private static bool TryProject(
        Vector3 worldPosition,
        Matrix4x4 viewProjection,
        uint viewportWidthPixels,
        uint viewportHeightPixels,
        out Vector2 screenPosition)
    {
        Vector4 clip = Vector4.Transform(new Vector4(worldPosition, 1f), viewProjection);
        if (!float.IsFinite(clip.X) || !float.IsFinite(clip.Y) ||
            !float.IsFinite(clip.Z) || !float.IsFinite(clip.W) ||
            clip.W <= ProjectionEpsilon)
        {
            screenPosition = default;
            return false;
        }
        float inverseW = 1f / clip.W;
        float depth = clip.Z * inverseW;
        if (!float.IsFinite(depth) || depth < 0f || depth > 1f)
        {
            screenPosition = default;
            return false;
        }

        float normalizedX = clip.X * inverseW;
        float normalizedY = clip.Y * inverseW;
        screenPosition = new Vector2(
            (normalizedX + 1f) * 0.5f * viewportWidthPixels,
            (1f - normalizedY) * 0.5f * viewportHeightPixels);
        return float.IsFinite(screenPosition.X) && float.IsFinite(screenPosition.Y);
    }

    private static void ThrowIfViewportPositionInvalid(
        Vector2 position,
        uint viewportWidthPixels,
        uint viewportHeightPixels)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) ||
            position.X < 0f || position.Y < 0f ||
            position.X > viewportWidthPixels || position.Y > viewportHeightPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "The viewport position must be finite and inside the viewport bounds.");
        }
    }

    private static void ThrowIfFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Vector components must be finite.");
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

    private readonly record struct Candidate(
        TransformGizmoMode Mode,
        TransformGizmoAxis Axis,
        Vector3 WorldPosition,
        float ScreenDistancePixels,
        float CameraDistance,
        bool IsMarkerHit);
}

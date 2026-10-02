using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Gizmos;

/// <summary>Identifies the transform operation performed by a gizmo interaction.</summary>
public enum TransformGizmoMode
{
    /// <summary>Moves the target along one axis.</summary>
    Translate,

    /// <summary>Rotates the target around one axis.</summary>
    Rotate,

    /// <summary>Scales the target along one axis or uniformly.</summary>
    Scale,
}

/// <summary>Identifies whether one gizmo operation follows world or target-local orientation.</summary>
public enum TransformGizmoSpace
{
    /// <summary>Uses fixed world axes. Scale operations instead use <see cref="Local"/>.</summary>
    World,

    /// <summary>Uses axes from the target pose captured when interaction begins.</summary>
    Local,
}

/// <summary>Identifies one axis or uniform scale handle selected by the host adapter.</summary>
public enum TransformGizmoAxis
{
    /// <summary>The X axis.</summary>
    X,

    /// <summary>The Y axis.</summary>
    Y,

    /// <summary>The Z axis.</summary>
    Z,

    /// <summary>All three scale axes together.</summary>
    Uniform,
}

/// <summary>Stores one immutable finite local transform snapshot for gizmo events and undo data.</summary>
public readonly record struct TransformGizmoPose
{
    /// <summary>Initializes a finite local transform snapshot.</summary>
    public TransformGizmoPose(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        ThrowIfNotFinite(position, nameof(position));
        ThrowIfNotFinite(rotation, nameof(rotation));
        ThrowIfNotFinite(scale, nameof(scale));
        if (rotation.LengthSquared() <= float.Epsilon)
        {
            throw new ArgumentOutOfRangeException(nameof(rotation), "Rotation must be non-zero.");
        }
        Position = position;
        Rotation = Quaternion.Normalize(rotation);
        Scale = scale;
    }

    /// <summary>Gets the local position.</summary>
    public Vector3 Position { get; }

    /// <summary>Gets the normalized local rotation.</summary>
    public Quaternion Rotation { get; }

    /// <summary>Gets the local scale.</summary>
    public Vector3 Scale { get; }

    /// <summary>Captures the current local pose of an application-owned scene node.</summary>
    public static TransformGizmoPose Capture(SceneNode target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return new TransformGizmoPose(
            target.Transform.Position,
            target.Transform.Rotation,
            target.Transform.Scale);
    }

    /// <summary>Applies this local pose to an application-owned scene node.</summary>
    public void ApplyTo(SceneNode target)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.Transform.Position = Position;
        target.Transform.Rotation = Rotation;
        target.Transform.Scale = Scale;
    }

    internal Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) *
        Matrix4x4.CreateFromQuaternion(Rotation) *
        Matrix4x4.CreateTranslation(Position);

    private static void ThrowIfNotFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Vector components must be finite.");
        }
    }

    private static void ThrowIfNotFinite(Quaternion value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) || !float.IsFinite(value.W))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Quaternion components must be finite.");
        }
    }
}

/// <summary>Reports one immutable transform-gizmo interaction snapshot.</summary>
public sealed class TransformGizmoInteractionEventArgs : EventArgs
{
    internal TransformGizmoInteractionEventArgs(
        SceneNode target,
        TransformGizmoMode mode,
        TransformGizmoSpace space,
        TransformGizmoAxis axis,
        TransformGizmoPose initialPose,
        TransformGizmoPose currentPose)
    {
        Target = target;
        Mode = mode;
        Space = space;
        Axis = axis;
        InitialPose = initialPose;
        CurrentPose = currentPose;
    }

    /// <summary>Gets the application-owned interaction target.</summary>
    public SceneNode Target { get; }

    /// <summary>Gets the operation captured when the interaction began.</summary>
    public TransformGizmoMode Mode { get; }

    /// <summary>Gets the effective coordinate space captured when the interaction began.</summary>
    public TransformGizmoSpace Space { get; }

    /// <summary>Gets the selected axis or uniform scale handle.</summary>
    public TransformGizmoAxis Axis { get; }

    /// <summary>Gets the initial local pose suitable for application undo data.</summary>
    public TransformGizmoPose InitialPose { get; }

    /// <summary>Gets the current local pose.</summary>
    public TransformGizmoPose CurrentPose { get; }
}

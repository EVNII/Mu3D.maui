using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Stores one FP32 translation, rotation and scale transform.</summary>
public sealed class Transform3D
{
    private Vector3 position;
    private Quaternion rotation = Quaternion.Identity;
    private Vector3 scale = Vector3.One;

    /// <summary>Gets or sets the local translation.</summary>
    public Vector3 Position
    {
        get => position;
        set
        {
            ThrowIfNotFinite(value, nameof(value));
            position = value;
        }
    }

    /// <summary>Gets or sets the normalized local orientation.</summary>
    public Quaternion Rotation
    {
        get => rotation;
        set
        {
            ThrowIfNotFinite(value, nameof(value));
            float lengthSquared = value.LengthSquared();
            if (lengthSquared <= float.Epsilon)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "A rotation quaternion must be non-zero.");
            }
            rotation = Quaternion.Normalize(value);
        }
    }

    /// <summary>Gets or sets the local scale. Zero and negative finite components are preserved.</summary>
    public Vector3 Scale
    {
        get => scale;
        set
        {
            ThrowIfNotFinite(value, nameof(value));
            scale = value;
        }
    }

    /// <summary>
    /// Gets the FP32 local matrix using System.Numerics row-vector order: scale, rotation, translation.
    /// </summary>
    public Matrix4x4 LocalMatrix =>
        Matrix4x4.CreateScale(scale) *
        Matrix4x4.CreateFromQuaternion(rotation) *
        Matrix4x4.CreateTranslation(position);

    private static void ThrowIfNotFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Transform components must be finite.");
        }
    }

    private static void ThrowIfNotFinite(Quaternion value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) ||
            !float.IsFinite(value.Z) || !float.IsFinite(value.W))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Quaternion components must be finite.");
        }
    }
}

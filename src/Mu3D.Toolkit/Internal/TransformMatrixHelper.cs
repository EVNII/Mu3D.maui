using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Internal;

internal static class TransformMatrixHelper
{
    internal static Vector3 GetWorldPosition(SceneNode node)
    {
        Vector3 result = Vector3.Transform(Vector3.Zero, node.WorldMatrix);
        ThrowIfNotFinite(result, nameof(node));
        return result;
    }

    internal static void SetWorldMatrix(SceneNode node, Matrix4x4 worldMatrix, string failureMessage)
    {
        Matrix4x4 local = worldMatrix;
        if (node.Parent is SceneNode parent)
        {
            if (!Matrix4x4.Invert(parent.WorldMatrix, out Matrix4x4 inverseParent))
            {
                throw new InvalidOperationException("The target parent world transform is not invertible.");
            }
            local = worldMatrix * inverseParent;
        }
        DecomposeRepresentable(local, failureMessage, out Vector3 scale, out Quaternion rotation, out Vector3 position);
        node.Transform.Position = position;
        node.Transform.Rotation = rotation;
        node.Transform.Scale = scale;
    }

    internal static void DecomposeRepresentable(
        Matrix4x4 matrix,
        string failureMessage,
        out Vector3 scale,
        out Quaternion rotation,
        out Vector3 position)
    {
        if (!Matrix4x4.Decompose(matrix, out scale, out rotation, out position))
        {
            throw new InvalidOperationException(failureMessage);
        }
        ThrowIfNotFinite(scale, nameof(scale));
        ThrowIfNotFinite(rotation, nameof(rotation));
        ThrowIfNotFinite(position, nameof(position));
        if (rotation.LengthSquared() <= float.Epsilon)
        {
            throw new InvalidOperationException(failureMessage);
        }

        rotation = Quaternion.Normalize(rotation);
        Matrix4x4 represented =
            Matrix4x4.CreateScale(scale) *
            Matrix4x4.CreateFromQuaternion(rotation) *
            Matrix4x4.CreateTranslation(position);
        if (!ApproximatelyEqual(matrix, represented))
        {
            throw new InvalidOperationException(failureMessage);
        }
    }

    internal static bool ApproximatelyEqual(Matrix4x4 left, Matrix4x4 right) =>
        ApproximatelyEqual(left.M11, right.M11) &&
        ApproximatelyEqual(left.M12, right.M12) &&
        ApproximatelyEqual(left.M13, right.M13) &&
        ApproximatelyEqual(left.M14, right.M14) &&
        ApproximatelyEqual(left.M21, right.M21) &&
        ApproximatelyEqual(left.M22, right.M22) &&
        ApproximatelyEqual(left.M23, right.M23) &&
        ApproximatelyEqual(left.M24, right.M24) &&
        ApproximatelyEqual(left.M31, right.M31) &&
        ApproximatelyEqual(left.M32, right.M32) &&
        ApproximatelyEqual(left.M33, right.M33) &&
        ApproximatelyEqual(left.M34, right.M34) &&
        ApproximatelyEqual(left.M41, right.M41) &&
        ApproximatelyEqual(left.M42, right.M42) &&
        ApproximatelyEqual(left.M43, right.M43) &&
        ApproximatelyEqual(left.M44, right.M44);

    internal static bool ApproximatelyEqual(float left, float right)
    {
        float scale = MathF.Max(1f, MathF.Max(MathF.Abs(left), MathF.Abs(right)));
        return MathF.Abs(left - right) <= scale * 0.0005f;
    }

    internal static void ThrowIfNotFinite(Vector3 value, string parameterName)
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

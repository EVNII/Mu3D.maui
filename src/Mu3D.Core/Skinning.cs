using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Stores four unsigned joint indices affecting one vertex.</summary>
public readonly record struct JointIndices4
{
    /// <summary>Initializes four joint indices.</summary>
    public JointIndices4(uint x, uint y = 0, uint z = 0, uint w = 0)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    /// <summary>Gets the first joint index.</summary>
    public uint X { get; }

    /// <summary>Gets the second joint index.</summary>
    public uint Y { get; }

    /// <summary>Gets the third joint index.</summary>
    public uint Z { get; }

    /// <summary>Gets the fourth joint index.</summary>
    public uint W { get; }

    /// <summary>Gets the largest stored index.</summary>
    public uint Maximum => Math.Max(Math.Max(X, Y), Math.Max(Z, W));
}

/// <summary>
/// Associates scene joints with FP32 inverse bind matrices in System.Numerics row-vector order.
/// </summary>
public sealed class Skin
{
    /// <summary>Initializes a skin and copies its joint and inverse-bind collections.</summary>
    public Skin(
        IEnumerable<SceneNode> joints,
        IEnumerable<Matrix4x4> inverseBindMatrices,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(joints);
        ArgumentNullException.ThrowIfNull(inverseBindMatrices);
        SceneNode[] jointArray = [.. joints];
        Matrix4x4[] inverseBindArray = [.. inverseBindMatrices];
        if (jointArray.Length == 0)
        {
            throw new ArgumentException("A skin requires at least one joint.", nameof(joints));
        }
        if (jointArray.Length != inverseBindArray.Length)
        {
            throw new ArgumentException("Every joint requires one inverse bind matrix.", nameof(inverseBindMatrices));
        }
        if (jointArray.Any(static joint => joint is null))
        {
            throw new ArgumentException("Skin joints cannot contain null.", nameof(joints));
        }
        if (jointArray.Distinct(ReferenceEqualityComparer.Instance).Count() != jointArray.Length)
        {
            throw new ArgumentException("A skin cannot contain the same joint more than once.", nameof(joints));
        }
        foreach (Matrix4x4 matrix in inverseBindArray)
        {
            ThrowIfNotFinite(matrix, nameof(inverseBindMatrices));
        }
        Joints = new ReadOnlyCollection<SceneNode>(jointArray);
        InverseBindMatrices = new ReadOnlyCollection<Matrix4x4>(inverseBindArray);
        Name = name;
    }

    /// <summary>Gets the optional application-facing skin name.</summary>
    public string? Name { get; }

    /// <summary>Gets the ordered joints referenced by vertex joint indices.</summary>
    public IReadOnlyList<SceneNode> Joints { get; }

    /// <summary>Gets the inverse bind matrix corresponding to each joint.</summary>
    public IReadOnlyList<Matrix4x4> InverseBindMatrices { get; }

    private static void ThrowIfNotFinite(Matrix4x4 value, string parameterName)
    {
        ReadOnlySpan<float> elements =
        [
            value.M11, value.M12, value.M13, value.M14,
            value.M21, value.M22, value.M23, value.M24,
            value.M31, value.M32, value.M33, value.M34,
            value.M41, value.M42, value.M43, value.M44,
        ];
        foreach (float element in elements)
        {
            if (!float.IsFinite(element))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Inverse bind matrices must be finite.");
            }
        }
    }
}

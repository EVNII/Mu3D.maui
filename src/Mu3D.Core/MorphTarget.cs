using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Stores immutable FP32 local-space vertex deltas for one morph target.</summary>
public sealed class MorphTarget
{
    /// <summary>
    /// Initializes a morph target and copies its position plus optional normal and tangent deltas.
    /// </summary>
    public MorphTarget(
        IEnumerable<Vector3> positionDeltas,
        IEnumerable<Vector3>? normalDeltas = null,
        string? name = null,
        IEnumerable<Vector3>? tangentDeltas = null)
    {
        ArgumentNullException.ThrowIfNull(positionDeltas);
        Vector3[] positions = [.. positionDeltas];
        Vector3[] normals = normalDeltas is null ? [] : [.. normalDeltas];
        Vector3[] tangents = tangentDeltas is null ? [] : [.. tangentDeltas];
        if (positions.Length == 0)
        {
            throw new ArgumentException("A morph target requires position deltas.", nameof(positionDeltas));
        }
        if (normals.Length != 0 && normals.Length != positions.Length)
        {
            throw new ArgumentException(
                "Morph normal deltas must be empty or match position deltas.",
                nameof(normalDeltas));
        }
        if (tangents.Length != 0 && tangents.Length != positions.Length)
        {
            throw new ArgumentException(
                "Morph tangent deltas must be empty or match position deltas.",
                nameof(tangentDeltas));
        }
        foreach (Vector3 value in positions.Concat(normals).Concat(tangents))
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(positionDeltas),
                    "Morph deltas must be finite.");
            }
        }
        PositionDeltas = new ReadOnlyCollection<Vector3>(positions);
        NormalDeltas = new ReadOnlyCollection<Vector3>(normals);
        TangentDeltas = new ReadOnlyCollection<Vector3>(tangents);
        Name = name;
    }

    /// <summary>Gets the optional target name.</summary>
    public string? Name { get; }

    /// <summary>Gets FP32 local-space position deltas.</summary>
    public IReadOnlyList<Vector3> PositionDeltas { get; }

    /// <summary>Gets optional FP32 local-space normal deltas.</summary>
    public IReadOnlyList<Vector3> NormalDeltas { get; }

    /// <summary>
    /// Gets optional FP32 tangent XYZ deltas. Tangent handedness remains on the base geometry.
    /// </summary>
    public IReadOnlyList<Vector3> TangentDeltas { get; }
}

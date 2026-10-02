using System.Numerics;
using System.Runtime.CompilerServices;
using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Helpers;

/// <summary>Represents a finite world-axis-aligned three-dimensional bound.</summary>
public readonly record struct Bounds3D
{
    /// <summary>Initializes a finite bound whose minimum does not exceed its maximum.</summary>
    public Bounds3D(Vector3 minimum, Vector3 maximum)
    {
        ThrowIfNotFinite(minimum, nameof(minimum));
        ThrowIfNotFinite(maximum, nameof(maximum));
        if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z)
        {
            throw new ArgumentException("A bound's minimum cannot exceed its maximum.", nameof(minimum));
        }
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>Gets the inclusive world-space minimum corner.</summary>
    public Vector3 Minimum { get; }

    /// <summary>Gets the inclusive world-space maximum corner.</summary>
    public Vector3 Maximum { get; }

    /// <summary>Gets the world-space center.</summary>
    public Vector3 Center => (Minimum + Maximum) * 0.5f;

    /// <summary>Gets the non-negative world-space size.</summary>
    public Vector3 Size => Maximum - Minimum;

    internal Bounds3D Transform(Matrix4x4 matrix)
    {
        Vector3 transformedMinimum = new(float.PositiveInfinity);
        Vector3 transformedMaximum = new(float.NegativeInfinity);
        for (int index = 0; index < 8; index++)
        {
            Vector3 corner = new(
                (index & 1) == 0 ? Minimum.X : Maximum.X,
                (index & 2) == 0 ? Minimum.Y : Maximum.Y,
                (index & 4) == 0 ? Minimum.Z : Maximum.Z);
            Vector3 transformed = Vector3.Transform(corner, matrix);
            ThrowIfNotFinite(transformed, nameof(matrix));
            transformedMinimum = Vector3.Min(transformedMinimum, transformed);
            transformedMaximum = Vector3.Max(transformedMaximum, transformed);
        }
        return new Bounds3D(transformedMinimum, transformedMaximum);
    }

    internal Bounds3D Expand(float padding)
    {
        Vector3 expansion = new(padding);
        return new Bounds3D(Minimum - expansion, Maximum + expansion);
    }

    private static void ThrowIfNotFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Bound components must be finite.");
        }
    }
}

/// <summary>Calculates a target node's UI-independent world-space bounds.</summary>
/// <remarks>
/// The helper borrows its target and owns no renderer, input source, native resource, or scene
/// membership. Immutable geometry bounds are cached weakly per helper; transforms are evaluated
/// each time <see cref="TryGetWorldBounds(out Bounds3D)"/> is called. Morph weights are included. Skinned meshes
/// currently use their unskinned geometry bound transformed by the mesh node.
/// The public geometry query starts at the target subtree without scene or camera context;
/// rendering additionally restricts that subtree to the pass's scene and camera layers.
/// </remarks>
public sealed class BoundsHelper
{
    private readonly ConditionalWeakTable<MeshGeometry, LocalBounds> localBounds = new();
    private float padding;

    /// <summary>Gets or sets the borrowed target node, or null to produce no bounds.</summary>
    public SceneNode? Target { get; set; }

    /// <summary>Gets or sets whether visible descendants contribute to the result.</summary>
    public bool IncludeDescendants { get; set; } = true;

    /// <summary>Gets or sets whether invisible target subtrees contribute to the result.</summary>
    /// <remarks>
    /// When false, an invisible node suppresses its complete subtree. The public geometry query
    /// examines visibility only within the target subtree. Rendering also examines target ancestors;
    /// true bypasses hiding but never the render pass's scene membership or camera visibility mask.
    /// </remarks>
    public bool IncludeInvisible { get; set; }

    /// <summary>Gets or sets whether transformed mesh vertices produce a tight world AABB.</summary>
    /// <remarks>
    /// The default true value computes exact extrema from current rigid/morph vertex positions.
    /// False transforms only each cached local AABB's eight corners, which is faster for very large
    /// meshes but may produce a visibly conservative result after rotation.
    /// </remarks>
    public bool Precise { get; set; } = true;

    /// <summary>Gets or sets the non-negative world-space expansion applied on every side.</summary>
    public float Padding
    {
        get => padding;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            padding = value;
        }
    }

    /// <summary>Calculates the current world-axis-aligned bound for eligible target meshes.</summary>
    /// <param name="bounds">Receives the finite bound when at least one mesh contributes.</param>
    /// <returns>True when a bound was produced; otherwise false.</returns>
    /// <remarks>
    /// This explicit geometry query has no scene or camera context. Detached targets are valid;
    /// ancestor visibility outside the target subtree and camera layer masks are not evaluated.
    /// <see cref="Mu3D.Toolkit.Rendering.BoundsHelperRenderPass"/> applies its context's scene and camera policy
    /// independently without changing the borrowed target.
    /// </remarks>
    public bool TryGetWorldBounds(out Bounds3D bounds) => TryGetWorldBounds(
        Target, inheritedVisible: true, visibilityMask: null, ignoreOwnVisibility: false, out bounds);

    internal bool TryGetWorldBounds(Scene scene, SceneVisibilityMask visibilityMask, out Bounds3D bounds)
    {
        SceneNode? target = Target;
        if (target is null ||
            !SceneTargetEligibility.TryGetInheritedVisibility(scene, target, out bool inheritedVisible))
        {
            bounds = default;
            return false;
        }
        return TryGetWorldBounds(target, inheritedVisible, visibilityMask,
            ignoreOwnVisibility: ReferenceEquals(target, scene.Root), out bounds);
    }

    private bool TryGetWorldBounds(
        SceneNode? target,
        bool inheritedVisible,
        SceneVisibilityMask? visibilityMask,
        bool ignoreOwnVisibility,
        out Bounds3D bounds)
    {
        if (target is null)
        {
            bounds = default;
            return false;
        }

        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        bool found = false;
        AddSubtree(target, inheritedVisible, visibilityMask, ignoreOwnVisibility,
            ref minimum, ref maximum, ref found);
        if (!found)
        {
            bounds = default;
            return false;
        }
        bounds = new Bounds3D(minimum, maximum).Expand(Padding);
        return true;
    }

    private void AddSubtree(
        SceneNode node,
        bool inheritedVisible,
        SceneVisibilityMask? visibilityMask,
        bool ignoreOwnVisibility,
        ref Vector3 minimum,
        ref Vector3 maximum,
        ref bool found)
    {
        bool visible = inheritedVisible && (ignoreOwnVisibility || node.IsVisible);
        if (!IncludeInvisible && !visible)
        {
            return;
        }
        // Layer filtering affects each mesh's contribution, never independently layered children.
        if (node is Mesh mesh &&
            (visibilityMask is null || mesh.VisibilityMask.Intersects(visibilityMask.Value)))
        {
            Bounds3D world = Precise
                ? CalculatePreciseWorldBounds(mesh)
                : GetLocalBounds(mesh).Transform(mesh.WorldMatrix);
            minimum = Vector3.Min(minimum, world.Minimum);
            maximum = Vector3.Max(maximum, world.Maximum);
            found = true;
        }
        if (!IncludeDescendants)
        {
            return;
        }
        IReadOnlyList<SceneNode> children = node.Children;
        for (int childIndex = 0; childIndex < children.Count; childIndex++)
        {
            AddSubtree(children[childIndex], visible, visibilityMask, ignoreOwnVisibility: false,
                ref minimum, ref maximum, ref found);
        }
    }

    private Bounds3D GetLocalBounds(Mesh mesh)
    {
        IReadOnlyList<MorphTarget> targets = mesh.Geometry.MorphTargets;
        IReadOnlyList<float> weights = mesh.MorphWeights;
        bool hasActiveMorph = false;
        for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            if (weights[targetIndex] != 0f)
            {
                hasActiveMorph = true;
                break;
            }
        }
        if (!hasActiveMorph)
        {
            return localBounds.GetValue(mesh.Geometry, static geometry =>
                new LocalBounds(CalculateLocalBounds(geometry.Positions))).Value;
        }

        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        for (int vertexIndex = 0; vertexIndex < mesh.Geometry.Positions.Count; vertexIndex++)
        {
            Vector3 position = mesh.Geometry.Positions[vertexIndex];
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                position += targets[targetIndex].PositionDeltas[vertexIndex] * weights[targetIndex];
            }
            minimum = Vector3.Min(minimum, position);
            maximum = Vector3.Max(maximum, position);
        }
        return new Bounds3D(minimum, maximum);
    }

    private static Bounds3D CalculatePreciseWorldBounds(Mesh mesh)
    {
        IReadOnlyList<MorphTarget> targets = mesh.Geometry.MorphTargets;
        IReadOnlyList<float> weights = mesh.MorphWeights;
        Matrix4x4 worldMatrix = mesh.WorldMatrix;
        Vector3 minimum = new(float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity);
        for (int vertexIndex = 0; vertexIndex < mesh.Geometry.Positions.Count; vertexIndex++)
        {
            Vector3 position = mesh.Geometry.Positions[vertexIndex];
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                position += targets[targetIndex].PositionDeltas[vertexIndex] * weights[targetIndex];
            }
            Vector3 transformed = Vector3.Transform(position, worldMatrix);
            if (!float.IsFinite(transformed.X) ||
                !float.IsFinite(transformed.Y) ||
                !float.IsFinite(transformed.Z))
            {
                throw new InvalidOperationException(
                    $"Mesh '{mesh.Name ?? "<unnamed>"}' produced a non-finite world-space bound.");
            }
            minimum = Vector3.Min(minimum, transformed);
            maximum = Vector3.Max(maximum, transformed);
        }
        return new Bounds3D(minimum, maximum);
    }

    private static Bounds3D CalculateLocalBounds(IReadOnlyList<Vector3> positions)
    {
        Vector3 minimum = positions[0];
        Vector3 maximum = positions[0];
        for (int index = 1; index < positions.Count; index++)
        {
            minimum = Vector3.Min(minimum, positions[index]);
            maximum = Vector3.Max(maximum, positions[index]);
        }
        return new Bounds3D(minimum, maximum);
    }

    private sealed class LocalBounds(Bounds3D value)
    {
        internal Bounds3D Value { get; } = value;
    }
}

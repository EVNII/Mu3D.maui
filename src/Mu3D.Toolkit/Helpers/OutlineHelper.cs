using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Helpers;

/// <summary>Defines a bounded, UI-independent target silhouette source.</summary>
/// <remarks>
/// The helper borrows its target and owns no renderer, input source, native resource, or scene
/// membership. It produces current rigid and morph-deformed triangle positions for an outline
/// pass, restricted to that pass's scene and camera layers. Skinned deformation and material
/// alpha masks are not included in this initial slice.
/// </remarks>
public sealed class OutlineHelper
{
    /// <summary>Default maximum number of source triangles generated for one frame.</summary>
    public const int DefaultTriangleLimit = 1_000_000;

    /// <summary>Maximum configurable triangle limit.</summary>
    public const int MaximumTriangleLimit = 4_000_000;

    private int triangleLimit = DefaultTriangleLimit;

    /// <summary>Gets or sets the borrowed target node, or null to produce no outline.</summary>
    public SceneNode? Target { get; set; }

    /// <summary>Gets or sets whether eligible target descendants contribute to the silhouette.</summary>
    public bool IncludeDescendants { get; set; } = true;

    /// <summary>Gets or sets whether invisible target subtrees contribute to the silhouette.</summary>
    /// <remarks>
    /// True permits the target subtree below hidden ancestors and includes hidden descendants,
    /// but does not bypass the render pass's scene membership or camera visibility mask.
    /// </remarks>
    public bool IncludeInvisible { get; set; }

    /// <summary>Gets or sets the positive per-frame source-triangle safety limit.</summary>
    public int TriangleLimit
    {
        get => triangleLimit;
        set
        {
            if (value is < 1 or > MaximumTriangleLimit)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            triangleLimit = value;
        }
    }

    internal void BuildClipTriangles(
        Scene scene,
        SceneVisibilityMask visibilityMask,
        Matrix4x4 viewProjection,
        List<Vector4> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        SceneNode? target = Target;
        if (target is null ||
            !SceneTargetEligibility.TryGetInheritedVisibility(scene, target, out bool inheritedVisible))
        {
            return;
        }
        int triangleCount = 0;
        AddSubtree(target, inheritedVisible, visibilityMask, viewProjection, output,
            ref triangleCount, ignoreOwnVisibility: ReferenceEquals(target, scene.Root));
    }

    private void AddSubtree(
        SceneNode node,
        bool inheritedVisible,
        SceneVisibilityMask visibilityMask,
        Matrix4x4 viewProjection,
        List<Vector4> output,
        ref int triangleCount,
        bool ignoreOwnVisibility = false)
    {
        bool visible = inheritedVisible && (ignoreOwnVisibility || node.IsVisible);
        if (!IncludeInvisible && !visible)
        {
            return;
        }
        // Layers select individual meshes; a differently layered parent does not suppress
        // independently layered children, matching Scene.EnumerateVisible(cameraMask).
        if (node is Mesh mesh && mesh.VisibilityMask.Intersects(visibilityMask))
        {
            int meshTriangleCount = mesh.Geometry.Indices.Count / 3;
            if (triangleCount > TriangleLimit - meshTriangleCount)
            {
                throw new InvalidOperationException(
                    $"Outline source exceeds its {TriangleLimit:N0}-triangle frame limit.");
            }
            triangleCount += meshTriangleCount;
            AddMesh(mesh, viewProjection, output);
        }
        if (!IncludeDescendants)
        {
            return;
        }
        IReadOnlyList<SceneNode> children = node.Children;
        for (int childIndex = 0; childIndex < children.Count; childIndex++)
        {
            AddSubtree(children[childIndex], visible, visibilityMask, viewProjection, output, ref triangleCount);
        }
    }

    private static void AddMesh(Mesh mesh, Matrix4x4 viewProjection, List<Vector4> output)
    {
        IReadOnlyList<Vector3> positions = mesh.Geometry.Positions;
        IReadOnlyList<MorphTarget> targets = mesh.Geometry.MorphTargets;
        IReadOnlyList<float> weights = mesh.MorphWeights;
        Matrix4x4 worldViewProjection = mesh.WorldMatrix * viewProjection;
        IReadOnlyList<uint> indices = mesh.Geometry.Indices;
        for (int index = 0; index < indices.Count; index++)
        {
            int vertexIndex = checked((int)indices[index]);
            Vector3 position = positions[vertexIndex];
            for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
            {
                position += targets[targetIndex].PositionDeltas[vertexIndex] * weights[targetIndex];
            }
            Vector4 clip = Vector4.Transform(new Vector4(position, 1f), worldViewProjection);
            if (!float.IsFinite(clip.X) || !float.IsFinite(clip.Y) ||
                !float.IsFinite(clip.Z) || !float.IsFinite(clip.W))
            {
                throw new InvalidOperationException(
                    $"Mesh '{mesh.Name ?? "<unnamed>"}' produced a non-finite outline vertex.");
            }
            output.Add(clip);
        }
    }
}

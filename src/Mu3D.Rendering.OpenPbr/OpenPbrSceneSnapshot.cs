using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

internal readonly record struct OpenPbrIntersection(float Distance, Vector3 Barycentrics, int TriangleIndex);

internal sealed class OpenPbrSceneSnapshot(
    Vector4[] triangles,
    Vector4[] bvhNodes,
    OpenPbrMaterial[] materials,
    OpenPbrMaterial[] sourceMaterials,
    Scene source,
    SceneVisibilityMask visibilityMask,
    OpenPbrMeshState[] sourceMeshes)
{
    internal Vector4[] Triangles { get; } = triangles;
    internal Vector4[] BvhNodes { get; } = bvhNodes;
    internal OpenPbrMaterial[] Materials { get; } = materials;
    internal OpenPbrMaterial[] SourceMaterials { get; } = sourceMaterials;
    internal int TriangleCount => Triangles.Length / 12;
    internal int NodeCount => BvhNodes.Length / 2;

    // Material values are compared by the render pass's exact packed-parameter comparison.
    internal bool IsCurrent(Scene scene, SceneVisibilityMask mask)
    {
        if (!ReferenceEquals(scene, source) || mask != visibilityMask) return false;
        int index = 0;
        foreach (Mesh mesh in scene.EnumerateVisible(mask).OfType<Mesh>())
        {
            if (index >= sourceMeshes.Length || !sourceMeshes[index++].IsCurrent(mesh)) return false;
        }
        return index == sourceMeshes.Length;
    }

    internal bool TryIntersect(Vector3 origin, Vector3 direction, float minimumDistance,
        float maximumDistance, out OpenPbrIntersection intersection)
    {
        OpenPbrSceneCompiler.RequireFinite(origin);
        OpenPbrSceneCompiler.RequireFinite(direction);
        if (direction == Vector3.Zero || !float.IsFinite(minimumDistance) || minimumDistance < 0f ||
            float.IsNaN(maximumDistance) || maximumDistance < minimumDistance)
            throw new ArgumentOutOfRangeException(nameof(direction), "Ray direction and interval must be valid.");
        intersection = default;
        if (NodeCount == 0) return false;
        Span<int> stack = stackalloc int[64];
        int count = 1;
        stack[0] = 0;
        double closest = maximumDistance;
        int closestTriangle = -1;
        Vector3 barycentrics = default;
        while (count > 0)
        {
            int nodeIndex = stack[--count] * 2;
            Vector4 minimum = BvhNodes[nodeIndex];
            Vector4 maximum = BvhNodes[nodeIndex + 1];
            if (!IntersectsBounds(origin, direction, minimum, maximum, minimumDistance, closest)) continue;
            if (maximum.W < 0f)
            {
                int start = (int)minimum.W;
                int end = start - (int)maximum.W;
                for (int triangle = start; triangle < end; triangle++)
                {
                    int offset = triangle * 12;
                    if (IntersectTriangle(origin, direction, Triangles[offset], Triangles[offset + 1],
                        Triangles[offset + 2], out double distance, out Vector3 barycentric) &&
                        distance >= minimumDistance && distance <= closest &&
                        (distance < closest || closestTriangle < 0))
                    {
                        closest = distance;
                        closestTriangle = triangle;
                        barycentrics = barycentric;
                    }
                }
            }
            else
            {
                stack[count++] = (int)maximum.W;
                stack[count++] = (int)minimum.W;
            }
        }
        if (closestTriangle < 0 || closest > float.MaxValue) return false;
        intersection = new((float)closest, barycentrics, closestTriangle);
        return true;
    }

    private static bool IntersectsBounds(Vector3 origin, Vector3 direction, Vector4 minimum,
        Vector4 maximum, double near, double far)
    {
        for (int axis = 0; axis < 3; axis++)
        {
            double o = origin[axis], d = direction[axis];
            if (d == 0d)
            {
                if (o < minimum[axis] || o > maximum[axis]) return false;
                continue;
            }
            double first = ((double)minimum[axis] - o) / d;
            double second = ((double)maximum[axis] - o) / d;
            if (first > second) (first, second) = (second, first);
            near = Math.Max(near, first);
            far = Math.Min(far, second);
            if (near > far) return false;
        }
        return true;
    }

    private static bool IntersectTriangle(Vector3 origin, Vector3 direction, Vector4 a, Vector4 b,
        Vector4 c, out double distance, out Vector3 barycentrics)
    {
        // Double intermediates provide an independent oracle for the FP32 GPU traversal, including
        // very small nondegenerate triangles. No scene-scale intersection epsilon removes geometry.
        double e1x = (double)b.X - a.X, e1y = (double)b.Y - a.Y, e1z = (double)b.Z - a.Z;
        double e2x = (double)c.X - a.X, e2y = (double)c.Y - a.Y, e2z = (double)c.Z - a.Z;
        double px = direction.Y * e2z - direction.Z * e2y;
        double py = direction.Z * e2x - direction.X * e2z;
        double pz = direction.X * e2y - direction.Y * e2x;
        double determinant = e1x * px + e1y * py + e1z * pz;
        distance = 0d;
        barycentrics = default;
        if (determinant == 0d) return false;
        double tx = (double)origin.X - a.X, ty = (double)origin.Y - a.Y, tz = (double)origin.Z - a.Z;
        double u = (tx * px + ty * py + tz * pz) / determinant;
        if (u < 0d || u > 1d) return false;
        double qx = ty * e1z - tz * e1y, qy = tz * e1x - tx * e1z, qz = tx * e1y - ty * e1x;
        double v = (direction.X * qx + direction.Y * qy + direction.Z * qz) / determinant;
        if (v < 0d || u + v > 1d) return false;
        distance = (e2x * qx + e2y * qy + e2z * qz) / determinant;
        barycentrics = new((float)(1d - u - v), (float)u, (float)v);
        return double.IsFinite(distance);
    }
}

internal sealed class OpenPbrMeshState
{
    internal OpenPbrMeshState(Mesh mesh)
    {
        Mesh = mesh;
        Geometry = mesh.Geometry;
        Material = mesh.Material;
        World = mesh.WorldMatrix;
        ShadowCastingMode = mesh.ShadowCastingMode;
        Skin = mesh.Skin;
        MorphWeights = mesh.MorphWeights.ToArray();
        JointWorlds = mesh.Skin?.Joints.Select(joint => joint.WorldMatrix).ToArray() ?? [];
    }

    internal Mesh Mesh { get; }
    internal MeshGeometry Geometry { get; }
    internal Material Material { get; }
    internal Matrix4x4 World { get; }
    internal MeshShadowCastingMode ShadowCastingMode { get; }
    internal Skin? Skin { get; }
    internal float[] MorphWeights { get; }
    internal Matrix4x4[] JointWorlds { get; }

    internal bool IsCurrent(Mesh mesh)
    {
        if (!ReferenceEquals(mesh, Mesh) || !ReferenceEquals(mesh.Geometry, Geometry) ||
            !ReferenceEquals(mesh.Material, Material) || mesh.WorldMatrix != World || mesh.ShadowCastingMode != ShadowCastingMode ||
            !ReferenceEquals(mesh.Skin, Skin) || mesh.MorphWeights.Count != MorphWeights.Length) return false;
        for (int index = 0; index < MorphWeights.Length; index++)
            if (mesh.MorphWeights[index] != MorphWeights[index]) return false;
        for (int index = 0; index < JointWorlds.Length; index++)
            if (Skin!.Joints[index].WorldMatrix != JointWorlds[index]) return false;
        return true;
    }
}

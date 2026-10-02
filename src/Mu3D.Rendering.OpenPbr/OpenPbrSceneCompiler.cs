using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

internal static class OpenPbrSceneCompiler
{
    private const int MaximumExactlyIndexedTriangleCount = 1 << 23;

    internal static OpenPbrSceneSnapshot Compile(Scene scene, SceneVisibilityMask visibilityMask,
        int maxTriangles, long maxBytes)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (maxTriangles <= 0 || maxTriangles > MaximumExactlyIndexedTriangleCount)
            throw new ArgumentOutOfRangeException(nameof(maxTriangles));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        List<OpenPbrMeshState> meshes = [];
        long count = 0, stateBytes = 0, maximumVertexBytes = 0;
        foreach (Mesh mesh in scene.EnumerateVisible(visibilityMask).OfType<Mesh>())
        {
            if (mesh.Material is not OpenPbrMaterial)
                throw new NotSupportedException($"Mesh '{mesh.Name}' requires an OpenPbrMaterial for this render pass.");
            count = checked(count + mesh.Geometry.Indices.Count / 3);
            if (count > maxTriangles) throw new InvalidOperationException("OpenPBR scene exceeds its triangle budget.");
            stateBytes = checked(stateBytes + 2048L + 4L * mesh.MorphWeights.Count +
                64L * (mesh.Skin?.Joints.Count ?? 0));
            maximumVertexBytes = Math.Max(maximumVertexBytes, checked(64L * mesh.Geometry.Positions.Count +
                160L * (mesh.Skin?.Joints.Count ?? 0)));
            // Upper bound includes temporary triangles, packed triangles, BVH nodes, source
            // snapshots, material copies and peak per-mesh vertex/skin staging before allocation.
            if (checked(count * 512L + stateBytes + maximumVertexBytes) > maxBytes)
                throw new InvalidOperationException("OpenPBR scene exceeds its compilation memory budget.");
            meshes.Add(new OpenPbrMeshState(mesh));
        }

        Triangle[] triangles = new Triangle[(int)count];
        int written = 0;
        List<OpenPbrMaterial> materials = [];
        List<OpenPbrMaterial> sourceMaterials = [];
        Dictionary<Material, int> materialIndices = new(ReferenceEqualityComparer.Instance);
        for (int objectId = 0; objectId < meshes.Count; objectId++)
        {
            OpenPbrMeshState mesh = meshes[objectId];
            if (!materialIndices.TryGetValue(mesh.Material, out int materialIndex))
            {
                OpenPbrMaterial source = (OpenPbrMaterial)mesh.Material;
                materialIndex = materials.Count;
                materials.Add(new OpenPbrMaterial(source.Surface.Clone(), source.Name)
                {
                    Name = source.Name, NitsPerSceneUnit = source.NitsPerSceneUnit,
                    IsDoubleSided = source.IsDoubleSided, AlphaMode = source.AlphaMode,
                    AlphaCutoff = source.AlphaCutoff,
                });
                materialIndices.Add(source, materialIndex);
                sourceMaterials.Add(source);
            }
            Vertex[] vertices = TransformVertices(mesh);
            IReadOnlyList<uint> indices = mesh.Geometry.Indices;
            for (int offset = 0; offset < indices.Count; offset += 3)
            {
                Vertex a = vertices[(int)indices[offset]];
                Vertex b = vertices[(int)indices[offset + 1]];
                Vertex c = vertices[(int)indices[offset + 2]];
                // Reflections reverse transformed index winding, not the physical meaning of
                // outside/inside. Keep oriented boundaries consistent with inverse-transpose normals.
                if (a.Orientation < 0f && b.Orientation < 0f && c.Orientation < 0f) (b, c) = (c, b);
                if (!TryFaceNormal(a.Position, b.Position, c.Position, out Vector3 normal)) continue;
                Vector4 tangent = FaceTangent(a, b, c, normal);
                PrepareFrame(ref a, normal, tangent);
                PrepareFrame(ref b, normal, tangent);
                PrepareFrame(ref c, normal, tangent);
                triangles[written++] = new Triangle(a, b, c, materialIndex, objectId, mesh.ShadowCastingMode);
            }
        }

        List<Vector4> nodes = new(checked(written == 0 ? 0 : (written * 2 - 1) * 2));
        if (written > 0) BuildNode(0, written, 0);
        Vector4[] packed = new Vector4[checked(written * 12)];
        for (int index = 0; index < written; index++) triangles[index].Pack(packed.AsSpan(index * 12, 12));
        return new(packed, nodes.ToArray(), materials.ToArray(), sourceMaterials.ToArray(), scene, visibilityMask, meshes.ToArray());

        int BuildNode(int start, int length, int depth)
        {
            if (depth >= 63) throw new InvalidOperationException("OpenPBR BVH exceeds the traversal stack depth.");
            int node = nodes.Count / 2;
            nodes.Add(default);
            nodes.Add(default);
            Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
            Vector3 centroidMin = minimum, centroidMax = maximum;
            for (int index = start; index < start + length; index++)
            {
                minimum = Vector3.Min(minimum, triangles[index].Minimum);
                maximum = Vector3.Max(maximum, triangles[index].Maximum);
                centroidMin = Vector3.Min(centroidMin, triangles[index].Centroid);
                centroidMax = Vector3.Max(centroidMax, triangles[index].Centroid);
            }
            if (length <= 4)
            {
                nodes[node * 2] = new(minimum, start);
                nodes[node * 2 + 1] = new(maximum, -length);
                return node;
            }
            int axis = 0;
            double longest = (double)centroidMax.X - centroidMin.X;
            for (int candidate = 1; candidate < 3; candidate++)
            {
                double extent = (double)centroidMax[candidate] - centroidMin[candidate];
                if (extent > longest) { longest = extent; axis = candidate; }
            }
            Array.Sort(triangles, start, length, Comparer<Triangle>.Create((a, b) =>
            {
                int comparison = a.Centroid[axis].CompareTo(b.Centroid[axis]);
                return comparison != 0 ? comparison : a.ObjectId.CompareTo(b.ObjectId);
            }));
            int half = length / 2;
            int left = BuildNode(start, half, depth + 1);
            int right = BuildNode(start + half, length - half, depth + 1);
            nodes[node * 2] = new(minimum, left);
            nodes[node * 2 + 1] = new(maximum, right);
            return node;
        }
    }

    private static Vertex[] TransformVertices(OpenPbrMeshState mesh)
    {
        MeshGeometry geometry = mesh.Geometry;
        RequireFinite(mesh.World);
        if (!Matrix4x4.Invert(mesh.World, out Matrix4x4 inverseWorld))
            throw new InvalidOperationException("OpenPBR mesh transforms must be invertible.");
        Matrix4x4 normalWorld = Matrix4x4.Transpose(inverseWorld);
        bool skinned = geometry.JointIndices.Count != 0;
        if (skinned != (mesh.Skin is not null))
            throw new InvalidOperationException("OpenPBR skin data requires matching geometry weights and joints.");
        Matrix4x4[] positions = new Matrix4x4[mesh.JointWorlds.Length];
        Matrix4x4[] normals = new Matrix4x4[mesh.JointWorlds.Length];
        for (int index = 0; index < positions.Length; index++)
        {
            positions[index] = mesh.Skin!.InverseBindMatrices[index] * mesh.JointWorlds[index] * inverseWorld;
            RequireFinite(positions[index]);
            if (!Matrix4x4.Invert(positions[index], out Matrix4x4 inverse))
                throw new InvalidOperationException("OpenPBR skin transforms must be invertible.");
            normals[index] = Matrix4x4.Transpose(inverse);
        }
        Vertex[] vertices = new Vertex[geometry.Positions.Count];
        for (int vertex = 0; vertex < vertices.Length; vertex++)
        {
            Vector3 position = geometry.Positions[vertex];
            Vector3 normal = geometry.Normals.Count == 0 ? Vector3.Zero : geometry.Normals[vertex];
            Vector4 tangent = geometry.Tangents.Count == 0 ? default : geometry.Tangents[vertex];
            Vector3 tangentDirection = new(tangent.X, tangent.Y, tangent.Z);
            for (int target = 0; target < mesh.MorphWeights.Length; target++)
            {
                MorphTarget morph = geometry.MorphTargets[target];
                float weight = mesh.MorphWeights[target];
                position += morph.PositionDeltas[vertex] * weight;
                if (morph.NormalDeltas.Count != 0) normal += morph.NormalDeltas[vertex] * weight;
                if (morph.TangentDeltas.Count != 0) tangentDirection += morph.TangentDeltas[vertex] * weight;
            }
            Matrix4x4 skinPosition = Matrix4x4.Identity;
            Vector4 homogeneousPosition = new(position, 1f);
            if (skinned)
            {
                JointIndices4 joints = geometry.JointIndices[vertex];
                if (joints.Maximum >= positions.Length)
                    throw new InvalidOperationException("OpenPBR vertex references a joint outside its skin.");
                Vector4 weights = geometry.JointWeights[vertex];
                skinPosition = positions[joints.X] * weights.X + positions[joints.Y] * weights.Y +
                    positions[joints.Z] * weights.Z + positions[joints.W] * weights.W;
                Matrix4x4 skinNormal = normals[joints.X] * weights.X + normals[joints.Y] * weights.Y +
                    normals[joints.Z] * weights.Z + normals[joints.W] * weights.W;
                homogeneousPosition = Vector4.Transform(homogeneousPosition, skinPosition);
                normal = Vector3.TransformNormal(normal, skinNormal);
                tangentDirection = Vector3.TransformNormal(tangentDirection, skinPosition);
            }
            homogeneousPosition = Vector4.Transform(homogeneousPosition, mesh.World);
            position = new(homogeneousPosition.X, homogeneousPosition.Y, homogeneousPosition.Z);
            normal = Vector3.TransformNormal(normal, normalWorld);
            tangentDirection = Vector3.TransformNormal(tangentDirection, mesh.World);
            RequireFinite(position);
            RequireFinite(normal);
            RequireFinite(tangentDirection);
            float handedness = tangent.W;
            float orientation = DeterminantSign(skinPosition * mesh.World);
            if (handedness != 0f)
                handedness *= orientation;
            Vector2 uv0 = geometry.TextureCoordinates.Count == 0 ? default : geometry.TextureCoordinates[vertex];
            Vector2 uv1 = geometry.TextureCoordinates1.Count == 0 ? default : geometry.TextureCoordinates1[vertex];
            vertices[vertex] = new(position, normal, new(tangentDirection, handedness), new(uv0.X, uv0.Y, uv1.X, uv1.Y), orientation);
        }
        return vertices;
    }

    private static float DeterminantSign(Matrix4x4 matrix)
    {
        double determinant = (double)matrix.M11 * ((double)matrix.M22 * matrix.M33 - (double)matrix.M23 * matrix.M32) -
            (double)matrix.M12 * ((double)matrix.M21 * matrix.M33 - (double)matrix.M23 * matrix.M31) +
            (double)matrix.M13 * ((double)matrix.M21 * matrix.M32 - (double)matrix.M22 * matrix.M31);
        return determinant < 0d ? -1f : 1f;
    }

    private static void PrepareFrame(ref Vertex vertex, Vector3 faceNormal, Vector4 faceTangent)
    {
        Vector3 normal = vertex.Normal == Vector3.Zero ? faceNormal : Normalize(vertex.Normal);
        Vector3 tangent = vertex.Tangent.W == 0f ? new(faceTangent.X, faceTangent.Y, faceTangent.Z) : new(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z);
        tangent -= normal * Vector3.Dot(normal, tangent);
        tangent = tangent == Vector3.Zero ? Perpendicular(normal) : Normalize(tangent);
        float handedness = vertex.Tangent.W == 0f
            ? faceTangent.W * (Vector3.Dot(normal, faceNormal) < 0f ? -1f : 1f)
            : vertex.Tangent.W;
        vertex = vertex with { Normal = normal, Tangent = new(tangent, handedness) };
    }

    private static Vector4 FaceTangent(Vertex a, Vertex b, Vertex c, Vector3 normal)
    {
        double du1 = (double)b.Uv.X - a.Uv.X, dv1 = (double)b.Uv.Y - a.Uv.Y;
        double du2 = (double)c.Uv.X - a.Uv.X, dv2 = (double)c.Uv.Y - a.Uv.Y;
        double determinant = du1 * dv2 - dv1 * du2;
        if (determinant == 0d) return new(Perpendicular(normal), 1f);
        return new(NormalizeDouble(
            (((double)b.Position.X - a.Position.X) * dv2 - ((double)c.Position.X - a.Position.X) * dv1) / determinant,
            (((double)b.Position.Y - a.Position.Y) * dv2 - ((double)c.Position.Y - a.Position.Y) * dv1) / determinant,
            (((double)b.Position.Z - a.Position.Z) * dv2 - ((double)c.Position.Z - a.Position.Z) * dv1) / determinant),
            determinant < 0d ? -1f : 1f);
    }

    private static bool TryFaceNormal(Vector3 a, Vector3 b, Vector3 c, out Vector3 normal)
    {
        double x1 = (double)b.X - a.X, y1 = (double)b.Y - a.Y, z1 = (double)b.Z - a.Z;
        double x2 = (double)c.X - a.X, y2 = (double)c.Y - a.Y, z2 = (double)c.Z - a.Z;
        double x = y1 * z2 - z1 * y2, y = z1 * x2 - x1 * z2, z = x1 * y2 - y1 * x2;
        normal = default;
        if (x == 0d && y == 0d && z == 0d) return false;
        normal = NormalizeDouble(x, y, z);
        return true;
    }

    private static Vector3 Normalize(Vector3 value) => NormalizeDouble(value.X, value.Y, value.Z);
    private static Vector3 NormalizeDouble(double x, double y, double z)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (!double.IsFinite(length) || length == 0d)
            throw new InvalidOperationException("OpenPBR geometry produced an invalid direction.");
        Vector3 result = new((float)(x / length), (float)(y / length), (float)(z / length));
        RequireFinite(result);
        return result;
    }
    private static Vector3 Perpendicular(Vector3 normal) => Normalize(Vector3.Cross(
        MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY, normal));

    internal static void RequireFinite(Vector3 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new InvalidOperationException("OpenPBR geometry must remain finite after deformation and world transformation.");
    }
    internal static void RequireFinite(Matrix4x4 value)
    {
        RequireFinite(new Vector3(value.M11, value.M12, value.M13));
        RequireFinite(new Vector3(value.M21, value.M22, value.M23));
        RequireFinite(new Vector3(value.M31, value.M32, value.M33));
        RequireFinite(new Vector3(value.M41, value.M42, value.M43));
        if (!float.IsFinite(value.M14) || !float.IsFinite(value.M24) || !float.IsFinite(value.M34) || !float.IsFinite(value.M44))
            throw new InvalidOperationException("OpenPBR world matrices must be finite.");
    }

    private readonly record struct Vertex(Vector3 Position, Vector3 Normal, Vector4 Tangent, Vector4 Uv, float Orientation);
    private readonly record struct Triangle(Vertex A, Vertex B, Vertex C, int MaterialIndex, int ObjectId, MeshShadowCastingMode ShadowMode)
    {
        internal Vector3 Minimum => Vector3.Min(A.Position, Vector3.Min(B.Position, C.Position));
        internal Vector3 Maximum => Vector3.Max(A.Position, Vector3.Max(B.Position, C.Position));
        internal Vector3 Centroid => new(
            (float)(((double)A.Position.X + B.Position.X + C.Position.X) / 3d),
            (float)(((double)A.Position.Y + B.Position.Y + C.Position.Y) / 3d),
            (float)(((double)A.Position.Z + B.Position.Z + C.Position.Z) / 3d));
        internal void Pack(Span<Vector4> target)
        {
            target[0] = new(A.Position, MaterialIndex); target[1] = new(B.Position, ObjectId); target[2] = new(C.Position, (float)ShadowMode);
            target[3] = new(A.Normal, 0f); target[4] = new(B.Normal, 0f); target[5] = new(C.Normal, 0f);
            target[6] = A.Tangent; target[7] = B.Tangent; target[8] = C.Tangent;
            target[9] = A.Uv; target[10] = B.Uv; target[11] = C.Uv;
        }
    }
}

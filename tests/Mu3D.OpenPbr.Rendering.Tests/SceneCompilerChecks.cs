using System.Numerics;
using Mu3D.Color;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class SceneCompilerChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Expect(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(name);
        }
        void Throws<T>(Action action, string name) where T : Exception
        {
            checks++;
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException(name);
        }
        OpenPbrSceneSnapshot Compile(Scene scene) => OpenPbrSceneCompiler.Compile(scene, SceneVisibilityMask.All, 10000, 16 * 1024 * 1024);
        OpenPbrMaterial material = new(new() { BaseColor = new(0.8f, 0.2f, 0.3f, 1f, StandardColorSpaces.AcesCg) });
        Vector3[] positions = [new(-1, -1, 0), new(1, -1, 0), new(0, 1, 0)];
        Vector3[] normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ];
        Vector4[] tangents = [new(1, 0, 0, 1), new(1, 0, 0, 1), new(1, 0, 0, 1)];
        Vector2[] uv0 = [new(0, 0), new(1, 0), new(0.5f, 1)];
        Vector2[] uv1 = [new(0.2f, 0.3f), new(0.4f, 0.5f), new(0.6f, 0.7f)];
        MeshGeometry geometry = MeshGeometry.CreateWithTextureCoordinateSets(positions, [0, 1, 2], uv1, normals, uv0, tangents: tangents);
        Mesh mesh = new(geometry, material);
        Scene scene = new();
        scene.Add(mesh);
        OpenPbrSceneSnapshot snapshot = Compile(scene);
        Expect(snapshot.TriangleCount == 1 && snapshot.NodeCount == 1 && snapshot.Triangles.Length == 12,
            "Single triangle uses exact GPU record and leaf packing");
        Expect(snapshot.BvhNodes[0] == new Vector4(-1, -1, 0, 0) &&
            snapshot.BvhNodes[1] == new Vector4(1, 1, 0, -1), "Leaf stores start and negative count");
        Expect(snapshot.Triangles[0].W == 0 && snapshot.Triangles[1].W == 0 && snapshot.Triangles[2].W == (float)MeshShadowCastingMode.On,
            "Triangle metadata carries stable material/object identity and shadow participation");
        Expect(snapshot.Triangles[9] == new Vector4(0, 0, 0.2f, 0.3f) &&
            snapshot.Triangles[10] == new Vector4(1, 0, 0.4f, 0.5f), "Both UV sets survive packing independently");
        Expect(snapshot.TryIntersect(new(0, 0, 2), -Vector3.UnitZ, 0f, 10f, out var hit) &&
            hit.Distance == 2f && hit.Barycentrics == new Vector3(0.25f, 0.25f, 0.5f),
            "CPU traversal returns exact distance and barycentrics");
        Expect(snapshot.TryIntersect(new(0, 0, -2), Vector3.UnitZ, 0f, 10f, out hit) && hit.Distance == 2f,
            "Traversal retains back faces for interior transport");
        Expect(!snapshot.TryIntersect(new(0, 0, 2), -Vector3.UnitZ, 0f, 1f, out _) &&
            !snapshot.TryIntersect(new(0, 0, 2), -Vector3.UnitZ, 3f, 4f, out _) &&
            !snapshot.TryIntersect(new(3, 0, 2), -Vector3.UnitZ, 0f, 10f, out _),
            "Bounds and finite ray intervals reject excluded intersections");
        Expect(snapshot.IsCurrent(scene, SceneVisibilityMask.All), "Unedited snapshot remains current");
        material.Surface.BaseMetalness = 0.5f;
        Expect(ReferenceEquals(snapshot.SourceMaterials[0], material) && snapshot.SourceMaterials[0].Surface.BaseMetalness == 0.5f,
            "Live source material references retain deduplicated snapshot ordering for parameter invalidation");
        Expect(snapshot.Materials[0].Surface.BaseMetalness == 0f && snapshot.IsCurrent(scene, SceneVisibilityMask.All),
            "Snapshot material is independent; root pass owns numeric material invalidation");
        mesh.Transform.Position = new(0, 0, 3);
        Expect(!snapshot.IsCurrent(scene, SceneVisibilityMask.All) && snapshot.Triangles[0].Z == 0,
            "World edits invalidate geometry without mutating existing snapshot");
        mesh.Transform.Position = Vector3.Zero;
        mesh.Geometry = new MeshGeometry(positions, [0, 2, 1], normals);
        Expect(!snapshot.IsCurrent(scene, SceneVisibilityMask.All), "Immutable geometry replacement invalidates snapshot");
        mesh.Geometry = geometry;
        mesh.Material = new OpenPbrMaterial(new());
        Expect(!snapshot.IsCurrent(scene, SceneVisibilityMask.All), "Material identity replacement invalidates snapshot");
        mesh.Material = material;
        mesh.IsVisible = false;
        Expect(!snapshot.IsCurrent(scene, SceneVisibilityMask.All) && Compile(scene).TriangleCount == 0,
            "Visibility removes meshes and invalidates compiled ordering");
        mesh.IsVisible = true;
        mesh.VisibilityMask = SceneVisibilityMask.FromLayer(2);
        Expect(OpenPbrSceneCompiler.Compile(scene, SceneVisibilityMask.Default, 10, 100000).TriangleCount == 0,
            "Compilation honors requested visibility mask");
        mesh.VisibilityMask = SceneVisibilityMask.Default;

        mesh.Transform.Scale = new(-2, 3, 4);
        OpenPbrSceneSnapshot mirrored = Compile(scene);
        Expect(mirrored.Triangles[0].X == 2f && mirrored.Triangles[0].Y == -3f &&
            mirrored.Triangles[3] == new Vector4(0, 0, 1, 0) && mirrored.Triangles[6] == new Vector4(-1, 0, 0, -1),
            "Mirrored nonuniform world scale transforms positions, normal and tangent handedness");
        mesh.Transform.Scale = Vector3.One;
        mesh.Geometry = new MeshGeometry(positions, [0, 1, 2], normals, [new(0, 0), new(-1, 0), new(-0.5f, 1)]);
        OpenPbrSceneSnapshot derivedTangent = Compile(scene);
        Expect(derivedTangent.Triangles[6] == new Vector4(-1, 0, 0, -1), "Derived tangent follows mirrored UV handedness");

        SceneNode joint = new();
        joint.Transform.Scale = new(2, 3, 1);
        joint.Transform.Position = new(3, 0, 0);
        MorphTarget morph = new([new(1, 0, 0), new(1, 0, 0), new(1, 0, 0)],
            [new(1, 0, 0), new(1, 0, 0), new(1, 0, 0)], tangentDeltas: [Vector3.Zero, Vector3.Zero, Vector3.Zero]);
        mesh.Geometry = new MeshGeometry(positions, [0, 1, 2], normals, uv0,
            jointIndices: [new(0), new(0), new(0)], jointWeights: [Vector4.UnitX, Vector4.UnitX, Vector4.UnitX],
            morphTargets: [morph], tangents: tangents);
        mesh.Skin = new Skin([joint], [Matrix4x4.Identity]);
        mesh.MorphWeights = [0.5f];
        mesh.Transform.Position = new(9, 0, 0);
        OpenPbrSceneSnapshot deformed = Compile(scene);
        Expect(deformed.Triangles[0].X == 2f && deformed.Triangles[0].Y == -3f,
            "Morph applies before skin; inverse mesh cancels mesh transform exactly once");
        Vector3 expectedNormal = Vector3.Normalize(new(0.25f, 0, 1));
        Expect(Vector3.Distance(new(deformed.Triangles[3].X, deformed.Triangles[3].Y, deformed.Triangles[3].Z), expectedNormal) < 1e-6f,
            "Morphed normal follows inverse-transpose joint deformation");
        mesh.MorphWeights = [0.25f];
        Expect(!deformed.IsCurrent(scene, SceneVisibilityMask.All), "Morph edits invalidate snapshot");
        mesh.MorphWeights = [0.5f];
        joint.Transform.Position = new(4, 0, 0);
        Expect(!deformed.IsCurrent(scene, SceneVisibilityMask.All), "Joint world edits invalidate snapshot");
        mesh.Skin = null;
        Throws<InvalidOperationException>(() => Compile(scene), "Mismatched skin input rejected");

        mesh.Geometry = geometry;
        mesh.Transform.Position = Vector3.Zero;
        mesh.Transform.Scale = new(float.MaxValue, 1, 1);
        mesh.Geometry = new MeshGeometry([new(2, 0, 0), new(3, 0, 0), new(2, 1, 0)], [0, 1, 2]);
        Throws<InvalidOperationException>(() => Compile(scene), "Nonfinite transformed geometry rejected");
        mesh.Transform.Scale = Vector3.One;
        mesh.Geometry = new MeshGeometry([Vector3.Zero, new(1e-20f, 0, 0), new(0, 1e-20f, 0)], [0, 1, 2]);
        OpenPbrSceneSnapshot tiny = Compile(scene);
        Expect(tiny.TriangleCount == 1 && tiny.TryIntersect(new(2e-21f, 2e-21f, 1), -Vector3.UnitZ, 0, 2, out _),
            "Nonzero tiny triangles survive without arbitrary area floor");
        mesh.Geometry = new MeshGeometry([Vector3.Zero, Vector3.UnitX, new(2, 0, 0)], [0, 1, 2]);
        Expect(Compile(scene).TriangleCount == 0, "Exactly degenerate triangles are skipped");
        mesh.Geometry = geometry;
        Throws<InvalidOperationException>(() => OpenPbrSceneCompiler.Compile(scene, SceneVisibilityMask.All, 10, 1),
            "Memory budget rejects before geometry allocation");
        Throws<ArgumentOutOfRangeException>(() => OpenPbrSceneCompiler.Compile(scene, SceneVisibilityMask.All, 0, 100000),
            "Invalid triangle budget rejected");
        mesh.Material = new PbrMaterial(new(1, 1, 1, 1, StandardColorSpaces.LinearSrgb));
        Throws<NotSupportedException>(() => Compile(scene), "Other material roots cannot silently change rendering meaning");
        mesh.Material = material;

        Scene layered = new();
        Mesh nearLayer = new(geometry, material);
        nearLayer.Transform.Position = new(0, 0, 3);
        layered.Add(new Mesh(geometry, material));
        layered.Add(nearLayer);
        OpenPbrSceneSnapshot layers = Compile(layered);
        Expect(layers.TryIntersect(new(0, 0, 5), -Vector3.UnitZ, 0, 10, out hit) &&
            hit.Distance == 2f && layers.Triangles[hit.TriangleIndex * 12 + 1].W == 1f,
            "Traversal chooses nearest overlapping object without losing medium identity");
        Expect(layers.TryIntersect(new(0, 0, 5), -Vector3.UnitZ, 3, 10, out hit) && hit.Distance == 5f,
            "Ray minimum interval can exclude the nearer interface and retain the far boundary");

        Scene many = new();
        for (int index = 0; index < 49; index++)
        {
            Mesh item = new(geometry, material);
            item.Transform.Position = new((index % 7) * 4, (index / 7) * 4, index % 3);
            many.Add(item);
        }
        OpenPbrSceneSnapshot bvh = Compile(many);
        Expect(bvh.NodeCount > 1 && bvh.TriangleCount == 49 && bvh.Materials.Length == 1,
            "Many meshes build a split BVH with deduplicated material snapshots");
        bool[] covered = new bool[bvh.TriangleCount];
        for (int node = 0; node < bvh.NodeCount; node++)
        {
            Vector4 minimum = bvh.BvhNodes[node * 2], maximum = bvh.BvhNodes[node * 2 + 1];
            if (maximum.W >= 0)
            {
                Expect(minimum.W > node && maximum.W > node && maximum.W < bvh.NodeCount,
                    "Internal node child indices are valid forward references");
                continue;
            }
            int start = (int)minimum.W, count = -(int)maximum.W;
            Expect(count is > 0 and <= 4 && start >= 0 && start + count <= covered.Length,
                "Leaves contain bounded contiguous triangle ranges");
            for (int triangle = start; triangle < start + count; triangle++)
            {
                Expect(!covered[triangle], "BVH leaf triangle ownership is unique");
                covered[triangle] = true;
            }
        }
        Expect(covered.All(value => value), "BVH covers every emitted triangle exactly once");
        for (int index = 0; index < 49; index++)
        {
            Expect(bvh.TryIntersect(new((index % 7) * 4, (index / 7) * 4, 10), -Vector3.UnitZ, 0, 20, out hit) &&
                hit.Distance == 10 - index % 3 && bvh.Triangles[hit.TriangleIndex * 12 + 1].W == index,
                "Reordering preserves object medium identity and correct hit distances");
        }
        Random random = new(13);
        for (int trial = 0; trial < 300; trial++)
        {
            int index = random.Next(49);
            float u = (float)random.NextDouble() * 0.4f + 0.1f;
            float v = (float)random.NextDouble() * 0.3f + 0.1f;
            Vector3 point = positions[0] * (1 - u - v) + positions[1] * u + positions[2] * v +
                new Vector3((index % 7) * 4, (index / 7) * 4, index % 3);
            Expect(bvh.TryIntersect(point + Vector3.UnitZ * 4, -Vector3.UnitZ, 0, 10, out hit) &&
                MathF.Abs(hit.Distance - 4) < 1e-5f &&
                Vector3.Distance(hit.Barycentrics, new(1 - u - v, u, v)) < 2e-6f,
                "Random barycentric rays match independent constructed triangle points");
        }
        Throws<InvalidOperationException>(() => OpenPbrSceneCompiler.Compile(many, SceneVisibilityMask.All, 48, 10000000),
            "Source triangle budget is applied before BVH construction");
        Mesh moved = (Mesh)many.Root.Children[0];
        many.Remove(moved); many.Add(moved);
        Expect(!bvh.IsCurrent(many, SceneVisibilityMask.All), "Mesh reorder invalidates stable medium identity");
        return checks;
    }
}

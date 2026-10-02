using System.Numerics;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class InitialMediaChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Expect(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
        void Reject(Action action, string message)
        {
            checks++;
            try { action(); }
            catch (NotSupportedException) { return; }
            throw new InvalidOperationException(message);
        }
        OpenPbrSceneSnapshot Compile(Scene scene) => OpenPbrSceneCompiler.Compile(scene, SceneVisibilityMask.All, 10000, 16000000);
        OpenPbrMaterial Glass() => new(new() { TransmissionWeight = 1f });
        Vector3[] positions =
        [
            new(-1,-1,-1), new(1,-1,-1), new(1,1,-1), new(-1,1,-1),
            new(-1,-1,1), new(1,-1,1), new(1,1,1), new(-1,1,1),
        ];
        uint[] indices = [4,5,6,4,6,7, 0,2,1,0,3,2, 0,1,5,0,5,4, 3,7,6,3,6,2, 0,4,7,0,7,3, 1,2,6,1,6,5];
        MeshGeometry box = new(positions, indices);
        Scene scene = new();
        OpenPbrMaterial glass = Glass();
        Mesh mesh = new(box, glass);
        scene.Add(mesh);
        OpenPbrSceneSnapshot snapshot = Compile(scene);
        Vector4[] inside = OpenPbrInitialMedia.Resolve(snapshot, Vector3.Zero);
        Expect(inside.Length == 1 && inside[0] == Vector4.Zero,
            "Camera inside closed convex volume receives material/object stack entry");
        Expect(OpenPbrInitialMedia.Resolve(snapshot, new(4, 0, 0)).Length == 0,
            "Camera outside volume starts in air");
        Reject(() => OpenPbrInitialMedia.Resolve(snapshot, new(1, 0, 0)),
            "Camera on boundary cannot silently choose an arbitrary medium");
        Reject(() => OpenPbrInitialMedia.Resolve(snapshot, Vector3.Zero, maximumFaceTests: 1),
            "Volume validation respects bounded CPU work");
        glass.Surface.GeometryThinWalled = true;
        Expect(OpenPbrInitialMedia.Resolve(snapshot, Vector3.Zero).Length == 0,
            "Live thin-wall edit excludes closed interior without relying on stale frozen material");
        glass.Surface.GeometryThinWalled = false;
        glass.Surface.TransmissionWeight = 0;
        Expect(OpenPbrInitialMedia.Resolve(snapshot, Vector3.Zero).Length == 0,
            "Ordinary opaque material is not forced through volume validation");
        glass.Surface.SubsurfaceWeight = 1;
        Expect(OpenPbrInitialMedia.Resolve(snapshot, Vector3.Zero).Length == 1,
            "Subsurface interiors initialize even without transmission weight");
        glass.Surface.GeometryOpacity = 0.5f;
        Reject(() => OpenPbrInitialMedia.Resolve(snapshot, Vector3.Zero), "Partially opaque initial interiors require explicit support");
        glass.Surface.GeometryOpacity = 1;
        glass.Surface.SubsurfaceWeight = 0;
        glass.Surface.TransmissionWeight = 1;

        mesh.Transform.Scale = new(-2, 3, 4);
        OpenPbrSceneSnapshot reflected = Compile(scene);
        Expect(OpenPbrInitialMedia.Resolve(reflected, Vector3.Zero).Length == 1,
            "Mirrored object preserves physical outward winding and initial-medium classification");
        mesh.Transform.Scale = Vector3.One;
        mesh.Geometry = new(positions, indices.Skip(6));
        Reject(() => OpenPbrInitialMedia.Resolve(Compile(scene), Vector3.Zero), "Open surrounding mesh cannot invent an interior");
        uint[] inward = indices.ToArray();
        for (int triangle = 0; triangle < inward.Length; triangle += 3)
            (inward[triangle + 1], inward[triangle + 2]) = (inward[triangle + 2], inward[triangle + 1]);
        mesh.Geometry = new(positions, inward);
        Reject(() => OpenPbrInitialMedia.Resolve(Compile(scene), Vector3.Zero), "Inward winding is rejected explicitly");
        mesh.Geometry = box;

        Scene nested = new();
        nested.Add(new Mesh(box, Glass()));
        Mesh outer = new(box, Glass());
        outer.Transform.Scale = new(4);
        nested.Add(outer);
        Vector4[] nestedMedia = OpenPbrInitialMedia.Resolve(Compile(nested), Vector3.Zero);
        Expect(nestedMedia.Length == 2 && nestedMedia[0] == new Vector4(1, 1, 0, 0) && nestedMedia[1] == Vector4.Zero,
            "Nested stack orders outer to inner independent of mesh/material order");

        Scene overlap = new();
        Mesh left = new(box, Glass()), right = new(box, Glass());
        left.Transform.Position = new(-0.4f, 0, 0); right.Transform.Position = new(0.4f, 0, 0);
        overlap.Add(left); overlap.Add(right);
        Reject(() => OpenPbrInitialMedia.Resolve(Compile(overlap), Vector3.Zero), "Intersecting volumes are not disguised as nested media");
        right.Transform.Position = Vector3.Zero;
        left.Transform.Position = new(0.5f, 0, 0);
        left.Transform.Scale = new(1.5f); right.Transform.Scale = new(2);
        Reject(() => OpenPbrInitialMedia.Resolve(Compile(overlap), Vector3.Zero), "Touching nested boundaries are ambiguous and rejected");

        Scene deep = new();
        for (int depth = 1; depth <= 9; depth++)
        {
            Mesh item = new(box, Glass()); item.Transform.Scale = new(depth); deep.Add(item);
        }
        Reject(() => OpenPbrInitialMedia.Resolve(Compile(deep), Vector3.Zero), "Initial nesting exceeding GPU medium capacity is rejected");

        Vector2[] polygon = [new(-2,-2),new(2,-2),new(2,0),new(0,0),new(0,2),new(-2,2)];
        Vector3[] concavePositions = polygon.Select(p => new Vector3(p, -1))
            .Concat(polygon.Select(p => new Vector3(p, 1))).ToArray();
        uint[] faceIndices = [0,1,3,1,2,3,0,3,5,3,4,5];
        List<uint> concaveIndices = [];
        for (int offset = 0; offset < faceIndices.Length; offset += 3)
        {
            uint a = faceIndices[offset], b = faceIndices[offset + 1], c = faceIndices[offset + 2];
            concaveIndices.AddRange([a,c,b,a+6,b+6,c+6]);
        }
        for (uint edge = 0; edge < 6; edge++)
        {
            uint next = (edge + 1) % 6;
            concaveIndices.AddRange([edge,next,next+6,edge,next+6,edge+6]);
        }
        mesh.Geometry = new(concavePositions, concaveIndices);
        Expect(OpenPbrInitialMedia.Resolve(Compile(scene), new(-1, -1, 0)).Length == 1,
            "Closed concave volume is classified by winding and topology");
        Expect(OpenPbrInitialMedia.Resolve(Compile(scene), new(1, 1, 0)).Length == 0,
            "AABB inclusion alone never mistakes a concave cutout for occupied medium");
        Mesh enclosing = new(box, Glass()); enclosing.Transform.Scale = new(4); scene.Add(enclosing);
        Expect(OpenPbrInitialMedia.Resolve(Compile(scene), new(-1,-1,0)).Length == 2,
            "Concave interiors can be strictly nested inside another volume.");
        scene.Remove(enclosing);
        glass.Surface.TransmissionWeight = 0;
        glass.Surface.Graph = new(new Dictionary<Mu3D.SceneGraph.OpenPbrInput, Mu3D.SceneGraph.OpenPbrNode>
            { [Mu3D.SceneGraph.OpenPbrInput.TransmissionWeight] = Mu3D.SceneGraph.OpenPbrNode.Float(1) });
        Expect(OpenPbrInitialMedia.Resolve(Compile(scene), new(-1,-1,0)).Length == 1,
            "Connected transmission is used by camera medium classification.");
        var density = Mu3D.SceneGraph.OpenPbrNode.Image(Mu3D.SceneGraph.OpenPbrTexture.FromData(1,1,[Vector4.One]), Mu3D.SceneGraph.OpenPbrNodeType.Float);
        glass.Surface.Graph = new(new Dictionary<Mu3D.SceneGraph.OpenPbrInput, Mu3D.SceneGraph.OpenPbrNode>
            { [Mu3D.SceneGraph.OpenPbrInput.TransmissionDepth] = density });
        glass.Surface.TransmissionWeight = 1;
        Reject(() => OpenPbrInitialMedia.Resolve(Compile(scene), new(-1,-1,0)),
            "Camera interior cannot invent a volume field from spatial surface UVs.");
        glass.Surface.Graph = null;
        mesh.Geometry = MeshPrimitives.CreateUvSphere(longitudeSegments: 24, latitudeSegments: 12);
        OpenPbrSceneSnapshot sphere = Compile(scene);
        Expect(OpenPbrInitialMedia.Resolve(sphere, Vector3.Zero).Length == 1,
            "Built-in UV sphere forms an exactly closed volume at its seam and poles");
        for (int latitude = 0; latitude <= 12; latitude++)
        {
            int first = latitude * 25, last = first + 24;
            Expect(mesh.Geometry.Positions[first] == mesh.Geometry.Positions[last] &&
                mesh.Geometry.Normals[first] == mesh.Geometry.Normals[last] &&
                mesh.Geometry.TextureCoordinates[first].X == 0f && mesh.Geometry.TextureCoordinates[last].X == 1f,
                "Sphere geometric seam is exact while both UV endpoints remain distinct");
        }
        Expect(Enumerable.Range(0, 25).All(vertex => mesh.Geometry.Positions[vertex] == mesh.Geometry.Positions[0]) &&
            Enumerable.Range(300, 25).All(vertex => mesh.Geometry.Positions[vertex] == mesh.Geometry.Positions[300]),
            "Duplicated north and south pole positions remain bit-exact");
        return checks;
    }
}

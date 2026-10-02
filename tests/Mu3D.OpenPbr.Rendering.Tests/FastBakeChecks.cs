using System.Numerics;
using Mu3D.Color;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class FastBakeChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Expect(bool condition, string name) { checks++; if (!condition) throw new InvalidOperationException("OpenPBR Fast bake: " + name); }

        // Uniform connections fold back into the packed constants; nothing is baked.
        OpenPbrMaterial uniform = new(new()
        {
            Graph = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
            { [OpenPbrInput.BaseColor] = OpenPbrNode.Color(new(.25f, .5f, .75f, 1, StandardColorSpaces.AcesCg)) }),
        });
        OpenPbrFastBake bake = OpenPbrFastBake.Bake([uniform], 64L * 1024 * 1024);
        Expect(bake.Textures.Count == 0 && bake.Entries.Count == 0 && bake.Folds.Count == 1, "uniform connection folds to constants");
        Vector4[] packed = OpenPbrGpuMaterial.Pack(uniform, 1);
        bake.ApplyFolds(packed);
        Vector4 reference = OpenPbrGraphEvaluator.Evaluate(uniform.Surface.Graph!, OpenPbrInput.BaseColor,
            Vector2.Zero, Vector2.Zero, Vector3.UnitZ, new(1, 0, 0, 1));
        Expect(Vector4.Distance(new(packed[0].X, packed[0].Y, packed[0].Z, 0), new(reference.X, reference.Y, reference.Z, 0)) < 1e-6f,
            "folded constant matches the graph evaluator");

        // A connected image bakes at its own resolution and reproduces the evaluator at texel centers.
        OpenPbrTexture colors = OpenPbrTexture.FromColor(new(2, 2,
            [new(1, 0, 0, 1), new(0, 1, 0, 1), new(0, 0, 1, 1), new(1, 1, 1, 1)], StandardColorSpaces.AcesCg));
        OpenPbrMaterial textured = new(new()
        {
            Graph = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
            { [OpenPbrInput.BaseColor] = OpenPbrNode.Image(colors, OpenPbrNodeType.Color3) }),
        });
        bake = OpenPbrFastBake.Bake([textured], 64L * 1024 * 1024);
        Expect(bake.Textures.Count == 1 && bake.Entries.Count == 1 && bake.Folds.Count == 0, "image connection bakes one texture");
        Expect(bake.Textures[0].Width == 2 && bake.Textures[0].Height == 2, "bake keeps the application-owned source resolution");
        Expect(bake.Approximations[0].Kinds.HasFlag(OpenPbrFastApproximationKinds.BakedGraphTextures), "baked connection is reported");
        Expect(bake.Approximations[0].BakedTextureCount == 1 && bake.Approximations[0].BakedBytes > 0, "bake footprint is reported per surface");
        foreach (var (x, y) in new[] { (0, 0), (1, 0), (0, 1), (1, 1) })
        {
            Vector2 uv = new((x + .5f) / 2, (y + .5f) / 2);
            Vector4 expected = OpenPbrGraphEvaluator.Evaluate(textured.Surface.Graph!, OpenPbrInput.BaseColor,
                uv, Vector2.Zero, Vector3.UnitZ, new(1, 0, 0, 1));
            Expect(Vector4.Distance(bake.Textures[0].Texels[y * 2 + x], expected) < 1e-6f,
                $"baked texel ({x},{y}) matches the reference evaluator");
        }

        // One connection mixing UV0 and UV1 cannot be a single baked texture.
        OpenPbrMaterial mixed = new(new()
        {
            Graph = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
            {
                [OpenPbrInput.BaseColor] = OpenPbrNode.Image(colors, OpenPbrNodeType.Color3,
                    OpenPbrNode.Add(OpenPbrNode.Texcoord(0), OpenPbrNode.Multiply(OpenPbrNode.Texcoord(1), OpenPbrNode.Float(0)))),
            }),
        });
        try { OpenPbrFastBake.Bake([mixed], 64L * 1024 * 1024); throw new Exception("missing mixed-UV rejection"); }
        catch (NotSupportedException) { checks++; }

        // Only a root normal map on a shading normal can move its decode to shade time.
        OpenPbrTexture data = OpenPbrTexture.FromData(2, 2, Enumerable.Repeat(new Vector4(.5f, .5f, 1, 1), 4));
        OpenPbrMaterial normalMapped = new(new()
        {
            Graph = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
            { [OpenPbrInput.GeometryNormal] = OpenPbrNode.NormalMap(OpenPbrNode.Image(data, OpenPbrNodeType.Vector3), .75f) }),
        });
        bake = OpenPbrFastBake.Bake([normalMapped], 64L * 1024 * 1024);
        Expect(bake.Entries.Count == 1 && bake.Entries[0].IsNormalMap && bake.Entries[0].NormalScale == .75f,
            "root normal map bakes its [0,1] data and keeps the scale");
        OpenPbrMaterial misplaced = new(new()
        {
            Graph = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
            { [OpenPbrInput.GeometryNormal] = OpenPbrNode.Multiply(OpenPbrNode.NormalMap(OpenPbrNode.Vector3(new(.5f, .5f, 1))), OpenPbrNode.Float(.5f)) }),
        });
        try { OpenPbrFastBake.Bake([misplaced], 64L * 1024 * 1024); throw new Exception("missing mid-graph normal rejection"); }
        catch (NotSupportedException) { checks++; }

        // The fixed binding budget rejects scenes needing more than eight baked inputs.
        List<OpenPbrMaterial> many = [];
        for (int i = 0; i < 9; i++)
            many.Add(new(new()
            {
                Graph = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
                { [OpenPbrInput.BaseColor] = OpenPbrNode.Image(colors, OpenPbrNodeType.Color3) }),
            }));
        try { OpenPbrFastBake.Bake(many, 64L * 1024 * 1024); throw new Exception("missing bake slot rejection"); }
        catch (NotSupportedException) { checks++; }
        try { OpenPbrFastBake.Bake([textured], 1); throw new Exception("missing bake budget rejection"); }
        catch (InvalidOperationException) { checks++; }

        // Lobe approximations follow conservative maxima, including through graphs.
        OpenPbrMaterial lobes = new(new()
        {
            CoatWeight = 1, FuzzWeight = 0,
            Graph = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
            { [OpenPbrInput.FuzzWeight] = OpenPbrNode.Float(.6f), [OpenPbrInput.SubsurfaceWeight] = OpenPbrNode.Float(.2f) }),
            ThinFilmWeight = .5f,
        });
        bake = OpenPbrFastBake.Bake([lobes], 64L * 1024 * 1024);
        var kinds = bake.Approximations[0].Kinds;
        Expect(kinds.HasFlag(OpenPbrFastApproximationKinds.CoatEnvironmentBaseGgxChain), "coat mapping reported");
        Expect(kinds.HasFlag(OpenPbrFastApproximationKinds.FuzzEnvironmentCharlieChain), "graph-connected fuzz mapping reported");
        Expect(kinds.HasFlag(OpenPbrFastApproximationKinds.SubsurfaceDropped), "graph-connected subsurface dropout reported");
        Expect(kinds.HasFlag(OpenPbrFastApproximationKinds.ThinFilmDropped), "thin-film dropout reported");
        Expect(!kinds.HasFlag(OpenPbrFastApproximationKinds.BakedGraphTextures), "uniform lobes graph does not claim baked textures");

        // Bake-info words: per-material header then two words per entry.
        OpenPbrMaterial texturedNormal = new(new()
        {
            Graph = new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode>
            {
                [OpenPbrInput.BaseColor] = OpenPbrNode.Image(colors, OpenPbrNodeType.Color3),
                [OpenPbrInput.GeometryNormal] = OpenPbrNode.NormalMap(OpenPbrNode.Image(data, OpenPbrNodeType.Vector3)),
            }),
        });
        bake = OpenPbrFastBake.Bake([texturedNormal], 64L * 1024 * 1024);
        Vector4[] info = bake.PackBakeInfo(1);
        Expect((int)info[0].X == 1 && (int)info[0].Y == 2, "header locates the material's two entries");
        Expect((int)info[1].X == 0 && (int)info[1].Y == -1 && (int)info[1].Z == 0 && (int)info[1].W == 0, "base-color entry packs slot/channel/texture/flags");
        Expect((int)info[3].X == 14 && (int)info[3].Y == -1 && (int)info[3].Z == 1 && (int)info[3].W == 2, "normal entry packs the normal-map flag");

        // Box downsampling averages the 2x2 neighborhood.
        Vector4[] flat = OpenPbrFastBake.DownsampleBox(
            [new(1, 0, 0, 0), new(0, 1, 0, 0), new(0, 0, 1, 0), new(0, 0, 0, 1)], 2, 2, out int nw, out int nh);
        Expect(nw == 1 && nh == 1 && flat[0] == new Vector4(.25f), "box downsample averages");
        return checks;
    }
}

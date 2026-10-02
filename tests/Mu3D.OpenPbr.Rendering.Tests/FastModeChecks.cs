using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

// ADR 0030 Fast mode on a real headless GPU: split-sum IBL, baked graph textures and the
// explicit per-surface approximation report. The pinned reference modes are covered elsewhere.
internal static class FastModeChecks
{
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        int checks = 0;
        using GraphicsTexture target = device.CreateTexture(new(new(4, 4), GraphicsTextureFormat.Rgba32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        Scene scene = new();
        PerspectiveCamera camera = new() { FieldOfViewRadians = .05f };
        camera.Transform.Position = new(0, 0, 2);
        using OpenPbrRenderPass pass = new(StandardColorSpaces.AcesCg) { Mode = OpenPbrRenderMode.Fast };
        RenderPassContext Context() => new(scene, camera, target, null, StandardColorSpaces.AcesCg);
        async Task<Vector4[]> Render()
        {
            pass.Execute(Context());
            return await TransportChecks.ReadAsync(device, target);
        }

        // Background keeps the constant environment and premultiplied background alpha, like Raster.
        pass.BackgroundAlpha = .25f;
        pass.EnvironmentRadiance = new(8, 2, 1, 1, StandardColorSpaces.AcesCg);
        Compare(await Render(), new(2, .5f, .25f, .25f), 1e-5f, "fast background keeps HDR and premultiplied alpha");
        Expect(pass.AccumulatedSamples == 0 && pass.ReferenceSamples == 0, "fast does not accumulate");
        Expect(pass.FastApproximations.Count == 0, "empty scene reports no surface approximations");
        pass.BackgroundAlpha = 0;
        pass.EnvironmentRadiance = new(0, 0, 0, 1, StandardColorSpaces.AcesCg);

        // Emission and depth match the raster contract.
        OpenPbrMaterial emission = new(new() { BaseWeight = 0, SpecularWeight = 0, EmissionLuminance = 8,
            EmissionColor = new(1, .5f, .25f, 1, StandardColorSpaces.AcesCg) }) { NitsPerSceneUnit = 2 };
        Mesh front = new(Quad(20), emission);
        OpenPbrMaterial black = new(new() { BaseWeight = 0, SpecularWeight = 0 });
        Mesh back = new(Quad(20), black);
        back.Transform.Position = new(0, 0, -1);
        scene.Add(back); scene.Add(front);
        Compare(await Render(), new(4, 2, 1, 1), 1e-5f, "fast depth selects closest emissive triangle");
        scene.Remove(back);
        camera.NearClip = 3;
        Compare(await Render(), Vector4.Zero, 1e-6f, "fast applies ordinary near clipping");
        camera.NearClip = .1f;
        scene.Remove(front);

        // Direct delta lighting uses the same pinned closure as Raster: analytic Lambert.
        OpenPbrMaterial diffuse = new(new() { BaseColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg),
            SpecularWeight = 0, SpecularIor = 1, BaseDiffuseRoughness = 0 });
        Mesh receiver = new(Quad(20), diffuse); scene.Add(receiver);
        DirectionalLight sun = new(new(1, 1, 1, 1, StandardColorSpaces.AcesCg), 4) { AngularDiameterRadians = 0 };
        scene.Add(sun);
        Compare(await Render(), new(4 / MathF.PI, 4 / MathF.PI, 4 / MathF.PI, 1), 2e-5f, "fast Lambert directional lighting");
        pass.Mode = OpenPbrRenderMode.Raster;
        Vector4[] rasterDirect = await Render();
        pass.Mode = OpenPbrRenderMode.Fast;
        Vector4[] fastDirect = await Render();
        for (int i = 0; i < fastDirect.Length; i++)
            Expect(Vector4.Distance(fastDirect[i], rasterDirect[i]) < 1e-4f, "fast direct light matches raster");
        scene.Remove(sun);

        // Constant-environment furnaces through the split-sum terms.
        pass.EnvironmentRadiance = new(1, 1, 1, 1, StandardColorSpaces.AcesCg);
        Compare(await Render(), Vector4.One, 1e-4f, "fast diffuse white furnace");
        diffuse.Surface = new() { BaseMetalness = 1, SpecularRoughness = 0,
            BaseColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg) };
        Vector4[] metal = await Render();
        foreach (Vector4 pixel in metal)
            Expect(pixel.Y is > .97f and <= 1.05f, $"fast metal split-sum furnace conserves energy: {pixel.Y:G9}");
        diffuse.Surface = new() { BaseColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg),
            SpecularWeight = 0, SpecularIor = 1, BaseDiffuseRoughness = 0 };
        Vector4[] first = await Render();
        Vector4[] repeated = await Render();
        Expect(first.SequenceEqual(repeated), "fast split-sum has no temporal noise");

        // A uniform image-based environment replaces the constant term through the prefiltered chains.
        pass.EnvironmentRadiance = new(0, 0, 0, 1, StandardColorSpaces.AcesCg);
        ImageBasedLight image = new(new EquirectangularHdrEnvironment(4, 2,
            Enumerable.Repeat(new Vector3(2), 8), StandardColorSpaces.AcesCg)) { Intensity = .5f };
        scene.Add(image);
        Compare(await Render(), Vector4.One, .02f, "fast diffuse furnace through the SH irradiance cube");
        scene.Remove(image);

        // Explicit rejections and reported dropouts.
        diffuse.Surface.TransmissionWeight = .5f;
        ExpectThrows<NotSupportedException>(() => pass.Execute(Context()), "fast rejects transmission");
        diffuse.Surface.TransmissionWeight = 0; diffuse.Surface.GeometryOpacity = .5f;
        ExpectThrows<NotSupportedException>(() => pass.Execute(Context()), "fast rejects partial opacity");
        diffuse.Surface.GeometryOpacity = 1;
        diffuse.Surface.SubsurfaceWeight = .5f; diffuse.Surface.FuzzWeight = .8f; diffuse.Surface.CoatWeight = .6f;
        diffuse.Surface.ThinFilmWeight = 1; diffuse.Surface.ThinFilmThickness = .45f;
        Vector4[] dropped = await Render();
        foreach (Vector4 pixel in dropped) Expect(float.IsFinite(pixel.X), "dropped lobes still render finite");
        OpenPbrFastApproximation report = pass.FastApproximations.First(a => a.Kinds.HasFlag(OpenPbrFastApproximationKinds.SubsurfaceDropped));
        var kinds = report.Kinds;
        Expect(kinds.HasFlag(OpenPbrFastApproximationKinds.SubsurfaceDropped) &&
            kinds.HasFlag(OpenPbrFastApproximationKinds.ThinFilmDropped) &&
            kinds.HasFlag(OpenPbrFastApproximationKinds.FuzzEnvironmentCharlieChain) &&
            kinds.HasFlag(OpenPbrFastApproximationKinds.CoatEnvironmentBaseGgxChain), "report lists every approximated lobe");
        pass.Mode = OpenPbrRenderMode.Raster;
        ExpectThrows<NotSupportedException>(() => pass.Execute(Context()), "raster still rejects subsurface");
        Expect(pass.FastApproximations.Count == 0, "reference modes report no fast approximations");
        pass.Mode = OpenPbrRenderMode.Fast;
        diffuse.Surface = new() { BaseColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg),
            SpecularWeight = 0, SpecularIor = 1, BaseDiffuseRoughness = 0 };

        // A connected emission texture is baked and sampled with hardware filtering.
        scene.Remove(receiver);
        OpenPbrMaterial textured = new(new() { BaseWeight = 0, SpecularWeight = 0, EmissionLuminance = 4 })
        { NitsPerSceneUnit = 1 };
        textured.Surface.Graph = new(new Dictionary<OpenPbrInput, OpenPbrNode>
        {
            [OpenPbrInput.EmissionColor] = OpenPbrNode.Image(OpenPbrTexture.FromColor(new(2, 2,
                [new(1, 0, 0, 1), new(0, 1, 0, 1), new(0, 0, 1, 1), new(1, 1, 1, 1)], StandardColorSpaces.AcesCg)), OpenPbrNodeType.Color3),
        });
        Mesh quad = new(QuadUv(10), textured); scene.Add(quad);
        pass.EnvironmentRadiance = new(0, 0, 0, 1, StandardColorSpaces.AcesCg);
        Vector4[] baked = await Render();
        // The 2x2 bake sampled at quad-center UV (.5,.5) bilinearly mixes all four texels.
        foreach (Vector4 pixel in baked)
            Expect(Vector4.Distance(pixel, new(2, 2, 2, 1)) < .05f, $"fast baked emission matches bilinear expectation: {pixel}");
        Expect(pass.FastApproximations.Any(a => a.Kinds.HasFlag(OpenPbrFastApproximationKinds.BakedGraphTextures) && a.BakedTextureCount == 1),
            "baked graph texture is reported");
        scene.Remove(quad);

        // UV0 has zero gradients; the UV1 checker spans 16 texels per screen pixel.
        // The half-texel phase makes level-zero sampling hit a checker texel, not an
        // accidental bilinear average. Every nonzero mip of this checker is exactly .5.
        const int checkerSize = 64;
        Vector2 texelCenter = new(.5f / checkerSize);
        OpenPbrTexture checker = OpenPbrTexture.FromColor(new(checkerSize, checkerSize,
            Enumerable.Range(0, checkerSize * checkerSize).Select(i => ((i + i / checkerSize) & 1) == 0
                ? new Vector4(0, 0, 0, 1) : Vector4.One), StandardColorSpaces.AcesCg));
        textured.Surface.Graph = new(new Dictionary<OpenPbrInput, OpenPbrNode>
        {
            [OpenPbrInput.EmissionColor] = OpenPbrNode.Image(checker, OpenPbrNodeType.Color3, OpenPbrNode.Texcoord(1)),
        });
        float viewHalf = 2 * MathF.Tan(camera.FieldOfViewRadians * .5f);
        quad.Geometry = QuadUv1(viewHalf, texelCenter, Enumerable.Repeat(texelCenter, 4));
        scene.Add(quad);
        Vector4 texel = OpenPbrGraphEvaluator.Evaluate(textured.Surface.Graph!, OpenPbrInput.EmissionColor,
            texelCenter, texelCenter, Vector3.UnitZ, new(1, 0, 0, 1));
        Compare(await Render(), new(texel.X * 4, texel.Y * 4, texel.Z * 4, 1), .05f,
            "fast constant UV1 preserves an individual checker texel");
        Vector2[] varyingUv1 = [texelCenter, texelCenter + Vector2.UnitX, texelCenter + Vector2.One, texelCenter + Vector2.UnitY];
        quad.Geometry = QuadUv1(viewHalf, texelCenter, varyingUv1);
        Compare(await Render(), new(2, 2, 2, 1), .05f,
            "fast UV1 minification uses UV1 gradients and the baked mip chain");

        // Background and textured foreground share fragment quads at this silhouette.
        // Gradient evaluation must remain valid when neighboring background lanes return.
        quad.Geometry = QuadUv1(viewHalf * .5f, texelCenter, varyingUv1);
        pass.BackgroundAlpha = .25f;
        pass.EnvironmentRadiance = new(8, 2, 1, 1, StandardColorSpaces.AcesCg);
        Vector4[] silhouette = await Render();
        for (int y = 0; y < 4; y++)
        for (int x = 0; x < 4; x++)
        {
            bool foreground = x is 1 or 2 && y is 1 or 2;
            Vector4 expected = foreground ? new(2, 2, 2, 1) : new(2, .5f, .25f, .25f);
            Expect(Vector4.Distance(silhouette[y * 4 + x], expected) < (foreground ? .05f : 1e-5f),
                $"fast UV1 textured silhouette / HDR background at ({x},{y}): {silhouette[y * 4 + x]} vs {expected}");
        }
        pass.BackgroundAlpha = 0;
        pass.EnvironmentRadiance = new(0, 0, 0, 1, StandardColorSpaces.AcesCg);
        scene.Remove(quad);

        pass.MaximumTextureBytes = 1;
        scene.Add(quad);
        ExpectThrows<InvalidOperationException>(() => pass.Execute(Context()), "fast bake obeys MaximumTextureBytes");
        pass.MaximumTextureBytes = 64L * 1024 * 1024;
        scene.Remove(quad);
        pass.Mode = OpenPbrRenderMode.Reference;
        await Render();
        Expect(pass.ReferenceSamples == 1, "switching back to reference restores its own pipeline");
        return checks;

        void Expect(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("OpenPBR fast: " + label);
            checks++;
        }
        void ExpectThrows<T>(Action action, string label) where T : Exception
        {
            bool rejected = false;
            try { action(); } catch (T) { rejected = true; }
            Expect(rejected, label);
        }
        void Compare(Vector4[] actual, Vector4 expected, float tolerance, string label)
        {
            foreach (Vector4 pixel in actual)
            for (int c = 0; c < 4; c++) Expect(float.IsFinite(pixel[c]) && MathF.Abs(pixel[c] - expected[c]) <= tolerance,
                $"{label}: channel {c}, {pixel[c]:G9} vs {expected[c]:G9}");
        }
    }
    private static MeshGeometry Quad(float half) => new(
        [new(-half, -half, 0), new(half, -half, 0), new(half, half, 0), new(-half, half, 0)], [0, 1, 2, 0, 2, 3]);
    private static MeshGeometry QuadUv(float half) => MeshGeometry.CreateWithTextureCoordinateSets(
        [new(-half, -half, 0), new(half, -half, 0), new(half, half, 0), new(-half, half, 0)], [0, 1, 2, 0, 2, 3],
        normals: Enumerable.Repeat(Vector3.UnitZ, 4),
        textureCoordinates: new[] { new Vector2(0, 0), new(1, 0), new(1, 1), new(0, 1) },
        textureCoordinates1: Enumerable.Repeat(Vector2.Zero, 4),
        tangents: Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4));
    private static MeshGeometry QuadUv1(float half, Vector2 uv0, IEnumerable<Vector2> uv1) => MeshGeometry.CreateWithTextureCoordinateSets(
        [new(-half, -half, 0), new(half, -half, 0), new(half, half, 0), new(-half, half, 0)], [0, 1, 2, 0, 2, 3],
        normals: Enumerable.Repeat(Vector3.UnitZ, 4), textureCoordinates: Enumerable.Repeat(uv0, 4),
        textureCoordinates1: uv1, tangents: Enumerable.Repeat(new Vector4(1, 0, 0, 1), 4));
}

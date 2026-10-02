using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class RasterHybridChecks
{
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        int checks = 0;
        using GraphicsTexture target = device.CreateTexture(new(new(4, 4), GraphicsTextureFormat.Rgba32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        Scene scene = new();
        PerspectiveCamera camera = new() { FieldOfViewRadians = .05f };
        camera.Transform.Position = new(0, 0, 2);
        using OpenPbrRenderPass pass = new(StandardColorSpaces.AcesCg) { Mode = OpenPbrRenderMode.Raster };
        RenderPassContext Context() => new(scene, camera, target, null, StandardColorSpaces.AcesCg);
        async Task<Vector4[]> Render()
        {
            pass.Execute(Context());
            return await TransportChecks.ReadAsync(device, target);
        }
        Expect((int)OpenPbrRenderMode.Interactive == 0 && (int)OpenPbrRenderMode.Reference == 1 &&
            (int)OpenPbrRenderMode.Raster == 2 && (int)OpenPbrRenderMode.Hybrid == 3 && (int)OpenPbrRenderMode.Fast == 4, "append-only mode values");
        pass.BackgroundAlpha = .25f;
        pass.EnvironmentRadiance = new(8, 2, 1, 1, StandardColorSpaces.AcesCg);
        Compare(await Render(), new(2, .5f, .25f, .25f), 1e-6f, "raster background keeps HDR and premultiplied alpha");
        Expect(pass.AccumulatedSamples == 0 && pass.ReferenceSamples == 0, "raster does not accumulate");
        pass.BackgroundAlpha = 0;
        pass.EnvironmentRadiance = new(0, 0, 0, 1, StandardColorSpaces.AcesCg);
        OpenPbrMaterial emission = new(new() { BaseWeight = 0, SpecularWeight = 0, EmissionLuminance = 8,
            EmissionColor = new(1, .5f, .25f, 1, StandardColorSpaces.AcesCg) }) { NitsPerSceneUnit = 2 };
        Mesh front = new(Quad(20), emission);
        OpenPbrMaterial black = new(new() { BaseWeight = 0, SpecularWeight = 0 });
        Mesh back = new(Quad(20), black);
        back.Transform.Position = new(0, 0, -1);
        // Far geometry can be submitted after near geometry; only actual depth testing may choose the front.
        scene.Add(front); scene.Add(back);
        Compare(await Render(), new(4, 2, 1, 1), 1e-6f, "raster depth selects closest emissive triangle");
        scene.Remove(front); scene.Add(front);
        Compare(await Render(), new(4, 2, 1, 1), 1e-6f, "depth is independent of submission order");
        scene.Remove(back);
        camera.NearClip = 3;
        Compare(await Render(), Vector4.Zero, 1e-6f, "raster applies ordinary near clipping");
        pass.Mode = OpenPbrRenderMode.Hybrid;
        Vector4[] boundary = await Render();
        Compare(boundary, new(4, 2, 1, 1), 1e-6f, "hybrid retains boundary before near plane");
        Expect(pass.UsesPrimaryRayFallback && pass.AccumulatedSamples == 1 && pass.ReferenceSamples == 0,
            "hybrid exposes primary-ray fallback and its independent sample counter");
        camera.NearClip = .1f;
        Compare(await Render(), new(4, 2, 1, 1), 1e-6f, "hybrid raster primary emission");
        Expect(!pass.UsesPrimaryRayFallback && pass.AccumulatedSamples == 1, "safe primary visibility uses raster and camera edit resets");
        await Render(); Expect(pass.AccumulatedSamples == 2, "hybrid retains unchanged history");
        emission.Surface.EmissionLuminance = 4;
        Compare(await Render(), new(2, 1, .5f, 1), 1e-6f, "hybrid live material update");
        Expect(pass.AccumulatedSamples == 1, "hybrid material changes reset history");
        pass.HybridMaxBounces = 5; await Render();
        Expect(pass.AccumulatedSamples == 1, "hybrid event budget changes reset history");
        pass.ResetAccumulation(); await Render();
        Expect(pass.AccumulatedSamples == 1, "explicit hybrid reset");
        camera.FarClip = 1;
        Compare(await Render(), Vector4.Zero, 1e-6f, "raster-primary hybrid far clipping");
        camera.FarClip = 1000;
        scene.Remove(front);

        // Analytic Lambertian radiance validates the real BSDF, not a preview material conversion.
        OpenPbrMaterial diffuse = new(new() { BaseColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg),
            SpecularWeight = 0, SpecularIor = 1, BaseDiffuseRoughness = 0 });
        Mesh receiver = new(Quad(20), diffuse); scene.Add(receiver);
        DirectionalLight sun = new(new(1, 1, 1, 1, StandardColorSpaces.AcesCg), 4) { AngularDiameterRadians = 0 };
        scene.Add(sun);
        pass.Mode = OpenPbrRenderMode.Raster;
        Compare(await Render(), new(4 / MathF.PI, 4 / MathF.PI, 4 / MathF.PI, 1), 2e-5f, "raster Lambert directional lighting");
        sun.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        Mesh occluder = new(Quad(.4f), black); occluder.Transform.Position = new(1, 0, 1); scene.Add(occluder);
        float unshadowed = 4 * MathF.Sqrt(.5f) / MathF.PI;
        Compare(await Render(), new(unshadowed, unshadowed, unshadowed, 1), 2e-5f, "raster is explicitly unshadowed");
        pass.Mode = OpenPbrRenderMode.Hybrid;
        Compare(await Render(), new(0, 0, 0, 1), 1e-6f, "hybrid traces offscreen shadow geometry");
        Expect(!pass.UsesPrimaryRayFallback, "shadow scene actually uses raster primary hits");
        scene.Remove(occluder);
        Compare(await Render(), new(unshadowed, unshadowed, unshadowed, 1), 2e-5f, "hybrid geometry changes restore illumination");
        Expect(pass.AccumulatedSamples == 1, "hybrid geometry edits reset");
        scene.Remove(sun);
        pass.EnvironmentRadiance = new(1, 1, 1, 1, StandardColorSpaces.AcesCg);
        pass.Mode = OpenPbrRenderMode.Raster;
        Vector4[] environment = await Render();
        Compare(environment, Vector4.One, 2e-4f, "raster deterministic diffuse white furnace");
        Vector4[] repeated = await Render();
        Expect(environment.SequenceEqual(repeated), "raster environment has no temporal noise");
        pass.Seed = 123;
        repeated = await Render();
        Expect(environment.SequenceEqual(repeated), "raster quadrature does not depend on random seed");
        pass.Seed = 1;

        foreach (Action<OpenPbrSurface> unsupported in new Action<OpenPbrSurface>[] {
            s => s.TransmissionWeight = .5f, s => s.SubsurfaceWeight = .5f, s => s.GeometryOpacity = .5f })
        {
            unsupported(diffuse.Surface);
            ExpectThrows<NotSupportedException>(() => pass.Execute(Context()), "raster rejects unsupported transport");
            diffuse.Surface.TransmissionWeight = 0; diffuse.Surface.SubsurfaceWeight = 0; diffuse.Surface.GeometryOpacity = 1;
        }
        pass.MaximumHistoryBytes = 4 * 4 * 32;
        ExpectThrows<InvalidOperationException>(() => pass.Execute(Context()), "raster visibility and depth count toward budget");
        pass.MaximumHistoryBytes = 512L * 1024 * 1024;
        pass.RasterEnvironmentSamples = 0;
        ExpectThrows<ArgumentOutOfRangeException>(() => pass.Execute(Context()), "zero quadrature samples rejected");
        pass.RasterEnvironmentSamples = 16;
        await Render();

        // With a delta light and black environment, one-event reference and raster must evaluate
        // the same complex closure. A narrow camera bounds jitter differences, independent of raster barycentrics.
        pass.EnvironmentRadiance = new(0, 0, 0, 1, StandardColorSpaces.AcesCg);
        scene.Add(sun); camera.FieldOfViewRadians = .0001f;
        using (OpenPbrRenderPass reference = new(StandardColorSpaces.AcesCg)
            { Mode = OpenPbrRenderMode.Reference, ReferenceMaxBounces = 1 })
        {
            foreach (OpenPbrSurface surface in new OpenPbrSurface[] {
                new() { BaseMetalness = .7f, SpecularRoughness = .3f, SpecularRoughnessAnisotropy = .6f,
                    CoatWeight = .8f, CoatRoughness = .2f, ThinFilmWeight = 1, ThinFilmThickness = .45f },
                new() { BaseDiffuseRoughness = .7f, FuzzWeight = .8f, FuzzRoughness = .5f, CoatWeight = .3f } })
            {
                diffuse.Surface = surface;
                Vector4[] rasterValues = await Render();
                reference.Execute(Context());
                Vector4[] referenceValues = await TransportChecks.ReadAsync(device, target);
                for (int i = 0; i < rasterValues.Length; i++)
                    Expect(Vector4.Distance(rasterValues[i], referenceValues[i]) < .001f && rasterValues[i].Y > .01f,
                        "raster preserves pinned metal/coat/anisotropy/thin-film/fuzz direct closure");
            }
        }
        scene.Remove(sun); camera.FieldOfViewRadians = .05f;
        diffuse.Surface = new() { BaseColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg), SpecularWeight = 0,
            SpecularIor = 1, BaseDiffuseRoughness = 0 };
        pass.EnvironmentRadiance = new(1, 1, 1, 1, StandardColorSpaces.AcesCg);

        // Progressive hybrid estimates must be arithmetic means of the same independent samples.
        pass.Mode = OpenPbrRenderMode.Hybrid;
        sun.AngularDiameterRadians = .6f; scene.Add(sun);
        Vector4[] first = await Render(); await Render();
        Vector4[] mean = await TransportChecks.ReadAsync(device, target);
        pass.ResetAccumulation(); pass.Seed = 2; // RNG hash(sampleIndex + seed): seed 2 / sample 0 == seed 1 / sample 1.
        Vector4[] second = await Render();
        Expect(first.Zip(second).Any(p => Vector4.Distance(p.First, p.Second) > .001f), "hybrid secondary estimates vary");
        for (int i = 0; i < first.Length; i++)
            Expect(Vector4.Distance(mean[i], (first[i] + second[i]) / 2) < 3e-5f, "hybrid history is the independent-sample mean");
        scene.Remove(sun); scene.Remove(receiver); pass.Seed = 1;

        // A mirror must see the environment even when primary background coverage is zero.
        OpenPbrMaterial mirror = new(new() { BaseMetalness = 1, SpecularRoughness = 0,
            BaseColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg) });
        Mesh mirrorMesh = new(Quad(20), mirror); scene.Add(mirrorMesh);
        pass.EnvironmentRadiance = new(4, 2, 1, 1, StandardColorSpaces.AcesCg);
        Compare(await Render(), new(4, 2, 1, 1), .005f, "hybrid secondary mirror reflection retains HDR");
        scene.Remove(mirrorMesh);

        // Zero-opacity first hit must continue through the BVH rather than stop at the visibility buffer.
        emission.Surface.EmissionLuminance = 8; scene.Add(front);
        OpenPbrMaterial cutout = new(new() { GeometryOpacity = 0 });
        Mesh transparent = new(Quad(20), cutout); transparent.Transform.Position = new(0, 0, 1); scene.Add(transparent);
        Compare(await Render(), new(4, 2, 1, 1), 1e-6f, "hybrid opacity cutout continues behind raster primary");
        scene.Remove(transparent); scene.Remove(front);

        OpenPbrMaterial glass = new(new() { TransmissionWeight = 1, SpecularIor = 1, SpecularWeight = 0,
            SpecularRoughness = 0, TransmissionDepth = 1,
            TransmissionColor = new(.5f, .5f, .5f, 1, StandardColorSpaces.AcesCg) });
        Mesh volume = new(TransportChecks.Box(), glass); scene.Add(volume);
        camera.Transform.Position = new(0, 0, 3); pass.HybridMaxBounces = 32; pass.BackgroundAlpha = 1;
        pass.EnvironmentRadiance = new(1, 1, 1, 1, StandardColorSpaces.AcesCg);
        for (int i = 0; i < 96; i++) pass.Execute(Context());
        Vector4[] transmitted = await TransportChecks.ReadAsync(device, target);
        Expect(!pass.UsesPrimaryRayFallback && MathF.Abs(transmitted.Average(v => v.Y) - .25f) < .055f && transmitted.All(v => v.W == 1),
            "hybrid raster entry and BVH exit obey two-unit Beer absorption");
        camera.Transform.Position = Vector3.Zero;
        for (int i = 0; i < 96; i++) pass.Execute(Context());
        transmitted = await TransportChecks.ReadAsync(device, target);
        Expect(pass.UsesPrimaryRayFallback && MathF.Abs(transmitted.Average(v => v.Y) - .5f) < .065f && transmitted.All(v => v.W == 1),
            "hybrid camera-inside fallback obeys one-unit Beer absorption");
        scene.Remove(volume);
        pass.Mode = OpenPbrRenderMode.Reference; await Render();
        Expect(pass.ReferenceSamples == 1 && pass.AccumulatedSamples == 1 && !pass.UsesPrimaryRayFallback,
            "switching to reference restores its own history and resource layout");
        pass.Mode = OpenPbrRenderMode.Interactive; await Render();
        Expect(pass.AccumulatedSamples == 0, "interactive clears hybrid/reference history");
        return checks;

        void Expect(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("OpenPBR raster/hybrid: " + label);
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
        [new(-half,-half,0),new(half,-half,0),new(half,half,0),new(-half,half,0)], [0,1,2,0,2,3]);
}

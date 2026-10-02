using System.Numerics;
using Mu3D.Color;
using Mu3D.GalleryApp.Pages;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class FurnaceChecks
{
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        int checks = 0;
        using var target = device.CreateTexture(new(new(16, 16), GraphicsTextureFormat.Rgba32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        Scene scene = new();
        PerspectiveCamera camera = new() { FieldOfViewRadians = 40 * MathF.PI / 180, AspectRatio = 1 };
        camera.Transform.Position = new(0, 0, 4);
        byte[] fixture = new byte[256 * 16];
        for (int i = 0; i < 256; i++)
        {
            BitConverter.GetBytes(.5f).CopyTo(fixture, i * 16);
            BitConverter.GetBytes(.75f).CopyTo(fixture, i * 16 + 4);
            BitConverter.GetBytes(1f).CopyTo(fixture, i * 16 + 8);
        }
        BitConverter.GetBytes(float.NaN).CopyTo(fixture, 0); // Background must not enter statistics.
        Matrix4x4.Invert(camera.ViewProjectionMatrix, out var inverse);
        var analytic = FurnaceMeasurement.Analyze(fixture, new(16, 16), 256, inverse, new(0, 0, 4), 1, 1,
            OpenPbrRenderMode.Reference, 128);
        Expect(analytic.Mean == new Vector3(.5f, .75f, 1) && analytic.Minimum == .5f && analytic.Maximum == 1 &&
            Math.Abs(analytic.Rms - Math.Sqrt(.3125 / 3)) < 1e-12, "analytical mean/range/RMS excludes invalid background");
        var material = new OpenPbrMaterial(FurnaceMaterials.Create(0, 0, 0));
        scene.Add(new Mesh(MeshPrimitives.CreateUvSphere(), material));
        using var furnace = new OpenPbrFurnaceRenderer();
        furnace.Transport.Mode = OpenPbrRenderMode.Raster;
        RenderPassContext Context() => new(scene, camera, target, null, StandardColorSpaces.LinearSrgb);
        async Task<FurnaceMeasurement> Measure()
        {
            var task = furnace.MeasureNextFrameAsync(); furnace.Execute(Context());
            return await task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        var white = await Measure();
        Console.WriteLine($"Furnace white sphere: {white}");
        Expect(white.Pixels > 0 && white.Pixels < 256, "measurement excludes the background");
        // Fixed quadrature and smooth-versus-geometric normals have integration error on this
        // tessellated sphere. Test the near-unit baseline, not an exact per-pixel furnace certificate.
        Expect(Vector3.Distance(white.Mean, Vector3.One) < .001f && white.Rms < .005,
            "raster white sphere has near-unit mean and bounded quadrature error");
        Expect(white.Mode == OpenPbrRenderMode.Raster && white.Samples == 0, "measurement records mode and accumulation");
        var raw = await TransportChecks.ReadAsync(device, target);
        Expect(Math.Abs(raw[0].X - 1) < .001f && Math.Abs(raw[8 * 16 + 8].X - 1) < .001f,
            "white sphere center matches the white background");

        material.Surface = FurnaceMaterials.Create(6, .8f, .7f);
        furnace.ExpectedRatio = .5f;
        var gray = await Measure();
        Expect(Vector3.Distance(gray.Mean, white.Mean * .5f) < .0002f && Math.Abs(gray.Rms - white.Rms * .5) < .0002,
            "gray control scales the same quadrature to 0.5 instead of falsely requiring unit energy");
        furnace.EnvironmentLevel = 8;
        var hdr = await Measure();
        Expect(Vector3.Distance(gray.Mean, hdr.Mean) < .0002f, "energy ratio is invariant under HDR illumination scaling");
        raw = await TransportChecks.ReadAsync(device, target);
        Expect(raw.Any(p => p.X > 7.9f) && raw.Any(p => Math.Abs(p.X - 4) < .001f),
            "raw HDR view preserves environment 8 and gray sphere radiance 4");

        furnace.DisplayMode = 2;
        var falseColor = await Measure();
        Expect(Vector3.Distance(falseColor.Mean, hdr.Mean) < .0002f, "measurement ignores the diagnostic display transform");
        var diagnostic = await TransportChecks.ReadAsync(device, target);
        Vector4 center = diagnostic[8 * 16 + 8];
        Expect(Vector3.Distance(new(center.X, center.Y, center.Z), new(.18f)) < .002f,
            "zero target error is neutral gray in the difference view");
        furnace.ExpectedRatio = 1;
        await Measure(); diagnostic = await TransportChecks.ReadAsync(device, target); center = diagnostic[8 * 16 + 8];
        Expect(center.Z > center.X + .5f, "loss against a unit target appears blue");
        furnace.ExpectedRatio = .25f;
        await Measure(); diagnostic = await TransportChecks.ReadAsync(device, target); center = diagnostic[8 * 16 + 8];
        Expect(center.X > center.Z + .5f, "gain against a lower target appears red");

        furnace.ExpectedRatio = 1; furnace.EnvironmentLevel = 1; furnace.DisplayMode = 0;
        material.Surface = FurnaceMaterials.Create(0, 0, 0);
        furnace.Transport.Mode = OpenPbrRenderMode.Reference;
        var first = await Measure();
        furnace.DisplayMode = 1;
        var second = await Measure();
        Expect(first.Samples == 1 && second.Samples == 2, "display changes retain progressive history");
        Expect(Vector3.Distance(second.Mean, Vector3.One) < .05f, "early reference estimate remains near unit energy with sampling noise");
        for (int preset = 1; preset <= 5; preset++)
        {
            material.Surface = FurnaceMaterials.Create(preset, .3f, .2f);
            FurnaceMeasurement sample = await Measure();
            Expect(sample.Samples == 1 && float.IsFinite(sample.Mean.X) && sample.Minimum >= 0 && sample.Pixels == white.Pixels,
                $"preset {preset} executes and records the reset without pretending one sample is conformance");
        }
        // Fast joins the furnace with its own explicit error budget: split-sum IBL and baked graphs
        // instead of quadrature. Values are recorded, not forced to a unit pass.
        furnace.ExpectedRatio = 1; furnace.EnvironmentLevel = 1; furnace.DisplayMode = 0;
        material.Surface = FurnaceMaterials.Create(0, 0, 0);
        furnace.Transport.Mode = OpenPbrRenderMode.Fast;
        FurnaceMeasurement fastWhite = await Measure();
        Console.WriteLine($"Furnace white sphere (Fast): {fastWhite}");
        Expect(fastWhite.Mode == OpenPbrRenderMode.Fast && fastWhite.Samples == 0, "fast furnace records mode and no accumulation");
        // Constant-environment diffuse through the SH chain is near-exact; tighter than the quadrature budget.
        Expect(Vector3.Distance(fastWhite.Mean, Vector3.One) < .001f && fastWhite.Rms < .005,
            "fast diffuse white sphere has near-unit mean");
        foreach (int preset in new[] { 1, 2, 3 })
        {
            material.Surface = FurnaceMaterials.Create(preset, .3f, .2f);
            FurnaceMeasurement sample = await Measure();
            Console.WriteLine($"Furnace preset {preset} (Fast): mean {sample.Mean} RMS {sample.Rms:F4}");
            Expect(float.IsFinite(sample.Mean.X) && sample.Minimum >= 0 && sample.Pixels == fastWhite.Pixels &&
                Vector3.Distance(sample.Mean, Vector3.One) < .2f,
                $"fast preset {preset} split-sum error stays inside its recorded budget");
        }
        material.Surface = FurnaceMaterials.Create(5, .3f, .2f);
        FurnaceMeasurement subsurface = await Measure();
        Expect(float.IsFinite(subsurface.Mean.X) &&
            furnace.Transport.FastApproximations.Any(a => a.Kinds.HasFlag(OpenPbrFastApproximationKinds.SubsurfaceDropped)),
            "fast furnace drops subsurface and reports it");
        using var fastGlass = new OpenPbrFurnaceRenderer();
        fastGlass.Transport.Mode = OpenPbrRenderMode.Fast;
        material.Surface = FurnaceMaterials.Create(4, .3f, 0);
        Task<FurnaceMeasurement> fastRejected = fastGlass.MeasureNextFrameAsync();
        try { fastGlass.Execute(Context()); } catch (NotSupportedException) { }
        bool fastFailedExplicitly = false;
        try { await fastRejected.WaitAsync(TimeSpan.FromSeconds(5)); } catch (NotSupportedException) { fastFailedExplicitly = true; }
        Expect(fastFailedExplicitly, "fast furnace rejects glass with the same explicit cause as raster");
        furnace.Transport.Mode = OpenPbrRenderMode.Reference;
        material.Surface = FurnaceMaterials.Create(0, 0, 0);

        // Faults/cancellation complete pending requests instead of leaving a disabled measurement button.
        using var queued = new OpenPbrFurnaceRenderer();
        Task<FurnaceMeasurement> canceled = queued.MeasureNextFrameAsync();
        bool duplicateRejected = false;
        try { _ = queued.MeasureNextFrameAsync(); } catch (InvalidOperationException) { duplicateRejected = true; }
        Expect(duplicateRejected, "duplicate in-flight measurement is rejected");
        queued.Dispose();
        Expect(canceled.IsCanceled, "page disposal cancels an unsubmitted measurement");
        using var invalid = new OpenPbrFurnaceRenderer();
        invalid.Transport.Mode = OpenPbrRenderMode.Raster;
        material.Surface = FurnaceMaterials.Create(4, .3f, 0);
        Task<FurnaceMeasurement> failed = invalid.MeasureNextFrameAsync();
        try { invalid.Execute(Context()); } catch (NotSupportedException) { }
        bool failedExplicitly = false;
        try { await failed.WaitAsync(TimeSpan.FromSeconds(5)); } catch (NotSupportedException) { failedExplicitly = true; }
        Expect(failedExplicitly, "render failure completes the waiting measurement with its cause");
        using var sdr = device.CreateTexture(new(new(16, 16), GraphicsTextureFormat.Rgba8Unorm,
            GraphicsTextureUsage.RenderAttachment));
        bool rejectedSdr = false;
        try { furnace.Execute(new(scene, camera, sdr, null, StandardColorSpaces.LinearSrgb)); }
        catch (NotSupportedException) { rejectedSdr = true; }
        Expect(rejectedSdr, "the raw HDR probe cannot silently clip to an SDR target");
        return checks;
        void Expect(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("Furnace: " + label);
            checks++;
        }
    }
}

using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class TransportChecks
{
    // Exercises the public render pass and its generated scene shader without a window or surface.
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        int checks = 0;
        using GraphicsTexture target = device.CreateTexture(new(new(4, 4), GraphicsTextureFormat.Rgba32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        Scene scene = new();
        PerspectiveCamera camera = new();
        camera.Transform.Position = new(0, 0, 2);
        using OpenPbrRenderPass pass = new() { Mode = OpenPbrRenderMode.Reference, BackgroundAlpha = .25f,
            EnvironmentRadiance = new(8, 2, 1, 1, StandardColorSpaces.AcesCg) };
        RenderPassContext Context() => new(scene, camera, target, null, StandardColorSpaces.LinearSrgb);
        Console.WriteLine("Compiling and executing the OpenPBR scene transport shader on the native device...");
        pass.Execute(Context());
        Vector4 expected = Convert(new(8, 2, 1), .25f);
        CompareImage(await ReadAsync(device, target), expected, 5e-5f, "empty HDR background / premultiplication / color conversion");
        Expect(pass.ReferenceSamples == 1, "First reference frame records one sample");
        pass.Execute(Context());
        CompareImage(await ReadAsync(device, target), expected, 5e-5f, "stationary HDR accumulation");
        Expect(pass.ReferenceSamples == 2, "A stationary frame retains reference history");
        Console.WriteLine("OpenPBR scene transport native smoke passed.");

        pass.BackgroundAlpha = 0;
        pass.Execute(Context());
        CompareImage(await ReadAsync(device, target), Vector4.Zero, 1e-6f, "transparent empty background");
        Expect(pass.ReferenceSamples == 1, "Changing background coverage resets history");
        pass.EnvironmentRadiance = new(0, 0, 0, 1, StandardColorSpaces.AcesCg);
        OpenPbrMaterial emissive = new(new() { BaseWeight = 0, SpecularWeight = 0, EmissionLuminance = 10,
            EmissionColor = new(.4f, .8f, .2f, 1, StandardColorSpaces.AcesCg) }) { NitsPerSceneUnit = 2 };
        Mesh quad = new(new MeshGeometry([new(-20,-20,0),new(20,-20,0),new(20,20,0),new(-20,20,0)],
            [0,1,2,0,2,3]), emissive);
        scene.Add(quad);
        pass.Execute(Context());
        CompareImage(await ReadAsync(device, target), Convert(new(2, 4, 1), 1), 5e-5f, "emission nits / scene radiance");
        emissive.Surface.EmissionLuminance = 20;
        pass.Execute(Context());
        CompareImage(await ReadAsync(device, target), Convert(new(4, 8, 2), 1), 8e-5f, "live material edit");
        Expect(pass.ReferenceSamples == 1, "Material edits discard reference history");
        pass.Execute(Context());
        Expect(pass.ReferenceSamples == 2, "Unchanged material retains reference history");
        camera.Transform.Position = new(.1f, 0, 2);
        pass.Execute(Context());
        Expect(pass.ReferenceSamples == 1, "Camera movement discards reference history");
        pass.ResetAccumulation(); pass.Execute(Context());
        Expect(pass.ReferenceSamples == 1, "Explicit reset starts a new reference estimate");

        // Geometry is already within the near plane: transport must still see its boundary.
        camera.NearClip = 3;
        pass.Execute(Context());
        CompareImage(await ReadAsync(device, target), Convert(new(4, 8, 2), 1), 8e-5f, "transport starts at camera before near plane");
        camera.NearClip = .1f;
        camera.FieldOfViewRadians = .01f;
        quad.Transform.Position = new(0, 0, -950);
        pass.Execute(Context());
        CompareImage(await ReadAsync(device, target), Convert(new(4, 8, 2), 1), 8e-5f,
            "primary far plane remains at its authored distance despite depth unprojection precision");
        camera.FarClip = 900;
        pass.Execute(Context());
        CompareImage(await ReadAsync(device, target), Vector4.Zero, 1e-6f, "primary geometry beyond the far plane is excluded");
        camera.FarClip = 1000; camera.FieldOfViewRadians = MathF.PI / 3;
        quad.Transform.Position = Vector3.Zero;
        scene.Remove(quad);

        await CheckHistoryAsync();
        await CheckVolumeAsync();
        await CheckSceneRecoveryAsync();
        return checks;

        async Task CheckHistoryAsync()
        {
            camera.Transform.Position = new(0, 0, 2);
            Vector3[] pixels = Enumerable.Range(0, 32).Select(i => new Vector3((i % 8) + .2f, (i / 8) + .3f, .4f)).ToArray();
            ImageBasedLight image = new(new(8, 4, pixels, StandardColorSpaces.AcesCg));
            scene.Add(image);
            pass.Mode = OpenPbrRenderMode.Interactive; pass.InteractiveResolutionScale = 1; pass.BackgroundAlpha = 1;
            pass.EnvironmentRadiance = new(.5f, .5f, .5f, 1, StandardColorSpaces.AcesCg);
            pass.Execute(Context()); Vector4[] sample0 = await ReadAsync(device, target);
            pass.Execute(Context()); Vector4[] sample1 = await ReadAsync(device, target);
            Expect(sample0.Zip(sample1).Any(pair => Vector4.Distance(pair.First, pair.Second) > .01f),
                "Interactive frames use changing samples without retaining reference history");
            Expect(pass.ReferenceSamples == 0, "Interactive frames do not accumulate reference samples");
            pass.Mode = OpenPbrRenderMode.Reference;
            pass.Execute(Context());
            CompareImages(await ReadAsync(device, target), sample0, 2e-5f, "reference initial sample equals interactive sample zero");
            pass.Execute(Context());
            CompareImages(await ReadAsync(device, target), sample0.Zip(sample1, (a, b) => (a + b) / 2).ToArray(),
                3e-5f, "FP32 history stores the arithmetic mean of independent path estimates");
            Expect(pass.ReferenceSamples == 2, "Two reference frames accumulate two samples");
            // The 1x1 image and constant term must add in the same linear working space.
            image.Environment = new(1, 1, [new Vector3(2, 1, .5f)], StandardColorSpaces.AcesCg);
            pass.Execute(Context());
            CompareImage(await ReadAsync(device, target), Convert(new(2.5f, 1.5f, 1), 1), 5e-5f, "image plus constant environment");
            scene.Remove(image);
        }

        async Task CheckVolumeAsync()
        {
            camera.Transform.Position = Vector3.Zero;
            camera.FieldOfViewRadians = .1f;
            pass.BackgroundAlpha = 1;
            pass.EnvironmentRadiance = new(1, 1, 1, 1, StandardColorSpaces.AcesCg);
            OpenPbrMaterial glass = new(new() { TransmissionWeight = 1, SpecularIor = 1, SpecularWeight = 0,
                SpecularRoughness = 0, TransmissionDepth = 1,
                TransmissionColor = new(.5f, .5f, .5f, 1, StandardColorSpaces.AcesCg) });
            Mesh volume = new(Box(), glass);
            scene.Add(volume);
            // At near-normal incidence the distance from the camera to the back face is one unit.
            // Absorption is sampled stochastically: 1,024 paths give a robust Beer-law smoke check.
            for (int i = 0; i < 64; i++) pass.Execute(Context());
            Vector4[] tinted = await ReadAsync(device, target);
            float transmission = tinted.Average(v => v.Y);
            Expect(float.IsFinite(transmission) && MathF.Abs(transmission - .5f) < .07f,
                $"Camera inside absorbing volume follows Beer transmittance (mean {transmission:G6})");
            Expect(tinted.All(v => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && v.W == 1),
                "Inside-volume absorption remains finite and preserves full coverage instead of creating transparent holes");
            glass.Surface.TransmissionColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg);
            pass.Execute(Context());
            CompareImage(await ReadAsync(device, target), Convert(Vector3.One, 1), .003f, "clear interior transmission");
            Expect(pass.ReferenceSamples == 1, "Interior material edits reset history and recompute medium properties");
            camera.Transform.Position = new(0, 0, 3);
            glass.Surface.TransmissionColor = new(.5f, .5f, .5f, 1, StandardColorSpaces.AcesCg);
            for (int i = 0; i < 64; i++) pass.Execute(Context());
            transmission = (await ReadAsync(device, target)).Average(v => v.Y);
            Expect(float.IsFinite(transmission) && MathF.Abs(transmission - .25f) < .07f,
                $"Entry and exit boundaries track a two-unit absorbing segment (mean {transmission:G6})");
            scene.Remove(volume);
        }

        async Task CheckSceneRecoveryAsync()
        {
            ImageBasedLight image = new(new(1, 1, [new Vector3(1)], StandardColorSpaces.AcesCg));
            scene.Add(image); pass.Execute(Context());
            image.Environment = new(1, 1, [new Vector3(-1)], StandardColorSpaces.AcesCg);
            scene.Add(quad);
            bool rejected = false;
            try { pass.Execute(Context()); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Expect(rejected, "Invalid physical environment data is rejected before replacing usable GPU resources");
            image.Environment = new(1, 1, [new Vector3(1)], StandardColorSpaces.AcesCg);
            pass.Execute(Context());
            CompareImage(await ReadAsync(device, target), Convert(new(4, 8, 2), 1), 8e-5f,
                "Failed scene upload can recover with the newly compiled geometry");
            scene.Remove(quad); scene.Remove(image);
        }

        void Expect(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("OpenPBR transport: " + label);
            checks++;
        }
        void CompareImage(Vector4[] actual, Vector4 expectedValue, float tolerance, string label) =>
            CompareImages(actual, Enumerable.Repeat(expectedValue, actual.Length).ToArray(), tolerance, label);
        void CompareImages(Vector4[] actual, Vector4[] expectedValues, float tolerance, string label)
        {
            for (int pixel = 0; pixel < actual.Length; pixel++)
            for (int channel = 0; channel < 4; channel++)
            {
                float value = actual[pixel][channel], reference = expectedValues[pixel][channel];
                Expect(float.IsFinite(value) && MathF.Abs(value - reference) <= tolerance,
                    $"{label}; pixel {pixel}, channel {channel}: {value:G9} versus {reference:G9}");
            }
        }
    }

    private static Vector4 Convert(Vector3 acesCg, float alpha)
    {
        LinearRgba rgb = StandardLinearRgbConverter.Convert(new(acesCg.X, acesCg.Y, acesCg.Z, alpha,
            StandardColorSpaces.AcesCg), StandardColorSpaces.LinearSrgb);
        return new(rgb.Red * alpha, rgb.Green * alpha, rgb.Blue * alpha, alpha);
    }

    internal static MeshGeometry Box() => new(
        [new(-1,-1,-1),new(1,-1,-1),new(1,1,-1),new(-1,1,-1),new(-1,-1,1),new(1,-1,1),new(1,1,1),new(-1,1,1)],
        [4,5,6,4,6,7,0,2,1,0,3,2,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5]);

    internal static async Task<Vector4[]> ReadAsync(WgpuGraphicsDevice device, GraphicsTexture texture)
    {
        uint width = texture.Descriptor.Size.Width, height = texture.Descriptor.Size.Height;
        using GraphicsBuffer buffer = device.CreateBuffer(new(256 * height, GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
        using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("OpenPBR transport verification readback");
        encoder.CopyTextureToBuffer(texture, 0, default, texture.Descriptor.Size, buffer, 0, 256, height);
        using GraphicsCommandBuffer commands = encoder.Finish(); device.Queue.Submit(commands);
        byte[] bytes = await buffer.ReadAsync(0, checked((int)height * 256)).WaitAsync(TimeSpan.FromSeconds(60));
        Vector4[] values = new Vector4[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = y * 256 + x * 16;
            values[y * width + x] = new(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4),
                BitConverter.ToSingle(bytes, offset + 8), BitConverter.ToSingle(bytes, offset + 12));
        }
        return values;
    }
}

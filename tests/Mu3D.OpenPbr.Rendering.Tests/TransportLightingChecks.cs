using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class TransportLightingChecks
{
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        int checks = 0;
        using GraphicsTexture target = device.CreateTexture(new(new(4, 4), GraphicsTextureFormat.Rgba32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        Scene scene = new();
        PerspectiveCamera camera = new() { FieldOfViewRadians = .05f };
        camera.Transform.Position = new(0, 0, 2);
        using OpenPbrRenderPass pass = new(StandardColorSpaces.AcesCg)
        {
            Mode = OpenPbrRenderMode.Reference,
            BackgroundAlpha = 1,
            EnvironmentRadiance = new(0, 0, 0, 1, StandardColorSpaces.AcesCg),
        };
        OpenPbrMaterial diffuse = new(new()
        {
            BaseColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg),
            BaseDiffuseRoughness = 0,
            SpecularWeight = 0,
            SpecularIor = 1,
        });
        Mesh receiver = new(Quad(20), diffuse);
        scene.Add(receiver);
        DirectionalLight sun = new(new(1, 1, 1, 1, StandardColorSpaces.AcesCg), 4)
        {
            AngularDiameterRadians = 0,
            CastsShadows = true,
        };
        scene.Add(sun);
        RenderPassContext Context() => new(scene, camera, target, null, StandardColorSpaces.AcesCg);

        pass.Execute(Context());
        CompareConstant(await ReadAsync(), 4 / MathF.PI, 2e-5f, "normal-incidence directional irradiance / pi");
        Expect(pass.ReferenceSamples == 1, "first diffuse light frame starts history");
        pass.Execute(Context());
        Expect(pass.ReferenceSamples == 2, "unchanged light preserves history");
        sun.Intensity = 2;
        pass.Execute(Context());
        CompareConstant(await ReadAsync(), 2 / MathF.PI, 2e-5f, "light intensity edits update the direct estimate");
        Expect(pass.ReferenceSamples == 1, "light intensity edits invalidate reference history");

        sun.Intensity = 4;
        sun.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
        pass.Execute(Context());
        CompareConstant(await ReadAsync(), 4 * MathF.Sqrt(.5f) / MathF.PI, 2e-5f,
            "directional orientation contributes one incoming cosine");
        OpenPbrMaterial black = new(new() { BaseWeight = 0, SpecularWeight = 0 });
        Mesh occluder = new(Quad(.4f), black);
        occluder.Transform.Position = new(1, 0, 1);
        scene.Add(occluder);
        pass.Execute(Context());
        CompareConstant(await ReadAsync(), 0, 1e-6f, "off-camera geometry blocks the directional light path");
        Expect(pass.ReferenceSamples == 1, "adding the shadow occluder invalidates history");
        scene.Remove(occluder);
        pass.Execute(Context());
        CompareConstant(await ReadAsync(), 4 * MathF.Sqrt(.5f) / MathF.PI, 2e-5f,
            "removing shadow geometry restores direct illumination");

        // A finite disk preserves its specified normal irradiance. Light sampling and BSDF
        // escape sampling both see the same disk and combine with MIS rather than doubling it.
        sun.Transform.Rotation = Quaternion.Identity;
        sun.AngularDiameterRadians = .6f;
        for (int i = 0; i < 128; i++) pass.Execute(Context());
        Vector4[] finiteSun = await ReadAsync();
        float finiteSunMean = finiteSun.Average(value => value.Y);
        Expect(finiteSun.All(Finite) && MathF.Abs(finiteSunMean - 4 / MathF.PI) < .025f,
            $"finite emitter conserves directional irradiance with MIS (mean {finiteSunMean:G7}, expected {4 / MathF.PI:G7})");
        Expect(pass.ReferenceSamples == 128, "finite-emitter samples accumulate without accidental resets");
        Console.WriteLine($"OpenPBR finite-sun diffuse radiance: {finiteSunMean:G7}; analytic target {4 / MathF.PI:G7}.");
        scene.Remove(receiver);
        scene.Remove(sun);

        // Index-matched conservative homogeneous media in a unit white environment must return
        // unit radiance, including paths with internal scattering. These energy smoke tests are
        // intentionally separate from exact BSDF/volume fixtures and use stated Monte Carlo bounds.
        camera.Transform.Position = Vector3.Zero;
        pass.EnvironmentRadiance = new(1, 1, 1, 1, StandardColorSpaces.AcesCg);
        pass.ReferenceMaxBounces = 128;
        OpenPbrMaterial scattering = new(new()
        {
            TransmissionWeight = 1, SpecularWeight = 0, SpecularIor = 1, SpecularRoughness = 0,
            TransmissionDepth = 1,
            TransmissionColor = new(MathF.Exp(-.5f), MathF.Exp(-.5f), MathF.Exp(-.5f), 1, StandardColorSpaces.AcesCg),
            TransmissionScatter = new(.5f, .5f, .5f, 1, StandardColorSpaces.AcesCg),
            TransmissionScatterAnisotropy = .4f,
        });
        Mesh box = new(Box(), scattering);
        scene.Add(box);
        await CheckConservativeMedium("homogeneous forward scattering");
        scattering.Surface = new()
        {
            SubsurfaceWeight = 1,
            SubsurfaceColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg),
            SubsurfaceRadius = 2,
            SubsurfaceRadiusScale = Vector3.One,
            SubsurfaceScatterAnisotropy = -.3f,
            SpecularIor = 1,
            SpecularWeight = 0,
        };
        await CheckConservativeMedium("subsurface backward scattering");
        return checks;

        async Task CheckConservativeMedium(string name)
        {
            for (int i = 0; i < 128; i++) pass.Execute(Context());
            Vector4[] image = await ReadAsync();
            float mean = image.Average(value => value.Y);
            Expect(image.All(Finite), $"{name} remains finite");
            Expect(MathF.Abs(mean - 1) < .08f, $"{name} white-environment energy is near unity (mean {mean:G7})");
            Expect(image.All(value => MathF.Abs(value.W - 1) < 1e-6f), $"{name} has opaque primary coverage");
            Expect(pass.ReferenceSamples == 128, $"{name} resets then accumulates 128 samples");
            Console.WriteLine($"OpenPBR {name} white-environment mean: {mean:G7}; target 1, Monte Carlo tolerance 0.08.");
        }

        async Task<Vector4[]> ReadAsync()
        {
            using GraphicsBuffer readback = device.CreateBuffer(new(1024, GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
            using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("OpenPBR light transport verification");
            encoder.CopyTextureToBuffer(target, 0, default, target.Descriptor.Size, readback, 0, 256, 4);
            using GraphicsCommandBuffer commands = encoder.Finish();
            device.Queue.Submit(commands);
            byte[] bytes = await readback.ReadAsync(0, 1024).WaitAsync(TimeSpan.FromSeconds(30));
            Vector4[] values = new Vector4[16];
            for (int i = 0; i < values.Length; i++)
            {
                int offset = i / 4 * 256 + i % 4 * 16;
                values[i] = new(BitConverter.ToSingle(bytes, offset), BitConverter.ToSingle(bytes, offset + 4),
                    BitConverter.ToSingle(bytes, offset + 8), BitConverter.ToSingle(bytes, offset + 12));
            }
            return values;
        }

        void CompareConstant(Vector4[] image, float rgb, float tolerance, string label)
        {
            foreach (Vector4 value in image)
            for (int channel = 0; channel < 4; channel++)
            {
                float expected = channel == 3 ? 1 : rgb;
                Expect(float.IsFinite(value[channel]) && MathF.Abs(value[channel] - expected) <= tolerance,
                    $"{label}: channel {channel}, {value[channel]:G9} versus {expected:G9}");
            }
        }

        void Expect(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("OpenPBR lighting: " + label);
            checks++;
        }
    }

    private static bool Finite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static MeshGeometry Quad(float extent) => new(
        [new(-extent, -extent, 0), new(extent, -extent, 0), new(extent, extent, 0), new(-extent, extent, 0)],
        [0, 1, 2, 0, 2, 3]);

    private static MeshGeometry Box() => new(
        [new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1),
         new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)],
        [4, 5, 6, 4, 6, 7, 0, 2, 1, 0, 3, 2, 0, 1, 5, 0, 5, 4, 3, 7, 6, 3, 6, 2, 0, 4, 7, 0, 7, 3, 1, 2, 6, 1, 6, 5]);
}

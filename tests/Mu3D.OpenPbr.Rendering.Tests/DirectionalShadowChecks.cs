using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class DirectionalShadowChecks
{
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        int checks = 0;
        using var target = device.CreateTexture(new(new(16, 16), GraphicsTextureFormat.Rgba32Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        foreach (var mode in new[] { OpenPbrRenderMode.Raster, OpenPbrRenderMode.Fast })
        {
            Scene scene = new();
            PerspectiveCamera camera = new() { FieldOfViewRadians = .05f };
            camera.Transform.Position = new(0, 0, 2);
            OpenPbrMaterial white = new(new() { BaseColor = new(1, 1, 1, 1, StandardColorSpaces.AcesCg), SpecularWeight = 0, SpecularIor = 1 });
            OpenPbrMaterial black = new(new() { BaseWeight = 0, SpecularWeight = 0 });
            Mesh receiver = new(Quad(2), white);
            Mesh caster = new(Quad(.4f), black);
            caster.Transform.Position = new(1, 0, 1); // Offscreen, on the light path to the receiver.
            DirectionalLight sun = new(new(1, 1, 1, 1, StandardColorSpaces.AcesCg), 4)
            { AngularDiameterRadians = 0, CastsShadows = true };
            sun.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
            scene.Add(receiver); scene.Add(caster); scene.Add(sun);
            using OpenPbrRenderPass pass = new(StandardColorSpaces.AcesCg) { Mode = mode, DirectionalShadowMapSize = 256 };
            RenderPassContext Context() => new(scene, camera, target, null, StandardColorSpaces.AcesCg);
            async Task<Vector4[]> Render()
            {
                pass.Execute(Context());
                return await TransportChecks.ReadAsync(device, target);
            }
            float direct = 4 * MathF.Sqrt(.5f) / MathF.PI;
            Compare(await Render(), new(0, 0, 0, 1), .0001f, $"{mode} offscreen caster blocks direct light");
            pass.DirectionalShadowsEnabled = false;
            Compare(await Render(), new(direct, direct, direct, 1), .0001f, $"{mode} disable restores direct light");
            pass.DirectionalShadowsEnabled = true;
            sun.CastsShadows = false;
            Compare(await Render(), new(direct, direct, direct, 1), .0001f, $"{mode} light opt-out");
            sun.CastsShadows = true; sun.ShadowOpacity = .5f;
            Compare(await Render(), new(direct / 2, direct / 2, direct / 2, 1), .0001f, $"{mode} light shadow opacity");
            sun.ShadowOpacity = 1;
            caster.ShadowCastingMode = MeshShadowCastingMode.Off;
            Compare(await Render(), new(direct, direct, direct, 1), .0001f, $"{mode} live caster opt-out");
            caster.ShadowCastingMode = MeshShadowCastingMode.ShadowsOnly;
            Compare(await Render(), new(0, 0, 0, 1), .0001f, $"{mode} shadow-only mesh casts");
            pass.EnvironmentRadiance = new(.25f, .25f, .25f, 1, StandardColorSpaces.AcesCg);
            white.Surface.EmissionLuminance = 2; white.NitsPerSceneUnit = 2;
            Compare(await Render(), new(1.25f, 1.25f, 1.25f, 1), .0003f, $"{mode} shadow preserves environment and emission");
            // Only the selected directional light is shadowed, even with preceding point/directional lights.
            PointLight zeroPoint = new(new(1, 1, 1, 1, StandardColorSpaces.AcesCg), 0);
            DirectionalLight fill = new(new(1, 1, 1, 1, StandardColorSpaces.AcesCg), MathF.PI) { AngularDiameterRadians = 0 };
            scene.Remove(sun); scene.Add(zeroPoint); scene.Add(fill); scene.Add(sun);
            Compare(await Render(), new(2.25f, 2.25f, 2.25f, 1), .0004f, $"{mode} other lights remain unaffected");
            scene.Remove(zeroPoint); scene.Remove(fill);
            white.Surface.EmissionLuminance = 0; pass.EnvironmentRadiance = new(0, 0, 0, 1, StandardColorSpaces.AcesCg);
            caster.Transform.Position = new(3, 0, 1);
            Compare(await Render(), new(direct, direct, direct, 1), .0001f, $"{mode} moved caster restores light");
            caster.Transform.Position = new(1, 0, 1);
            pass.DirectionalShadowMapSize = 128;
            Compare(await Render(), new(0, 0, 0, 1), .0001f, $"{mode} resize refreshes map");
            pass.DirectionalShadowDepthBias = 1;
            Compare(await Render(), new(direct, direct, direct, 1), .0001f, $"{mode} depth bias is live");
            pass.DirectionalShadowDepthBias = .001f;
            pass.MaximumHistoryBytes = 16 * 16 * 52;
            Throws<InvalidOperationException>(() => pass.Execute(Context()), "shadow allocation obeys attachment budget");
            pass.MaximumHistoryBytes = 512L * 1024 * 1024;
            fill.CastsShadows = true; scene.Add(fill);
            Throws<NotSupportedException>(() => pass.Execute(Context()), "multiple shadow lights are explicitly rejected");
            scene.Remove(fill);

            // A visible shadow-only surface must not cover the receiver in the camera pass.
            caster.Transform.Position = new(0, 0, 1);
            sun.CastsShadows = false;
            Compare(await Render(), new(direct, direct, direct, 1), .0001f, $"{mode} shadow-only excludes camera visibility");
            caster.ShadowCastingMode = MeshShadowCastingMode.On;
            Compare(await Render(), new(0, 0, 0, 1), .0001f, $"{mode} restoring On restores camera visibility");

            // Move an offscreen caster edge across a wider receiver footprint. PCF must introduce
            // fractional visibility without stochastic time variation.
            caster.ShadowCastingMode = MeshShadowCastingMode.ShadowsOnly;
            caster.Transform.Position = new(1.4f, 0, 1);
            sun.CastsShadows = true; camera.FieldOfViewRadians = .3f;
            pass.DirectionalShadowMapSize = 64;
            pass.DirectionalShadowPcfRadius = 0;
            Vector4[] hard = await Render();
            pass.DirectionalShadowPcfRadius = 2;
            Vector4[] soft = await Render();
            Expect(soft.Any(p => p.X > direct * .02f && p.X < direct * .98f), $"{mode} PCF edge has fractional coverage");
            Expect(!hard.SequenceEqual(soft), $"{mode} PCF differs from hard comparison");
            Vector4[] repeated = await Render();
            Expect(soft.SequenceEqual(repeated), $"{mode} PCF is temporally deterministic");
            pass.DirectionalShadowNormalBias = float.NaN;
            Throws<ArgumentOutOfRangeException>(() => pass.Execute(Context()), "nonfinite bias rejected");
            pass.DirectionalShadowNormalBias = .002f; pass.DirectionalShadowMapSize = 65;
            Throws<ArgumentOutOfRangeException>(() => pass.Execute(Context()), "non-power-of-two resolution rejected");
        }
        return checks;

        void Expect(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
        void Compare(Vector4[] image, Vector4 expected, float tolerance, string message)
        {
            Expect(image.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && float.IsFinite(p.W)), message + " finite");
            float error = image.Max(p => Vector4.Abs(p - expected).Length());
            Expect(error <= tolerance, $"{message}: max error {error}, first {image[0]}, expected {expected}");
        }
        void Throws<T>(Action action, string message) where T : Exception
        {
            try { action(); } catch (T) { checks++; return; }
            throw new InvalidOperationException(message);
        }
    }

    private static MeshGeometry Quad(float half) => new(
        [new(-half, -half, 0), new(half, -half, 0), new(half, half, 0), new(-half, half, 0)],
        [0, 1, 2, 0, 2, 3], [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ]);
}

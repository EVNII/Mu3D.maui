using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class GraphRenderingChecks
{
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        int checks = 0;
        void Expect(bool condition, string name) { checks++; if (!condition) throw new InvalidOperationException(name); }
        using var target = device.CreateTexture(new(new(2, 2), GraphicsTextureFormat.Rgba32Float, GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        var camera = new PerspectiveCamera { FieldOfViewRadians = .01f }; camera.Transform.Position = new(0,0,2);
        var material = new OpenPbrMaterial(new() { BaseWeight = 0, SpecularWeight = 0, EmissionLuminance = 8 }) { NitsPerSceneUnit = 2 };
        var scene = new Scene(); var mesh = new Mesh(Quad(default, default), material); scene.Add(mesh);
        var texture = OpenPbrTexture.FromColor(new(2,2,[new(1,0,0,1),new(0,1,0,1),new(0,0,1,1),new(1,1,1,1)], StandardColorSpaces.AcesCg));
        var uv = OpenPbrNode.Add(OpenPbrNode.Multiply(OpenPbrNode.Texcoord(1), OpenPbrNode.Float(2)), OpenPbrNode.Vector2(new(-.5f,.25f)));
        var sample = OpenPbrNode.Image(texture, OpenPbrNodeType.Color3, uv, OpenPbrAddressMode.Mirror, OpenPbrAddressMode.Clamp);
        var color = OpenPbrNode.Clamp(OpenPbrNode.Mix(OpenPbrNode.Color(new(.1f,.2f,.3f,1,StandardColorSpaces.AcesCg)), sample, OpenPbrNode.Float(.75f)));
        material.Surface.Graph = new(new Dictionary<OpenPbrInput, OpenPbrNode> { [OpenPbrInput.EmissionColor] = color });
        using var pass = new OpenPbrRenderPass(StandardColorSpaces.AcesCg) { InteractiveResolutionScale = 1 };
        RenderPassContext Context() => new(scene, camera, target, null, StandardColorSpaces.AcesCg);
        async Task<Vector4[]> Render() { pass.Execute(Context()); return await TransportChecks.ReadAsync(device,target); }
        foreach (var mode in Enum.GetValues<OpenPbrRenderMode>().Where(m => m != OpenPbrRenderMode.Fast))
        {
            // Fast bakes the graph to a hardware-filtered texture instead of per-texel interpretation;
            // it is compared against the evaluator with bake-aware tolerances in FastModeChecks.
            pass.Mode = mode;
            foreach (Vector2 point in new Vector2[] { new(.125f,.25f), new(.6f,.65f), new(-.6f,1.2f) })
            {
                mesh.Geometry = Quad(default, point);
                Vector4 value = OpenPbrGraphEvaluator.Evaluate(material.Surface.Graph, OpenPbrInput.EmissionColor, default, point, Vector3.UnitZ, new(1,0,0,1));
                Vector4 expected = new(value.X * 4, value.Y * 4, value.Z * 4, 1);
                foreach (Vector4 pixel in await Render()) Expect(Vector4.Distance(pixel,expected) < 3e-5f, $"{mode} textured HDR emission matches CPU at {point}: {pixel} vs {expected}");
            }
        }
        pass.Mode = OpenPbrRenderMode.Reference;
        await Render(); await Render(); Expect(pass.AccumulatedSamples == 2, "Unchanged graph retains history.");
        material.Surface.Graph = new(new Dictionary<OpenPbrInput, OpenPbrNode> { [OpenPbrInput.EmissionColor] = OpenPbrNode.Color(new(.2f,.4f,.8f,1,StandardColorSpaces.AcesCg)) });
        material.Surface.EmissionColor = new(-3,-2,-1,1,StandardColorSpaces.AcesCg); // Inactive fallback must not override the graph.
        foreach (var pixel in await Render()) Expect(Vector4.Distance(pixel,new(.8f,1.6f,3.2f,1)) < 3e-5f, "Same-size graph replacement uploads new instructions.");
        Expect(pass.AccumulatedSamples == 1, "Graph replacement resets accumulation.");
        pass.MaximumTextureBytes = 1;
        try { await Render(); throw new Exception("Missing graph budget rejection"); } catch (InvalidOperationException) { checks++; }
        pass.MaximumTextureBytes = 64 * 1024 * 1024;
        await Render();
        // Normal maps exercise the world tangent frame and real BSDF, against analytic Lambertian light.
        material.Surface = new() { SpecularWeight = 0, BaseColor = new(1,1,1,1,StandardColorSpaces.AcesCg) };
        var normal = OpenPbrNode.NormalMap(OpenPbrNode.Vector3(new(.8f,.5f,.9f)));
        material.Surface.Graph = new(new Dictionary<OpenPbrInput, OpenPbrNode> { [OpenPbrInput.GeometryNormal] = normal });
        var sun = new DirectionalLight(new(1,1,1,1,StandardColorSpaces.AcesCg), MathF.PI) { AngularDiameterRadians = 0 }; scene.Add(sun);
        pass.Mode = OpenPbrRenderMode.Raster;
        foreach (var pixel in await Render()) Expect(Vector4.Distance(pixel,new(.8f,.8f,.8f,1)) < 2e-4f, "Tangent normal changes cosine irradiance correctly.");
        // An opacity expression must affect the hybrid primary cutout walk.
        material.Surface.Graph = new(new Dictionary<OpenPbrInput, OpenPbrNode> { [OpenPbrInput.GeometryOpacity] = OpenPbrNode.Extract(OpenPbrNode.Vector3(Vector3.Zero), 1) });
        pass.Mode = OpenPbrRenderMode.Hybrid; pass.BackgroundAlpha = 1; pass.EnvironmentRadiance = new(2,3,4,1,StandardColorSpaces.AcesCg);
        foreach (var pixel in await Render()) Expect(Vector4.Distance(pixel,new(2,3,4,1)) < 2e-5f, "Connected opacity exposes HDR background.");
        pass.Mode = OpenPbrRenderMode.Raster;
        try { await Render(); throw new Exception("Missing raster graph-domain rejection"); } catch (NotSupportedException) { checks++; }
        // Every public parameter independently compares a connected constant against the
        // ordinary material path; unlike a packing-only test this reaches the native closure.
        scene.Remove(sun); pass.Mode = OpenPbrRenderMode.Reference; pass.ReferenceMaxBounces = 1;
        pass.EnvironmentRadiance = new(0,0,0,1,StandardColorSpaces.AcesCg); pass.BackgroundAlpha = 0; scene.Add(sun);
        material.NitsPerSceneUnit = 100;
        foreach (OpenPbrInput input in Enum.GetValues<OpenPbrInput>())
        {
            var authored = new OpenPbrSurface();
            var property = typeof(OpenPbrSurface).GetProperty(input.ToString())!;
            object value = input switch
            {
                OpenPbrInput.GeometryThinWalled => true,
                OpenPbrInput.GeometryNormal or OpenPbrInput.GeometryCoatNormal => new Vector3(.3f,0,.95f),
                OpenPbrInput.GeometryTangent or OpenPbrInput.GeometryCoatTangent => Vector3.UnitY,
                OpenPbrInput.SubsurfaceRadiusScale => new Vector3(.8f,.4f,.2f),
                _ when property.PropertyType == typeof(LinearRgba) => new LinearRgba(.15f,.35f,.55f,1,StandardColorSpaces.AcesCg),
                _ => .45f,
            };
            property.SetValue(authored,value); material.Surface = authored;
            var plain = await Render();
            var node = value switch
            {
                bool boolean => OpenPbrNode.Boolean(boolean), Vector3 vector => OpenPbrNode.Vector3(vector),
                LinearRgba rgb => OpenPbrNode.Color(rgb), float scalar => OpenPbrNode.Float(scalar),
                _ => throw new InvalidOperationException(),
            };
            material.Surface = new() { Graph = new(new Dictionary<OpenPbrInput, OpenPbrNode> { [input] = node }) };
            pass.ResetAccumulation(); var connected = await Render();
            for (int pixel = 0; pixel < plain.Length; pixel++)
                Expect(Vector4.Distance(plain[pixel], connected[pixel]) < 2e-5f, $"Connected {input} preserves native closure behavior.");
        }
        return checks;
    }
    private static MeshGeometry Quad(Vector2 uv0, Vector2 uv1) => MeshGeometry.CreateWithTextureCoordinateSets(
        [new(-10,-10,0),new(10,-10,0),new(10,10,0),new(-10,10,0)], [0,1,2,0,2,3],
        normals: Enumerable.Repeat(Vector3.UnitZ,4), textureCoordinates: Enumerable.Repeat(uv0,4),
        tangents: Enumerable.Repeat(new Vector4(1,0,0,1),4), textureCoordinates1: Enumerable.Repeat(uv1,4));
}

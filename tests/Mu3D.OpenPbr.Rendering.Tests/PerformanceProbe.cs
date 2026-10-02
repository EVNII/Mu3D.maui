using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class PerformanceProbe
{
    internal static async Task RunAsync(WgpuGraphicsDevice device, string destination)
    {
        const uint width = 1920, height = 1080;
        Scene scene = new();
        var material = new OpenPbrMaterial(new() { BaseColor = new(.4f,.15f,.06f,1,StandardColorSpaces.AcesCg),
            BaseMetalness = .6f, CoatWeight = .3f, CoatRoughness = .2f, SpecularRoughness = .35f });
        scene.Add(new Mesh(MeshPrimitives.CreateUvSphere(longitudeSegments: 48, latitudeSegments: 24), material));
        scene.Add(new DirectionalLight(new(1,1,1,1,StandardColorSpaces.AcesCg),4));
        PerspectiveCamera camera = new() { AspectRatio = width / (float)height }; camera.Transform.Position = new(0,0,4);
        using var target = device.CreateTexture(new(new(width,height), GraphicsTextureFormat.Rgba16Float, GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        using var readback = device.CreateBuffer(new(256,GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
        using var pass = new OpenPbrRenderPass { BackgroundAlpha = 1, EnvironmentRadiance = new(.1f,.1f,.1f,1,StandardColorSpaces.AcesCg) };
        RenderPassContext context = new(scene,camera,target,null,StandardColorSpaces.LinearSrgb);
        List<object> results = [];
        foreach (bool textured in new[] { false, true })
        foreach (var mode in Enum.GetValues<OpenPbrRenderMode>())
        {
            material.Surface.Graph = textured ? new(new Dictionary<OpenPbrInput, OpenPbrNode>
            {
                [OpenPbrInput.BaseColor] = OpenPbrNode.Image(OpenPbrTexture.FromColor(new(2,2,
                    [new(.4f,.15f,.06f,1),new(.02f,.2f,.5f,1),new(.02f,.2f,.5f,1),new(.4f,.15f,.06f,1)],StandardColorSpaces.AcesCg)), OpenPbrNodeType.Color3),
            }) : null;
            pass.Mode = mode;
            Stopwatch cold = Stopwatch.StartNew(); pass.Execute(context); await Fence(); cold.Stop();
            const int frames = 8;
            Stopwatch timer = Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) pass.Execute(context);
            await Fence(); timer.Stop();
            double ms = timer.Elapsed.TotalMilliseconds / frames;
            results.Add(new { mode = mode.ToString(), textured, width, height, frames, coldMilliseconds = cold.Elapsed.TotalMilliseconds,
                millisecondsPerSubmittedFrame = ms, submittedFramesPerSecond = 1000 / ms,
                internalScale = mode == OpenPbrRenderMode.Interactive ? pass.InteractiveResolutionScale : 1,
                events = mode switch { OpenPbrRenderMode.Reference => pass.ReferenceMaxBounces,
                    OpenPbrRenderMode.Interactive or OpenPbrRenderMode.Hybrid => 4, _ => 0 } });
            Console.WriteLine($"OpenPBR {mode} (textured={textured}): {ms:F2} ms/submitted frame, {1000/ms:F1} submitted FPS (headless batch; no display or denoising).");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        await File.WriteAllTextAsync(destination, JsonSerializer.Serialize(new { scene = "one 48x24 coated metal sphere, one directional light, uniform environment", measurements = results }, new JsonSerializerOptions { WriteIndented = true }));
        async Task Fence()
        {
            using var encoder = device.CreateCommandEncoder("OpenPBR benchmark completion");
            encoder.CopyTextureToBuffer(target,0,default,new(1,1),readback,0,256,1);
            using var commands = encoder.Finish(); device.Queue.Submit(commands);
            await readback.ReadAsync(0,256).WaitAsync(TimeSpan.FromSeconds(60));
        }
    }
}

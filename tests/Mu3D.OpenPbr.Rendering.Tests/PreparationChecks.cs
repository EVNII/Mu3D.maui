using System.Diagnostics;
using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class PreparationChecks
{
    internal static async Task<int> RunAsync(WgpuGraphicsDevice device)
    {
        int checks = 0;
        using var pass = new OpenPbrRenderPass(StandardColorSpaces.AcesCg)
        {
            Mode = OpenPbrRenderMode.Raster, BackgroundAlpha = 1,
            EnvironmentRadiance = new(2, .5f, .25f, 1, StandardColorSpaces.AcesCg),
        };
        var timer = Stopwatch.StartNew();
        Task preparation = pass.PrepareAsync(device, GraphicsTextureFormat.Rgba32Float);
        double dispatch = timer.Elapsed.TotalMilliseconds;
        await preparation;
        Console.WriteLine($"PrepareAsync dispatch {dispatch:F2} ms; completed {timer.Elapsed.TotalMilliseconds:F2} ms.");
        Scene scene = new(); PerspectiveCamera camera = new(); camera.Transform.Position = new(0, 0, 3);
        foreach (var mode in new[] { OpenPbrRenderMode.Raster, OpenPbrRenderMode.Fast })
        foreach (uint size in new uint[] { 8, 16, 4 })
        {
            pass.Mode = mode;
            using var target = device.CreateTexture(new(new(size, size), GraphicsTextureFormat.Rgba32Float,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
            timer.Restart();
            pass.Execute(new RenderPassContext(scene, camera, target, null, StandardColorSpaces.AcesCg));
            Console.WriteLine($"Prepared/resize {mode} {size}: Execute {timer.Elapsed.TotalMilliseconds:F2} ms.");
            var pixels = await TransportChecks.ReadAsync(device, target);
            if (!pixels.All(p => Vector4.Distance(p, new(2, .5f, .25f, 1)) < .00001f))
                throw new Exception("Prepared/resized output lost environment or bindings.");
            checks++;
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Throws<OperationCanceledException>(() => pass.PrepareAsync(device, GraphicsTextureFormat.Rgba32Float, canceled.Token));
        using var pendingCancel = new CancellationTokenSource();
        Task pending = pass.PrepareAsync(device, GraphicsTextureFormat.Rgba32Float, pendingCancel.Token);
        pendingCancel.Cancel();
        await Throws<OperationCanceledException>(() => pending);
        var abandoned = new OpenPbrRenderPass { Mode = OpenPbrRenderMode.Raster };
        Task abandonedPreparation = abandoned.PrepareAsync(device, GraphicsTextureFormat.Rgba32Float);
        abandoned.Dispose();
        await Throws<ObjectDisposedException>(() => abandonedPreparation);
        // A canceled/abandoned preparation must not dispose the caller's graphics device.
        if (device.State != GraphicsDeviceState.Active) throw new Exception("Preparation disposed caller device.");
        return checks + 1;

        async Task Throws<T>(Func<Task> action) where T : Exception
        {
            try { await action(); } catch (T) { checks++; return; }
            throw new Exception($"Expected {typeof(T).Name}");
        }
    }
}

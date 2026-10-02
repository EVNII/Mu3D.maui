using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Mu3D.Color;
using Mu3D.GalleryApp.Pages;
using Mu3D.GalleryApp.Examples;
using Mu3D.GalleryApp.Web.Infrastructure;
using Mu3D.Graphics;
using Mu3D.Native.Wgpu;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

namespace Mu3D.Web.Validation;

internal static partial class RenderProbe
{
    [JSExport]
    public static async Task<string> CheckGalleryOpenPbr()
    {
        using BrowserGpu browser = await BrowserGpu.OpenAsync();
        using WgpuGraphicsDevice device = browser.CreateDevice();
        using OpenPbrGalleryExample example = new();
        const uint size = 128;
        using GraphicsTexture target = device.CreateTexture(new(new(size, size), GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        using GraphicsBuffer pixels = device.CreateBuffer(new(size * size * 8,
            GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
        List<string> reports = [];
        foreach (OpenPbrRenderMode mode in Enum.GetValues<OpenPbrRenderMode>())
        {
            example.Transport.Mode = mode;
            browser.BeginValidationScope();
            try
            {
                await example.Transport.PrepareAsync(device, GraphicsTextureFormat.Rgba16Float);
                example.Draw(device, target, null, 0);
                using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("Gallery OpenPBR merge readback");
                encoder.CopyTextureToBuffer(target, 0, default, new(size, size), pixels, 0, size * 8, size);
                using GraphicsCommandBuffer commands = encoder.Finish();
                device.Queue.Submit(commands);
            }
            finally { await browser.EndValidationScopeAsync(); }
            await browser.WaitForSubmittedWorkAsync();
            BrowserGpu.ThrowIfErrors($"Gallery OpenPBR {mode}");
            int lit = CountLitPixels(await pixels.ReadAsync(0, (int)(size * size * 8)));
            if (lit < 100) throw new InvalidOperationException($"Gallery OpenPBR {mode} is black: {lit} lit pixels.");
            reports.Add($"{mode}: {lit} finite lit pixels");
        }
        return string.Join("\n", reports);
    }

    // Exercise the actual Gallery presenters through the WASM C-API backend, without a UI.
    [JSExport]
    public static async Task<string> CheckGalleryLighting(string encodedHdr)
    {
        using MemoryStream source = new(Convert.FromBase64String(encodedHdr), writable: false);
        EquirectangularHdrEnvironment environment = RadianceHdrReader.Read(source, StandardColorSpaces.LinearSrgb);
        await SceneRenderer.PrepareImageBasedLightingAsync(environment);
        using BrowserGpu browser = await BrowserGpu.OpenAsync();
        using WgpuGraphicsDevice device = browser.CreateDevice();
        // An asynchronous validation failure must be reported even without another scheduled frame.
        browser.BeginValidationScope();
        using (device.CreateShaderModule(new("invalid WGSL", "expected first-frame validation failure"))) { }
        bool validationReported = false;
        try { await browser.EndValidationScopeAsync(); }
        catch (InvalidOperationException error) when (error.Message.Contains("WGSL", StringComparison.OrdinalIgnoreCase))
        { validationReported = true; }
        if (!validationReported) throw new InvalidOperationException("First-frame validation failure was not reported.");
        BrowserGpu.ThrowIfErrors("scoped validation regression");
        const uint size = 256;
        using GraphicsTexture target = device.CreateTexture(new(new(size, size), GraphicsTextureFormat.Rgba16Float,
            GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.CopySource));
        using GraphicsBuffer pixels = device.CreateBuffer(new(size * size * 8,
            GraphicsBufferUsage.CopyDestination | GraphicsBufferUsage.MapRead));
        using LightingLabPresenter lighting = new(device, environment);
        using OcclusionLabPresenter occlusion = new(device, environment);
        List<string> reports = [];
        for (int mode = 0; mode < 4; mode++)
        {
            browser.BeginValidationScope();
            try
            {
                if (mode < 2)
                {
                    lighting.DirectLightEnabled = mode == 0;
                    lighting.Render(target, size, size, 0);
                }
                else
                {
                    occlusion.ShadowsEnabled = mode == 2;
                    occlusion.AmbientOcclusionEnabled = mode == 2;
                    occlusion.Render(target, size, size);
                }
                using GraphicsCommandEncoder encoder = device.CreateCommandEncoder("Gallery lighting regression readback");
                encoder.CopyTextureToBuffer(target, 0, default, new(size, size), pixels, 0, size * 8, size);
                using GraphicsCommandBuffer commands = encoder.Finish();
                device.Queue.Submit(commands);
            }
            finally { await browser.EndValidationScopeAsync(); }
            await browser.WaitForSubmittedWorkAsync();
            BrowserGpu.ThrowIfErrors($"Gallery lighting mode {mode}");
            byte[] bytes = await pixels.ReadAsync(0, (int)(size * size * 8));
            int lit = CountLitPixels(bytes);
            if (lit < 100) throw new InvalidOperationException($"Gallery lighting mode {mode} is black: {lit} lit pixels.");
            reports.Add($"mode {mode}: {lit} lit pixels");
        }
        return string.Join("\n", reports);
    }

    private static int CountLitPixels(byte[] bytes)
    {
        ReadOnlySpan<Half> values = MemoryMarshal.Cast<byte, Half>(bytes);
        int lit = 0;
        for (int i = 0; i < values.Length; i += 4)
        {
            float red = (float)values[i], green = (float)values[i + 1], blue = (float)values[i + 2];
            if (!float.IsFinite(red) || !float.IsFinite(green) || !float.IsFinite(blue))
                throw new InvalidOperationException("Gallery produced non-finite FP16 pixels.");
            if (Math.Max(red, Math.Max(green, blue)) > .01f) lit++;
        }
        return lit;
    }
}

using Mu3D.Native.Wgpu;
using Mu3D.Native.Wgpu.Interop;

int checks = SceneCompilerChecks.Run();
Console.WriteLine($"OpenPBR scene compilation and CPU traversal checks passed: {checks}.");
Console.WriteLine($"OpenPBR camera medium checks passed: {InitialMediaChecks.Run()}.");
Console.WriteLine($"OpenPBR material packing checks passed: {MaterialPackingChecks.Run()}.");
Console.WriteLine($"OpenPBR Fast bake checks passed: {FastBakeChecks.Run()}.");
if (args.Contains("--native", StringComparer.Ordinal))
{
    if (args.Contains("--dump-shaders", StringComparer.Ordinal)) ShaderDiagnostics.Enable();
    else if (args.Contains("--cache-log", StringComparer.Ordinal)) ShaderDiagnostics.Enable(false);
    using WgpuGraphicsDevice gpu = args.Contains("--dx12", StringComparer.Ordinal)
        ? await WgpuGraphicsDevice.CreateForTestingAsync(TimeSpan.FromSeconds(15), false, WGPUBackendType.D3D12,
            disableShaderF16: args.Contains("--no-shader-f16", StringComparer.Ordinal))
        : await WgpuGraphicsDevice.CreateAsync(TimeSpan.FromSeconds(15));
    Console.WriteLine($"Device ShaderF16: {gpu.Capabilities.SupportsShaderFloat16}");
    if (args.Contains("--cache-warmup-only", StringComparer.Ordinal))
    {
        ShaderDiagnostics.RecordCaptureSources();
        foreach (string family in new[] { "Raster", "Fast", "Transport" })
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            using var resources = new Mu3D.Rendering.OpenPbr.OpenPbrGpuResources(gpu, 1, 1,
                Mu3D.Graphics.GraphicsTextureFormat.Rgba16Float, Mu3D.Color.StandardColorSpaces.LinearSrgb,
                true, fast: family == "Fast", rasterOnly: family == "Raster");
            Console.WriteLine($"Cache preparation {family}: {timer.Elapsed.TotalMilliseconds:F2} ms");
        }
        return;
    }
    if (args.Contains("--startup-only", StringComparer.Ordinal))
    {
        for (uint i = 0; i < 2; i++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            using var resources = new Mu3D.Rendering.OpenPbr.OpenPbrGpuResources(gpu, 16 + i * 16, 16,
                Mu3D.Graphics.GraphicsTextureFormat.Rgba16Float, Mu3D.Color.StandardColorSpaces.LinearSrgb, true, rasterOnly: !args.Contains("--legacy-transport"));
            Console.WriteLine($"Raster construction {i}: {timer.Elapsed.TotalMilliseconds:F2} ms; " +
                System.Text.Json.JsonSerializer.Serialize(resources.InitializationMilliseconds));
            timer.Restart(); resources.Resize(64, 32);
            Console.WriteLine($"Resize: {timer.Elapsed.TotalMilliseconds:F2} ms (pipelines retained)");
        }
        return;
    }
    if (args.Contains("--prepare-only", StringComparer.Ordinal))
    {
        Console.WriteLine($"OpenPBR preparation checks passed: {await PreparationChecks.RunAsync(gpu)}.");
        return;
    }
    Console.WriteLine($"Presentation white checks passed: {await PresentationWhiteChecks.RunAsync(gpu)}.");
    if (args.Contains("--white-only", StringComparer.Ordinal)) return;
    if (args.Contains("--benchmark", StringComparer.Ordinal))
    {
        await PerformanceProbe.RunAsync(gpu, "artifacts/validation/openpbr-completion/performance.json");
        return;
    }
    if (args.Contains("--oracle-only", StringComparer.Ordinal))
    {
        Console.WriteLine($"OpenPBR native reference oracle checks passed: {await BsdfOracleChecks.RunAsync(gpu)}.");
        return;
    }
    if (args.Contains("--graph-only", StringComparer.Ordinal))
    {
        Console.WriteLine($"OpenPBR graph rendering checks passed: {await GraphRenderingChecks.RunAsync(gpu)}.");
        return;
    }
    if (args.Contains("--furnace-only", StringComparer.Ordinal))
    {
        Console.WriteLine($"OpenPBR Gallery furnace checks passed: {await FurnaceChecks.RunAsync(gpu)}.");
        return;
    }
    if (args.Contains("--raster-only", StringComparer.Ordinal))
    {
        Console.WriteLine($"OpenPBR native raster and hybrid checks passed: {await RasterHybridChecks.RunAsync(gpu)}.");
        return;
    }
    if (args.Contains("--fast-only", StringComparer.Ordinal))
    {
        Console.WriteLine($"OpenPBR Fast mode checks passed: {await FastModeChecks.RunAsync(gpu)}.");
        return;
    }
    if (args.Contains("--shadows-only", StringComparer.Ordinal))
    {
        Console.WriteLine($"OpenPBR directional shadow checks passed: {await DirectionalShadowChecks.RunAsync(gpu)}.");
        return;
    }
    Console.WriteLine($"OpenPBR preparation checks passed: {await PreparationChecks.RunAsync(gpu)}.");
    Console.WriteLine($"OpenPBR graph rendering checks passed: {await GraphRenderingChecks.RunAsync(gpu)}.");
    int nativeChecks = await BsdfOracleChecks.RunAsync(gpu);
    Console.WriteLine($"OpenPBR native reference oracle checks passed: {nativeChecks}; no UI or surface was created.");
    Console.WriteLine($"OpenPBR native scene transport checks passed: {await TransportChecks.RunAsync(gpu)}.");
    Console.WriteLine($"OpenPBR native lighting and scattering checks passed: {await TransportLightingChecks.RunAsync(gpu)}.");
    Console.WriteLine($"OpenPBR native raster and hybrid checks passed: {await RasterHybridChecks.RunAsync(gpu)}.");
    Console.WriteLine($"OpenPBR Fast mode checks passed: {await FastModeChecks.RunAsync(gpu)}.");
    Console.WriteLine($"OpenPBR directional shadow checks passed: {await DirectionalShadowChecks.RunAsync(gpu)}.");
    Console.WriteLine($"OpenPBR Gallery furnace checks passed: {await FurnaceChecks.RunAsync(gpu)}.");
}

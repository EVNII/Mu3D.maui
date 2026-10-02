using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mu3D.Native.Wgpu.Interop;

internal static unsafe class ShaderDiagnostics
{
    private const string DirectoryPath = "artifacts/validation/openpbr-dx12";
    private static int sequence;
    private static bool dumpShaders;

    internal static void Enable(bool dump = true)
    {
        dumpShaders = dump;
        Directory.CreateDirectory(DirectoryPath);
        WgpuNative.wgpuSetLogCallback(&Log, null);
        WgpuNative.wgpuSetLogLevel(WGPULogLevel.Debug);
    }

    internal static void RecordCaptureSources()
    {
        string? directory = Environment.GetEnvironmentVariable("MU3D_SHADER_CACHE_CAPTURE");
        if (string.IsNullOrEmpty(directory)) return;
        if (!Path.IsPathFullyQualified(directory)) throw new InvalidOperationException("Use an absolute capture directory.");
        var assembly = typeof(Mu3D.Rendering.OpenPbr.OpenPbrRenderPass).Assembly;
        const string prefix = "Mu3D.Rendering.OpenPbr.Shaders.";
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            hashes[name[prefix.Length..]] = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).ToLowerInvariant();
        }
        string json = System.Text.Json.JsonSerializer.Serialize(hashes);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "source-manifest.json");
        if (File.Exists(path) && File.ReadAllText(path) != json)
            throw new InvalidOperationException("Capture directory contains shaders from another renderer build. Use a new directory.");
        File.WriteAllText(path, json);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void Log(WGPULogLevel level, WGPUStringView message, void* userdata)
    {
        try
        {
            string text = WgpuBootstrap.Decode(message);
            if (text.StartsWith("Mu3D ", StringComparison.Ordinal)) Console.WriteLine(text);
            if (!dumpShaders) return;
            if (!text.Contains("Naga generated shader", StringComparison.Ordinal)) return;
            int start = text.IndexOf('\n');
            if (start >= 0)
                File.WriteAllText(Path.Combine(DirectoryPath, $"shader-{Interlocked.Increment(ref sequence)}.hlsl"), text[(start + 1)..]);
        }
        catch (Exception exception)
        {
            // A diagnostic failure must never unwind through the native logging callback.
            Console.Error.WriteLine($"Shader dump failed: {exception.Message}");
        }
    }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Mu3D.DebugRuntime.Tests;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["verify-android-aar", string aarPath, string stagingRoot])
        {
            return VerifyAndroidAar(aarPath, stagingRoot);
        }

        if (args is ["verify-windows-output", string outputPath, string stagedDebugPath])
        {
            return VerifyWindowsOutput(outputPath, stagedDebugPath);
        }

        string repositoryRoot = args is [string root]
            ? Path.GetFullPath(root)
            : Directory.GetCurrentDirectory();
        string fixtures = Path.Combine(repositoryRoot, "tests", "Mu3D.DebugRuntime.Tests", "Fixtures");
        List<string> failures = [];

        await ExpectMsBuildAsync(
            Path.Combine(fixtures, "AppleDebugRuntime.proj"),
            repositoryRoot,
            expectedSuccess: true,
            expectedText: "Verified one Debug NativeReference",
            failures).ConfigureAwait(false);
        await ExpectMsBuildAsync(
            Path.Combine(fixtures, "AndroidDebugRuntime.proj"),
            repositoryRoot,
            expectedSuccess: true,
            expectedText: "Verified four Debug AndroidNativeLibrary items",
            failures).ConfigureAwait(false);
        await ExpectMsBuildAsync(
            Path.Combine(fixtures, "WindowsDebugRuntime.proj"),
            repositoryRoot,
            expectedSuccess: true,
            expectedText: "Verified one Debug Windows content item",
            failures).ConfigureAwait(false);
        await ExpectMsBuildAsync(
            Path.Combine(fixtures, "AbiMismatch.proj"),
            repositoryRoot,
            expectedSuccess: false,
            expectedText: "requires the exactly matched Mu3D.Native.Wgpu v29.0.1.1 ABI",
            failures).ConfigureAwait(false);

        if (failures.Count == 0)
        {
            Console.WriteLine("Validated DebugRuntime replacement and ABI failure paths.");
            return 0;
        }

        foreach (string failure in failures)
        {
            Console.Error.WriteLine($"error: {failure}");
        }

        return 1;
    }

    private static int VerifyAndroidAar(string aarPath, string stagingRoot)
    {
        Dictionary<string, string> expected = new(StringComparer.Ordinal)
        {
            ["jni/armeabi-v7a/libwgpu_native.so"] = "android-arm",
            ["jni/arm64-v8a/libwgpu_native.so"] = "android-arm64",
            ["jni/x86/libwgpu_native.so"] = "android-x86",
            ["jni/x86_64/libwgpu_native.so"] = "android-x64",
        };
        List<string> failures = [];
        using ZipArchive archive = ZipFile.OpenRead(aarPath);
        ZipArchiveEntry[] nativeEntries = archive.Entries
            .Where(entry => entry.FullName.EndsWith("/libwgpu_native.so", StringComparison.Ordinal))
            .ToArray();
        if (nativeEntries.Length != expected.Count)
        {
            failures.Add($"expected {expected.Count} wgpu-native AAR entries, got {nativeEntries.Length}.");
        }

        foreach ((string entryPath, string rid) in expected)
        {
            ZipArchiveEntry? entry = archive.GetEntry(entryPath);
            if (entry is null)
            {
                failures.Add($"AAR is missing {entryPath}.");
                continue;
            }

            string sourcePath = Path.Combine(stagingRoot, rid, "native", "libwgpu_native.so");
            using Stream entryStream = entry.Open();
            using FileStream sourceStream = File.OpenRead(sourcePath);
            string actualHash = Convert.ToHexStringLower(SHA256.HashData(entryStream));
            string expectedHash = Convert.ToHexStringLower(SHA256.HashData(sourceStream));
            if (actualHash != expectedHash)
            {
                failures.Add($"{entryPath} does not match the verified Debug runtime for {rid}.");
            }
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("Verified all four Android AAR libraries are the staged Debug runtimes.");
            return 0;
        }

        foreach (string failure in failures)
        {
            Console.Error.WriteLine($"error: {failure}");
        }

        return 1;
    }

    private static int VerifyWindowsOutput(string outputPath, string stagedDebugPath)
    {
        using FileStream output = File.OpenRead(outputPath);
        using FileStream stagedDebug = File.OpenRead(stagedDebugPath);
        string outputHash = Convert.ToHexStringLower(SHA256.HashData(output));
        string stagedDebugHash = Convert.ToHexStringLower(SHA256.HashData(stagedDebug));
        if (outputHash == stagedDebugHash)
        {
            Console.WriteLine("Verified Windows output DLL is the staged Debug runtime.");
            return 0;
        }

        Console.Error.WriteLine("error: Windows output wgpu_native.dll is not the staged Debug runtime.");
        return 1;
    }

    private static async Task ExpectMsBuildAsync(
        string projectPath,
        string repositoryRoot,
        bool expectedSuccess,
        string expectedText,
        List<string> failures)
    {
        ProcessStartInfo startInfo = new("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("/t:Verify");
        startInfo.ArgumentList.Add($"/p:Mu3DRepositoryRoot={repositoryRoot}");
        startInfo.ArgumentList.Add("/nologo");
        startInfo.ArgumentList.Add("/v:minimal");

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start MSBuild fixture.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);
        string combined = output + error;
        bool succeeded = process.ExitCode == 0;
        if (succeeded != expectedSuccess || !combined.Contains(expectedText, StringComparison.Ordinal))
        {
            failures.Add(
                $"{Path.GetFileName(projectPath)} returned {process.ExitCode}; " +
                $"expected success={expectedSuccess} and text '{expectedText}'. Output: {combined.Trim()}");
        }
    }
}

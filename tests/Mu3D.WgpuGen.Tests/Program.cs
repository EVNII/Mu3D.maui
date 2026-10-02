namespace Mu3D.WgpuGen.Tests;

internal static class Program
{
    public static int Main(string[] args)
    {
        string repositoryRoot = args is [string root] ? Path.GetFullPath(root) : Directory.GetCurrentDirectory();
        string manifestPath = Path.Combine(repositoryRoot, "eng", "wgpu-native-assets.json");
        string propsPath = Path.Combine(repositoryRoot, "eng", "Mu3D.WgpuNativeVersion.props");
        List<string> failures = [];

        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        ExpectNoErrors(ManifestValidator.Validate(manifest, propsPath), "valid manifest", failures);
        ExpectInvalidBindingShape(failures);
        ExpectNativeExportComparison(failures);
        ExpectWindowsD3D12BridgeExports(failures);
        ExpectRuntimeSelection(manifest, failures);

        UpstreamArtifact duplicate = manifest.Artifacts[0];
        manifest.Artifacts.Add(duplicate);
        ExpectError(
            ManifestValidator.Validate(manifest, propsPath),
            "Duplicate artifact",
            "duplicate artifact",
            failures);
        manifest.Artifacts.RemoveAt(manifest.Artifacts.Count - 1);

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), $"mu3d-wgpugen-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            string driftedPropsPath = Path.Combine(temporaryDirectory, "drifted.props");
            File.WriteAllText(
                driftedPropsPath,
                "<Project><PropertyGroup><Mu3DWgpuNativeVersion>v0.0.0.0</Mu3DWgpuNativeVersion>" +
                $"<Mu3DWgpuNativeCommit>{manifest.Upstream.Commit}</Mu3DWgpuNativeCommit>" +
                "</PropertyGroup><ItemGroup>" +
                "<_Mu3DPinnedWgpuNativeVersion Include=\"v0.0.0.0\" />" +
                $"<_Mu3DPinnedWgpuNativeCommit Include=\"{manifest.Upstream.Commit}\" />" +
                "</ItemGroup></Project>");
            ExpectError(
                ManifestValidator.Validate(manifest, driftedPropsPath),
                "does not match Mu3D.WgpuNativeVersion.props",
                "version drift",
                failures);

            string corruptArchivePath = Path.Combine(temporaryDirectory, "corrupt.zip");
            File.WriteAllBytes(corruptArchivePath, [0x00]);
            string? archiveError = ArchiveVerifier.Verify(
                manifest,
                duplicate.Rid,
                duplicate.Configuration,
                corruptArchivePath);
            if (archiveError is null || !archiveError.Contains("size mismatch", StringComparison.Ordinal))
            {
                failures.Add("corrupt archive was not rejected by size before hash verification.");
            }

            string corruptDllPath = Path.Combine(temporaryDirectory, "corrupt.dll");
            File.WriteAllBytes(corruptDllPath, [0x4D, 0x5A]);
            try
            {
                _ = PeExportReader.Read(corruptDllPath);
                failures.Add("corrupt PE image was accepted by the export reader.");
            }
            catch (BadImageFormatException)
            {
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("Validated manifest success, drift, duplicate and corrupt-archive paths.");
            return 0;
        }

        foreach (string failure in failures)
        {
            Console.Error.WriteLine($"error: {failure}");
        }

        return 1;
    }

    private static void ExpectNoErrors(
        IReadOnlyList<string> errors,
        string scenario,
        List<string> failures)
    {
        if (errors.Count != 0)
        {
            failures.Add($"{scenario} unexpectedly failed: {string.Join("; ", errors)}");
        }
    }

    private static void ExpectError(
        IReadOnlyList<string> errors,
        string expectedFragment,
        string scenario,
        List<string> failures)
    {
        if (!errors.Any(x => x.Contains(expectedFragment, StringComparison.Ordinal)))
        {
            failures.Add($"{scenario} did not produce an error containing '{expectedFragment}'.");
        }
    }

    private static void ExpectInvalidBindingShape(List<string> failures)
    {
        try
        {
            _ = ClangSharpPostProcessor.Process("invalid output", "unused", "unused");
            failures.Add("invalid ClangSharp declaration counts were accepted.");
        }
        catch (InvalidDataException exception) when (
            exception.Message.Contains("header shape changed", StringComparison.Ordinal))
        {
        }
    }

    private static void ExpectNativeExportComparison(List<string> failures)
    {
        const string generated = """
            internal static partial void wgpuCreateInstance();
            internal static partial void wgpuInstanceRelease();
            """;
        const string symbols = """
            0000000000000000 T _wgpuCreateInstance
            0000000000000010 T _wgpuInstanceRelease
            """;

        HashSet<string> imports = NativeExportVerifier.ParseManagedImports(generated);
        HashSet<string> exports = NativeExportVerifier.ParseNativeExports(symbols);
        if (NativeExportVerifier.Compare(imports, exports).Count != 0)
        {
            failures.Add("matching native exports were rejected.");
        }

        exports.Remove("wgpuInstanceRelease");
        IReadOnlyList<string> errors = NativeExportVerifier.Compare(imports, exports);
        if (!errors.Any(error => error.Contains("wgpuInstanceRelease", StringComparison.Ordinal)))
        {
            failures.Add("missing native export was not reported.");
        }
    }

    private static void ExpectRuntimeSelection(NativeAssetManifest manifest, List<string> failures)
    {
        IReadOnlyList<UpstreamArtifact> release = RuntimeSetPreparer.SelectArtifacts(
            manifest,
            "release");
        IReadOnlyList<UpstreamArtifact> debug = RuntimeSetPreparer.SelectArtifacts(
            manifest,
            "debug");
        ExpectCount(release.Count, 12, "pinned Release runtime selection", failures);
        ExpectCount(debug.Count, 12, "pinned Debug runtime selection", failures);
        if (release.Any(artifact => artifact.Configuration != "release"))
        {
            failures.Add("Release runtime selection included a non-Release artifact.");
        }

        ExpectCount(
            RuntimeSetPreparer.SelectArtifacts(manifest, "debug", "android").Count,
            4,
            "Android Debug runtime selection",
            failures);
        ExpectCount(
            RuntimeSetPreparer.SelectArtifacts(manifest, "debug", "ios").Count,
            3,
            "iOS Debug runtime selection",
            failures);
        ExpectCount(
            RuntimeSetPreparer.SelectArtifacts(manifest, "debug", "windows").Count,
            3,
            "Windows Debug runtime selection",
            failures);
        ExpectCount(
            RuntimeSetPreparer.SelectArtifacts(manifest, "debug", "maccatalyst").Count,
            2,
            "Mac Catalyst Debug runtime selection",
            failures);
        ExpectCount(
            RuntimeSetPreparer.SelectArtifacts(manifest, "debug", "unknown").Count,
            0,
            "unknown platform runtime selection",
            failures);
    }

    private static void ExpectWindowsD3D12BridgeExports(List<string> failures)
    {
        HashSet<string> exports =
            WindowsD3D12BridgeVerifier.RequiredExports.ToHashSet(StringComparer.Ordinal);
        ExpectNoErrors(
            WindowsD3D12BridgeVerifier.VerifyExports(exports),
            "complete Windows D3D12 bridge exports",
            failures);

        exports.Remove("wgpuTextureGetNativeD3D12Resource");
        ExpectError(
            WindowsD3D12BridgeVerifier.VerifyExports(exports),
            "wgpuTextureGetNativeD3D12Resource",
            "missing Windows D3D12 bridge export",
            failures);
    }

    private static void ExpectCount(
        int actual,
        int expected,
        string scenario,
        List<string> failures)
    {
        if (actual != expected)
        {
            failures.Add($"{scenario}: expected {expected}, got {actual}.");
        }
    }
}

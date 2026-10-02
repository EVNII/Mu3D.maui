namespace Mu3D.WgpuGen;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return args switch
            {
                ["validate-manifest", string manifestPath, string propsPath] =>
                    ValidateManifest(manifestPath, propsPath),
                ["verify-archive", string manifestPath, string rid, string configuration, string archivePath] =>
                    VerifyArchive(manifestPath, rid, configuration, archivePath),
                ["generate-metadata", string manifestPath, string propsPath, string outputPath] =>
                    GenerateMetadata(manifestPath, propsPath, outputPath),
                ["generate-bindings", string manifestPath, string propsPath, string webGpuHeaderPath, string wgpuHeaderPath, string outputPath] =>
                    await GenerateBindingsAsync(manifestPath, propsPath, webGpuHeaderPath, wgpuHeaderPath, outputPath).ConfigureAwait(false),
                ["verify-bindings", string manifestPath, string propsPath, string webGpuHeaderPath, string wgpuHeaderPath, string checkedInPath] =>
                    await VerifyBindingsAsync(manifestPath, propsPath, webGpuHeaderPath, wgpuHeaderPath, checkedInPath).ConfigureAwait(false),
                ["verify-native-exports", string generatedBindingsPath, string nativeLibraryPath] =>
                    await VerifyNativeExportsAsync(generatedBindingsPath, nativeLibraryPath).ConfigureAwait(false),
                ["verify-windows-d3d12-bridge", string nativeLibraryPath] =>
                    VerifyWindowsD3D12Bridge(nativeLibraryPath),
                ["fetch-artifact", string manifestPath, string propsPath, string rid, string configuration, string destinationDirectory] =>
                    await FetchArtifactAsync(manifestPath, propsPath, rid, configuration, destinationDirectory).ConfigureAwait(false),
                ["extract-binding-input", string manifestPath, string propsPath, string rid, string configuration, string archivePath, string destinationDirectory] =>
                    ExtractBindingInput(manifestPath, propsPath, rid, configuration, archivePath, destinationDirectory),
                ["stage-runtime", string manifestPath, string propsPath, string rid, string configuration, string archivePath, string stagingRoot] =>
                    StageRuntime(manifestPath, propsPath, rid, configuration, archivePath, stagingRoot),
                ["prepare-runtimes", string manifestPath, string propsPath, string configuration, string downloadDirectory, string stagingRoot] =>
                    await PrepareRuntimesAsync(manifestPath, propsPath, configuration, null, downloadDirectory, stagingRoot).ConfigureAwait(false),
                ["prepare-platform-runtimes", string manifestPath, string propsPath, string platform, string configuration, string downloadDirectory, string stagingRoot] =>
                    await PrepareRuntimesAsync(manifestPath, propsPath, configuration, platform, downloadDirectory, stagingRoot).ConfigureAwait(false),
                _ => ShowUsage(),
            };
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
                HttpRequestException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int ValidateManifest(string manifestPath, string propsPath)
    {
        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        IReadOnlyList<string> errors = ManifestValidator.Validate(manifest, propsPath);
        if (errors.Count == 0)
        {
            Console.WriteLine($"Validated {manifest.Artifacts.Count} pinned native artifacts.");
            return 0;
        }

        foreach (string error in errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        return 1;
    }

    private static int VerifyArchive(
        string manifestPath,
        string rid,
        string configuration,
        string archivePath)
    {
        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        string? error = ArchiveVerifier.Verify(manifest, rid, configuration, archivePath);
        if (error is not null)
        {
            Console.Error.WriteLine($"error: {error}");
            return 1;
        }

        Console.WriteLine($"Verified {rid}/{configuration}: {Path.GetFileName(archivePath)}");
        return 0;
    }

    private static int GenerateMetadata(string manifestPath, string propsPath, string outputPath)
    {
        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        IReadOnlyList<string> errors = ManifestValidator.Validate(manifest, propsPath);
        if (errors.Count != 0)
        {
            foreach (string error in errors)
            {
                Console.Error.WriteLine($"error: {error}");
            }

            return 1;
        }

        MetadataEmitter.Write(manifest, manifestPath, outputPath);
        Console.WriteLine($"Generated {outputPath}");
        return 0;
    }

    private static async Task<int> GenerateBindingsAsync(
        string manifestPath,
        string propsPath,
        string webGpuHeaderPath,
        string wgpuHeaderPath,
        string outputPath)
    {
        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        List<string> errors = [.. ManifestValidator.Validate(manifest, propsPath)];
        errors.AddRange(BindingEmitter.ValidateHeaders(webGpuHeaderPath, wgpuHeaderPath));
        if (errors.Count != 0)
        {
            foreach (string error in errors)
            {
                Console.Error.WriteLine($"error: {error}");
            }

            return 1;
        }

        string repositoryRoot = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(propsPath))!)!.FullName;
        string rawSource = await ClangSharpDriver.GenerateAsync(
            repositoryRoot,
            webGpuHeaderPath,
            wgpuHeaderPath,
            CancellationToken.None).ConfigureAwait(false);
        ClangSharpPostProcessor.Write(rawSource, webGpuHeaderPath, wgpuHeaderPath, outputPath);
        Console.WriteLine($"Generated {outputPath}");
        return 0;
    }

    private static async Task<int> VerifyBindingsAsync(
        string manifestPath,
        string propsPath,
        string webGpuHeaderPath,
        string wgpuHeaderPath,
        string checkedInPath)
    {
        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        List<string> errors = [.. ManifestValidator.Validate(manifest, propsPath)];
        errors.AddRange(BindingEmitter.ValidateHeaders(webGpuHeaderPath, wgpuHeaderPath));
        if (!WriteValidationErrors(errors))
        {
            return 1;
        }

        string repositoryRoot = Directory.GetParent(Path.GetDirectoryName(Path.GetFullPath(propsPath))!)!.FullName;
        string rawSource = await ClangSharpDriver.GenerateAsync(
            repositoryRoot,
            webGpuHeaderPath,
            wgpuHeaderPath,
            CancellationToken.None).ConfigureAwait(false);
        string generated = ClangSharpPostProcessor.Process(rawSource, webGpuHeaderPath, wgpuHeaderPath);
        string checkedIn = await File.ReadAllTextAsync(checkedInPath).ConfigureAwait(false);
        if (!string.Equals(generated, checkedIn, StringComparison.Ordinal))
        {
            Console.Error.WriteLine(
                $"error: Generated bindings differ from {checkedInPath}. Run generate-bindings and commit the result.");
            return 1;
        }

        Console.WriteLine($"Verified generated binding drift: {checkedInPath}");
        return 0;
    }

    private static async Task<int> VerifyNativeExportsAsync(
        string generatedBindingsPath,
        string nativeLibraryPath)
    {
        IReadOnlyList<string> errors = await NativeExportVerifier.VerifyAsync(
            generatedBindingsPath,
            nativeLibraryPath,
            CancellationToken.None).ConfigureAwait(false);
        if (!WriteValidationErrors(errors))
        {
            return 1;
        }

        Console.WriteLine(
            $"Verified {ClangSharpPostProcessor.ExpectedFunctionCount} native exports: {nativeLibraryPath}");
        return 0;
    }

    private static int VerifyWindowsD3D12Bridge(string nativeLibraryPath)
    {
        IReadOnlyList<string> errors = WindowsD3D12BridgeVerifier.Verify(nativeLibraryPath);
        if (!WriteValidationErrors(errors))
        {
            return 1;
        }

        Console.WriteLine(
            $"Verified Windows D3D12 composition bridge exports: {nativeLibraryPath}");
        return 0;
    }

    private static async Task<int> FetchArtifactAsync(
        string manifestPath,
        string propsPath,
        string rid,
        string configuration,
        string destinationDirectory)
    {
        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        if (!WriteValidationErrors(ManifestValidator.Validate(manifest, propsPath)))
        {
            return 1;
        }

        (string? path, string? error) = await ArtifactFetcher.FetchAsync(
            manifest,
            rid,
            configuration,
            destinationDirectory,
            CancellationToken.None).ConfigureAwait(false);
        if (error is not null)
        {
            Console.Error.WriteLine($"error: {error}");
            return 1;
        }

        Console.WriteLine($"Ready: {path}");
        return 0;
    }

    private static int ExtractBindingInput(
        string manifestPath,
        string propsPath,
        string rid,
        string configuration,
        string archivePath,
        string destinationDirectory)
    {
        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        if (!WriteValidationErrors(ManifestValidator.Validate(manifest, propsPath)))
        {
            return 1;
        }

        string? error = BindingInputExtractor.Extract(
            manifest,
            rid,
            configuration,
            archivePath,
            destinationDirectory);
        if (error is not null)
        {
            Console.Error.WriteLine($"error: {error}");
            return 1;
        }

        Console.WriteLine($"Extracted pinned binding inputs to {destinationDirectory}");
        return 0;
    }

    private static int StageRuntime(
        string manifestPath,
        string propsPath,
        string rid,
        string configuration,
        string archivePath,
        string stagingRoot)
    {
        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        if (!WriteValidationErrors(ManifestValidator.Validate(manifest, propsPath)))
        {
            return 1;
        }

        (string? path, string? error) = NativeRuntimeStager.Stage(
            manifest,
            rid,
            configuration,
            archivePath,
            stagingRoot);
        if (error is not null)
        {
            Console.Error.WriteLine($"error: {error}");
            return 1;
        }

        Console.WriteLine($"Staged: {path}");
        return 0;
    }

    private static async Task<int> PrepareRuntimesAsync(
        string manifestPath,
        string propsPath,
        string configuration,
        string? platformGroup,
        string downloadDirectory,
        string stagingRoot)
    {
        NativeAssetManifest manifest = ManifestIO.Read(manifestPath);
        if (!WriteValidationErrors(ManifestValidator.Validate(manifest, propsPath)))
        {
            return 1;
        }

        IReadOnlyList<string> errors = await RuntimeSetPreparer.PrepareAsync(
            manifest,
            configuration,
            platformGroup,
            downloadDirectory,
            stagingRoot,
            Console.WriteLine,
            CancellationToken.None).ConfigureAwait(false);
        if (!WriteValidationErrors(errors))
        {
            return 1;
        }

        int count = RuntimeSetPreparer.SelectArtifacts(
            manifest,
            configuration,
            platformGroup).Count;
        string scope = platformGroup is null ? "pinned" : platformGroup;
        Console.WriteLine($"Prepared {count} verified {scope} {configuration} runtimes.");
        return 0;
    }

    private static bool WriteValidationErrors(IReadOnlyList<string> errors)
    {
        foreach (string error in errors)
        {
            Console.Error.WriteLine($"error: {error}");
        }

        return errors.Count == 0;
    }

    private static int ShowUsage()
    {
        Console.Error.WriteLine("""
            Mu3D.WgpuGen maintainer tool

              validate-manifest <manifest.json> <Mu3D.WgpuNativeVersion.props>
              verify-archive <manifest.json> <rid> <release|debug> <archive.zip>
              generate-metadata <manifest.json> <Mu3D.WgpuNativeVersion.props> <output.cs>
              generate-bindings <manifest.json> <version.props> <webgpu.h> <wgpu.h> <output.cs>
              verify-bindings <manifest.json> <version.props> <webgpu.h> <wgpu.h> <checked-in.cs>
              verify-native-exports <generated-bindings.cs> <native-library>
              verify-windows-d3d12-bridge <wgpu_native.dll>
              fetch-artifact <manifest.json> <version.props> <rid> <release|debug> <destination>
              extract-binding-input <manifest.json> <version.props> <rid> <release|debug> <archive.zip> <destination>
              stage-runtime <manifest.json> <version.props> <rid> <release|debug> <archive.zip> <staging-root>
              prepare-runtimes <manifest.json> <version.props> <release|debug> <downloads> <staging-root>
              prepare-platform-runtimes <manifest.json> <version.props> <android|ios|maccatalyst|windows> <release|debug> <downloads> <staging-root>
            """);
        return 2;
    }
}

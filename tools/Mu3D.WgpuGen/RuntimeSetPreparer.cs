namespace Mu3D.WgpuGen;

internal static class RuntimeSetPreparer
{
    internal static IReadOnlyList<UpstreamArtifact> SelectArtifacts(
        NativeAssetManifest manifest,
        string configuration,
        string? platformGroup = null)
    {
        return manifest.Artifacts
            .Where(artifact =>
                artifact.Configuration == configuration &&
                IsInPlatformGroup(artifact.Platform, platformGroup))
            .OrderBy(artifact => artifact.Rid, StringComparer.Ordinal)
            .ToArray();
    }

    public static async Task<IReadOnlyList<string>> PrepareAsync(
        NativeAssetManifest manifest,
        string configuration,
        string? platformGroup,
        string downloadDirectory,
        string stagingRoot,
        Action<string>? reportProgress,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<UpstreamArtifact> artifacts = SelectArtifacts(
            manifest,
            configuration,
            platformGroup);
        if (artifacts.Count == 0)
        {
            string scope = platformGroup is null ? "all platforms" : platformGroup;
            return [$"No pinned native artifacts exist for '{scope}/{configuration}'."];
        }

        List<string> errors = [];
        foreach (UpstreamArtifact artifact in artifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reportProgress?.Invoke($"Preparing {artifact.Rid}/{configuration}...");
            (string? archivePath, string? fetchError) = await ArtifactFetcher.FetchAsync(
                manifest,
                artifact.Rid,
                configuration,
                downloadDirectory,
                cancellationToken).ConfigureAwait(false);
            if (fetchError is not null)
            {
                errors.Add(fetchError);
                continue;
            }

            (string? stagedPath, string? stageError) = NativeRuntimeStager.Stage(
                manifest,
                artifact.Rid,
                configuration,
                archivePath!,
                stagingRoot);
            if (stageError is not null)
            {
                errors.Add(stageError);
                continue;
            }

            reportProgress?.Invoke($"Staged {stagedPath}");
        }

        return errors;
    }

    private static bool IsInPlatformGroup(string platform, string? platformGroup)
    {
        return platformGroup switch
        {
            null => true,
            "android" => platform == "android",
            "ios" => platform is "ios" or "ios-simulator",
            "maccatalyst" => platform == "maccatalyst",
            "windows" => platform == "windows",
            _ => false,
        };
    }
}

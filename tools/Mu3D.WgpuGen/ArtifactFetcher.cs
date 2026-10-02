using System.Net.Http.Headers;
using System.Text.Json;

namespace Mu3D.WgpuGen;

internal static class ArtifactFetcher
{
    public static async Task<(string? Path, string? Error)> FetchAsync(
        NativeAssetManifest manifest,
        string rid,
        string configuration,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        UpstreamArtifact? artifact = manifest.Artifacts.SingleOrDefault(x =>
            x.Rid == rid && x.Configuration == configuration);
        if (artifact is null)
        {
            return (null, $"No pinned artifact exists for '{rid}/{configuration}'.");
        }

        Directory.CreateDirectory(destinationDirectory);
        string destinationPath = Path.Combine(destinationDirectory, artifact.Archive);
        if (File.Exists(destinationPath))
        {
            string? cachedError = ArchiveVerifier.Verify(manifest, rid, configuration, destinationPath);
            if (cachedError is null)
            {
                return (destinationPath, null);
            }

            return (null, $"Cached artifact is invalid and will not be overwritten: {cachedError}");
        }

        string temporaryPath = $"{destinationPath}.partial-{Guid.NewGuid():N}";
        try
        {
            using HttpClient client = new();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mu3D.WgpuGen/0.1");
            string? githubToken = Environment.GetEnvironmentVariable("MU3D_GITHUB_TOKEN");
            bool useAuthenticatedDistributionApi = artifact.Url.StartsWith(
                    $"{manifest.Distribution.ReleaseBaseUrl}/",
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(githubToken);
            string downloadUrl = artifact.Url;
            if (useAuthenticatedDistributionApi)
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", githubToken);
                client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
                downloadUrl = await ResolvePrivateAssetApiUrlAsync(
                    client,
                    manifest.Distribution,
                    artifact.Archive,
                    cancellationToken).ConfigureAwait(false);
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/octet-stream"));
            }

            using HttpResponseMessage response = await client.GetAsync(
                downloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream destination = File.Create(temporaryPath))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            string? error = ArchiveVerifier.Verify(manifest, rid, configuration, temporaryPath);
            if (error is not null)
            {
                return (null, error);
            }

            File.Move(temporaryPath, destinationPath);
            return (destinationPath, null);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<string> ResolvePrivateAssetApiUrlAsync(
        HttpClient client,
        Mu3DDistribution distribution,
        string archiveName,
        CancellationToken cancellationToken)
    {
        string releaseUrl =
            $"https://api.github.com/repos/{distribution.Repository}/releases/tags/{Uri.EscapeDataString(distribution.Tag)}";
        using HttpResponseMessage releaseResponse = await client.GetAsync(
            releaseUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        releaseResponse.EnsureSuccessStatusCode();
        await using Stream releaseStream = await releaseResponse.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using JsonDocument release = await JsonDocument.ParseAsync(
            releaseStream,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        foreach (JsonElement asset in release.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() == archiveName)
            {
                return asset.GetProperty("url").GetString()
                    ?? throw new InvalidDataException($"GitHub asset '{archiveName}' has no API URL.");
            }
        }

        throw new InvalidDataException(
            $"GitHub release '{distribution.Tag}' does not contain asset '{archiveName}'.");
    }
}

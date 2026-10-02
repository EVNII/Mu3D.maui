using System.IO.Compression;

namespace Mu3D.WgpuGen;

internal static class BindingInputExtractor
{
    private const string MetadataPath = "wgpu-native-meta/webgpu.yml";

    public static string? Extract(
        NativeAssetManifest manifest,
        string rid,
        string configuration,
        string archivePath,
        string destinationDirectory)
    {
        UpstreamArtifact? artifact = manifest.Artifacts.SingleOrDefault(x =>
            x.Rid == rid && x.Configuration == configuration);
        if (artifact is null)
        {
            return $"No pinned artifact exists for '{rid}/{configuration}'.";
        }

        if (artifact.Archive != manifest.Upstream.HeaderSourceArchive)
        {
            return $"'{artifact.Archive}' is not the pinned binding-input archive '{manifest.Upstream.HeaderSourceArchive}'.";
        }

        string? verificationError = ArchiveVerifier.Verify(manifest, rid, configuration, archivePath);
        if (verificationError is not null)
        {
            return verificationError;
        }

        Directory.CreateDirectory(destinationDirectory);
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (string entryPath in manifest.Upstream.Headers.Append(MetadataPath))
        {
            ZipArchiveEntry? entry = archive.GetEntry(entryPath);
            if (entry is null)
            {
                return $"Verified archive is missing required entry '{entryPath}'.";
            }

            string outputPath = Path.Combine(destinationDirectory, Path.GetFileName(entryPath));
            using Stream source = entry.Open();
            using FileStream destination = File.Create(outputPath);
            source.CopyTo(destination);
        }

        return null;
    }
}

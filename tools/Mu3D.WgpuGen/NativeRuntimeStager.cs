using System.IO.Compression;

namespace Mu3D.WgpuGen;

internal static class NativeRuntimeStager
{
    public static (string? Path, string? Error) Stage(
        NativeAssetManifest manifest,
        string rid,
        string configuration,
        string archivePath,
        string stagingRoot)
    {
        UpstreamArtifact? artifact = manifest.Artifacts.SingleOrDefault(x =>
            x.Rid == rid && x.Configuration == configuration);
        if (artifact is null)
        {
            return (null, $"No pinned artifact exists for '{rid}/{configuration}'.");
        }

        string? verificationError = ArchiveVerifier.Verify(manifest, rid, configuration, archivePath);
        if (verificationError is not null)
        {
            return (null, verificationError);
        }

        (string entryPath, string outputName) = artifact.Platform switch
        {
            "android" => ("lib/libwgpu_native.so", "libwgpu_native.so"),
            "windows" => ("lib/wgpu_native.dll", "wgpu_native.dll"),
            "ios" or "ios-simulator" or "maccatalyst" =>
                ("lib/libwgpu_native.a", "libwgpu_native.a"),
            _ => (string.Empty, string.Empty),
        };

        if (entryPath.Length == 0)
        {
            return (null, $"No native staging rule exists for platform '{artifact.Platform}'.");
        }

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry? entry = archive.GetEntry(entryPath);
        if (entry is null)
        {
            return (null, $"Verified archive is missing expected runtime '{entryPath}'.");
        }

        string outputDirectory = Path.Combine(
            stagingRoot,
            manifest.Upstream.Tag,
            configuration,
            "runtimes",
            rid,
            "native");
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, outputName);
        using Stream source = entry.Open();
        using FileStream destination = File.Create(outputPath);
        source.CopyTo(destination);
        return (outputPath, null);
    }
}

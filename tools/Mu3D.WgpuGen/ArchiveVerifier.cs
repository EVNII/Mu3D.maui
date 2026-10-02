using System.Security.Cryptography;

namespace Mu3D.WgpuGen;

internal static class ArchiveVerifier
{
    public static string? Verify(
        NativeAssetManifest manifest,
        string rid,
        string configuration,
        string archivePath)
    {
        UpstreamArtifact? artifact = manifest.Artifacts.SingleOrDefault(x =>
            x.Rid == rid && x.Configuration == configuration);

        if (artifact is null)
        {
            return $"No pinned artifact exists for '{rid}/{configuration}'.";
        }

        FileInfo file = new(archivePath);
        if (!file.Exists)
        {
            return $"Archive '{archivePath}' does not exist.";
        }

        if (file.Length != artifact.Size)
        {
            return $"Archive size mismatch: expected {artifact.Size}, got {file.Length}.";
        }

        using FileStream stream = file.OpenRead();
        string actualHash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return actualHash == artifact.Sha256
            ? null
            : $"Archive SHA-256 mismatch: expected {artifact.Sha256}, got {actualHash}.";
    }
}

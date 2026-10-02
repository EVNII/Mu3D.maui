using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Mu3D.WgpuGen;

internal static partial class ManifestValidator
{
    private static readonly string[] ExpectedUpstreamRids =
    [
        "android-arm",
        "android-arm64",
        "android-x64",
        "android-x86",
        "ios-arm64",
        "iossimulator-arm64",
        "iossimulator-x64",
        "win-arm64",
        "win-x64",
        "win-x86",
    ];

    private static readonly string[] ExpectedMacCatalystRids =
    [
        "maccatalyst-arm64",
        "maccatalyst-x64",
    ];

    public static IReadOnlyList<string> Validate(NativeAssetManifest manifest, string versionPropsPath)
    {
        List<string> errors = [];

        if (manifest.SchemaVersion != 2)
        {
            errors.Add($"Unsupported schemaVersion '{manifest.SchemaVersion}'.");
        }

        if (manifest.Upstream.Repository != "gfx-rs/wgpu-native")
        {
            errors.Add("The upstream repository must be gfx-rs/wgpu-native.");
        }

        if (!VersionRegex().IsMatch(manifest.Upstream.Tag))
        {
            errors.Add("The upstream tag is not a four-component wgpu-native release tag.");
        }

        if (!Sha256Regex().IsMatch(manifest.Upstream.Commit) || manifest.Upstream.Commit.Length != 40)
        {
            errors.Add("The upstream commit must be a lowercase 40-character Git commit hash.");
        }

        ValidateVersionProps(manifest, versionPropsPath, errors);

        if (manifest.Policy.ConsumerVersionOverrideAllowed)
        {
            errors.Add("Consumers must not be allowed to override the native ABI version.");
        }

        if (manifest.Policy.DefaultRuntimeConfiguration != "release")
        {
            errors.Add("The public backend must default to the Release native runtime.");
        }

        if (!manifest.Policy.DebugRuntimeIsOptIn)
        {
            errors.Add("Debug native runtimes must remain opt-in.");
        }

        string expectedBaseUrl = $"https://github.com/{manifest.Upstream.Repository}/releases/download/{manifest.Upstream.Tag}";
        if (manifest.Upstream.ReleaseBaseUrl != expectedBaseUrl)
        {
            errors.Add("releaseBaseUrl does not match repository and tag.");
        }

        const string expectedDistributionRepository = "EVNII/Mu3d";
        string expectedDistributionTagPrefix = $"wgpu-native-{manifest.Upstream.Tag}-mu3d.";
        string expectedDistributionBaseUrl =
            $"https://github.com/{expectedDistributionRepository}/releases/download/{manifest.Distribution.Tag}";
        bool hasValidDistributionRevision =
            manifest.Distribution.Tag.StartsWith(expectedDistributionTagPrefix, StringComparison.Ordinal) &&
            int.TryParse(manifest.Distribution.Tag[expectedDistributionTagPrefix.Length..], out int revision) &&
            revision > 0;
        if (manifest.Distribution.Repository != expectedDistributionRepository ||
            !hasValidDistributionRevision ||
            manifest.Distribution.ReleaseBaseUrl != expectedDistributionBaseUrl)
        {
            errors.Add("Mu3D distribution repository, tag, or release URL is not pinned correctly.");
        }

        if (manifest.Upstream.Headers is not ["include/webgpu/webgpu.h", "include/webgpu/wgpu.h"])
        {
            errors.Add("Binding input must contain the pinned webgpu.h followed by wgpu.h.");
        }

        HashSet<string> artifactKeys = new(StringComparer.Ordinal);
        foreach (UpstreamArtifact artifact in manifest.Artifacts)
        {
            string key = $"{artifact.Rid}/{artifact.Configuration}";
            if (!artifactKeys.Add(key))
            {
                errors.Add($"Duplicate artifact '{key}'.");
            }

            if (artifact.Configuration is not ("release" or "debug"))
            {
                errors.Add($"Artifact '{key}' has an invalid configuration.");
            }

            if (artifact.PreferredLinkage is not ("shared" or "static"))
            {
                errors.Add($"Artifact '{key}' has an invalid preferred linkage.");
            }

            if (!Sha256Regex().IsMatch(artifact.Sha256) || artifact.Sha256.Length != 64)
            {
                errors.Add($"Artifact '{key}' has an invalid SHA-256.");
            }

            if (artifact.Size <= 0)
            {
                errors.Add($"Artifact '{key}' has an invalid size.");
            }

            string artifactBaseUrl = artifact.Platform == "maccatalyst"
                ? expectedDistributionBaseUrl
                : expectedBaseUrl;
            if (artifact.Url != $"{artifactBaseUrl}/{artifact.Archive}")
            {
                errors.Add($"Artifact '{key}' URL does not match its archive and pinned release.");
            }

            if (artifact.Platform == "macos" || artifact.Rid.StartsWith("osx-", StringComparison.Ordinal))
            {
                errors.Add($"Artifact '{key}' incorrectly treats macOS as Mac Catalyst.");
            }
        }

        foreach (string rid in ExpectedUpstreamRids)
        {
            RequirePair(artifactKeys, rid, errors, "upstream");
        }

        foreach (string rid in ExpectedMacCatalystRids)
        {
            RequirePair(artifactKeys, rid, errors, "Mu3D distribution");
        }

        if (manifest.Artifacts.Count !=
            (ExpectedUpstreamRids.Length + ExpectedMacCatalystRids.Length) * 2)
        {
            errors.Add("The manifest contains unexpected native target/configuration entries.");
        }

        if (!manifest.Artifacts.Any(x =>
                x.Archive == manifest.Upstream.HeaderSourceArchive && x.Configuration == "release"))
        {
            errors.Add("headerSourceArchive must name a pinned Release artifact.");
        }

        if (manifest.Artifacts.Where(x => x.Rid.StartsWith("maccatalyst-", StringComparison.Ordinal)).Any(x =>
                x.Platform != "maccatalyst" ||
                x.Configuration is not ("release" or "debug") ||
                x.PreferredLinkage != "static"))
        {
            errors.Add("Mac Catalyst distribution entries must be static macabi artifacts.");
        }

        return errors;
    }

    private static void RequirePair(HashSet<string> keys, string rid, List<string> errors, string source)
    {
        foreach (string configuration in new[] { "release", "debug" })
        {
            if (!keys.Contains($"{rid}/{configuration}"))
            {
                errors.Add($"Missing {source} artifact '{rid}/{configuration}'.");
            }
        }
    }

    private static void ValidateVersionProps(
        NativeAssetManifest manifest,
        string versionPropsPath,
        List<string> errors)
    {
        XDocument document = XDocument.Load(versionPropsPath);
        string? version = document.Descendants("Mu3DWgpuNativeVersion").SingleOrDefault()?.Value;
        string? commit = document.Descendants("Mu3DWgpuNativeCommit").SingleOrDefault()?.Value;
        string? pinnedVersion = document.Descendants("_Mu3DPinnedWgpuNativeVersion")
            .SingleOrDefault()?.Attribute("Include")?.Value;
        string? pinnedCommit = document.Descendants("_Mu3DPinnedWgpuNativeCommit")
            .SingleOrDefault()?.Attribute("Include")?.Value;

        if (version != manifest.Upstream.Tag)
        {
            errors.Add("Manifest tag does not match Mu3D.WgpuNativeVersion.props.");
        }

        if (commit != manifest.Upstream.Commit)
        {
            errors.Add("Manifest commit does not match Mu3D.WgpuNativeVersion.props.");
        }

        if (pinnedVersion != manifest.Upstream.Tag || pinnedCommit != manifest.Upstream.Commit)
        {
            errors.Add("The non-overridable MSBuild ABI lock does not match the manifest.");
        }
    }

    [GeneratedRegex("^[0-9a-f]+$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex("^v[0-9]+\\.[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();
}

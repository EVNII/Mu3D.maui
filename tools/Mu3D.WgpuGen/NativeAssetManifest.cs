namespace Mu3D.WgpuGen;

internal sealed class NativeAssetManifest
{
    public int SchemaVersion { get; init; }

    public UpstreamRelease Upstream { get; init; } = new();

    public Mu3DDistribution Distribution { get; init; } = new();

    public RuntimePolicy Policy { get; init; } = new();

    public List<UpstreamArtifact> Artifacts { get; init; } = [];
}

internal sealed class Mu3DDistribution
{
    public string Repository { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string ReleaseBaseUrl { get; init; } = string.Empty;
}

internal sealed class UpstreamRelease
{
    public string Repository { get; init; } = string.Empty;

    public string Tag { get; init; } = string.Empty;

    public string Commit { get; init; } = string.Empty;

    public string ReleaseBaseUrl { get; init; } = string.Empty;

    public string HeaderSourceArchive { get; init; } = string.Empty;

    public List<string> Headers { get; init; } = [];
}

internal sealed class RuntimePolicy
{
    public bool ConsumerVersionOverrideAllowed { get; init; }

    public string DefaultRuntimeConfiguration { get; init; } = string.Empty;

    public bool DebugRuntimeIsOptIn { get; init; }
}

internal sealed class UpstreamArtifact
{
    public string Rid { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public string Architecture { get; init; } = string.Empty;

    public string Configuration { get; init; } = string.Empty;

    public string PreferredLinkage { get; init; } = string.Empty;

    public string Archive { get; init; } = string.Empty;

    public long Size { get; init; }

    public string Sha256 { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;
}

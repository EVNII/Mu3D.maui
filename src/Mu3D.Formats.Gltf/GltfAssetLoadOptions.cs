namespace Mu3D.Formats.Gltf;

/// <summary>Configures bounded asynchronous loading of one glTF or GLB source stream.</summary>
public sealed class GltfAssetLoadOptions
{
    /// <summary>
    /// Gets the maximum number of source bytes read from the stream. The default is 256 MiB.
    /// External resources returned by application resolvers are not included in this limit.
    /// </summary>
    public int MaximumSourceByteCount { get; init; } = GltfAssetLoader.DefaultMaximumSourceByteCount;

    /// <summary>
    /// Gets the optional application-owned resolver for external buffer and image URIs. The loader
    /// deduplicates exact URI strings and owns each returned stream. This resolver cannot be combined
    /// with the synchronous external resolvers on <see cref="ImportOptions"/>.
    /// </summary>
    public GltfExternalResourceStreamResolver? ExternalResourceResolver { get; init; }

    /// <summary>Gets the maximum bytes read from one external resource. The default is 64 MiB.</summary>
    public int MaximumExternalResourceByteCount { get; init; } =
        GltfAssetLoader.DefaultMaximumExternalResourceByteCount;

    /// <summary>
    /// Gets the maximum combined bytes read from all unique external resources. The default is
    /// 256 MiB. The main glTF/GLB source is accounted separately by
    /// <see cref="MaximumSourceByteCount"/>.
    /// </summary>
    public int MaximumTotalExternalResourceByteCount { get; init; } =
        GltfAssetLoader.DefaultMaximumTotalExternalResourceByteCount;

    /// <summary>
    /// Gets an optional application-owned external-resource cache. The loader borrows this cache,
    /// uses exact URI keys and never clears or disposes it. A resolver is required only for cache
    /// misses.
    /// </summary>
    public GltfExternalResourceCache? ExternalResourceCache { get; init; }

    /// <summary>
    /// Gets which exact encoded source bytes remain attached to the returned asset. The default
    /// does not retain source bytes after import. Retaining external resources uses the asynchronous
    /// resolver/cache path and cannot be combined with synchronous import resolvers.
    /// </summary>
    public GltfSourceRetentionMode SourceRetention { get; init; }

    /// <summary>
    /// Gets the maximum combined bytes retained in the returned source archive. The default is
    /// 256 MiB. This independent limit is used only when <see cref="SourceRetention"/> is enabled.
    /// </summary>
    public int MaximumRetainedSourceByteCount { get; init; } =
        GltfAssetLoader.DefaultMaximumRetainedSourceByteCount;

    /// <summary>Gets the services and destination capabilities used by the importer.</summary>
    public GltfImportOptions ImportOptions { get; init; } = new();
}

using System.Buffers;

namespace Mu3D.Formats.Gltf;

/// <summary>
/// Provides bounded asynchronous stream loading for backend-independent glTF assets.
/// </summary>
public static class GltfAssetLoader
{
    /// <summary>Gets the default maximum source size, in bytes (256 MiB).</summary>
    public const int DefaultMaximumSourceByteCount = 256 * 1024 * 1024;

    /// <summary>Gets the default maximum size of one external resource (64 MiB).</summary>
    public const int DefaultMaximumExternalResourceByteCount = 64 * 1024 * 1024;

    /// <summary>Gets the default maximum combined external-resource size (256 MiB).</summary>
    public const int DefaultMaximumTotalExternalResourceByteCount = 256 * 1024 * 1024;

    /// <summary>Gets the default maximum retained encoded-source size (256 MiB).</summary>
    public const int DefaultMaximumRetainedSourceByteCount = 256 * 1024 * 1024;

    private const int ReadBufferSize = 80 * 1024;

    /// <summary>
    /// Asynchronously reads and imports one JSON glTF or binary GLB stream. The caller retains
    /// ownership of <paramref name="source"/> and the stream remains open after completion,
    /// cancellation or failure.
    /// </summary>
    /// <param name="source">A readable stream positioned at the start of the glTF source.</param>
    /// <param name="options">Optional limits, cache, source-retention policy and import services.</param>
    /// <param name="cancellationToken">Cancels stream reading and cooperative import work.</param>
    /// <returns>The imported backend-independent asset.</returns>
    /// <exception cref="ArgumentException">The stream is not readable.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A byte limit is not positive or the source-retention mode is invalid.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// A source exceeds its configured limit or an external resource cannot be resolved.
    /// </exception>
    public static async Task<GltfAsset> LoadAsync(
        Stream source,
        GltfAssetLoadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The glTF source stream must be readable.", nameof(source));
        }

        options ??= new GltfAssetLoadOptions();
        if (options.MaximumSourceByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumSourceByteCount),
                options.MaximumSourceByteCount,
                "The maximum glTF source byte count must be positive.");
        }
        ArgumentNullException.ThrowIfNull(options.ImportOptions);
        ValidateExternalOptions(options);
        cancellationToken.ThrowIfCancellationRequested();

        using MemoryStream bufferedSource = await ReadBoundedAsync(
            source,
            options.MaximumSourceByteCount,
            "The glTF source",
            cancellationToken).ConfigureAwait(false);
        if (bufferedSource.Length == 0)
        {
            throw new ArgumentException("glTF input cannot be empty.", nameof(source));
        }

        ReadOnlyMemory<byte> bytes = AsMemory(bufferedSource);
        GltfImportOptions importOptions = options.ImportOptions;
        List<MemoryStream>? externalOwners = null;
        Dictionary<string, ReadOnlyMemory<byte>>? externalResources = null;
        if (options.ExternalResourceResolver is not null ||
            options.ExternalResourceCache is not null ||
            options.SourceRetention == GltfSourceRetentionMode.MainSourceAndExternalResources)
        {
            IReadOnlyList<string> uris = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return GltfImporter.ReadExternalResourceUris(bytes);
                },
                cancellationToken).ConfigureAwait(false);
            externalOwners = new List<MemoryStream>(uris.Count);
            externalResources = new Dictionary<string, ReadOnlyMemory<byte>>(
                uris.Count,
                StringComparer.Ordinal);
            int totalExternalByteCount = 0;
            try
            {
                foreach (string uri in uris)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int remainingTotal = options.MaximumTotalExternalResourceByteCount -
                        totalExternalByteCount;
                    if (remainingTotal <= 0)
                    {
                        throw new InvalidDataException(
                            "The glTF external resources exceed the configured aggregate byte limit.");
                    }

                    if (options.ExternalResourceCache?.TryGet(uri, out ReadOnlyMemory<byte> cached) ==
                        true)
                    {
                        ValidateExternalResourceSize(
                            uri,
                            cached,
                            options.MaximumExternalResourceByteCount,
                            remainingTotal);
                        externalResources.Add(uri, cached);
                        totalExternalByteCount = checked(totalExternalByteCount + cached.Length);
                        continue;
                    }

                    GltfExternalResourceStreamResolver resolver =
                        options.ExternalResourceResolver ??
                        throw new InvalidDataException(
                            $"External glTF resource '{uri}' was not cached and no resolver was provided.");
                    Stream resolved = await resolver(uri, cancellationToken).ConfigureAwait(false) ??
                        throw new InvalidDataException(
                            $"The external glTF resolver returned no stream for '{uri}'.");
                    await using (resolved.ConfigureAwait(false))
                    {
                        if (!resolved.CanRead)
                        {
                            throw new InvalidDataException(
                                $"The external glTF resolver returned an unreadable stream for '{uri}'.");
                        }
                        int resourceLimit = Math.Min(
                            options.MaximumExternalResourceByteCount,
                            remainingTotal);
                        MemoryStream bufferedResource = await ReadBoundedAsync(
                            resolved,
                            resourceLimit,
                            $"External glTF resource '{uri}'",
                            cancellationToken).ConfigureAwait(false);
                        if (bufferedResource.Length == 0)
                        {
                            bufferedResource.Dispose();
                            throw new InvalidDataException(
                                $"External glTF resource '{uri}' is empty.");
                        }
                        externalOwners.Add(bufferedResource);
                        ReadOnlyMemory<byte> resourceBytes = AsMemory(bufferedResource);
                        externalResources.Add(uri, resourceBytes);
                        totalExternalByteCount = checked(
                            totalExternalByteCount + resourceBytes.Length);
                        _ = options.ExternalResourceCache?.TryAdd(uri, resourceBytes);
                    }
                }

                importOptions = CopyImportOptionsWithExternalResources(
                    importOptions,
                    externalResources);
            }
            catch
            {
                DisposeAll(externalOwners);
                throw;
            }
        }

        try
        {
            ValidateRetainedSourceSize(options, bytes, externalResources);
            GltfAsset asset = await Task.Run(
                () => GltfImporter.ImportAssetCore(bytes, importOptions, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (options.SourceRetention != GltfSourceRetentionMode.None)
            {
                IReadOnlyDictionary<string, ReadOnlyMemory<byte>> retainedExternalResources =
                    options.SourceRetention ==
                        GltfSourceRetentionMode.MainSourceAndExternalResources &&
                    externalResources is not null
                        ? externalResources
                        : EmptyExternalResources;
                asset.AttachSourceArchive(new GltfSourceArchive(bytes, retainedExternalResources));
            }
            return asset;
        }
        finally
        {
            if (externalOwners is not null)
            {
                DisposeAll(externalOwners);
            }
        }
    }

    private static async Task<MemoryStream> ReadBoundedAsync(
        Stream source,
        int maximumByteCount,
        string description,
        CancellationToken cancellationToken)
    {
        int initialCapacity = GetInitialCapacity(source, maximumByteCount, description);
        MemoryStream bufferedSource = new(initialCapacity);
        byte[] readBuffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
        try
        {
            while (true)
            {
                int read = await source.ReadAsync(
                    readBuffer.AsMemory(0, ReadBufferSize),
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (read == 0)
                {
                    break;
                }
                if (bufferedSource.Length > maximumByteCount - read)
                {
                    throw new InvalidDataException(
                        $"{description} exceeds the configured {maximumByteCount} byte limit.");
                }
                bufferedSource.Write(readBuffer, 0, read);
            }

            return bufferedSource;
        }
        catch
        {
            bufferedSource.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer);
        }
    }

    private static int GetInitialCapacity(
        Stream source,
        int maximumByteCount,
        string description)
    {
        if (!source.CanSeek)
        {
            return Math.Min(ReadBufferSize, maximumByteCount);
        }

        try
        {
            long remaining = source.Length - source.Position;
            if (remaining > maximumByteCount)
            {
                throw new InvalidDataException(
                    $"{description} exceeds the configured {maximumByteCount} byte limit.");
            }
            if (remaining > 0)
            {
                return checked((int)remaining);
            }
        }
        catch (NotSupportedException)
        {
        }

        return Math.Min(ReadBufferSize, maximumByteCount);
    }

    private static void ValidateExternalOptions(GltfAssetLoadOptions options)
    {
        if (options.MaximumExternalResourceByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumExternalResourceByteCount),
                options.MaximumExternalResourceByteCount,
                "The maximum external glTF resource byte count must be positive.");
        }
        if (options.MaximumTotalExternalResourceByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumTotalExternalResourceByteCount),
                options.MaximumTotalExternalResourceByteCount,
                "The maximum total external glTF resource byte count must be positive.");
        }
        if (!Enum.IsDefined(options.SourceRetention))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.SourceRetention),
                options.SourceRetention,
                "The glTF source retention mode is not supported.");
        }
        if (options.MaximumRetainedSourceByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.MaximumRetainedSourceByteCount),
                options.MaximumRetainedSourceByteCount,
                "The maximum retained glTF source byte count must be positive.");
        }
        if ((options.ExternalResourceResolver is not null ||
             options.ExternalResourceCache is not null ||
             options.SourceRetention == GltfSourceRetentionMode.MainSourceAndExternalResources) &&
            (options.ImportOptions.ExternalBufferResolver is not null ||
             options.ImportOptions.ExternalImageResolver is not null))
        {
            throw new ArgumentException(
                "Asynchronous external-resource loading, caching or retention cannot be combined " +
                "with synchronous import resolvers.",
                nameof(options));
        }
    }

    private static void ValidateExternalResourceSize(
        string uri,
        ReadOnlyMemory<byte> source,
        int maximumResourceByteCount,
        int remainingTotalByteCount)
    {
        if (source.IsEmpty)
        {
            throw new InvalidDataException($"External glTF resource '{uri}' is empty.");
        }
        if (source.Length > maximumResourceByteCount)
        {
            throw new InvalidDataException(
                $"External glTF resource '{uri}' exceeds the configured " +
                $"{maximumResourceByteCount} byte limit.");
        }
        if (source.Length > remainingTotalByteCount)
        {
            throw new InvalidDataException(
                "The glTF external resources exceed the configured aggregate byte limit.");
        }
    }

    private static void ValidateRetainedSourceSize(
        GltfAssetLoadOptions options,
        ReadOnlyMemory<byte> mainSource,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>>? externalResources)
    {
        if (options.SourceRetention == GltfSourceRetentionMode.None)
        {
            return;
        }
        long retainedByteCount = mainSource.Length;
        if (options.SourceRetention == GltfSourceRetentionMode.MainSourceAndExternalResources &&
            externalResources is not null)
        {
            foreach (ReadOnlyMemory<byte> source in externalResources.Values)
            {
                retainedByteCount = checked(retainedByteCount + source.Length);
            }
        }
        if (retainedByteCount > options.MaximumRetainedSourceByteCount)
        {
            throw new InvalidDataException(
                "The retained glTF source archive exceeds the configured " +
                $"{options.MaximumRetainedSourceByteCount} byte limit.");
        }
    }

    private static GltfImportOptions CopyImportOptionsWithExternalResources(
        GltfImportOptions source,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> resources) => new()
        {
            ExternalBufferResolver = uri => ResolveExternalResource(resources, uri),
            ExternalImageResolver = uri => ResolveExternalResource(resources, uri),
            Name = source.Name,
            ImageDecoder = source.ImageDecoder,
            TextureTranscoder = source.TextureTranscoder,
            GraphicsCapabilities = source.GraphicsCapabilities,
        };

    private static ReadOnlyMemory<byte> ResolveExternalResource(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> resources,
        string uri) => resources.TryGetValue(uri, out ReadOnlyMemory<byte> bytes)
            ? bytes
            : throw new InvalidDataException($"External glTF resource '{uri}' was not preloaded.");

    private static ReadOnlyMemory<byte> AsMemory(MemoryStream stream) =>
        stream.GetBuffer().AsMemory(0, checked((int)stream.Length));

    private static void DisposeAll(IEnumerable<MemoryStream> streams)
    {
        foreach (MemoryStream stream in streams)
        {
            stream.Dispose();
        }
    }

    private static readonly IReadOnlyDictionary<string, ReadOnlyMemory<byte>>
        EmptyExternalResources = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
}

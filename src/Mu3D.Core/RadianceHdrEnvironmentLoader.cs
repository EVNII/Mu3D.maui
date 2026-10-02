using System.Buffers;

namespace Mu3D.SceneGraph;

/// <summary>Loads bounded Radiance HDR environment assets from caller streams or keyed resolvers.</summary>
public static class RadianceHdrEnvironmentLoader
{
    /// <summary>Gets the default maximum encoded source size (256 MiB).</summary>
    public const int DefaultMaximumSourceByteCount = 256 * 1024 * 1024;

    /// <summary>Gets the default maximum decoded FP32 RGB output size (256 MiB).</summary>
    public const long DefaultMaximumOutputByteCount = 256L * 1024 * 1024;

    /// <summary>
    /// Loads from a caller-owned stream. The loader reads from the current position and leaves the
    /// stream open.
    /// </summary>
    public static async Task<RadianceHdrEnvironmentAsset> LoadAsync(
        Stream stream,
        RadianceHdrEnvironmentLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The Radiance source stream must be readable.", nameof(stream));
        }

        byte[] source = await ReadBoundedAsync(
            stream,
            options.MaximumSourceByteCount,
            cancellationToken).ConfigureAwait(false);
        return await DecodeAsync(source, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads an exact application-defined key. A cache hit skips the resolver. A cache miss owns and
    /// closes the resolver stream, and successful cache insertion is independent of asset retention.
    /// </summary>
    public static async Task<RadianceHdrEnvironmentAsset> LoadAsync(
        string sourceKey,
        RadianceHdrSourceStreamResolver sourceResolver,
        RadianceHdrSourceCache? sourceCache,
        RadianceHdrEnvironmentLoadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentNullException.ThrowIfNull(sourceResolver);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);

        byte[] source;
        bool addToCache = false;
        if (sourceCache is not null && sourceCache.TryGetCopy(sourceKey, out byte[] cached))
        {
            if (cached.Length > options.MaximumSourceByteCount)
            {
                throw new InvalidDataException(
                    $"The cached Radiance source contains {cached.Length} bytes, exceeding the configured {options.MaximumSourceByteCount}-byte source limit.");
            }
            source = cached;
        }
        else
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using Stream resolved = await sourceResolver(cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                throw new InvalidOperationException("The Radiance source resolver returned a null stream.");
            }
            if (!resolved.CanRead)
            {
                throw new InvalidDataException("The Radiance source resolver returned an unreadable stream.");
            }
            source = await ReadBoundedAsync(
                resolved,
                options.MaximumSourceByteCount,
                cancellationToken).ConfigureAwait(false);
            addToCache = sourceCache is not null;
        }

        RadianceHdrEnvironmentAsset asset = await DecodeAsync(
            source,
            options,
            cancellationToken).ConfigureAwait(false);
        if (addToCache)
        {
            _ = sourceCache!.TryAdd(sourceKey, source);
        }
        return asset;
    }

    private static async Task<RadianceHdrEnvironmentAsset> DecodeAsync(
        byte[] source,
        RadianceHdrEnvironmentLoadOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EquirectangularHdrEnvironment environment = await Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(source, writable: false);
                EquirectangularHdrEnvironment decoded = RadianceHdrReader.Read(
                    stream,
                    options.ColorSpace,
                    options.Name,
                    options.MaximumOutputByteCount);
                cancellationToken.ThrowIfCancellationRequested();
                return decoded;
            },
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return new RadianceHdrEnvironmentAsset(
            environment,
            options.RetainEncodedSource ? source.ToArray() : null);
    }

    private static async Task<byte[]> ReadBoundedAsync(
        Stream stream,
        int maximumByteCount,
        CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            long remainingByteCount = checked(stream.Length - stream.Position);
            if (remainingByteCount < 0)
            {
                throw new InvalidDataException("The Radiance source stream position exceeds its length.");
            }
            if (remainingByteCount > maximumByteCount)
            {
                throw new InvalidDataException(
                    $"The Radiance source exceeds the configured {maximumByteCount}-byte source limit.");
            }

            byte[] exactSource = GC.AllocateUninitializedArray<byte>(checked((int)remainingByteCount));
            int totalRead = 0;
            while (totalRead < exactSource.Length)
            {
                int read = await stream.ReadAsync(
                    exactSource.AsMemory(totalRead),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    Array.Resize(ref exactSource, totalRead);
                    break;
                }
                totalRead += read;
            }
            return exactSource;
        }

        using var destination = new MemoryStream();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                if (destination.Length + read > maximumByteCount)
                {
                    throw new InvalidDataException(
                        $"The Radiance source exceeds the configured {maximumByteCount}-byte source limit.");
                }
                destination.Write(buffer, 0, read);
            }
            return destination.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ValidateOptions(RadianceHdrEnvironmentLoadOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumSourceByteCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaximumOutputByteCount);
    }
}

namespace Mu3D.Formats.Gltf;

/// <summary>
/// Provides an application-owned, bounded cache of exact external glTF buffer and image bytes.
/// </summary>
/// <remarks>
/// Scope one cache to sources that share the same URI identity. The loader borrows the cache and
/// never clears or disposes it. Cache entries do not bypass loader size limits.
/// </remarks>
public sealed class GltfExternalResourceCache
{
    private readonly object sync = new();
    private readonly Dictionary<string, byte[]> resources = new(StringComparer.Ordinal);
    private long totalByteCount;

    /// <summary>Initializes a cache with the default 256 MiB capacity.</summary>
    public GltfExternalResourceCache()
        : this(GltfAssetLoader.DefaultMaximumTotalExternalResourceByteCount)
    {
    }

    /// <summary>Initializes a cache with the specified combined byte capacity.</summary>
    /// <param name="maximumByteCount">Maximum combined bytes owned by this cache.</param>
    public GltfExternalResourceCache(long maximumByteCount)
    {
        if (maximumByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumByteCount),
                maximumByteCount,
                "The external glTF cache capacity must be positive.");
        }
        MaximumByteCount = maximumByteCount;
    }

    /// <summary>Gets the maximum combined bytes owned by this cache.</summary>
    public long MaximumByteCount { get; }

    /// <summary>Gets the current number of exact-URI entries.</summary>
    public int Count
    {
        get
        {
            lock (sync)
            {
                return resources.Count;
            }
        }
    }

    /// <summary>Gets the current combined number of cached bytes.</summary>
    public long TotalByteCount
    {
        get
        {
            lock (sync)
            {
                return totalByteCount;
            }
        }
    }

    /// <summary>
    /// Attempts to get cached bytes for an exact authored URI. The returned memory remains valid
    /// after the entry is removed or the cache is cleared.
    /// </summary>
    public bool TryGet(string uri, out ReadOnlyMemory<byte> source)
    {
        ArgumentNullException.ThrowIfNull(uri);
        lock (sync)
        {
            if (resources.TryGetValue(uri, out byte[]? cached))
            {
                source = cached;
                return true;
            }
        }
        source = default;
        return false;
    }

    /// <summary>
    /// Attempts to add an owned copy for an exact authored URI. Existing entries and additions
    /// that would exceed <see cref="MaximumByteCount"/> return false without changing the cache.
    /// </summary>
    public bool TryAdd(string uri, ReadOnlyMemory<byte> source)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);
        if (source.IsEmpty)
        {
            throw new ArgumentException("An external glTF cache entry cannot be empty.", nameof(source));
        }

        lock (sync)
        {
            if (resources.ContainsKey(uri) ||
                source.Length > MaximumByteCount - totalByteCount)
            {
                return false;
            }
            byte[] ownedSource = source.ToArray();
            resources.Add(uri, ownedSource);
            totalByteCount = checked(totalByteCount + ownedSource.Length);
            return true;
        }
    }

    /// <summary>Removes the entry for an exact authored URI.</summary>
    public bool Remove(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        lock (sync)
        {
            if (!resources.Remove(uri, out byte[]? removed))
            {
                return false;
            }
            totalByteCount -= removed.Length;
            return true;
        }
    }

    /// <summary>Removes every cached external resource.</summary>
    public void Clear()
    {
        lock (sync)
        {
            resources.Clear();
            totalByteCount = 0;
        }
    }
}

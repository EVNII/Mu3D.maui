namespace Mu3D.Native.Ktx;

/// <summary>Provides an application-owned, bounded cache of exact encoded KTX2 source bytes.</summary>
/// <remarks>
/// Keys are application-defined and compared ordinally. Scope keys to the source identity and
/// revision policy required by the application. The loader borrows this cache and never clears or
/// disposes it; cache hits still obey the active load's source limit.
/// </remarks>
public sealed class Ktx2SourceCache
{
    private readonly object sync = new();
    private readonly Dictionary<string, byte[]> sources = new(StringComparer.Ordinal);
    private long totalByteCount;

    /// <summary>Initializes a source cache with the default 256 MiB capacity.</summary>
    public Ktx2SourceCache()
        : this(Ktx2TextureLoader.DefaultMaximumSourceByteCount)
    {
    }

    /// <summary>Initializes a source cache with the specified combined byte capacity.</summary>
    public Ktx2SourceCache(long maximumByteCount)
    {
        if (maximumByteCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumByteCount),
                maximumByteCount,
                "The KTX2 source-cache capacity must be positive.");
        }
        MaximumByteCount = maximumByteCount;
    }

    /// <summary>Gets the maximum combined bytes owned by this cache.</summary>
    public long MaximumByteCount { get; }

    /// <summary>Gets the current number of application-defined source keys.</summary>
    public int Count
    {
        get
        {
            lock (sync)
            {
                return sources.Count;
            }
        }
    }

    /// <summary>Gets the current combined number of cached encoded bytes.</summary>
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
    /// Attempts to get bytes for an exact source key. Returned memory remains valid after removal
    /// or clearing because each entry owns its byte array.
    /// </summary>
    public bool TryGet(string sourceKey, out ReadOnlyMemory<byte> source)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        lock (sync)
        {
            if (sources.TryGetValue(sourceKey, out byte[]? cached))
            {
                source = cached;
                return true;
            }
        }
        source = default;
        return false;
    }

    /// <summary>
    /// Attempts to add an owned source copy. Existing keys and additions that exceed
    /// <see cref="MaximumByteCount"/> return false without changing the cache.
    /// </summary>
    public bool TryAdd(string sourceKey, ReadOnlyMemory<byte> source)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceKey);
        if (source.IsEmpty)
        {
            throw new ArgumentException("A cached KTX2 source cannot be empty.", nameof(source));
        }
        lock (sync)
        {
            if (sources.ContainsKey(sourceKey) ||
                source.Length > MaximumByteCount - totalByteCount)
            {
                return false;
            }
            byte[] ownedSource = source.ToArray();
            sources.Add(sourceKey, ownedSource);
            totalByteCount = checked(totalByteCount + ownedSource.Length);
            return true;
        }
    }

    /// <summary>Removes the entry for an exact source key.</summary>
    public bool Remove(string sourceKey)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        lock (sync)
        {
            if (!sources.Remove(sourceKey, out byte[]? removed))
            {
                return false;
            }
            totalByteCount -= removed.Length;
            return true;
        }
    }

    /// <summary>Removes every cached encoded KTX2 source.</summary>
    public void Clear()
    {
        lock (sync)
        {
            sources.Clear();
            totalByteCount = 0;
        }
    }
}

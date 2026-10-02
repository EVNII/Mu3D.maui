namespace Mu3D.SceneGraph;

/// <summary>
/// Stores bounded encoded Radiance HDR sources under exact application-defined keys. The cache is
/// application owned; clearing or discarding it never invalidates already decoded environments.
/// </summary>
public sealed class RadianceHdrSourceCache
{
    private readonly object gate = new();
    private readonly Dictionary<string, byte[]> sources = new(StringComparer.Ordinal);
    private long totalByteCount;

    /// <summary>Initializes a source cache with the default 256 MiB capacity.</summary>
    public RadianceHdrSourceCache()
        : this(RadianceHdrEnvironmentLoader.DefaultMaximumSourceByteCount)
    {
    }

    /// <summary>Initializes a source cache with an explicit aggregate byte limit.</summary>
    /// <param name="maximumByteCount">The positive maximum aggregate encoded byte count.</param>
    public RadianceHdrSourceCache(long maximumByteCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumByteCount);
        MaximumByteCount = maximumByteCount;
    }

    /// <summary>Gets the maximum aggregate encoded byte count.</summary>
    public long MaximumByteCount { get; }

    /// <summary>Gets the number of cached sources.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return sources.Count;
            }
        }
    }

    /// <summary>Gets the aggregate encoded byte count.</summary>
    public long TotalByteCount
    {
        get
        {
            lock (gate)
            {
                return totalByteCount;
            }
        }
    }

    /// <summary>Attempts to retrieve a defensive copy of one encoded source.</summary>
    public bool TryGet(string sourceKey, out ReadOnlyMemory<byte> source)
    {
        if (TryGetCopy(sourceKey, out byte[] copiedSource))
        {
            source = copiedSource;
            return true;
        }
        source = default;
        return false;
    }

    internal bool TryGetCopy(string sourceKey, out byte[] source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        lock (gate)
        {
            if (sources.TryGetValue(sourceKey, out byte[]? bytes))
            {
                source = bytes.ToArray();
                return true;
            }
        }
        source = [];
        return false;
    }

    /// <summary>
    /// Attempts to add a defensive copy. Existing keys are not replaced, and entries that would
    /// exceed the aggregate limit are rejected.
    /// </summary>
    public bool TryAdd(string sourceKey, ReadOnlyMemory<byte> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        if (source.IsEmpty)
        {
            throw new ArgumentException("A cached Radiance HDR source cannot be empty.", nameof(source));
        }
        lock (gate)
        {
            if (sources.ContainsKey(sourceKey) ||
                source.Length > MaximumByteCount - totalByteCount)
            {
                return false;
            }
            byte[] copiedSource = source.ToArray();
            sources.Add(sourceKey, copiedSource);
            totalByteCount += copiedSource.Length;
            return true;
        }
    }

    /// <summary>Removes one encoded source.</summary>
    public bool Remove(string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        lock (gate)
        {
            if (!sources.Remove(sourceKey, out byte[]? source))
            {
                return false;
            }
            totalByteCount -= source.Length;
            return true;
        }
    }

    /// <summary>Removes all encoded sources.</summary>
    public void Clear()
    {
        lock (gate)
        {
            sources.Clear();
            totalByteCount = 0;
        }
    }
}

namespace Mu3D.Toolkit.Assets;

/// <summary>
/// Provides application-owned, bounded least-recently-used retention and single-flight loading for
/// managed asset definitions.
/// </summary>
/// <typeparam name="TKey">Application-defined source identity and revision key.</typeparam>
/// <typeparam name="TAsset">Managed asset-definition type.</typeparam>
/// <remarks>
/// Caller cancellation stops that caller's wait. When every waiter for an unfinished entry leaves,
/// the cache removes the entry and cancels its shared loader token. Removing, clearing or disposing
/// the cache drops its references but never disposes a returned asset; scenes or instances that
/// still reference shared data remain valid. The cache owns no view, renderer, device or native GPU
/// resource. Set capacity at least as large as the maximum number of distinct simultaneously live
/// requests; a new miss fails closed when every retained slot is still loading.
/// </remarks>
public sealed class AsyncAssetCache<TKey, TAsset> : IDisposable
    where TKey : notnull
    where TAsset : class
{
    private readonly object gate = new();
    private readonly Dictionary<TKey, Entry> entries;
    private readonly LinkedList<TKey> completedLru = [];
    private bool disposed;

    /// <summary>Initializes a cache with a positive retained-entry capacity.</summary>
    /// <param name="maximumEntryCount">Maximum completed and in-flight entry count.</param>
    /// <param name="comparer">Optional application key comparer.</param>
    public AsyncAssetCache(int maximumEntryCount, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEntryCount);
        MaximumEntryCount = maximumEntryCount;
        entries = new Dictionary<TKey, Entry>(maximumEntryCount, comparer);
    }

    /// <summary>Gets the maximum combined completed and in-flight entry count.</summary>
    public int MaximumEntryCount { get; }

    /// <summary>Gets the current completed and in-flight entry count.</summary>
    public int Count
    {
        get
        {
            lock (gate)
            {
                return entries.Count;
            }
        }
    }

    /// <summary>Gets the number of entries whose shared loader has not completed.</summary>
    public int InFlightCount
    {
        get
        {
            lock (gate)
            {
                return entries.Values.Count(static entry => !entry.IsCompleted);
            }
        }
    }

    /// <summary>Attempts to get one completed cached definition and updates its LRU position.</summary>
    public bool TryGet(TKey key, out TAsset? asset)
    {
        ArgumentNullException.ThrowIfNull(key);
        lock (gate)
        {
            ThrowIfDisposed();
            if (entries.TryGetValue(key, out Entry? entry) && entry.Asset is not null)
            {
                TouchCompleted(entry);
                asset = entry.Asset;
                return true;
            }
        }
        asset = null;
        return false;
    }

    /// <summary>Gets a completed definition or joins/starts one shared load for the exact key.</summary>
    /// <param name="key">Application-defined source identity including revision semantics.</param>
    /// <param name="loader">Loader invoked once for a cache miss with a cache-managed token.</param>
    /// <param name="cancellationToken">Cancels only this caller's wait.</param>
    public async ValueTask<TAsset> GetOrLoadAsync(
        TKey key,
        Func<TKey, CancellationToken, ValueTask<TAsset>> loader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(loader);
        cancellationToken.ThrowIfCancellationRequested();
        Entry entry;
        bool joinedUnfinished;
        lock (gate)
        {
            ThrowIfDisposed();
            if (entries.TryGetValue(key, out Entry? existing))
            {
                entry = existing;
                joinedUnfinished = !entry.IsCompleted;
                if (entry.IsCompleted)
                {
                    TouchCompleted(entry);
                }
                else
                {
                    entry.WaiterCount++;
                }
            }
            else
            {
                EvictCompletedUntilSlotAvailable();
                if (entries.Count >= MaximumEntryCount)
                {
                    throw new InvalidOperationException(
                        "The asset cache is full and every retained entry is still loading.");
                }
                entry = new Entry(key) { WaiterCount = 1 };
                entries.Add(key, entry);
                entry.LoadTask = LoadEntryAsync(entry, loader);
                joinedUnfinished = true;
            }
        }

        if (!joinedUnfinished)
        {
            return await entry.LoadTask.ConfigureAwait(false);
        }
        try
        {
            return await entry.LoadTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ReleaseWaiter(entry);
        }
    }

    /// <summary>Removes one completed or in-flight entry and cancels unfinished work.</summary>
    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        CancellationTokenSource? cancellation;
        lock (gate)
        {
            ThrowIfDisposed();
            if (!entries.Remove(key, out Entry? entry))
            {
                return false;
            }
            RemoveCompletedNode(entry);
            entry.IsRemoved = true;
            cancellation = entry.IsCompleted ? null : entry.LoadCancellation;
        }
        cancellation?.Cancel();
        return true;
    }

    /// <summary>Drops all retained entries and cancels unfinished shared loaders.</summary>
    public void Clear()
    {
        CancellationTokenSource[] cancellations;
        lock (gate)
        {
            ThrowIfDisposed();
            cancellations = ClearLocked();
        }
        Cancel(cancellations);
    }

    /// <summary>Drops all retained entries and cancels unfinished shared loaders.</summary>
    public void Dispose()
    {
        CancellationTokenSource[] cancellations;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            cancellations = ClearLocked();
        }
        Cancel(cancellations);
    }

    private async Task<TAsset> LoadEntryAsync(
        Entry entry,
        Func<TKey, CancellationToken, ValueTask<TAsset>> loader)
    {
        TAsset asset;
        try
        {
            asset = await loader(entry.Key, entry.LoadCancellation.Token).ConfigureAwait(false) ??
                throw new InvalidOperationException("An asset cache loader returned null.");
        }
        catch
        {
            lock (gate)
            {
                if (entries.TryGetValue(entry.Key, out Entry? current) &&
                    ReferenceEquals(current, entry))
                {
                    entries.Remove(entry.Key);
                }
                entry.IsRemoved = true;
            }
            throw;
        }

        lock (gate)
        {
            entry.Asset = asset;
            entry.IsCompleted = true;
            if (!entry.IsRemoved &&
                entries.TryGetValue(entry.Key, out Entry? current) &&
                ReferenceEquals(current, entry))
            {
                entry.CompletedNode = completedLru.AddFirst(entry.Key);
            }
        }
        return asset;
    }

    private void ReleaseWaiter(Entry entry)
    {
        CancellationTokenSource? cancellation = null;
        lock (gate)
        {
            if (entry.WaiterCount > 0)
            {
                entry.WaiterCount--;
            }
            if (entry.WaiterCount == 0 && !entry.IsCompleted && !entry.IsRemoved)
            {
                if (entries.TryGetValue(entry.Key, out Entry? current) &&
                    ReferenceEquals(current, entry))
                {
                    entries.Remove(entry.Key);
                }
                entry.IsRemoved = true;
                cancellation = entry.LoadCancellation;
            }
        }
        cancellation?.Cancel();
    }

    private void EvictCompletedUntilSlotAvailable()
    {
        while (entries.Count >= MaximumEntryCount && completedLru.Last is LinkedListNode<TKey> node)
        {
            TKey key = node.Value;
            completedLru.Remove(node);
            if (entries.Remove(key, out Entry? entry))
            {
                entry.CompletedNode = null;
                entry.IsRemoved = true;
            }
        }
    }

    private void TouchCompleted(Entry entry)
    {
        if (entry.CompletedNode is null || ReferenceEquals(entry.CompletedNode, completedLru.First))
        {
            return;
        }
        completedLru.Remove(entry.CompletedNode);
        completedLru.AddFirst(entry.CompletedNode);
    }

    private void RemoveCompletedNode(Entry entry)
    {
        if (entry.CompletedNode is not null)
        {
            completedLru.Remove(entry.CompletedNode);
            entry.CompletedNode = null;
        }
    }

    private CancellationTokenSource[] ClearLocked()
    {
        CancellationTokenSource[] cancellations = entries.Values
            .Where(static entry => !entry.IsCompleted)
            .Select(static entry => entry.LoadCancellation)
            .ToArray();
        foreach (Entry entry in entries.Values)
        {
            entry.IsRemoved = true;
            entry.CompletedNode = null;
        }
        entries.Clear();
        completedLru.Clear();
        return cancellations;
    }

    private static void Cancel(IEnumerable<CancellationTokenSource> cancellations)
    {
        foreach (CancellationTokenSource cancellation in cancellations)
        {
            cancellation.Cancel();
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

    private sealed class Entry(TKey Key)
    {
        internal TKey Key { get; } = Key;
        internal CancellationTokenSource LoadCancellation { get; } = new();
        internal Task<TAsset> LoadTask { get; set; } = null!;
        internal TAsset? Asset { get; set; }
        internal LinkedListNode<TKey>? CompletedNode { get; set; }
        internal int WaiterCount { get; set; }
        internal bool IsCompleted { get; set; }
        internal bool IsRemoved { get; set; }
    }
}

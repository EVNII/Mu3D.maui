namespace Mu3D.Color;

internal sealed class BoundedLruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly object synchronization = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> entries;
    private readonly LinkedList<Entry> leastRecentlyUsed = [];

    internal BoundedLruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Transform-cache capacity must be positive.");
        }
        Capacity = capacity;
        entries = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    internal int Capacity { get; }

    internal int Count
    {
        get
        {
            lock (synchronization)
            {
                return entries.Count;
            }
        }
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2091",
        Justification = "Lazy is always given the explicit value factory; its default reflection-based constructor path is never used.")]
    internal TValue GetOrCreate(TKey key, Func<TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        LinkedListNode<Entry> node;
        lock (synchronization)
        {
            if (entries.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                leastRecentlyUsed.Remove(existing);
                leastRecentlyUsed.AddFirst(existing);
                node = existing;
            }
            else
            {
                Lazy<TValue> value = new(factory, LazyThreadSafetyMode.ExecutionAndPublication);
                node = leastRecentlyUsed.AddFirst(new Entry(key, value));
                entries.Add(key, node);
                if (entries.Count > Capacity)
                {
                    LinkedListNode<Entry> oldest = leastRecentlyUsed.Last!;
                    leastRecentlyUsed.RemoveLast();
                    entries.Remove(oldest.Value.Key);
                }
            }
        }

        try
        {
            return node.Value.Value.Value;
        }
        catch
        {
            lock (synchronization)
            {
                if (entries.TryGetValue(key, out LinkedListNode<Entry>? current) &&
                    ReferenceEquals(current, node))
                {
                    entries.Remove(key);
                    leastRecentlyUsed.Remove(node);
                }
            }
            throw;
        }
    }

    internal void Clear()
    {
        lock (synchronization)
        {
            entries.Clear();
            leastRecentlyUsed.Clear();
        }
    }

    private sealed record Entry(TKey Key, Lazy<TValue> Value);
}

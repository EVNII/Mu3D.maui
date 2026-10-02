using System.Runtime.CompilerServices;

namespace Mu3D.Color;

/// <summary>Stores a bounded least-recently-used set of ICC RGB-to-linear input transforms.</summary>
/// <remarks>
/// Input transform identity consists of the complete immutable profile fingerprint and rendering
/// intent. Profiles with byte-identical payloads may therefore share parsed transform data safely;
/// input transforms return a caller-selected standard working-space identity and do not expose or
/// attach the source profile object. The cache is thread-safe and caller-owned.
/// </remarks>
public sealed class IccRgbToLinearTransformCache
{
    private readonly BoundedLruCache<CacheKey, IccRgbToLinearTransform> cache;

    /// <summary>Initializes an empty bounded input-transform cache.</summary>
    /// <param name="capacity">The maximum number of parsed transforms retained, which must be positive.</param>
    public IccRgbToLinearTransformCache(int capacity = 64) =>
        cache = new BoundedLruCache<CacheKey, IccRgbToLinearTransform>(capacity);

    /// <summary>Gets the maximum number of input transforms retained by this cache.</summary>
    public int Capacity => cache.Capacity;

    /// <summary>Gets the current number of retained input transforms.</summary>
    public int Count => cache.Count;

    /// <summary>Gets or creates the input transform for a complete profile identity and intent.</summary>
    /// <param name="profile">The immutable RGB source profile.</param>
    /// <param name="renderingIntent">The requested rendering intent.</param>
    /// <returns>The cached or newly parsed input transform.</returns>
    public IccRgbToLinearTransform GetOrCreate(
        IccProfile profile,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric)
    {
        ArgumentNullException.ThrowIfNull(profile);
        CacheKey key = new(profile.Fingerprint, renderingIntent);
        return cache.GetOrCreate(key, () => new IccRgbToLinearTransform(profile, renderingIntent));
    }

    /// <summary>Removes all retained input transforms without changing the cache capacity.</summary>
    public void Clear() => cache.Clear();

    private readonly record struct CacheKey(string ProfileFingerprint, IccRenderingIntent RenderingIntent);
}

/// <summary>Stores a bounded least-recently-used set of ICC linear-to-RGB output transforms.</summary>
/// <remarks>
/// Output transform identity includes the exact immutable profile instance, its complete
/// fingerprint and rendering intent. Exact instance identity ensures that every
/// <see cref="IccEncodedRgba"/> returned by a cached transform carries the profile object supplied
/// by its caller. The cache is thread-safe and caller-owned.
/// </remarks>
public sealed class IccLinearToRgbTransformCache
{
    private readonly BoundedLruCache<CacheKey, IccLinearToRgbTransform> cache;

    /// <summary>Initializes an empty bounded output-transform cache.</summary>
    /// <param name="capacity">The maximum number of parsed transforms retained, which must be positive.</param>
    public IccLinearToRgbTransformCache(int capacity = 64) =>
        cache = new BoundedLruCache<CacheKey, IccLinearToRgbTransform>(
            capacity,
            CacheKeyComparer.Instance);

    /// <summary>Gets the maximum number of output transforms retained by this cache.</summary>
    public int Capacity => cache.Capacity;

    /// <summary>Gets the current number of retained output transforms.</summary>
    public int Count => cache.Count;

    /// <summary>Gets or creates the output transform for an exact profile instance and intent.</summary>
    /// <param name="profile">The exact immutable RGB target profile instance.</param>
    /// <param name="renderingIntent">The requested rendering intent.</param>
    /// <returns>The cached or newly parsed output transform.</returns>
    public IccLinearToRgbTransform GetOrCreate(
        IccProfile profile,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric)
    {
        ArgumentNullException.ThrowIfNull(profile);
        CacheKey key = new(profile, profile.Fingerprint, renderingIntent);
        return cache.GetOrCreate(key, () => new IccLinearToRgbTransform(profile, renderingIntent));
    }

    /// <summary>Removes all retained output transforms without changing the cache capacity.</summary>
    public void Clear() => cache.Clear();

    private readonly record struct CacheKey(
        IccProfile Profile,
        string ProfileFingerprint,
        IccRenderingIntent RenderingIntent);

    private sealed class CacheKeyComparer : IEqualityComparer<CacheKey>
    {
        internal static CacheKeyComparer Instance { get; } = new();

        public bool Equals(CacheKey x, CacheKey y) =>
            ReferenceEquals(x.Profile, y.Profile) &&
            x.ProfileFingerprint == y.ProfileFingerprint &&
            x.RenderingIntent == y.RenderingIntent;

        public int GetHashCode(CacheKey value) => HashCode.Combine(
            RuntimeHelpers.GetHashCode(value.Profile),
            value.ProfileFingerprint,
            value.RenderingIntent);
    }
}

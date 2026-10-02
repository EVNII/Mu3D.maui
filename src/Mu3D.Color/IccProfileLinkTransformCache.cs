using System.Runtime.CompilerServices;

namespace Mu3D.Color;

/// <summary>
/// Stores a bounded least-recently-used set of compiled ICC profile-link transforms.
/// </summary>
/// <remarks>
/// Cache identity includes the exact immutable source and destination profile instances, their
/// fingerprints, rendering intent, and disabled, explicit, or automatic black-point-compensation
/// policy. Using instance identity preserves the profile objects exposed by cached transforms and
/// returned encoded colors. ICC PCS D50 handling and chromatic adaptation are defined by the full
/// profile payload represented by each fingerprint. The cache is thread-safe and caller-owned; it
/// does not retain profiles in a process-wide static cache.
/// </remarks>
public sealed class IccProfileLinkTransformCache
{
    private readonly BoundedLruCache<CacheKey, IccProfileLinkTransform> cache;

    /// <summary>Initializes an empty bounded transform cache.</summary>
    /// <param name="capacity">The maximum number of compiled links retained, which must be positive.</param>
    public IccProfileLinkTransformCache(int capacity = 64)
    {
        cache = new BoundedLruCache<CacheKey, IccProfileLinkTransform>(
            capacity,
            CacheKeyComparer.Instance);
    }

    /// <summary>Gets the maximum number of compiled profile links retained by this cache.</summary>
    public int Capacity => cache.Capacity;

    /// <summary>Gets the current number of retained compiled profile links.</summary>
    public int Count => cache.Count;

    /// <summary>Gets or creates a profile link with disabled or explicitly supplied BPC.</summary>
    /// <param name="sourceProfile">The exact immutable source RGB profile instance.</param>
    /// <param name="destinationProfile">The exact immutable destination RGB profile instance.</param>
    /// <param name="renderingIntent">The rendering intent applied on both profile sides.</param>
    /// <param name="blackPointCompensation">Explicit black points, or null to disable BPC.</param>
    /// <returns>The cached or newly compiled profile link.</returns>
    public IccProfileLinkTransform GetOrCreate(
        IccProfile sourceProfile,
        IccProfile destinationProfile,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric,
        IccBlackPointCompensation? blackPointCompensation = null)
    {
        ArgumentNullException.ThrowIfNull(sourceProfile);
        ArgumentNullException.ThrowIfNull(destinationProfile);
        CacheKey key = new(
            sourceProfile,
            destinationProfile,
            renderingIntent,
            blackPointCompensation is null ? BlackPointMode.Disabled : BlackPointMode.Explicit,
            blackPointCompensation);
        return GetOrCreate(
            key,
            () => new IccProfileLinkTransform(
                sourceProfile,
                destinationProfile,
                renderingIntent,
                blackPointCompensation));
    }

    /// <summary>Gets or creates a profile link with automatically estimated ISO 18619 RGB BPC.</summary>
    /// <param name="sourceProfile">The exact immutable source RGB profile instance.</param>
    /// <param name="destinationProfile">The exact immutable destination RGB profile instance.</param>
    /// <param name="renderingIntent">Perceptual, media-relative colorimetric or saturation intent.</param>
    /// <returns>The cached or newly estimated and compiled profile link.</returns>
    public IccProfileLinkTransform GetOrCreateWithAutomaticBlackPointCompensation(
        IccProfile sourceProfile,
        IccProfile destinationProfile,
        IccRenderingIntent renderingIntent = IccRenderingIntent.MediaRelativeColorimetric)
    {
        ArgumentNullException.ThrowIfNull(sourceProfile);
        ArgumentNullException.ThrowIfNull(destinationProfile);
        CacheKey key = new(
            sourceProfile,
            destinationProfile,
            renderingIntent,
            BlackPointMode.Automatic,
            null);
        return GetOrCreate(
            key,
            () => IccProfileLinkTransform.CreateWithAutomaticBlackPointCompensation(
                sourceProfile,
                destinationProfile,
                renderingIntent));
    }

    /// <summary>Removes all retained links without changing the cache capacity.</summary>
    public void Clear()
    {
        cache.Clear();
    }

    private IccProfileLinkTransform GetOrCreate(
        CacheKey key,
        Func<IccProfileLinkTransform> factory)
    {
        return cache.GetOrCreate(key, factory);
    }

    private enum BlackPointMode
    {
        Disabled,
        Explicit,
        Automatic,
    }

    private readonly record struct CacheKey(
        IccProfile SourceProfile,
        IccProfile DestinationProfile,
        IccRenderingIntent RenderingIntent,
        BlackPointMode BlackPointMode,
        IccBlackPointCompensation? BlackPointCompensation);

    private sealed class CacheKeyComparer : IEqualityComparer<CacheKey>
    {
        internal static CacheKeyComparer Instance { get; } = new();

        public bool Equals(CacheKey x, CacheKey y) =>
            ReferenceEquals(x.SourceProfile, y.SourceProfile) &&
            ReferenceEquals(x.DestinationProfile, y.DestinationProfile) &&
            x.SourceProfile.Fingerprint == y.SourceProfile.Fingerprint &&
            x.DestinationProfile.Fingerprint == y.DestinationProfile.Fingerprint &&
            x.RenderingIntent == y.RenderingIntent &&
            x.BlackPointMode == y.BlackPointMode &&
            x.BlackPointCompensation == y.BlackPointCompensation;

        public int GetHashCode(CacheKey value) => HashCode.Combine(
            RuntimeHelpers.GetHashCode(value.SourceProfile),
            RuntimeHelpers.GetHashCode(value.DestinationProfile),
            value.SourceProfile.Fingerprint,
            value.DestinationProfile.Fingerprint,
            value.RenderingIntent,
            value.BlackPointMode,
            value.BlackPointCompensation);
    }
}

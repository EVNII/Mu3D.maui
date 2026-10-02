using System.Collections.ObjectModel;

namespace Mu3D.Toolkit.Overlays;

/// <summary>Stores the immutable projection of one registered node-local point.</summary>
/// <param name="Id">The opaque registration identity within its anchor source.</param>
/// <param name="Projected">Whether the target belongs to the visible scene and projects finitely in front of the camera.</param>
/// <param name="InsideViewport">Whether a projected point lies inside the captured clip volume.</param>
/// <param name="X">The top-left-origin horizontal coordinate in logical host units, or zero when unprojected.</param>
/// <param name="Y">The top-left-origin vertical coordinate in logical host units, or zero when unprojected.</param>
/// <param name="Depth">The normalized projected depth, or zero when unprojected.</param>
public readonly record struct SceneNodeAnchorPoint(
    string Id,
    bool Projected,
    bool InsideViewport,
    double X,
    double Y,
    float Depth);

/// <summary>
/// Owns a complete immutable batch of logical node-anchor projections from one captured viewport frame.
/// </summary>
/// <remarks>
/// The batch retains no scene or node references. A successful host submission does not establish
/// GPU completion, physical presentation or depth occlusion.
/// </remarks>
public sealed class SceneNodeAnchorFrame
{
    private readonly ReadOnlyCollection<SceneNodeAnchorPoint> points;

    /// <summary>Initializes a frame and defensively copies every projected point.</summary>
    /// <param name="sourceId">The non-empty identity of the originating anchor source.</param>
    /// <param name="revision">The non-negative registration revision captured with this frame.</param>
    /// <param name="frameId">The host-defined successful-frame identity without numeric narrowing.</param>
    /// <param name="width">The positive finite logical viewport width.</param>
    /// <param name="height">The positive finite logical viewport height.</param>
    /// <param name="points">The complete batch of finite points with unique non-empty registration identities.</param>
    public SceneNodeAnchorFrame(
        string sourceId,
        long revision,
        ulong frameId,
        double width,
        double height,
        IEnumerable<SceneNodeAnchorPoint> points)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceId);
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        ValidateExtent(width, nameof(width));
        ValidateExtent(height, nameof(height));
        ArgumentNullException.ThrowIfNull(points);

        SceneNodeAnchorPoint[] copy = points.ToArray();
        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (SceneNodeAnchorPoint point in copy)
        {
            if (string.IsNullOrEmpty(point.Id) || !double.IsFinite(point.X) ||
                !double.IsFinite(point.Y) || !float.IsFinite(point.Depth) || !identities.Add(point.Id))
            {
                throw new ArgumentException("Anchor points must have unique identities and finite coordinates.", nameof(points));
            }
        }

        SourceId = sourceId;
        Revision = revision;
        FrameId = frameId;
        Width = width;
        Height = height;
        this.points = Array.AsReadOnly(copy);
    }

    /// <summary>Gets the originating anchor source's identity.</summary>
    public string SourceId { get; }

    /// <summary>Gets the registration revision captured with this batch.</summary>
    public long Revision { get; }

    /// <summary>Gets the host-defined successful-frame identity.</summary>
    public ulong FrameId { get; }

    /// <summary>Gets the logical viewport width used for placement.</summary>
    public double Width { get; }

    /// <summary>Gets the logical viewport height used for placement.</summary>
    public double Height { get; }

    /// <summary>Gets the complete, read-only batch of value-only projected points.</summary>
    public IReadOnlyList<SceneNodeAnchorPoint> Points => points;

    private static void ValidateExtent(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Logical viewport extents must be positive and finite.");
        }
    }
}

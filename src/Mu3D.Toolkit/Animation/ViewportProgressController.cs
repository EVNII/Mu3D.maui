using Mu3D.Toolkit.Viewports;

namespace Mu3D.Toolkit.Animation;

/// <summary>Maps one normalized application progress value into caller-owned state.</summary>
/// <remarks>
/// Implementations may target cameras, nodes, clip time, instance transforms, material parameters,
/// or other application-owned state. They do not request frames individually; the controller
/// coalesces all mappings into one frame request.
/// </remarks>
public interface IViewportProgressMapping
{
    /// <summary>Applies a normalized progress value in the inclusive range zero through one.</summary>
    /// <param name="progress">The finite normalized progress value.</param>
    void Apply(double progress);
}

/// <summary>Adapts an explicitly supplied delegate into one progress mapping.</summary>
public sealed class DelegateViewportProgressMapping : IViewportProgressMapping
{
    private readonly Action<double> apply;

    /// <summary>Initializes a mapping that invokes the supplied delegate.</summary>
    /// <param name="apply">The application-owned progress mutation.</param>
    public DelegateViewportProgressMapping(Action<double> apply) =>
        this.apply = apply ?? throw new ArgumentNullException(nameof(apply));

    /// <inheritdoc />
    public void Apply(double progress) => apply(progress);
}

/// <summary>
/// Applies replaceable progress mappings and requests one coalesced viewport frame.
/// </summary>
/// <remarks>
/// The controller owns no clock, timer, animation, scroll view, camera, scene, or mapping. Values
/// outside zero through one are clamped so overscroll and easing overshoot cannot push a target
/// outside its declared endpoint range. Autonomous playback remains a separate VSync-driven
/// concern.
/// </remarks>
public sealed class ViewportProgressController
{
    private readonly IViewportFrameRequester frameRequester;
    private readonly IReadOnlyList<IViewportProgressMapping> mappings;
    private double progress;

    /// <summary>Initializes a controller over an immutable mapping snapshot.</summary>
    /// <param name="frameRequester">The host's coalesced, suspend-aware frame boundary.</param>
    /// <param name="mappings">The ordered application-owned mappings to apply.</param>
    public ViewportProgressController(
        IViewportFrameRequester frameRequester,
        IEnumerable<IViewportProgressMapping> mappings)
    {
        this.frameRequester = frameRequester ?? throw new ArgumentNullException(nameof(frameRequester));
        ArgumentNullException.ThrowIfNull(mappings);
        IViewportProgressMapping[] snapshot = [.. mappings];
        if (snapshot.Any(static mapping => mapping is null))
        {
            throw new ArgumentException("A progress mapping snapshot cannot contain null.", nameof(mappings));
        }
        this.mappings = snapshot;
    }

    /// <summary>Gets the most recently accepted normalized progress value.</summary>
    public double Progress => progress;

    /// <summary>Gets the ordered mapping snapshot borrowed by this controller.</summary>
    public IReadOnlyList<IViewportProgressMapping> Mappings => mappings;

    /// <summary>Applies a new finite progress value when its clamped value changed.</summary>
    /// <param name="value">Application, MAUI animation, or scroll progress.</param>
    /// <returns><see langword="true"/> when mappings and one frame request were issued.</returns>
    public bool SetProgress(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Progress must be finite.");
        }

        double normalized = Math.Clamp(value, 0d, 1d);
        if (normalized.Equals(progress))
        {
            return false;
        }
        progress = normalized;
        ApplyMappingsAndRequestFrame();
        return true;
    }

    /// <summary>Reapplies the current value after mapping configuration or targets change.</summary>
    public void Reapply() => ApplyMappingsAndRequestFrame();

    private void ApplyMappingsAndRequestFrame()
    {
        foreach (IViewportProgressMapping mapping in mappings)
        {
            mapping.Apply(progress);
        }
        frameRequester.RequestFrame();
    }
}

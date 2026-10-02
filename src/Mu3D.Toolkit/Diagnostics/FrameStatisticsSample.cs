using Mu3D.Graphics;
using Mu3D.Rendering;

namespace Mu3D.Toolkit.Diagnostics;

/// <summary>Stores one immutable backend-independent frame-statistics input sample.</summary>
/// <remarks>
/// <see cref="FrameDuration"/> is the host-observed interval used for FPS and frame-time
/// calculation. Optional presentation and renderer timings are CPU wall-clock diagnostics, not GPU
/// timestamps. The host decides which successfully completed or presented frames to record.
/// </remarks>
public readonly record struct FrameStatisticsSample
{
    /// <summary>Initializes one validated frame sample.</summary>
    /// <param name="frameDuration">Positive host-observed interval represented by this frame.</param>
    /// <param name="presentationTimings">Optional presentation-session CPU timings.</param>
    /// <param name="rendererTimings">Optional scene-renderer CPU timings.</param>
    /// <param name="drawCallCount">Optional non-negative draw-call count for this frame.</param>
    /// <param name="primitiveCount">Optional non-negative rendered primitive count for this frame.</param>
    public FrameStatisticsSample(
        TimeSpan frameDuration,
        PresentationSurfaceFrameTimings? presentationTimings = null,
        SceneRendererFrameTimings? rendererTimings = null,
        long? drawCallCount = null,
        long? primitiveCount = null)
    {
        FrameDuration = frameDuration;
        PresentationTimings = presentationTimings;
        RendererTimings = rendererTimings;
        DrawCallCount = drawCallCount;
        PrimitiveCount = primitiveCount;
        Validate();
    }

    /// <summary>Gets the positive host-observed frame interval.</summary>
    public TimeSpan FrameDuration { get; }

    /// <summary>Gets optional CPU wall-clock timings around presentation.</summary>
    public PresentationSurfaceFrameTimings? PresentationTimings { get; }

    /// <summary>Gets optional CPU wall-clock timings around scene rendering.</summary>
    public SceneRendererFrameTimings? RendererTimings { get; }

    /// <summary>Gets the optional non-negative draw-call count.</summary>
    public long? DrawCallCount { get; }

    /// <summary>Gets the optional non-negative rendered primitive count.</summary>
    public long? PrimitiveCount { get; }

    internal void Validate()
    {
        if (FrameDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(FrameDuration),
                "Frame duration must be positive.");
        }
        if (PresentationTimings is PresentationSurfaceFrameTimings presentation)
        {
            ThrowIfNegativeOrNonFinite(
                presentation.AcquireMilliseconds,
                nameof(PresentationTimings));
            ThrowIfNegativeOrNonFinite(
                presentation.RenderMilliseconds,
                nameof(PresentationTimings));
            ThrowIfNegativeOrNonFinite(
                presentation.PresentMilliseconds,
                nameof(PresentationTimings));
            ThrowIfNegativeOrNonFinite(
                presentation.TotalMilliseconds,
                nameof(PresentationTimings));
        }
        if (RendererTimings is SceneRendererFrameTimings renderer)
        {
            ThrowIfNegativeOrNonFinite(
                renderer.PrepareMilliseconds,
                nameof(RendererTimings));
            ThrowIfNegativeOrNonFinite(
                renderer.EncodeMilliseconds,
                nameof(RendererTimings));
            ThrowIfNegativeOrNonFinite(
                renderer.SubmitMilliseconds,
                nameof(RendererTimings));
            ThrowIfNegativeOrNonFinite(
                renderer.CacheTrimMilliseconds,
                nameof(RendererTimings));
            ThrowIfNegativeOrNonFinite(
                renderer.TotalMilliseconds,
                nameof(RendererTimings));
        }
        ThrowIfNegative(DrawCallCount, nameof(DrawCallCount));
        ThrowIfNegative(PrimitiveCount, nameof(PrimitiveCount));
    }

    private static void ThrowIfNegative(long? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The count must be non-negative.");
        }
    }

    private static void ThrowIfNegativeOrNonFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value < 0d)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Timing values must be non-negative and finite.");
        }
    }
}

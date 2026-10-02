using Mu3D.Graphics;
using Mu3D.Rendering;

namespace Mu3D.Toolkit.Diagnostics;

/// <summary>Collects a fixed-capacity rolling window of backend-independent frame samples.</summary>
/// <remarks>
/// Recording writes into a preallocated ring buffer. Snapshot creation performs no UI work and can
/// be throttled independently by a host or telemetry consumer. This collector is not thread-safe.
/// </remarks>
public sealed class FrameStatisticsCollector
{
    private readonly FrameStatisticsSample[] samples;
    private int nextIndex;
    private int sampleCount;
    private RenderResourceCounts? resourceCounts;

    /// <summary>Initializes a collector retaining at most the requested positive sample count.</summary>
    /// <param name="maximumSampleCount">Positive fixed capacity of the rolling frame window.</param>
    public FrameStatisticsCollector(int maximumSampleCount = 120)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumSampleCount);
        samples = new FrameStatisticsSample[maximumSampleCount];
    }

    /// <summary>Gets the fixed maximum number of retained samples.</summary>
    public int MaximumSampleCount => samples.Length;

    /// <summary>Gets the current number of retained samples.</summary>
    public int SampleCount => sampleCount;

    /// <summary>Gets the latest explicit resource-count snapshot, or null when unavailable.</summary>
    public RenderResourceCounts? ResourceCounts => resourceCounts;

    /// <summary>Records one validated frame and evicts the oldest sample when the window is full.</summary>
    /// <param name="sample">Immutable frame sample to retain.</param>
    public void RecordFrame(FrameStatisticsSample sample)
    {
        sample.Validate();
        samples[nextIndex] = sample;
        nextIndex = (nextIndex + 1) % samples.Length;
        if (sampleCount < samples.Length)
        {
            sampleCount++;
        }
    }

    /// <summary>Replaces or clears the latest application-supplied resource counts.</summary>
    /// <param name="counts">Latest resource counts, or null to mark them unavailable.</param>
    public void UpdateResourceCounts(RenderResourceCounts? counts) => resourceCounts = counts;

    /// <summary>Creates an immutable summary of the currently retained rolling window.</summary>
    public FrameStatisticsSnapshot CaptureSnapshot()
    {
        if (sampleCount == 0)
        {
            return new FrameStatisticsSnapshot(
                0,
                0d,
                0d,
                0d,
                0d,
                0,
                null,
                null,
                null,
                null,
                0,
                null,
                null,
                null,
                null,
                null,
                0,
                null,
                0,
                null,
                resourceCounts);
        }

        double windowMilliseconds = 0d;
        double minimumFrameMilliseconds = double.MaxValue;
        double maximumFrameMilliseconds = 0d;
        int presentationCount = 0;
        double acquireMilliseconds = 0d;
        double presentationRenderMilliseconds = 0d;
        double presentMilliseconds = 0d;
        double presentationTotalMilliseconds = 0d;
        int rendererCount = 0;
        double rendererPrepareMilliseconds = 0d;
        double rendererEncodeMilliseconds = 0d;
        double rendererSubmitMilliseconds = 0d;
        double rendererCacheTrimMilliseconds = 0d;
        double rendererTotalMilliseconds = 0d;
        int drawCallCount = 0;
        double drawCalls = 0d;
        int primitiveCount = 0;
        double primitives = 0d;

        int firstIndex = (nextIndex - sampleCount + samples.Length) % samples.Length;
        for (int offset = 0; offset < sampleCount; offset++)
        {
            FrameStatisticsSample sample = samples[(firstIndex + offset) % samples.Length];
            double frameMilliseconds = sample.FrameDuration.TotalMilliseconds;
            windowMilliseconds += frameMilliseconds;
            minimumFrameMilliseconds = Math.Min(minimumFrameMilliseconds, frameMilliseconds);
            maximumFrameMilliseconds = Math.Max(maximumFrameMilliseconds, frameMilliseconds);
            if (sample.PresentationTimings is PresentationSurfaceFrameTimings presentation)
            {
                presentationCount++;
                acquireMilliseconds += presentation.AcquireMilliseconds;
                presentationRenderMilliseconds += presentation.RenderMilliseconds;
                presentMilliseconds += presentation.PresentMilliseconds;
                presentationTotalMilliseconds += presentation.TotalMilliseconds;
            }
            if (sample.RendererTimings is SceneRendererFrameTimings renderer)
            {
                rendererCount++;
                rendererPrepareMilliseconds += renderer.PrepareMilliseconds;
                rendererEncodeMilliseconds += renderer.EncodeMilliseconds;
                rendererSubmitMilliseconds += renderer.SubmitMilliseconds;
                rendererCacheTrimMilliseconds += renderer.CacheTrimMilliseconds;
                rendererTotalMilliseconds += renderer.TotalMilliseconds;
            }
            if (sample.DrawCallCount is long sampleDrawCalls)
            {
                drawCallCount++;
                drawCalls += sampleDrawCalls;
            }
            if (sample.PrimitiveCount is long samplePrimitives)
            {
                primitiveCount++;
                primitives += samplePrimitives;
            }
        }

        return new FrameStatisticsSnapshot(
            sampleCount,
            windowMilliseconds,
            windowMilliseconds / sampleCount,
            minimumFrameMilliseconds,
            maximumFrameMilliseconds,
            presentationCount,
            AverageOrNull(acquireMilliseconds, presentationCount),
            AverageOrNull(presentationRenderMilliseconds, presentationCount),
            AverageOrNull(presentMilliseconds, presentationCount),
            AverageOrNull(presentationTotalMilliseconds, presentationCount),
            rendererCount,
            AverageOrNull(rendererPrepareMilliseconds, rendererCount),
            AverageOrNull(rendererEncodeMilliseconds, rendererCount),
            AverageOrNull(rendererSubmitMilliseconds, rendererCount),
            AverageOrNull(rendererCacheTrimMilliseconds, rendererCount),
            AverageOrNull(rendererTotalMilliseconds, rendererCount),
            drawCallCount,
            AverageOrNull(drawCalls, drawCallCount),
            primitiveCount,
            AverageOrNull(primitives, primitiveCount),
            resourceCounts);
    }

    /// <summary>Clears retained frames and resource counts while preserving allocated capacity.</summary>
    public void Reset()
    {
        nextIndex = 0;
        sampleCount = 0;
        resourceCounts = null;
    }

    private static double? AverageOrNull(double total, int count) =>
        count == 0 ? null : total / count;
}

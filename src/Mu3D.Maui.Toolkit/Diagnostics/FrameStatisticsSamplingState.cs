using System.Diagnostics;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.Toolkit.Diagnostics;

namespace Mu3D.Maui.Toolkit.Diagnostics;

internal sealed class FrameStatisticsSamplingState
{
    private long previousPresentedTimestamp;
    private long previousPublishedTimestamp;

    internal bool TryRecord(
        long timestamp,
        TimeSpan snapshotInterval,
        FrameStatisticsCollector collector,
        PresentationSurfaceFrameTimings presentationTimings,
        SceneRendererFrameTimings? rendererTimings,
        out FrameStatisticsSnapshot snapshot) => TryRecord(
            timestamp,
            snapshotInterval,
            collector,
            presentationTimings,
            rendererTimings,
            drawCallCount: null,
            primitiveCount: null,
            out snapshot);

    internal bool TryRecord(
        long timestamp,
        TimeSpan snapshotInterval,
        FrameStatisticsCollector collector,
        PresentationSurfaceFrameTimings presentationTimings,
        SceneRendererFrameTimings? rendererTimings,
        long? drawCallCount,
        long? primitiveCount,
        out FrameStatisticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(collector);
        if (snapshotInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(snapshotInterval));
        }
        snapshot = default;
        if (previousPresentedTimestamp == 0)
        {
            previousPresentedTimestamp = timestamp;
            return false;
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(previousPresentedTimestamp, timestamp);
        previousPresentedTimestamp = timestamp;
        if (duration <= TimeSpan.Zero)
        {
            return false;
        }
        collector.RecordFrame(new FrameStatisticsSample(
            duration,
            presentationTimings,
            rendererTimings,
            drawCallCount,
            primitiveCount));
        if (previousPublishedTimestamp != 0 &&
            Stopwatch.GetElapsedTime(previousPublishedTimestamp, timestamp) < snapshotInterval)
        {
            return false;
        }

        snapshot = collector.CaptureSnapshot();
        previousPublishedTimestamp = timestamp;
        return true;
    }

    internal void MarkPublished(long timestamp) => previousPublishedTimestamp = timestamp;

    internal void ResetTimestamps()
    {
        previousPresentedTimestamp = 0;
        previousPublishedTimestamp = 0;
    }
}

using System.Diagnostics;
using Mu3D.Rendering;
using Mu3D.Toolkit.Diagnostics;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// This validation-host payload uses the shared portable snapshot; its separate JSON context is
// source-generated and camel-case. A version in the ordinary frame report avoids serializing the
// rolling snapshot on every submitted frame.
internal sealed record ViewerStatisticsReport(long Version, FrameStatisticsSnapshot Snapshot);

internal sealed record ViewerStatisticsOptions(bool IsEnabled = true,
    double SnapshotIntervalMilliseconds = 500,
    long? DrawCallCount = null,
    long? PrimitiveCount = null);

internal sealed partial class ViewerScene
{
    private FrameStatisticsCollector statistics = new();
    private ViewerStatisticsOptions statisticsOptions = new();
    private long? previousSubmittedTimestamp;
    private long? previousStatisticsPublicationTimestamp;
    private bool statisticsDisposed;

    internal ViewerStatisticsReport LatestStatisticsReport { get; private set; } = new(0, default);

    internal long LatestStatisticsVersion { get; private set; }

    internal ViewerStatisticsOptions StatisticsOptions => statisticsOptions;

    // Assigned collectors are borrowed, just as in the native behavior. Replacing the source clears
    // only the publication/clock state, without mutating the previous or new collector.
    internal FrameStatisticsCollector StatisticsCollector
    {
        get => statistics;
        set
        {
            ObjectDisposedException.ThrowIf(statisticsDisposed, this);
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(statistics, value)) return;
            statistics = value;
            ResetStatisticsClock();
            PublishStatisticsReport(default);
        }
    }

    internal void ConfigureStatistics(ViewerStatisticsOptions options)
    {
        ObjectDisposedException.ThrowIf(statisticsDisposed, this);
        ArgumentNullException.ThrowIfNull(options);
        if (!double.IsFinite(options.SnapshotIntervalMilliseconds) ||
            options.SnapshotIntervalMilliseconds < 0 ||
            options.SnapshotIntervalMilliseconds > TimeSpan.MaxValue.TotalMilliseconds ||
            options.DrawCallCount < 0 || options.PrimitiveCount < 0)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (statisticsOptions.IsEnabled != options.IsEnabled) ResetStatisticsClock();
        statisticsOptions = options;
    }

    // Call only after a complete scene + Gizmo + Canvas submission succeeds. The host's elapsed
    // value supplies the resume/continuity signal, while completed-submission timestamps measure
    // cadence. These are CPU host observations, never GPU execution or physical presentation times.
    internal bool RecordSubmittedFrame(TimeSpan? elapsed,
        SceneRendererFrameTimings? rendererTimings = null,
        long? drawCallCount = null,
        long? primitiveCount = null,
        RenderResourceCounts? resourceCounts = null,
        long? timestamp = null)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        if (statisticsDisposed || !statisticsOptions.IsEnabled) return false;
        long submittedTimestamp = timestamp ?? Stopwatch.GetTimestamp();
        if (elapsed is null || elapsed == TimeSpan.Zero) ResetStatisticsClock();
        statistics.UpdateResourceCounts(resourceCounts);
        if (previousSubmittedTimestamp is not long previousTimestamp)
        {
            previousSubmittedTimestamp = submittedTimestamp;
            return false;
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(previousTimestamp, submittedTimestamp);
        previousSubmittedTimestamp = submittedTimestamp;
        if (duration <= TimeSpan.Zero) return false;
        statistics.RecordFrame(new FrameStatisticsSample(duration,
            presentationTimings: null,
            rendererTimings,
            drawCallCount ?? statisticsOptions.DrawCallCount,
            primitiveCount ?? statisticsOptions.PrimitiveCount));
        if (previousStatisticsPublicationTimestamp is long publishedTimestamp &&
            Stopwatch.GetElapsedTime(publishedTimestamp, submittedTimestamp).TotalMilliseconds <
            statisticsOptions.SnapshotIntervalMilliseconds)
            return false;

        PublishStatisticsReport(statistics.CaptureSnapshot());
        previousStatisticsPublicationTimestamp = submittedTimestamp;
        return true;
    }

    internal ViewerStatisticsReport PublishStatisticsSnapshot(long? timestamp = null)
    {
        ObjectDisposedException.ThrowIf(statisticsDisposed, this);
        PublishStatisticsReport(statistics.CaptureSnapshot());
        previousStatisticsPublicationTimestamp = timestamp ?? Stopwatch.GetTimestamp();
        return LatestStatisticsReport;
    }

    internal ViewerStatisticsReport ResetStatistics()
    {
        ObjectDisposedException.ThrowIf(statisticsDisposed, this);
        statistics.Reset();
        ResetStatisticsClock();
        PublishStatisticsReport(default);
        return LatestStatisticsReport;
    }

    private void PublishStatisticsReport(FrameStatisticsSnapshot snapshot)
    {
        LatestStatisticsVersion++;
        LatestStatisticsReport = new(LatestStatisticsVersion, snapshot);
    }

    private void ResetStatisticsClock()
    {
        previousSubmittedTimestamp = null;
        previousStatisticsPublicationTimestamp = null;
    }

    private void DisposeStatistics()
    {
        statisticsDisposed = true;
        ResetStatisticsClock();
    }
}

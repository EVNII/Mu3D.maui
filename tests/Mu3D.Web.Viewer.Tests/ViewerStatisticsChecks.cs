using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Diagnostics;
using Mu3D.GalleryApp.Web.Infrastructure;
using static ViewerTestChecks;

internal static class ViewerStatisticsChecks
{
    internal static void Validate(Action<bool, string> check)
    {
        TimeSpan continuity = TimeSpan.FromMilliseconds(1);
        static long At(double milliseconds) => (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000);
        static ViewerScene Open() => new(SharedValidationScene.Create(), new PerspectiveCamera());
        using ViewerScene viewer = Open();
        check(viewer.StatisticsOptions == new ViewerStatisticsOptions() &&
            viewer.StatisticsCollector.MaximumSampleCount == 120 &&
            viewer.LatestStatisticsVersion == 0 && viewer.LatestStatisticsReport.Snapshot == default,
            "Web diagnostics match native 500 ms/120-sample enabled defaults.");

        ViewerInput step = new(1f / 60, 1f / 60, true, 0, 0, 0, 0.35f, 0.28f, false);
        for (int index = 0; index < 180; index++) viewer.Apply(step);
        check(viewer.StatisticsCollector.SampleCount == 0 && viewer.Report(1).FramesPerSecond == 0,
            "Scene updates alone never record unsubmitted frames or publish FPS.");

        check(!viewer.RecordSubmittedFrame(null, default(SceneRendererFrameTimings), timestamp: At(1000)) &&
            viewer.StatisticsCollector.SampleCount == 0 && viewer.LatestStatisticsVersion == 0,
            "The first successful submission establishes the statistics clock.");
        check(viewer.RecordSubmittedFrame(continuity, default(SceneRendererFrameTimings), timestamp: At(1020)),
            "The first positive completed interval publishes immediately.");
        ViewerStatisticsReport initial = viewer.LatestStatisticsReport;
        FrameStatisticsSnapshot snapshot = initial.Snapshot;
        check(initial.Version == 1 && snapshot.SampleCount == 1 &&
            Math.Abs(snapshot.AverageFrameMilliseconds - 20) < 0.00001 &&
            Math.Abs(snapshot.FramesPerSecond - 50) < 0.00001,
            "Submission completion timestamps determine cadence rather than RAF-start intervals.");
        check(snapshot.PresentationSampleCount == 0 && snapshot.AverageAcquireMilliseconds is null &&
            snapshot.AveragePresentationRenderMilliseconds is null && snapshot.AveragePresentMilliseconds is null &&
            snapshot.AveragePresentationTotalMilliseconds is null && snapshot.DrawCallSampleCount == 0 &&
            snapshot.AverageDrawCallCount is null && snapshot.PrimitiveSampleCount == 0 &&
            snapshot.AveragePrimitiveCount is null && snapshot.ResourceCounts is null,
            "Unsupported presentation, draw, primitive and resource measurements remain unavailable.");
        check(snapshot.RendererSampleCount == 1 && snapshot.AverageRendererTotalMilliseconds == 0,
            "An explicitly supplied renderer timing is available, including a measured zero value.");
        check(!viewer.RecordSubmittedFrame(continuity, timestamp: At(1030)) &&
            viewer.StatisticsCollector.SampleCount == 2 && viewer.LatestStatisticsReport == initial,
            "The publication throttle keeps collecting while leaving the latest snapshot immutable.");
        check(viewer.Report(2).FramesPerSecond == initial.Snapshot.FramesPerSecond &&
            viewer.Report(2).StatisticsVersion == initial.Version,
            "Ordinary frame reports read the published snapshot and its version without recapturing.");
        check(!viewer.RecordSubmittedFrame(continuity, timestamp: At(1519)) &&
            viewer.StatisticsCollector.SampleCount == 3 && viewer.LatestStatisticsVersion == initial.Version,
            "Publication remains throttled before the exact 500 ms boundary.");
        check(viewer.RecordSubmittedFrame(continuity, timestamp: At(1520)) &&
            viewer.StatisticsCollector.SampleCount == 4 && viewer.LatestStatisticsVersion == 2,
            "The configured publication boundary is independent of frame collection.");

        FrameStatisticsSnapshot beforePause = viewer.LatestStatisticsReport.Snapshot;
        check(!viewer.RecordSubmittedFrame(TimeSpan.Zero, timestamp: At(90000)) &&
            viewer.StatisticsCollector.SampleCount == 4 &&
            viewer.LatestStatisticsReport.Snapshot == beforePause,
            "Resume resets the clock without clearing retained samples or the latest publication.");
        check(viewer.RecordSubmittedFrame(continuity, timestamp: At(90020)) &&
            viewer.LatestStatisticsReport.Snapshot.SampleCount == 5 &&
            viewer.LatestStatisticsReport.Snapshot.MaximumFrameMilliseconds < 500,
            "The suspension gap never becomes a frame interval and fresh intervals publish immediately.");
        ViewerStatisticsReport beforeDisabled = viewer.LatestStatisticsReport;
        viewer.ConfigureStatistics(new(false, 500));
        check(!viewer.RecordSubmittedFrame(continuity, timestamp: At(190000)) &&
            viewer.StatisticsCollector.SampleCount == 5 && viewer.LatestStatisticsReport == beforeDisabled,
            "Disabled diagnostics ignore submissions while preserving the borrowed collector and latest snapshot.");
        viewer.ConfigureStatistics(new(true, 0));
        check(!viewer.RecordSubmittedFrame(continuity, timestamp: At(290000)) &&
            viewer.RecordSubmittedFrame(continuity, timestamp: At(290010)) &&
            viewer.RecordSubmittedFrame(continuity, timestamp: At(290020)) &&
            viewer.StatisticsCollector.SampleCount == 7,
            "Re-enable primes a fresh clock and zero interval publishes every positive sample.");

        long beforeReset = viewer.LatestStatisticsVersion;
        ViewerStatisticsReport reset = viewer.ResetStatistics();
        check(reset.Version == beforeReset + 1 && reset.Snapshot == default &&
            viewer.StatisticsCollector.SampleCount == 0 && viewer.StatisticsCollector.ResourceCounts is null,
            "Explicit reset clears samples, resource counts and publication with one monotonically new version.");
        check(!viewer.RecordSubmittedFrame(continuity, timestamp: At(300000)) &&
            viewer.RecordSubmittedFrame(continuity, timestamp: At(300020)),
            "Explicit reset requires the same fresh successful boundary as initial attachment.");

        viewer.ConfigureStatistics(new(true, 500));
        long explicitVersion = viewer.LatestStatisticsVersion;
        ViewerStatisticsReport explicitPublication = viewer.PublishStatisticsSnapshot(At(300030));
        check(explicitPublication.Version == explicitVersion + 1 && explicitPublication.Snapshot.SampleCount == 1,
            "Explicit publication samples no new frame and advances the observable version.");
        check(!viewer.RecordSubmittedFrame(continuity, timestamp: At(300040)) &&
            !viewer.RecordSubmittedFrame(continuity, timestamp: At(300529)) &&
            viewer.RecordSubmittedFrame(continuity, timestamp: At(300530)),
            "Explicit publication marks the same throttle clock as native PublishSnapshot.");

        int beforeNonpositive = viewer.StatisticsCollector.SampleCount;
        check(!viewer.RecordSubmittedFrame(continuity, timestamp: At(300530)) &&
            !viewer.RecordSubmittedFrame(continuity, timestamp: At(300520)) &&
            viewer.StatisticsCollector.SampleCount == beforeNonpositive,
            "Duplicate/backward timestamps update the baseline without recording invalid durations.");
        double beforePositiveWindow = viewer.StatisticsCollector.CaptureSnapshot().WindowDurationMilliseconds;
        viewer.ConfigureStatistics(new(true, 0));
        check(viewer.RecordSubmittedFrame(continuity, timestamp: At(300540)) &&
            Math.Abs(viewer.LatestStatisticsReport.Snapshot.WindowDurationMilliseconds - beforePositiveWindow - 20) < 0.00001,
            "A subsequent valid timestamp uses the updated baseline after a backward timestamp.");

        RenderResourceCounts known = new(meshCount: 3, vertexCount: 400, materialCount: 3);
        check(viewer.RecordSubmittedFrame(continuity, default(SceneRendererFrameTimings),
            drawCallCount: 4, primitiveCount: 100, resourceCounts: known, timestamp: At(300560)) &&
            viewer.LatestStatisticsReport.Snapshot.ResourceCounts == known &&
            viewer.LatestStatisticsReport.Snapshot.DrawCallSampleCount == 1 &&
            viewer.LatestStatisticsReport.Snapshot.AverageDrawCallCount == 4 &&
            viewer.LatestStatisticsReport.Snapshot.PrimitiveSampleCount == 1 &&
            viewer.LatestStatisticsReport.Snapshot.AveragePrimitiveCount == 100,
            "Explicit host-known counts use the shared collector's available-sample averages.");
        check(known.TextureCount is null && known.BufferCount is null && known.PipelineCount is null &&
            known.EstimatedCpuBytes is null && known.EstimatedGpuBytes is null,
            "Known scene resources do not imply unknown backend texture/buffer/pipeline or memory counters.");
        check(viewer.RecordSubmittedFrame(continuity, timestamp: At(300580)) &&
            viewer.LatestStatisticsReport.Snapshot.ResourceCounts is null,
            "A source can explicitly clear resources instead of retaining stale measurements.");

        using ViewerScene configuredCounts = Open();
        configuredCounts.ConfigureStatistics(new(true, 0, DrawCallCount: 0, PrimitiveCount: 0));
        configuredCounts.RecordSubmittedFrame(null, timestamp: At(1000));
        configuredCounts.RecordSubmittedFrame(continuity, timestamp: At(1020));
        check(configuredCounts.LatestStatisticsReport.Snapshot.DrawCallSampleCount == 1 &&
            configuredCounts.LatestStatisticsReport.Snapshot.AverageDrawCallCount == 0 &&
            configuredCounts.LatestStatisticsReport.Snapshot.PrimitiveSampleCount == 1 &&
            configuredCounts.LatestStatisticsReport.Snapshot.AveragePrimitiveCount == 0,
            "Configured application counts preserve measured zero separately from null/unavailable.");
        configuredCounts.ConfigureStatistics(new(true, 0, DrawCallCount: 7, PrimitiveCount: 13));
        configuredCounts.RecordSubmittedFrame(continuity, timestamp: At(1040));
        check(configuredCounts.LatestStatisticsReport.Snapshot.AverageDrawCallCount == 3.5 &&
            configuredCounts.LatestStatisticsReport.Snapshot.AveragePrimitiveCount == 6.5,
            "Observable count options configure subsequent successful samples without clearing history.");
        configuredCounts.RecordSubmittedFrame(continuity, drawCallCount: 4, primitiveCount: 8, timestamp: At(1060));
        check(configuredCounts.LatestStatisticsReport.Snapshot.DrawCallSampleCount == 3 &&
            configuredCounts.LatestStatisticsReport.Snapshot.AverageDrawCallCount == 11d / 3 &&
            configuredCounts.LatestStatisticsReport.Snapshot.AveragePrimitiveCount == 7,
            "Explicit submitted-frame counts override source-configured defaults.");
        configuredCounts.ConfigureStatistics(new(true, 0));
        configuredCounts.RecordSubmittedFrame(continuity, timestamp: At(1080));
        check(configuredCounts.LatestStatisticsReport.Snapshot.SampleCount == 4 &&
            configuredCounts.LatestStatisticsReport.Snapshot.DrawCallSampleCount == 3 &&
            configuredCounts.LatestStatisticsReport.Snapshot.PrimitiveSampleCount == 3,
            "Null count options mark future samples unavailable without fabricating zero.");
        check(Throws<ArgumentOutOfRangeException>(() => configuredCounts.ConfigureStatistics(new(true, 0, DrawCallCount: -1))) &&
            configuredCounts.StatisticsOptions.DrawCallCount is null,
            "Negative configured draw counts cannot replace valid source options.");
        check(Throws<ArgumentOutOfRangeException>(() => configuredCounts.ConfigureStatistics(new(true, 0, PrimitiveCount: -1))) &&
            configuredCounts.StatisticsOptions.PrimitiveCount is null,
            "Negative configured primitive counts cannot replace valid source options.");
        configuredCounts.ConfigureStatistics(new(true, 0, DrawCallCount: 7, PrimitiveCount: 13));
        configuredCounts.ResetStatistics();
        check(configuredCounts.StatisticsOptions.DrawCallCount == 7 &&
            configuredCounts.StatisticsOptions.PrimitiveCount == 13,
            "Reset clears collection/publication while retaining configured application counts.");

        FrameStatisticsCollector borrowed = new();
        borrowed.RecordFrame(new(TimeSpan.FromMilliseconds(40)));
        borrowed.UpdateResourceCounts(known);
        FrameStatisticsCollector previous = viewer.StatisticsCollector;
        int previousSamples = previous.SampleCount;
        viewer.StatisticsCollector = borrowed;
        check(viewer.LatestStatisticsReport.Snapshot == default && borrowed.SampleCount == 1 &&
            previous.SampleCount == previousSamples && borrowed.ResourceCounts == known,
            "Collector replacement clears publication/clock while preserving both borrowed collectors.");
        check(!viewer.RecordSubmittedFrame(continuity, timestamp: At(400000)) &&
            viewer.RecordSubmittedFrame(continuity, resourceCounts: known, timestamp: At(400020)) &&
            viewer.LatestStatisticsReport.Snapshot.SampleCount == 2,
            "A replacement collector resumes from its retained samples after a new boundary.");
        ViewerStatisticsReport unchanged = viewer.LatestStatisticsReport;
        viewer.StatisticsCollector = borrowed;
        check(viewer.LatestStatisticsReport == unchanged,
            "Reassigning the same collector does not produce duplicate source changes.");

        using ViewerScene zeroTimestamp = Open();
        zeroTimestamp.ConfigureStatistics(new(true, 0));
        check(!zeroTimestamp.RecordSubmittedFrame(null, timestamp: 0) &&
            zeroTimestamp.RecordSubmittedFrame(continuity, timestamp: At(10)) &&
            zeroTimestamp.LatestStatisticsReport.Snapshot.SampleCount == 1,
            "A valid timestamp of zero establishes a single boundary rather than another sentinel frame.");

        using ViewerScene pausedDrag = Open();
        pausedDrag.SetViewport(1000, 1000);
        ViewerInput pausedStep = step with { Playing = false, DeltaSeconds = 0.02f,
            FrameIntervalSeconds = 0.02f };
        pausedDrag.Apply(pausedStep);
        check(pausedDrag.BeginDrag(0.542f, 0.5f),
            "Paused diagnostics regression captures the real shared X translation Gizmo.");
        float frozenAngle = pausedDrag.Report(0).Angle;
        check(!pausedDrag.RecordSubmittedFrame(TimeSpan.Zero, timestamp: At(1000)),
            "Beginning active Gizmo rendering establishes a fresh successful-submission clock.");
        int dragPublications = 0;
        for (int index = 1; index <= 32; index++)
        {
            pausedDrag.UpdateDrag(0.542f + index * 0.001f, 0.5f);
            pausedDrag.Apply(pausedStep);
            if (pausedDrag.RecordSubmittedFrame(TimeSpan.FromMilliseconds(20),
                default(SceneRendererFrameTimings), timestamp: At(1000 + index * 20))) dragPublications++;
        }
        ViewerFrameReport duringDrag = pausedDrag.Report(32);
        check(dragPublications == 2 && pausedDrag.StatisticsCollector.SampleCount == 32 &&
            duringDrag.StatisticsVersion == 2 &&
            pausedDrag.LatestStatisticsReport.Snapshot.SampleCount == 26 &&
            Math.Abs(duringDrag.FramesPerSecond - 50) < 0.00001,
            "Paused active Gizmo submissions keep collecting and republish FPS at the 500 ms boundary.");
        check(duringDrag.Dragging && duringDrag.Angle == frozenAngle && duringDrag.ModelPosition[0] > 0,
            "Sampling active drag cadence leaves model animation paused while the shared Gizmo moves its target.");
        pausedDrag.EndDrag(false);
        ViewerStatisticsReport retainedDragStatistics = pausedDrag.LatestStatisticsReport;
        pausedDrag.Apply(pausedStep with { DeltaSeconds = 0, FrameIntervalSeconds = 0 });
        check(!pausedDrag.Report(33).Dragging &&
            !pausedDrag.RecordSubmittedFrame(TimeSpan.Zero, timestamp: At(90000)) &&
            pausedDrag.StatisticsCollector.SampleCount == 32 &&
            pausedDrag.LatestStatisticsReport == retainedDragStatistics,
            "Released Gizmo returns to independent on-demand sampling without counting idle time or clearing history.");
        check(pausedDrag.BeginDrag(0.574f, 0.5f),
            "The moved Gizmo can capture a second paused drag after the idle boundary.");
        pausedDrag.Apply(pausedStep);
        check(pausedDrag.RecordSubmittedFrame(TimeSpan.FromMilliseconds(20), timestamp: At(90020)) &&
            pausedDrag.LatestStatisticsVersion == 3 &&
            pausedDrag.LatestStatisticsReport.Snapshot.SampleCount == 33 &&
            pausedDrag.LatestStatisticsReport.Snapshot.MaximumFrameMilliseconds < 500 &&
            pausedDrag.Report(34).Angle == frozenAngle,
            "A later paused drag resumes publication while excluding the previous long idle gap.");
        pausedDrag.EndDrag(true);

        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            check(Throws<ArgumentException>(() => viewer.ConfigureStatistics(new(true, invalid))) &&
                viewer.StatisticsOptions.SnapshotIntervalMilliseconds == 0,
                "Invalid snapshot intervals cannot replace valid source options.");
        check(Throws<ArgumentException>(() => viewer.StatisticsCollector = null!), "A null collector cannot replace the source.");
        check(Throws<ArgumentException>(() => viewer.RecordSubmittedFrame(TimeSpan.FromMilliseconds(-1))),
            "Negative host continuity signals are rejected.");
        string json = JsonSerializer.Serialize(zeroTimestamp.LatestStatisticsReport,
            ViewerStatisticsTestJsonContext.Default.ViewerStatisticsReport);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement payload = document.RootElement;
        JsonElement jsonSnapshot = payload.GetProperty("snapshot");
        check(payload.GetProperty("version").GetInt64() == zeroTimestamp.LatestStatisticsVersion &&
            jsonSnapshot.GetProperty("sampleCount").GetInt32() == 1 &&
            jsonSnapshot.GetProperty("averageAcquireMilliseconds").ValueKind == JsonValueKind.Null &&
            jsonSnapshot.GetProperty("averagePresentationTotalMilliseconds").ValueKind == JsonValueKind.Null &&
            jsonSnapshot.GetProperty("averageRendererTotalMilliseconds").ValueKind == JsonValueKind.Null &&
            jsonSnapshot.GetProperty("averageDrawCallCount").ValueKind == JsonValueKind.Null &&
            jsonSnapshot.GetProperty("resourceCounts").ValueKind == JsonValueKind.Null &&
            !payload.TryGetProperty("Snapshot", out _) && !jsonSnapshot.TryGetProperty("gpuMilliseconds", out _),
            "Source-generated camel-case payload preserves null metrics and declares no GPU timing.");
        json = JsonSerializer.Serialize(viewer.LatestStatisticsReport,
            ViewerStatisticsTestJsonContext.Default.ViewerStatisticsReport);
        using JsonDocument resourceDocument = JsonDocument.Parse(json);
        JsonElement resources = resourceDocument.RootElement.GetProperty("snapshot").GetProperty("resourceCounts");
        check(resources.GetProperty("meshCount").GetInt64() == 3 &&
            resources.GetProperty("vertexCount").GetInt64() == 400 &&
            resources.GetProperty("pipelineCount").ValueKind == JsonValueKind.Null &&
            resources.GetProperty("estimatedGpuBytes").ValueKind == JsonValueKind.Null,
            "Generated serialization retains known resource values without filling unsupported ones.");

        viewer.Dispose();
        check(!viewer.RecordSubmittedFrame(continuity, timestamp: At(500000)) && borrowed.SampleCount == 2,
            "Disposal ignores detached submissions and preserves the assigned collector.");
        check(Throws<ObjectDisposedException>(() => viewer.ResetStatistics()),
            "Disposed diagnostics reject publication/reset operations.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ViewerStatisticsReport))]
internal partial class ViewerStatisticsTestJsonContext : JsonSerializerContext;

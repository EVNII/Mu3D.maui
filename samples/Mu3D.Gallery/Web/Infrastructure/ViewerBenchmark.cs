using System.Diagnostics;
using Mu3D.Rendering;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Validation-host instrumentation only. Arrays are allocated once before the measured window;
// no per-frame telemetry serialization, GPU fence, readback, forced GC or public API is added.
internal enum ViewerBenchmarkStage { InputJson, SceneUpdate, RenderSubmit, AnchorsReport, OutputJson }

internal sealed record ViewerBenchmarkReport(string Build, int FrameCount, string[] Stages,
    double[][] StageMilliseconds, long[][] StageAllocatedBytes, double[] TotalMilliseconds,
    long[] TotalAllocatedBytes, SceneRendererFrameTimings[] RendererCpuTimings, int[] Collections,
    long StopwatchFrequency, string AllocationScope);

internal sealed class ViewerBenchmark
{
    private readonly double[][] milliseconds;
    private readonly long[][] bytes;
    private readonly double[] totalMilliseconds;
    private readonly long[] totalBytes;
    private readonly SceneRendererFrameTimings[] rendererTimings;
    private readonly int[] collections;
    private long startTicks, startBytes, previousTicks, previousBytes;
    private int nextStage;
    private bool active;

    internal ViewerBenchmark(int frames)
    {
        if (frames is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(frames));
        milliseconds = Enumerable.Range(0, 5).Select(_ => new double[frames]).ToArray();
        bytes = Enumerable.Range(0, 5).Select(_ => new long[frames]).ToArray();
        totalMilliseconds = new double[frames];
        totalBytes = new long[frames];
        rendererTimings = new SceneRendererFrameTimings[frames];
        collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
    }

    internal int FrameCount { get; private set; }

    internal void BeginFrame()
    {
        if (active || FrameCount == totalBytes.Length) throw new InvalidOperationException("Benchmark window is full or already active.");
        nextStage = 0;
        active = true;
        startBytes = previousBytes = GC.GetAllocatedBytesForCurrentThread();
        startTicks = previousTicks = Stopwatch.GetTimestamp();
    }

    internal void Mark(ViewerBenchmarkStage stage)
    {
        long now = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
        if (!active || (int)stage != nextStage) throw new InvalidOperationException("Benchmark stages must remain ordered.");
        milliseconds[(int)stage][FrameCount] = (now - previousTicks) * 1000d / Stopwatch.Frequency;
        bytes[(int)stage][FrameCount] = allocated - previousBytes;
        previousTicks = now; previousBytes = allocated; nextStage++;
    }

    internal void SetRendererTimings(SceneRendererFrameTimings value) => rendererTimings[FrameCount] = value;

    internal void CompleteFrame()
    {
        if (!active || nextStage != 5) throw new InvalidOperationException("A benchmark frame must complete all stages.");
        totalMilliseconds[FrameCount] = (previousTicks - startTicks) * 1000d / Stopwatch.Frequency;
        totalBytes[FrameCount] = previousBytes - startBytes;
        FrameCount++;
        active = false;
    }

    internal ViewerBenchmarkReport Finish(string build)
    {
        if (active || FrameCount != totalBytes.Length) throw new InvalidOperationException("Benchmark did not complete its window.");
        return new(build, FrameCount, Enum.GetNames<ViewerBenchmarkStage>(), milliseconds, bytes,
            totalMilliseconds, totalBytes, rendererTimings,
            Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - collections[generation]).ToArray(),
            Stopwatch.Frequency, "Managed current-thread allocated bytes; excludes JS, native heap and GPU memory. CPU wall times include probe overhead/back-pressure; not GPU timestamps.");
    }
}

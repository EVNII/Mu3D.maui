using System.Globalization;
using Mu3D.Toolkit.Diagnostics;

namespace Mu3D.Maui.Toolkit.Diagnostics;

internal readonly record struct FrameStatisticsText(string Headline, string Details);

internal static class FrameStatisticsDisplayState
{
    internal static FrameStatisticsDisplayMode Next(FrameStatisticsDisplayMode mode) => mode switch
    {
        FrameStatisticsDisplayMode.Compact => FrameStatisticsDisplayMode.Normal,
        FrameStatisticsDisplayMode.Normal => FrameStatisticsDisplayMode.Detail,
        FrameStatisticsDisplayMode.Detail => FrameStatisticsDisplayMode.Compact,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    internal static bool ShowsGraph(FrameStatisticsDisplayMode mode, bool isGraphVisible) =>
        mode != FrameStatisticsDisplayMode.Compact && isGraphVisible;
}

internal static class FrameStatisticsTextFormatter
{
    private const string Unavailable = "—";

    public static FrameStatisticsText Format(
        FrameStatisticsSnapshot snapshot,
        bool isDetailed) => Format(snapshot, isDetailed
            ? FrameStatisticsDisplayMode.Detail
            : FrameStatisticsDisplayMode.Normal);

    internal static FrameStatisticsText Format(
        FrameStatisticsSnapshot snapshot,
        FrameStatisticsDisplayMode mode,
        double? minimumFps = null,
        double? maximumFps = null)
    {
        if (mode == FrameStatisticsDisplayMode.Compact)
        {
            return new FrameStatisticsText(
                snapshot.SampleCount == 0
                    ? "— FPS"
                    : string.Create(CultureInfo.InvariantCulture, $"{snapshot.FramesPerSecond:0} FPS"),
                string.Empty);
        }
        if (snapshot.SampleCount == 0)
        {
            return new FrameStatisticsText(
                "Waiting for frame samples…",
                snapshot.ResourceCounts is RenderResourceCounts emptySnapshotResources
                    ? FormatResources(emptySnapshotResources)
                    : "No completed frame interval has been sampled.");
        }

        string headline = minimumFps is double minimum && maximumFps is double maximum
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{snapshot.FramesPerSecond:0} FPS ({minimum:0}–{maximum:0})  ·  " +
                $"{snapshot.AverageFrameMilliseconds:0.00} ms")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{snapshot.FramesPerSecond:0.0} FPS  ·  {snapshot.AverageFrameMilliseconds:0.00} ms");
        string summary =
            $"Render {FormatMilliseconds(snapshot.AverageRendererTotalMilliseconds)}  ·  " +
            $"Present {FormatMilliseconds(snapshot.AveragePresentationTotalMilliseconds)}\n" +
            $"Draws {FormatAverage(snapshot.AverageDrawCallCount)}  ·  " +
            $"Primitives {FormatAverage(snapshot.AveragePrimitiveCount)}";
        if (mode != FrameStatisticsDisplayMode.Detail)
        {
            return new FrameStatisticsText(headline, summary);
        }

        string presentation =
            $"Presentation  acquire {FormatMilliseconds(snapshot.AverageAcquireMilliseconds)}  ·  " +
            $"render {FormatMilliseconds(snapshot.AveragePresentationRenderMilliseconds)}  ·  " +
            $"present {FormatMilliseconds(snapshot.AveragePresentMilliseconds)}  ·  " +
            $"total {FormatMilliseconds(snapshot.AveragePresentationTotalMilliseconds)}";
        string renderer =
            $"Renderer  prepare {FormatMilliseconds(snapshot.AverageRendererPrepareMilliseconds)}  ·  " +
            $"encode {FormatMilliseconds(snapshot.AverageRendererEncodeMilliseconds)}  ·  " +
            $"submit {FormatMilliseconds(snapshot.AverageRendererSubmitMilliseconds)}  ·  " +
            $"trim {FormatMilliseconds(snapshot.AverageRendererCacheTrimMilliseconds)}  ·  " +
            $"total {FormatMilliseconds(snapshot.AverageRendererTotalMilliseconds)}";
        string frameRange = string.Create(
            CultureInfo.InvariantCulture,
            $"Frame  min {snapshot.MinimumFrameMilliseconds:0.00} ms  ·  " +
            $"avg {snapshot.AverageFrameMilliseconds:0.00} ms  ·  " +
            $"max {snapshot.MaximumFrameMilliseconds:0.00} ms  ·  " +
            $"samples {snapshot.SampleCount}");
        string resources = snapshot.ResourceCounts is RenderResourceCounts counts
            ? FormatResources(counts)
            : "Resources  —";

        return new FrameStatisticsText(
            headline,
            $"{frameRange}\n{presentation}\n{renderer}\n{summary}\n{resources}");
    }

    private static string FormatMilliseconds(double? value) => value is double milliseconds
        ? string.Create(CultureInfo.InvariantCulture, $"{milliseconds:0.00} ms")
        : Unavailable;

    private static string FormatAverage(double? value) => value is double average
        ? string.Create(CultureInfo.InvariantCulture, $"{average:0.0}")
        : Unavailable;

    private static string FormatResources(RenderResourceCounts counts)
    {
        List<string> values = [];
        AddCount(values, "meshes", counts.MeshCount);
        AddCount(values, "vertices", counts.VertexCount);
        AddCount(values, "materials", counts.MaterialCount);
        AddCount(values, "textures", counts.TextureCount);
        AddCount(values, "buffers", counts.BufferCount);
        AddCount(values, "pipelines", counts.PipelineCount);
        AddBytes(values, "CPU", counts.EstimatedCpuBytes);
        AddBytes(values, "GPU", counts.EstimatedGpuBytes);
        return values.Count == 0
            ? "Resources  —"
            : $"Resources  {string.Join("  ·  ", values)}";
    }

    private static void AddCount(List<string> values, string name, long? value)
    {
        if (value is long count)
        {
            values.Add(string.Create(CultureInfo.InvariantCulture, $"{name} {count:N0}"));
        }
    }

    private static void AddBytes(List<string> values, string name, long? value)
    {
        if (value is not long bytes)
        {
            return;
        }
        string formatted = bytes >= 1024 * 1024
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024d * 1024d):0.0} MiB")
            : bytes >= 1024
                ? string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024d:0.0} KiB")
                : string.Create(CultureInfo.InvariantCulture, $"{bytes} B");
        values.Add($"{name} {formatted}");
    }
}

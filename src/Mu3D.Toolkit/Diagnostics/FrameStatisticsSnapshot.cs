namespace Mu3D.Toolkit.Diagnostics;

/// <summary>Stores one immutable rolling frame and render statistics snapshot.</summary>
public readonly record struct FrameStatisticsSnapshot
{
    internal FrameStatisticsSnapshot(
        int sampleCount,
        double windowDurationMilliseconds,
        double averageFrameMilliseconds,
        double minimumFrameMilliseconds,
        double maximumFrameMilliseconds,
        int presentationSampleCount,
        double? averageAcquireMilliseconds,
        double? averagePresentationRenderMilliseconds,
        double? averagePresentMilliseconds,
        double? averagePresentationTotalMilliseconds,
        int rendererSampleCount,
        double? averageRendererPrepareMilliseconds,
        double? averageRendererEncodeMilliseconds,
        double? averageRendererSubmitMilliseconds,
        double? averageRendererCacheTrimMilliseconds,
        double? averageRendererTotalMilliseconds,
        int drawCallSampleCount,
        double? averageDrawCallCount,
        int primitiveSampleCount,
        double? averagePrimitiveCount,
        RenderResourceCounts? resourceCounts)
    {
        SampleCount = sampleCount;
        WindowDurationMilliseconds = windowDurationMilliseconds;
        FramesPerSecond = sampleCount == 0
            ? 0d
            : sampleCount * 1000d / windowDurationMilliseconds;
        AverageFrameMilliseconds = averageFrameMilliseconds;
        MinimumFrameMilliseconds = minimumFrameMilliseconds;
        MaximumFrameMilliseconds = maximumFrameMilliseconds;
        PresentationSampleCount = presentationSampleCount;
        AverageAcquireMilliseconds = averageAcquireMilliseconds;
        AveragePresentationRenderMilliseconds = averagePresentationRenderMilliseconds;
        AveragePresentMilliseconds = averagePresentMilliseconds;
        AveragePresentationTotalMilliseconds = averagePresentationTotalMilliseconds;
        RendererSampleCount = rendererSampleCount;
        AverageRendererPrepareMilliseconds = averageRendererPrepareMilliseconds;
        AverageRendererEncodeMilliseconds = averageRendererEncodeMilliseconds;
        AverageRendererSubmitMilliseconds = averageRendererSubmitMilliseconds;
        AverageRendererCacheTrimMilliseconds = averageRendererCacheTrimMilliseconds;
        AverageRendererTotalMilliseconds = averageRendererTotalMilliseconds;
        DrawCallSampleCount = drawCallSampleCount;
        AverageDrawCallCount = averageDrawCallCount;
        PrimitiveSampleCount = primitiveSampleCount;
        AveragePrimitiveCount = averagePrimitiveCount;
        ResourceCounts = resourceCounts;
    }

    /// <summary>Gets the number of frames currently retained in the rolling window.</summary>
    public int SampleCount { get; }

    /// <summary>Gets the sum of retained host-observed frame intervals in milliseconds.</summary>
    public double WindowDurationMilliseconds { get; }

    /// <summary>Gets frames per second from retained frame count divided by window duration.</summary>
    public double FramesPerSecond { get; }

    /// <summary>Gets the average host-observed frame interval in milliseconds.</summary>
    public double AverageFrameMilliseconds { get; }

    /// <summary>Gets the minimum retained frame interval in milliseconds.</summary>
    public double MinimumFrameMilliseconds { get; }

    /// <summary>Gets the maximum retained frame interval in milliseconds.</summary>
    public double MaximumFrameMilliseconds { get; }

    /// <summary>Gets how many retained samples supplied presentation timings.</summary>
    public int PresentationSampleCount { get; }

    /// <summary>Gets average surface-acquire CPU time, or null when unavailable.</summary>
    public double? AverageAcquireMilliseconds { get; }

    /// <summary>Gets average caller render/encode/submit CPU time inside presentation.</summary>
    public double? AveragePresentationRenderMilliseconds { get; }

    /// <summary>Gets average surface-present/error-poll CPU time, or null when unavailable.</summary>
    public double? AveragePresentMilliseconds { get; }

    /// <summary>Gets average total presentation-operation CPU time, or null when unavailable.</summary>
    public double? AveragePresentationTotalMilliseconds { get; }

    /// <summary>Gets how many retained samples supplied scene-renderer timings.</summary>
    public int RendererSampleCount { get; }

    /// <summary>Gets average scene preparation CPU time, or null when unavailable.</summary>
    public double? AverageRendererPrepareMilliseconds { get; }

    /// <summary>Gets average scene command-encoding CPU time, or null when unavailable.</summary>
    public double? AverageRendererEncodeMilliseconds { get; }

    /// <summary>Gets average renderer queue-submission CPU time, or null when unavailable.</summary>
    public double? AverageRendererSubmitMilliseconds { get; }

    /// <summary>Gets average renderer cache-trimming CPU time, or null when unavailable.</summary>
    public double? AverageRendererCacheTrimMilliseconds { get; }

    /// <summary>Gets average total renderer-invocation CPU time, or null when unavailable.</summary>
    public double? AverageRendererTotalMilliseconds { get; }

    /// <summary>Gets how many retained samples supplied draw-call counts.</summary>
    public int DrawCallSampleCount { get; }

    /// <summary>Gets the average draw-call count over samples that supplied it.</summary>
    public double? AverageDrawCallCount { get; }

    /// <summary>Gets how many retained samples supplied rendered primitive counts.</summary>
    public int PrimitiveSampleCount { get; }

    /// <summary>Gets the average primitive count over samples that supplied it.</summary>
    public double? AveragePrimitiveCount { get; }

    /// <summary>Gets the latest explicit resource-count snapshot, or null when unavailable.</summary>
    public RenderResourceCounts? ResourceCounts { get; }
}

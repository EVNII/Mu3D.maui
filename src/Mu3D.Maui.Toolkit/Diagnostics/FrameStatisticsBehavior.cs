using System.Diagnostics;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Rendering;
using Mu3D.Toolkit.Diagnostics;

namespace Mu3D.Maui.Toolkit.Diagnostics;

/// <summary>Collects successful scene-view frames and publishes throttled immutable snapshots.</summary>
/// <remarks>
/// The behavior owns only its default collector. An explicitly assigned collector is borrowed and
/// is never reset automatically. It performs no layout or rendering and can feed application UI or
/// telemetry through <see cref="SnapshotUpdated"/>.
/// </remarks>
public sealed class FrameStatisticsBehavior : Behavior<Mu3DSceneView>, IDisposable
{
    private readonly FrameStatisticsSamplingState samplingState = new();
    private Mu3DSceneView? sceneView;
    private bool subscribed;
    private bool disposed;

    /// <summary>Identifies the <see cref="Collector"/> bindable property.</summary>
    public static readonly BindableProperty CollectorProperty = BindableProperty.Create(
        nameof(Collector),
        typeof(FrameStatisticsCollector),
        typeof(FrameStatisticsBehavior),
        defaultValueCreator: static _ => new FrameStatisticsCollector(),
        validateValue: static (_, value) => value is FrameStatisticsCollector,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsBehavior)bindable).ClearPublishedState());

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(FrameStatisticsBehavior),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsBehavior)bindable).RefreshSubscription());

    /// <summary>Identifies the <see cref="SnapshotInterval"/> bindable property.</summary>
    public static readonly BindableProperty SnapshotIntervalProperty = BindableProperty.Create(
        nameof(SnapshotInterval),
        typeof(TimeSpan),
        typeof(FrameStatisticsBehavior),
        TimeSpan.FromMilliseconds(500),
        validateValue: static (_, value) => value is TimeSpan interval && interval >= TimeSpan.Zero);

    /// <summary>Identifies the <see cref="DrawCallCount"/> bindable property.</summary>
    public static readonly BindableProperty DrawCallCountProperty = BindableProperty.Create(
        nameof(DrawCallCount),
        typeof(long?),
        typeof(FrameStatisticsBehavior),
        default(long?),
        validateValue: static (_, value) =>
            value is null || value is long count && count >= 0);

    /// <summary>Identifies the <see cref="PrimitiveCount"/> bindable property.</summary>
    public static readonly BindableProperty PrimitiveCountProperty = BindableProperty.Create(
        nameof(PrimitiveCount),
        typeof(long?),
        typeof(FrameStatisticsBehavior),
        default(long?),
        validateValue: static (_, value) =>
            value is null || value is long count && count >= 0);

    /// <summary>Occurs when the configured publication interval produces a new snapshot.</summary>
    public event EventHandler<FrameStatisticsSnapshotEventArgs>? SnapshotUpdated;

    /// <summary>Gets or sets the non-null collector used by this behavior.</summary>
    public FrameStatisticsCollector Collector
    {
        get => (FrameStatisticsCollector)GetValue(CollectorProperty);
        set => SetValue(CollectorProperty, value);
    }

    /// <summary>Gets or sets whether the behavior subscribes to scene-view frame events.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the non-negative interval between snapshot events. Zero publishes every sample.
    /// </summary>
    public TimeSpan SnapshotInterval
    {
        get => (TimeSpan)GetValue(SnapshotIntervalProperty);
        set => SetValue(SnapshotIntervalProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional application-known draw-call count recorded with each frame.
    /// </summary>
    public long? DrawCallCount
    {
        get => (long?)GetValue(DrawCallCountProperty);
        set => SetValue(DrawCallCountProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional application-known rendered primitive count recorded with each frame.
    /// </summary>
    public long? PrimitiveCount
    {
        get => (long?)GetValue(PrimitiveCountProperty);
        set => SetValue(PrimitiveCountProperty, value);
    }

    /// <summary>Gets the most recently published snapshot.</summary>
    public FrameStatisticsSnapshot LatestSnapshot { get; private set; }

    /// <inheritdoc />
    protected override void OnAttachedTo(Mu3DSceneView bindable)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        base.OnAttachedTo(bindable);
        sceneView = bindable;
        RefreshSubscription();
    }

    /// <inheritdoc />
    protected override void OnDetachingFrom(Mu3DSceneView bindable)
    {
        Detach();
        sceneView = null;
        base.OnDetachingFrom(bindable);
    }

    /// <summary>Captures and publishes a snapshot immediately without recording another frame.</summary>
    public FrameStatisticsSnapshot PublishSnapshot()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        LatestSnapshot = Collector.CaptureSnapshot();
        samplingState.MarkPublished(Stopwatch.GetTimestamp());
        SnapshotUpdated?.Invoke(this, new FrameStatisticsSnapshotEventArgs(LatestSnapshot));
        return LatestSnapshot;
    }

    /// <summary>Clears the active collector and published timing state.</summary>
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Collector.Reset();
        ClearPublishedState();
    }

    /// <summary>Detaches events without disposing the scene view or assigned collector.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Detach();
        sceneView = null;
        SnapshotUpdated = null;
    }

    internal void ProcessPresentedFrame(
        long timestamp,
        IPresentationSurfaceSession session,
        SceneRenderer? renderer)
    {
        if (!subscribed)
        {
            return;
        }
        ArgumentNullException.ThrowIfNull(session);
        if (samplingState.TryRecord(
            timestamp,
            SnapshotInterval,
            Collector,
            session.LastFrameTimings,
            renderer?.LastFrameTimings,
            DrawCallCount,
            PrimitiveCount,
            out FrameStatisticsSnapshot snapshot))
        {
            LatestSnapshot = snapshot;
            SnapshotUpdated?.Invoke(this, new FrameStatisticsSnapshotEventArgs(LatestSnapshot));
        }
    }

    private void RefreshSubscription()
    {
        Detach();
        if (disposed || !IsEnabled || sceneView is null)
        {
            return;
        }
        sceneView.FramePresented += OnFramePresented;
        sceneView.PresentationSessionChanged += OnPresentationSessionChanged;
        subscribed = true;
    }

    private void Detach()
    {
        if (sceneView is not null)
        {
            sceneView.FramePresented -= OnFramePresented;
            sceneView.PresentationSessionChanged -= OnPresentationSessionChanged;
        }
        subscribed = false;
        samplingState.ResetTimestamps();
    }

    private void ClearPublishedState()
    {
        LatestSnapshot = default;
        samplingState.ResetTimestamps();
    }

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        if (e.Status is PresentationSurfaceFrameStatus.PresentedOptimal or
            PresentationSurfaceFrameStatus.PresentedSuboptimal)
        {
            IPresentationSurfaceSession? session = sceneView?.PresentationSession;
            if (session is not null)
            {
                ProcessPresentedFrame(Stopwatch.GetTimestamp(), session, sceneView?.Renderer);
            }
        }
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        _ = e;
        samplingState.ResetTimestamps();
    }
}

/// <summary>Reports one immutable frame-statistics snapshot.</summary>
/// <param name="Snapshot">The newly published rolling snapshot.</param>
public sealed class FrameStatisticsSnapshotEventArgs(FrameStatisticsSnapshot Snapshot) : EventArgs
{
    /// <summary>Gets the newly published rolling snapshot.</summary>
    public FrameStatisticsSnapshot Snapshot { get; } = Snapshot;
}

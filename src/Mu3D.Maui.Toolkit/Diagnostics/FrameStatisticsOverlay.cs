using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Maui.Toolkit.Overlays;
using Mu3D.Toolkit.Diagnostics;
using Mu3D.Toolkit.Helpers;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Mu3D.Maui.Toolkit.Diagnostics;

/// <summary>
/// Collects throttled frame statistics and displays them in a pass-through MAUI viewport overlay.
/// </summary>
/// <remarks>
/// The tool creates one attachment-owned <see cref="FrameStatisticsBehavior"/> and one
/// <see cref="FrameStatisticsView"/>. It borrows the application-visible <see cref="Collector"/>,
/// owns no render loop and uses the common per-viewport overlay manager for layout and lifecycle.
/// </remarks>
public sealed class FrameStatisticsOverlay : BindableObject, IViewportTool
{
    private ToolAttachment? attachment;
    private FrameStatisticsSnapshot latestSnapshot;

    /// <summary>Identifies the <see cref="Collector"/> bindable property.</summary>
    public static readonly BindableProperty CollectorProperty = BindableProperty.Create(
        nameof(Collector),
        typeof(FrameStatisticsCollector),
        typeof(FrameStatisticsOverlay),
        defaultValueCreator: static _ => new FrameStatisticsCollector(),
        validateValue: static (_, value) => value is FrameStatisticsCollector,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).OnCollectorChanged());

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(FrameStatisticsOverlay),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.ApplyCollectionProperties());

    /// <summary>Identifies the <see cref="IsVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible),
        typeof(bool),
        typeof(FrameStatisticsOverlay),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.RefreshOverlay());

    /// <summary>Identifies the <see cref="SnapshotInterval"/> bindable property.</summary>
    public static readonly BindableProperty SnapshotIntervalProperty = BindableProperty.Create(
        nameof(SnapshotInterval),
        typeof(TimeSpan),
        typeof(FrameStatisticsOverlay),
        TimeSpan.FromMilliseconds(500d),
        validateValue: static (_, value) =>
            value is TimeSpan interval && interval >= TimeSpan.Zero,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.ApplyCollectionProperties());

    /// <summary>Identifies the <see cref="DrawCallCount"/> bindable property.</summary>
    public static readonly BindableProperty DrawCallCountProperty = BindableProperty.Create(
        nameof(DrawCallCount),
        typeof(long?),
        typeof(FrameStatisticsOverlay),
        default(long?),
        validateValue: static (_, value) => value is null || value is long count && count >= 0,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.ApplyCollectionProperties());

    /// <summary>Identifies the <see cref="PrimitiveCount"/> bindable property.</summary>
    public static readonly BindableProperty PrimitiveCountProperty = BindableProperty.Create(
        nameof(PrimitiveCount),
        typeof(long?),
        typeof(FrameStatisticsOverlay),
        default(long?),
        validateValue: static (_, value) => value is null || value is long count && count >= 0,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.ApplyCollectionProperties());

    /// <summary>Identifies the <see cref="IsDetailed"/> bindable property.</summary>
    public static readonly BindableProperty IsDetailedProperty = BindableProperty.Create(
        nameof(IsDetailed),
        typeof(bool),
        typeof(FrameStatisticsOverlay),
        false,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.ApplyDisplayProperties());

    /// <summary>Identifies the <see cref="IsGraphVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsGraphVisibleProperty = BindableProperty.Create(
        nameof(IsGraphVisible),
        typeof(bool),
        typeof(FrameStatisticsOverlay),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.ApplyDisplayProperties());

    /// <summary>Identifies the <see cref="TextColor"/> bindable property.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(MauiColor),
        typeof(FrameStatisticsOverlay),
        Colors.White,
        validateValue: static (_, value) => value is MauiColor,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.ApplyDisplayProperties());

    /// <summary>Identifies the <see cref="GraphColor"/> bindable property.</summary>
    public static readonly BindableProperty GraphColorProperty = BindableProperty.Create(
        nameof(GraphColor),
        typeof(MauiColor),
        typeof(FrameStatisticsOverlay),
        Colors.Cyan,
        validateValue: static (_, value) => value is MauiColor,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.ApplyDisplayProperties());

    /// <summary>Identifies the <see cref="GraphBackgroundColor"/> bindable property.</summary>
    public static readonly BindableProperty GraphBackgroundColorProperty = BindableProperty.Create(
        nameof(GraphBackgroundColor),
        typeof(MauiColor),
        typeof(FrameStatisticsOverlay),
        new MauiColor(0f, 0.035f, 0.09f, 1f),
        validateValue: static (_, value) => value is MauiColor,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.ApplyDisplayProperties());

    /// <summary>Identifies the <see cref="BackgroundColor"/> bindable property.</summary>
    public static readonly BindableProperty BackgroundColorProperty = BindableProperty.Create(
        nameof(BackgroundColor),
        typeof(MauiColor),
        typeof(FrameStatisticsOverlay),
        MauiColor.FromArgb("#D9192438"),
        validateValue: static (_, value) => value is MauiColor,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.RefreshOverlay());

    /// <summary>Identifies the <see cref="Placement"/> bindable property.</summary>
    public static readonly BindableProperty PlacementProperty = BindableProperty.Create(
        nameof(Placement),
        typeof(ViewportOverlayPlacement),
        typeof(FrameStatisticsOverlay),
        ViewportOverlayPlacement.TopRight,
        validateValue: static (_, value) =>
            value is ViewportOverlayPlacement placement && Enum.IsDefined(placement),
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.RefreshOverlay());

    /// <summary>Identifies the <see cref="Margin"/> bindable property.</summary>
    public static readonly BindableProperty MarginProperty = BindableProperty.Create(
        nameof(Margin),
        typeof(double),
        typeof(FrameStatisticsOverlay),
        12d,
        validateValue: static (_, value) =>
            value is double number && double.IsFinite(number) && number >= 0d,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.RefreshOverlay());

    /// <summary>Identifies the <see cref="MaximumWidth"/> bindable property.</summary>
    public static readonly BindableProperty MaximumWidthProperty = BindableProperty.Create(
        nameof(MaximumWidth),
        typeof(double),
        typeof(FrameStatisticsOverlay),
        520d,
        validateValue: static (_, value) =>
            value is double number && double.IsFinite(number) && number > 0d,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsOverlay)bindable).attachment?.RefreshOverlay());

    /// <summary>Occurs when the collection interval publishes a new immutable snapshot.</summary>
    public event EventHandler<FrameStatisticsSnapshotEventArgs>? SnapshotUpdated;

    /// <summary>Gets or sets the non-null application-visible statistics collector.</summary>
    public FrameStatisticsCollector Collector
    {
        get => (FrameStatisticsCollector)GetValue(CollectorProperty);
        set => SetValue(CollectorProperty, value);
    }

    /// <summary>Gets or sets whether frame collection is active while attached.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets or sets whether the statistics panel is mounted in the viewport.</summary>
    /// <remarks>Visibility does not change <see cref="IsEnabled"/> collection state.</remarks>
    public bool IsVisible
    {
        get => (bool)GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>Gets or sets the non-negative interval between published snapshots.</summary>
    public TimeSpan SnapshotInterval
    {
        get => (TimeSpan)GetValue(SnapshotIntervalProperty);
        set => SetValue(SnapshotIntervalProperty, value);
    }

    /// <summary>Gets or sets the optional application-known draw-call count.</summary>
    public long? DrawCallCount
    {
        get => (long?)GetValue(DrawCallCountProperty);
        set => SetValue(DrawCallCountProperty, value);
    }

    /// <summary>Gets or sets the optional application-known rendered primitive count.</summary>
    public long? PrimitiveCount
    {
        get => (long?)GetValue(PrimitiveCountProperty);
        set => SetValue(PrimitiveCountProperty, value);
    }

    /// <summary>Gets or sets whether timing and resource details are shown.</summary>
    public bool IsDetailed
    {
        get => (bool)GetValue(IsDetailedProperty);
        set => SetValue(IsDetailedProperty, value);
    }

    /// <summary>Gets or sets whether the rolling FPS graph is shown.</summary>
    public bool IsGraphVisible
    {
        get => (bool)GetValue(IsGraphVisibleProperty);
        set => SetValue(IsGraphVisibleProperty, value);
    }

    /// <summary>Gets or sets the statistics text color.</summary>
    public MauiColor TextColor
    {
        get => (MauiColor)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    /// <summary>Gets or sets the rolling graph foreground color.</summary>
    public MauiColor GraphColor
    {
        get => (MauiColor)GetValue(GraphColorProperty);
        set => SetValue(GraphColorProperty, value);
    }

    /// <summary>Gets or sets the rolling graph background color.</summary>
    public MauiColor GraphBackgroundColor
    {
        get => (MauiColor)GetValue(GraphBackgroundColorProperty);
        set => SetValue(GraphBackgroundColorProperty, value);
    }

    /// <summary>Gets or sets the panel background color.</summary>
    public MauiColor BackgroundColor
    {
        get => (MauiColor)GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    /// <summary>Gets or sets the viewport alignment containing the panel.</summary>
    public ViewportOverlayPlacement Placement
    {
        get => (ViewportOverlayPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>Gets or sets the non-negative viewport margin in device-independent units.</summary>
    public double Margin
    {
        get => (double)GetValue(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>Gets or sets the positive maximum panel width in device-independent units.</summary>
    public double MaximumWidth
    {
        get => (double)GetValue(MaximumWidthProperty);
        set => SetValue(MaximumWidthProperty, value);
    }

    /// <summary>Gets the most recently published immutable snapshot.</summary>
    public FrameStatisticsSnapshot LatestSnapshot => latestSnapshot;

    /// <summary>Captures and publishes a snapshot immediately without recording another frame.</summary>
    public FrameStatisticsSnapshot PublishSnapshot()
    {
        if (attachment is not null)
        {
            return attachment.PublishSnapshot();
        }
        FrameStatisticsSnapshot snapshot = Collector.CaptureSnapshot();
        Publish(snapshot);
        return snapshot;
    }

    /// <summary>Resets the collector, graph history and current published snapshot.</summary>
    public void Reset()
    {
        if (attachment is not null)
        {
            attachment.Reset();
            return;
        }
        Collector.Reset();
        ClearLatest();
    }

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("A FrameStatisticsOverlay can attach only once at a time.");
        }

        ToolAttachment created = new(this, context);
        attachment = created;
        try
        {
            created.Attach();
        }
        catch
        {
            attachment = null;
            created.Dispose();
            throw;
        }
        return created;
    }

    private void Publish(FrameStatisticsSnapshot snapshot)
    {
        latestSnapshot = snapshot;
        OnPropertyChanged(nameof(LatestSnapshot));
        SnapshotUpdated?.Invoke(this, new FrameStatisticsSnapshotEventArgs(snapshot));
    }

    private void OnCollectorChanged()
    {
        ClearLatest();
        attachment?.ReplaceCollector();
    }

    private void ClearLatest()
    {
        latestSnapshot = default;
        OnPropertyChanged(nameof(LatestSnapshot));
    }

    private sealed class ToolAttachment : IDisposable
    {
        private static readonly MauiColor StrokeColor = MauiColor.FromArgb("#FF53647D");
        private readonly FrameStatisticsOverlay owner;
        private readonly ViewportToolContext context;
        private readonly FrameStatisticsBehavior behavior = new();
        private readonly FrameStatisticsView statisticsView = new();
        private ViewportOverlay? overlayHost;
        private IDisposable? overlayLease;
        private bool behaviorAttached;
        private bool disposed;

        internal ToolAttachment(FrameStatisticsOverlay owner, ViewportToolContext context)
        {
            this.owner = owner;
            this.context = context;
        }

        internal void Attach()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ApplyCollectionProperties();
            ApplyDisplayProperties();
            behavior.SnapshotUpdated += OnSnapshotUpdated;
            context.View.Behaviors.Add(behavior);
            behaviorAttached = true;
            statisticsView.Source = behavior;
            RefreshOverlay();
        }

        internal void ApplyCollectionProperties()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            behavior.Collector = owner.Collector;
            behavior.IsEnabled = owner.IsEnabled;
            behavior.SnapshotInterval = owner.SnapshotInterval;
            behavior.DrawCallCount = owner.DrawCallCount;
            behavior.PrimitiveCount = owner.PrimitiveCount;
        }

        internal void ReplaceCollector()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            statisticsView.Source = null;
            ApplyCollectionProperties();
            statisticsView.Source = behavior;
        }

        internal void ApplyDisplayProperties()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            statisticsView.IsDetailed = owner.IsDetailed;
            statisticsView.IsGraphVisible = owner.IsGraphVisible;
            statisticsView.TextColor = owner.TextColor;
            statisticsView.GraphColor = owner.GraphColor;
            statisticsView.GraphBackgroundColor = owner.GraphBackgroundColor;
        }

        internal void RefreshOverlay()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RemoveOverlay();
            if (!owner.IsVisible)
            {
                return;
            }

            Border chrome = new()
            {
                Background = new SolidColorBrush(owner.BackgroundColor),
                Content = statisticsView,
                Stroke = new SolidColorBrush(StrokeColor),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7d) },
                StrokeThickness = 1d,
            };
            ViewportOverlay createdHost = new()
            {
                Content = chrome,
                InputMode = ViewportOverlayInputMode.PassThrough,
                Margin = new Thickness(owner.Margin),
                MaximumWidthRequest = owner.MaximumWidth,
                Placement = owner.Placement,
            };
            overlayHost = createdHost;
            overlayLease = createdHost.Attach(context);
        }

        internal void Reset()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            behavior.Reset();
            behavior.PublishSnapshot();
        }

        internal FrameStatisticsSnapshot PublishSnapshot()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return behavior.PublishSnapshot();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            RemoveOverlay();
            statisticsView.Source = null;
            statisticsView.Dispose();
            behavior.SnapshotUpdated -= OnSnapshotUpdated;
            if (behaviorAttached)
            {
                behaviorAttached = false;
                context.View.Behaviors.Remove(behavior);
            }
            behavior.Dispose();
            if (ReferenceEquals(owner.attachment, this))
            {
                owner.attachment = null;
            }
        }

        private void RemoveOverlay()
        {
            overlayLease?.Dispose();
            overlayLease = null;
            if (overlayHost is ViewportOverlay previous)
            {
                if (previous.Content is Border border)
                {
                    border.Content = null;
                }
                previous.Content = null;
                overlayHost = null;
            }
        }

        private void OnSnapshotUpdated(object? sender, FrameStatisticsSnapshotEventArgs e)
        {
            _ = sender;
            owner.Publish(e.Snapshot);
        }
    }
}

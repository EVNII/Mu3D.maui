using System.Globalization;
using Microsoft.Maui.Graphics;
using Mu3D.Toolkit.Diagnostics;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Mu3D.Maui.Toolkit.Diagnostics;

/// <summary>Displays throttled snapshots published by a frame-statistics behavior.</summary>
/// <remarks>
/// The view borrows <see cref="Source"/> and never attaches it to a scene view, resets it, or
/// disposes it. Text and graph history change only when the source publishes a snapshot or display
/// properties change; rendering frames do not directly invalidate MAUI layout.
/// </remarks>
public sealed class FrameStatisticsView : ContentView, IDisposable
{
    private readonly Label headlineLabel;
    private readonly Label detailsLabel;
    private readonly GraphicsView graphView;
    private readonly FrameStatisticsHistory history = new(capacity: 120);
    private readonly FrameStatisticsGraphDrawable graphDrawable;
    private FrameStatisticsBehavior? subscribedSource;
    private bool isLoaded;
    private bool disposed;

    /// <summary>Identifies the <see cref="Source"/> bindable property.</summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source),
        typeof(FrameStatisticsBehavior),
        typeof(FrameStatisticsView),
        default(FrameStatisticsBehavior),
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsView)bindable).OnSourceChanged());

    /// <summary>Identifies the <see cref="IsDetailed"/> bindable property.</summary>
    public static readonly BindableProperty IsDetailedProperty = BindableProperty.Create(
        nameof(IsDetailed),
        typeof(bool),
        typeof(FrameStatisticsView),
        false,
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsView)bindable).RefreshText());

    /// <summary>Identifies the <see cref="TextColor"/> bindable property.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(MauiColor),
        typeof(FrameStatisticsView),
        Colors.White,
        propertyChanged: static (bindable, _, value) =>
            ((FrameStatisticsView)bindable).ApplyTextColor((MauiColor)value));

    /// <summary>Identifies the <see cref="IsGraphVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsGraphVisibleProperty = BindableProperty.Create(
        nameof(IsGraphVisible),
        typeof(bool),
        typeof(FrameStatisticsView),
        true,
        propertyChanged: static (bindable, _, value) =>
            ((FrameStatisticsView)bindable).ApplyGraphVisibility((bool)value));

    /// <summary>Identifies the <see cref="GraphColor"/> bindable property.</summary>
    public static readonly BindableProperty GraphColorProperty = BindableProperty.Create(
        nameof(GraphColor),
        typeof(MauiColor),
        typeof(FrameStatisticsView),
        Colors.Cyan,
        propertyChanged: static (bindable, _, value) =>
            ((FrameStatisticsView)bindable).ApplyGraphColor((MauiColor)value));

    /// <summary>Identifies the <see cref="GraphBackgroundColor"/> bindable property.</summary>
    public static readonly BindableProperty GraphBackgroundColorProperty = BindableProperty.Create(
        nameof(GraphBackgroundColor),
        typeof(MauiColor),
        typeof(FrameStatisticsView),
        new MauiColor(0f, 0.035f, 0.09f, 1f),
        propertyChanged: static (bindable, _, value) =>
            ((FrameStatisticsView)bindable).ApplyGraphBackgroundColor((MauiColor)value));

    /// <summary>Initializes an empty frame-statistics display.</summary>
    public FrameStatisticsView()
    {
        headlineLabel = new Label
        {
            FontAttributes = FontAttributes.Bold,
            FontSize = 16d,
            LineBreakMode = LineBreakMode.NoWrap,
            TextColor = TextColor,
        };
        detailsLabel = new Label
        {
            FontSize = 12d,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = TextColor,
        };
        graphDrawable = new FrameStatisticsGraphDrawable(history)
        {
            GraphColor = GraphColor,
            BackgroundColor = GraphBackgroundColor,
        };
        graphView = new GraphicsView
        {
            Drawable = graphDrawable,
            HeightRequest = 52d,
            InputTransparent = true,
            IsVisible = IsGraphVisible,
        };
        Content = new VerticalStackLayout
        {
            Spacing = 3d,
            Children =
            {
                headlineLabel,
                graphView,
                detailsLabel,
            },
        };
        Padding = 10d;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RefreshText();
    }

    /// <summary>Gets or sets the behavior whose published snapshots are displayed.</summary>
    public FrameStatisticsBehavior? Source
    {
        get => (FrameStatisticsBehavior?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets or sets whether timing breakdowns and resource counts are shown.</summary>
    public bool IsDetailed
    {
        get => (bool)GetValue(IsDetailedProperty);
        set => SetValue(IsDetailedProperty, value);
    }

    /// <summary>Gets or sets the color used by both statistics labels.</summary>
    public MauiColor TextColor
    {
        get => (MauiColor)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    /// <summary>Gets or sets whether the rolling FPS graph is visible.</summary>
    public bool IsGraphVisible
    {
        get => (bool)GetValue(IsGraphVisibleProperty);
        set => SetValue(IsGraphVisibleProperty, value);
    }

    /// <summary>Gets or sets the rolling FPS graph line and bar color.</summary>
    public MauiColor GraphColor
    {
        get => (MauiColor)GetValue(GraphColorProperty);
        set => SetValue(GraphColorProperty, value);
    }

    /// <summary>Gets or sets the rolling FPS graph background color.</summary>
    public MauiColor GraphBackgroundColor
    {
        get => (MauiColor)GetValue(GraphBackgroundColorProperty);
        set => SetValue(GraphBackgroundColorProperty, value);
    }

    /// <summary>Gets the most recently displayed immutable snapshot.</summary>
    public FrameStatisticsSnapshot LatestSnapshot { get; private set; }

    /// <summary>Stops observing the borrowed source. Repeated calls have no effect.</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        DetachSource();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        isLoaded = true;
        RefreshSubscription();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        isLoaded = false;
        DetachSource();
    }

    private void RefreshSubscription()
    {
        DetachSource();
        if (disposed || !isLoaded || Source is null)
        {
            return;
        }
        subscribedSource = Source;
        subscribedSource.SnapshotUpdated += OnSnapshotUpdated;
        ApplySnapshot(subscribedSource.LatestSnapshot, appendHistory: history.Count == 0);
    }

    private void OnSourceChanged()
    {
        DetachSource();
        history.Clear();
        LatestSnapshot = default;
        OnPropertyChanged(nameof(LatestSnapshot));
        graphView?.Invalidate();
        RefreshText();
        RefreshSubscription();
    }

    private void DetachSource()
    {
        if (subscribedSource is not null)
        {
            subscribedSource.SnapshotUpdated -= OnSnapshotUpdated;
            subscribedSource = null;
        }
    }

    private void OnSnapshotUpdated(object? sender, FrameStatisticsSnapshotEventArgs e)
    {
        _ = sender;
        ApplySnapshot(e.Snapshot, appendHistory: true);
    }

    private void ApplySnapshot(FrameStatisticsSnapshot snapshot, bool appendHistory)
    {
        if (disposed)
        {
            return;
        }
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(() => ApplySnapshotCore(snapshot, appendHistory));
            return;
        }
        ApplySnapshotCore(snapshot, appendHistory);
    }

    private void ApplySnapshotCore(FrameStatisticsSnapshot snapshot, bool appendHistory)
    {
        if (disposed)
        {
            return;
        }
        LatestSnapshot = snapshot;
        if (snapshot.SampleCount == 0)
        {
            history.Clear();
        }
        else if (appendHistory)
        {
            history.Add(snapshot.FramesPerSecond);
        }
        OnPropertyChanged(nameof(LatestSnapshot));
        graphView.Invalidate();
        RefreshText();
    }

    private void RefreshText()
    {
        if (headlineLabel is null || detailsLabel is null)
        {
            return;
        }
        FrameStatisticsText text = FrameStatisticsTextFormatter.Format(LatestSnapshot, IsDetailed);
        headlineLabel.Text = history.Count == 0
            ? text.Headline
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{LatestSnapshot.FramesPerSecond:0} FPS " +
                $"({history.Minimum:0}–{history.Maximum:0})  ·  " +
                $"{LatestSnapshot.AverageFrameMilliseconds:0.00} ms");
        detailsLabel.Text = text.Details;
    }

    private void ApplyTextColor(MauiColor color)
    {
        if (headlineLabel is not null)
        {
            headlineLabel.TextColor = color;
        }
        if (detailsLabel is not null)
        {
            detailsLabel.TextColor = color;
        }
    }

    private void ApplyGraphVisibility(bool isVisible)
    {
        if (graphView is not null)
        {
            graphView.IsVisible = isVisible;
        }
    }

    private void ApplyGraphColor(MauiColor color)
    {
        if (graphDrawable is not null)
        {
            graphDrawable.GraphColor = color;
            graphView?.Invalidate();
        }
    }

    private void ApplyGraphBackgroundColor(MauiColor color)
    {
        if (graphDrawable is not null)
        {
            graphDrawable.BackgroundColor = color;
            graphView?.Invalidate();
        }
    }
}

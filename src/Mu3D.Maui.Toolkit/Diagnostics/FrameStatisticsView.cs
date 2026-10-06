using Microsoft.Maui.Graphics;
using Mu3D.Toolkit.Diagnostics;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Mu3D.Maui.Toolkit.Diagnostics;

/// <summary>Displays throttled snapshots published by a frame-statistics behavior.</summary>
/// <remarks>
/// The view borrows <see cref="Source"/> and never attaches it to a scene view, resets it, or
/// disposes it. Text and graph history change only when the source publishes a snapshot or display
/// properties change; rendering frames do not directly invalidate MAUI layout.
/// Tapping or clicking cycles Compact, Normal and Detail. Bind <see cref="DisplayMode"/> two-way
/// when application state must track this interaction.
/// </remarks>
public sealed class FrameStatisticsView : ContentView, IDisposable
{
    private readonly Label headlineLabel;
    private readonly Label detailsLabel;
    private readonly GraphicsView graphView;
    private readonly FrameStatisticsHistory history = new(capacity: 120);
    private readonly FrameStatisticsGraphDrawable graphDrawable;
    private readonly VerticalStackLayout contentLayout;
    private readonly TapGestureRecognizer modeTap = new();
    private View? modeTapTarget;
    private FrameStatisticsBehavior? subscribedSource;
    private bool synchronizingDetailed;
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

    /// <summary>Identifies the <see cref="DisplayMode"/> bindable property.</summary>
    public static readonly BindableProperty DisplayModeProperty = BindableProperty.Create(
        nameof(DisplayMode),
        typeof(FrameStatisticsDisplayMode),
        typeof(FrameStatisticsView),
        FrameStatisticsDisplayMode.Normal,
        BindingMode.TwoWay,
        validateValue: static (_, value) =>
            value is FrameStatisticsDisplayMode mode && Enum.IsDefined(mode),
        propertyChanged: static (bindable, _, _) =>
            ((FrameStatisticsView)bindable).OnDisplayModeChanged());

    /// <summary>Identifies the <see cref="IsDetailed"/> bindable property.</summary>
    public static readonly BindableProperty IsDetailedProperty = BindableProperty.Create(
        nameof(IsDetailed),
        typeof(bool),
        typeof(FrameStatisticsView),
        false,
        BindingMode.TwoWay,
        propertyChanged: static (bindable, _, value) =>
            ((FrameStatisticsView)bindable).OnIsDetailedChanged((bool)value));

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
            InputTransparent = true,
        };
        detailsLabel = new Label
        {
            FontSize = 12d,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = TextColor,
            InputTransparent = true,
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
        contentLayout = new VerticalStackLayout
        {
            Spacing = 3d,
            Children =
            {
                headlineLabel,
                graphView,
                detailsLabel,
            },
        };
        Content = contentLayout;
        Padding = 10d;
        modeTap.Tapped += OnModeTapped;
        SetModeTapTarget(this);
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

    /// <summary>Gets or sets the amount of information shown. The default is Normal.</summary>
    /// <remarks>
    /// Compact shows only FPS and hides the graph without changing <see cref="IsGraphVisible"/>.
    /// Tapping cycles Compact to Normal to Detail to Compact. The default binding mode is two-way.
    /// Prefer this property to the legacy <see cref="IsDetailed"/> alias when binding display state.
    /// </remarks>
    public FrameStatisticsDisplayMode DisplayMode
    {
        get => (FrameStatisticsDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>Gets or sets the legacy Normal/Detail display choice.</summary>
    /// <remarks>
    /// Assigning this CLR property selects Detail for true and Normal for false, even when the
    /// boolean is unchanged. Compact reports false. An unchanged bindable-property write does not
    /// change modes; use <see cref="DisplayMode"/> for explicit mode selection.
    /// Use <see cref="DisplayMode"/>
    /// to distinguish all three modes, and avoid binding both properties to independent state.
    /// The default binding mode is now two-way so clicks can update a legacy bound selector.
    /// </remarks>
    public bool IsDetailed
    {
        get => (bool)GetValue(IsDetailedProperty);
        set
        {
            SetValue(IsDetailedProperty, value);
            if (!synchronizingDetailed)
            {
                DisplayMode = value ? FrameStatisticsDisplayMode.Detail : FrameStatisticsDisplayMode.Normal;
            }
        }
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
        modeTap.Tapped -= OnModeTapped;
        SetModeTapTarget(null);
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
        FrameStatisticsText text = FrameStatisticsTextFormatter.Format(
            LatestSnapshot,
            DisplayMode,
            history.Count == 0 ? null : history.Minimum,
            history.Count == 0 ? null : history.Maximum);
        headlineLabel.Text = text.Headline;
        detailsLabel.Text = text.Details;
        detailsLabel.IsVisible = DisplayMode != FrameStatisticsDisplayMode.Compact;
    }

    private void OnDisplayModeChanged()
    {
        synchronizingDetailed = true;
        try
        {
            SetValue(IsDetailedProperty, DisplayMode == FrameStatisticsDisplayMode.Detail);
        }
        finally
        {
            synchronizingDetailed = false;
        }
        if (contentLayout is not null)
        {
            bool compact = DisplayMode == FrameStatisticsDisplayMode.Compact;
            contentLayout.Spacing = compact ? 0d : 3d;
            Padding = compact ? new Thickness(6d, 4d) : new Thickness(10d);
            headlineLabel.FontSize = compact ? 12d : 16d;
        }
        ApplyGraphVisibility(IsGraphVisible);
        RefreshText();
    }

    private void OnIsDetailedChanged(bool isDetailed)
    {
        if (!synchronizingDetailed)
        {
            DisplayMode = isDetailed ? FrameStatisticsDisplayMode.Detail : FrameStatisticsDisplayMode.Normal;
        }
    }

    private void OnModeTapped(object? sender, TappedEventArgs e)
    {
        _ = e;
        if (!disposed && IsEnabled && ReferenceEquals(sender, modeTapTarget))
        {
            DisplayMode = FrameStatisticsDisplayState.Next(DisplayMode);
        }
    }

    internal void SetModeTapTarget(View? target)
    {
        if (ReferenceEquals(modeTapTarget, target))
        {
            return;
        }
        modeTapTarget?.GestureRecognizers.Remove(modeTap);
        modeTapTarget = target;
        target?.GestureRecognizers.Add(modeTap);
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
            graphView.IsVisible = FrameStatisticsDisplayState.ShowsGraph(DisplayMode, isVisible);
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

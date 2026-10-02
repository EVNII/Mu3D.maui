using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Mu3D.Maui.Toolkit.Overlays;
using Mu3D.Toolkit.Animation;
using Mu3D.Toolkit.Helpers;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>
/// Displays a compact MAUI transport bar for one UI-independent <see cref="IPlayable"/>.
/// </summary>
/// <remarks>
/// The toolbar is hosted by the viewport's shared <see cref="ViewportOverlay"/> manager. It
/// observes and operates the assigned playable but owns no clock, animation evaluation, glTF
/// object, scene resource or navigation lifetime. Applications may therefore use the same control
/// with clip players, camera paths, MAUI animation bridges or custom timelines.
/// </remarks>
public sealed class PlaybackToolbar : BindableObject, IViewportTool
{
    private ToolAttachment? attachment;

    /// <summary>Identifies the <see cref="Playable"/> bindable property.</summary>
    public static readonly BindableProperty PlayableProperty = BindableProperty.Create(
        nameof(Playable),
        typeof(IPlayable),
        typeof(PlaybackToolbar),
        default(IPlayable),
        propertyChanged: static (bindable, _, _) =>
            ((PlaybackToolbar)bindable).attachment?.ReplacePlayable());

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(PlaybackToolbar),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((PlaybackToolbar)bindable).attachment?.RefreshState());

    /// <summary>Identifies the <see cref="IsVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible),
        typeof(bool),
        typeof(PlaybackToolbar),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((PlaybackToolbar)bindable).attachment?.RefreshOverlay());

    /// <summary>Identifies the <see cref="IsInteractive"/> bindable property.</summary>
    public static readonly BindableProperty IsInteractiveProperty = BindableProperty.Create(
        nameof(IsInteractive),
        typeof(bool),
        typeof(PlaybackToolbar),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((PlaybackToolbar)bindable).attachment?.RefreshState());

    /// <summary>Identifies the <see cref="ShowLoop"/> bindable property.</summary>
    public static readonly BindableProperty ShowLoopProperty = BindableProperty.Create(
        nameof(ShowLoop),
        typeof(bool),
        typeof(PlaybackToolbar),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((PlaybackToolbar)bindable).attachment?.RefreshOverlay());

    /// <summary>Identifies the <see cref="ShowTime"/> bindable property.</summary>
    public static readonly BindableProperty ShowTimeProperty = BindableProperty.Create(
        nameof(ShowTime),
        typeof(bool),
        typeof(PlaybackToolbar),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((PlaybackToolbar)bindable).attachment?.RefreshOverlay());

    /// <summary>Identifies the <see cref="Placement"/> bindable property.</summary>
    public static readonly BindableProperty PlacementProperty = BindableProperty.Create(
        nameof(Placement),
        typeof(ViewportOverlayPlacement),
        typeof(PlaybackToolbar),
        ViewportOverlayPlacement.BottomCenter,
        validateValue: static (_, value) =>
            value is ViewportOverlayPlacement placement && Enum.IsDefined(placement),
        propertyChanged: static (bindable, _, _) =>
            ((PlaybackToolbar)bindable).attachment?.RefreshOverlay());

    /// <summary>Identifies the <see cref="Margin"/> bindable property.</summary>
    public static readonly BindableProperty MarginProperty = BindableProperty.Create(
        nameof(Margin),
        typeof(double),
        typeof(PlaybackToolbar),
        12d,
        validateValue: static (_, value) =>
            value is double number && double.IsFinite(number) && number >= 0d,
        propertyChanged: static (bindable, _, _) =>
            ((PlaybackToolbar)bindable).attachment?.RefreshOverlay());

    /// <summary>Identifies the <see cref="MaximumWidth"/> bindable property.</summary>
    public static readonly BindableProperty MaximumWidthProperty = BindableProperty.Create(
        nameof(MaximumWidth),
        typeof(double),
        typeof(PlaybackToolbar),
        720d,
        validateValue: static (_, value) =>
            value is double number && double.IsFinite(number) && number > 0d,
        propertyChanged: static (bindable, _, _) =>
            ((PlaybackToolbar)bindable).attachment?.RefreshOverlay());

    /// <summary>Gets or sets the application-owned playback source shown by the toolbar.</summary>
    public IPlayable? Playable
    {
        get => (IPlayable?)GetValue(PlayableProperty);
        set => SetValue(PlayableProperty, value);
    }

    /// <summary>Gets or sets whether the toolbar can operate its assigned playable.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets or sets whether the attachment-owned toolbar is shown.</summary>
    public bool IsVisible
    {
        get => (bool)GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>Gets or sets whether the toolbar accepts pointer and touch input.</summary>
    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <summary>Gets or sets whether the loop toggle is included.</summary>
    public bool ShowLoop
    {
        get => (bool)GetValue(ShowLoopProperty);
        set => SetValue(ShowLoopProperty, value);
    }

    /// <summary>Gets or sets whether current and total time are shown.</summary>
    public bool ShowTime
    {
        get => (bool)GetValue(ShowTimeProperty);
        set => SetValue(ShowTimeProperty, value);
    }

    /// <summary>Gets or sets the viewport alignment containing the toolbar.</summary>
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

    /// <summary>Gets or sets the positive maximum toolbar width in device-independent units.</summary>
    /// <remarks>Transport content scrolls horizontally when it exceeds the available width.</remarks>
    public double MaximumWidth
    {
        get => (double)GetValue(MaximumWidthProperty);
        set => SetValue(MaximumWidthProperty, value);
    }

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("A PlaybackToolbar can attach only once at a time.");
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

    private sealed class ToolAttachment : IDisposable
    {
        private const double ControlHeight = 36d;
        private const double ToolbarPadding = 5d;
        private const double ToolbarHeight = ControlHeight + (ToolbarPadding * 2d) + 2d;
        private static readonly MauiColor ToolbarBackground = MauiColor.FromArgb("#E6111827");
        private static readonly MauiColor ToolbarStroke = MauiColor.FromArgb("#FF53647D");
        private static readonly MauiColor ActiveBackground = MauiColor.FromArgb("#FF2563EB");
        private static readonly MauiColor IdleBackground = MauiColor.FromArgb("#00111827");
        private readonly PlaybackToolbar owner;
        private readonly ViewportToolContext context;
        private ViewportOverlay? overlayHost;
        private IDisposable? overlayLease;
        private IPlayable? subscribedPlayable;
        private Button? restartButton;
        private Button? playPauseButton;
        private Button? loopButton;
        private Slider? timeline;
        private Label? timeLabel;
        private bool updatingTimeline;
        private bool disposed;

        internal ToolAttachment(PlaybackToolbar owner, ViewportToolContext context)
        {
            this.owner = owner;
            this.context = context;
        }

        internal void Attach()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Subscribe(owner.Playable);
            RefreshOverlay();
        }

        internal void ReplacePlayable()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Subscribe(owner.Playable);
            RefreshState();
        }

        internal void RefreshOverlay()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RemoveOverlay();
            if (!owner.IsVisible)
            {
                return;
            }

            HorizontalStackLayout row = new()
            {
                Spacing = 4d,
                VerticalOptions = LayoutOptions.Center,
            };
            restartButton = CreateButton("Start", "Seek to the beginning");
            restartButton.Clicked += OnRestartClicked;
            row.Children.Add(restartButton);

            playPauseButton = CreateButton("Play", "Start or pause playback");
            playPauseButton.Clicked += OnPlayPauseClicked;
            row.Children.Add(playPauseButton);

            timeline = new Slider
            {
                AutomationId = "Mu3D.Playback.Position",
                HeightRequest = ControlHeight,
                Maximum = 1d,
                Minimum = 0d,
                MinimumWidthRequest = 190d,
                WidthRequest = 260d,
            };
            SemanticProperties.SetDescription(timeline, "Playback position");
            timeline.ValueChanged += OnTimelineValueChanged;
            row.Children.Add(timeline);

            if (owner.ShowTime)
            {
                timeLabel = new Label
                {
                    AutomationId = "Mu3D.Playback.Time",
                    FontSize = 11d,
                    MinimumWidthRequest = 82d,
                    TextColor = Colors.White,
                    VerticalTextAlignment = TextAlignment.Center,
                };
                row.Children.Add(timeLabel);
            }

            if (owner.ShowLoop)
            {
                loopButton = CreateButton("Loop", "Toggle repeat playback");
                loopButton.Clicked += OnLoopClicked;
                row.Children.Add(loopButton);
            }

            ScrollView scroller = new()
            {
                Content = row,
                HeightRequest = ControlHeight,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                Orientation = ScrollOrientation.Horizontal,
                VerticalScrollBarVisibility = ScrollBarVisibility.Never,
                VerticalOptions = LayoutOptions.Start,
            };
            Border chrome = new()
            {
                Background = new SolidColorBrush(ToolbarBackground),
                Content = scroller,
                HeightRequest = ToolbarHeight,
                MaximumHeightRequest = ToolbarHeight,
                MinimumHeightRequest = ToolbarHeight,
                Padding = new Thickness(ToolbarPadding),
                Stroke = new SolidColorBrush(ToolbarStroke),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7d) },
                StrokeThickness = 1d,
            };
            ViewportOverlay createdHost = new()
            {
                Content = chrome,
                InputMode = ViewportOverlayInputMode.Interactive,
                Margin = new Thickness(owner.Margin),
                MaximumWidthRequest = owner.MaximumWidth,
                Placement = owner.Placement,
            };
            overlayHost = createdHost;
            overlayLease = createdHost.Attach(context);
            RefreshState();
        }

        internal void RefreshState()
        {
            if (disposed)
            {
                return;
            }
            if (context.View.Dispatcher.IsDispatchRequired)
            {
                context.View.Dispatcher.Dispatch(RefreshState);
                return;
            }

            PlaybackToolbarSnapshot snapshot = PlaybackToolbarState.Read(owner.Playable);
            bool interactive = owner.IsEnabled && owner.IsInteractive && owner.Playable is not null;
            if (overlayHost is not null)
            {
                overlayHost.InputMode = interactive
                    ? ViewportOverlayInputMode.Interactive
                    : ViewportOverlayInputMode.PassThrough;
            }
            if (restartButton is not null)
            {
                restartButton.IsEnabled = interactive && snapshot.CanSeek;
                restartButton.Opacity = restartButton.IsEnabled ? 1d : 0.55d;
            }
            if (playPauseButton is not null)
            {
                playPauseButton.IsEnabled = interactive && snapshot.CanPlayPause;
                playPauseButton.Text = snapshot.IsBuffering
                    ? "Wait"
                    : snapshot.IsPlaying ? "Pause" : "Play";
                playPauseButton.BackgroundColor = snapshot.IsPlaying
                    ? ActiveBackground
                    : IdleBackground;
                playPauseButton.Opacity = playPauseButton.IsEnabled ? 1d : 0.55d;
            }
            if (timeline is not null)
            {
                timeline.IsEnabled = interactive && snapshot.CanSeek;
                updatingTimeline = true;
                timeline.Value = snapshot.Progress;
                updatingTimeline = false;
                timeline.Opacity = timeline.IsEnabled ? 1d : 0.55d;
            }
            if (timeLabel is not null)
            {
                timeLabel.Text = snapshot.TimeText;
            }
            if (loopButton is not null)
            {
                loopButton.IsEnabled = interactive;
                loopButton.BackgroundColor = snapshot.IsLooping
                    ? ActiveBackground
                    : IdleBackground;
                loopButton.FontAttributes = snapshot.IsLooping
                    ? FontAttributes.Bold
                    : FontAttributes.None;
                loopButton.Opacity = loopButton.IsEnabled ? 1d : 0.55d;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Subscribe(null);
            RemoveOverlay();
            if (ReferenceEquals(owner.attachment, this))
            {
                owner.attachment = null;
            }
        }

        private static Button CreateButton(string text, string description)
        {
            Button button = new()
            {
                CornerRadius = 4,
                FontSize = 11d,
                HeightRequest = ControlHeight,
                MinimumHeightRequest = ControlHeight,
                Padding = new Thickness(10d, 2d),
                Text = text,
                TextColor = Colors.White,
            };
            SemanticProperties.SetDescription(button, description);
            ToolTipProperties.SetText(button, description);
            return button;
        }

        private void Subscribe(IPlayable? playable)
        {
            if (ReferenceEquals(subscribedPlayable, playable))
            {
                return;
            }
            if (subscribedPlayable is not null)
            {
                subscribedPlayable.PlaybackChanged -= OnPlaybackChanged;
            }
            subscribedPlayable = playable;
            if (playable is not null)
            {
                playable.PlaybackChanged += OnPlaybackChanged;
            }
        }

        private void RemoveOverlay()
        {
            if (restartButton is not null)
            {
                restartButton.Clicked -= OnRestartClicked;
                restartButton = null;
            }
            if (playPauseButton is not null)
            {
                playPauseButton.Clicked -= OnPlayPauseClicked;
                playPauseButton = null;
            }
            if (loopButton is not null)
            {
                loopButton.Clicked -= OnLoopClicked;
                loopButton = null;
            }
            if (timeline is not null)
            {
                timeline.ValueChanged -= OnTimelineValueChanged;
                timeline = null;
            }
            timeLabel = null;
            overlayLease?.Dispose();
            overlayLease = null;
            if (overlayHost is ViewportOverlay previous)
            {
                previous.Content = null;
                overlayHost = null;
            }
        }

        private void OnPlaybackChanged(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            RefreshState();
        }

        private void OnRestartClicked(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            IPlayable? playable = owner.Playable;
            if (owner.IsEnabled && owner.IsInteractive && playable?.CanSeek == true)
            {
                playable.Seek(TimeSpan.Zero);
            }
        }

        private void OnPlayPauseClicked(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            IPlayable? playable = owner.Playable;
            if (!owner.IsEnabled || !owner.IsInteractive || playable is null)
            {
                return;
            }
            if (playable.State == PlaybackState.Playing)
            {
                playable.Pause();
            }
            else if (playable.State != PlaybackState.Buffering)
            {
                playable.Play();
            }
        }

        private void OnTimelineValueChanged(object? sender, ValueChangedEventArgs e)
        {
            _ = sender;
            if (updatingTimeline || !owner.IsEnabled || !owner.IsInteractive)
            {
                return;
            }
            IPlayable? playable = owner.Playable;
            TimeSpan duration = playable?.Duration ?? TimeSpan.Zero;
            if (playable?.CanSeek == true && duration > TimeSpan.Zero)
            {
                playable.Seek(PlaybackToolbarState.PositionFromProgress(duration, e.NewValue));
            }
        }

        private void OnLoopClicked(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            IPlayable? playable = owner.Playable;
            if (owner.IsEnabled && owner.IsInteractive && playable is not null)
            {
                playable.IsLooping = !playable.IsLooping;
                RefreshState();
            }
        }
    }
}

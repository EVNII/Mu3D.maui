using Microsoft.Maui.Animations;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Toolkit.Animation;
using MauiAnimation = Microsoft.Maui.Animations.Animation;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates a viewport playback bar operating an application-owned playable.</summary>
public partial class PlaybackToolbarPage : ContentPage
{
    private DemoPlayable? player;

    /// <summary>Initializes the playback-toolbar example.</summary>
    public PlaybackToolbarPage()
    {
        InitializeComponent();
        player = new DemoPlayable(SceneView, SceneProgress);
        OnPropertyChanged(nameof(Player));
    }

    /// <summary>Gets the application-owned source consumed by the XAML playback toolbar.</summary>
    public IPlayable? Player => player;

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        SceneView.InvalidateScene();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        player?.Pause();
        base.OnDisappearing();
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        player?.Pause();
        StatusLabel.Text = $"Playback toolbar {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        player?.Pause();
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }

    private sealed class DemoPlayable : IPlayable, IDisposable
    {
        private static readonly TimeSpan PlaybackDuration = TimeSpan.FromSeconds(6d);
        private readonly Mu3DSceneView view;
        private readonly ProgressTool progressTool;
        private IAnimationManager? animationManager;
        private MauiAnimation? animation;
        private TimeSpan position;
        private PlaybackState state = PlaybackState.Stopped;
        private bool isLooping;
        private bool disposed;

        internal DemoPlayable(Mu3DSceneView view, ProgressTool progressTool)
        {
            this.view = view;
            this.progressTool = progressTool;
        }

        public event EventHandler? PlaybackChanged;

        public PlaybackState State => state;

        public TimeSpan Position => position;

        public TimeSpan Duration => PlaybackDuration;

        public bool CanSeek => !disposed;

        public bool IsLooping
        {
            get => isLooping;
            set
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (isLooping == value)
                {
                    return;
                }
                isLooping = value;
                PlaybackChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Play()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (state == PlaybackState.Playing)
            {
                return;
            }
            if (position >= PlaybackDuration)
            {
                SetPosition(TimeSpan.Zero);
            }
            StartAnimation();
        }

        public void Pause()
        {
            if (disposed || state != PlaybackState.Playing)
            {
                return;
            }
            RemoveAnimation();
            SetState(PlaybackState.Paused);
        }

        public void Stop()
        {
            if (disposed)
            {
                return;
            }
            RemoveAnimation();
            SetPosition(TimeSpan.Zero);
            SetState(PlaybackState.Stopped);
        }

        public void Seek(TimeSpan requestedPosition)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            bool resume = state == PlaybackState.Playing;
            RemoveAnimation();
            SetPosition(requestedPosition);
            if (resume)
            {
                StartAnimation();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            RemoveAnimation();
            disposed = true;
            state = PlaybackState.Stopped;
        }

        private void StartAnimation()
        {
            RemoveAnimation();
            if (view.Handler?.MauiContext?.Services.GetService(typeof(IAnimationManager))
                is not IAnimationManager manager)
            {
                SetState(PlaybackState.Paused);
                return;
            }

            TimeSpan initialPosition = position;
            double remainingSeconds = Math.Max(
                0.001d,
                (PlaybackDuration - initialPosition).TotalSeconds);
            MauiAnimation created = null!;
            created = new MauiAnimation(
                normalizedTime =>
                {
                    if (ReferenceEquals(animation, created))
                    {
                        SetPosition(initialPosition + TimeSpan.FromSeconds(
                            remainingSeconds * normalizedTime));
                    }
                },
                start: 0d,
                duration: remainingSeconds,
                easing: Easing.Linear,
                finished: () => OnAnimationFinished(created))
            {
                Name = "Mu3D Gallery IPlayable demo",
            };
            animationManager = manager;
            animation = created;
            SetState(PlaybackState.Playing);
            created.Commit(manager);
        }

        private void OnAnimationFinished(MauiAnimation completed)
        {
            if (!ReferenceEquals(animation, completed))
            {
                return;
            }

            animation = null;
            animationManager = null;
            completed.Dispose();
            if (isLooping && !disposed)
            {
                SetPosition(TimeSpan.Zero);
                StartAnimation();
            }
            else
            {
                SetPosition(PlaybackDuration);
                SetState(PlaybackState.Paused);
            }
        }

        private void SetPosition(TimeSpan value)
        {
            position = value < TimeSpan.Zero
                ? TimeSpan.Zero
                : value > PlaybackDuration ? PlaybackDuration : value;
            progressTool.Progress = position.TotalSeconds / PlaybackDuration.TotalSeconds;
            PlaybackChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetState(PlaybackState value)
        {
            if (state == value)
            {
                return;
            }
            state = value;
            PlaybackChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveAnimation()
        {
            MauiAnimation? current = animation;
            IAnimationManager? manager = animationManager;
            animation = null;
            animationManager = null;
            if (current is not null)
            {
                manager?.Remove(current);
                current.Dispose();
            }
        }
    }
}

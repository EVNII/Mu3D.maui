using System.Diagnostics;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Exercises backend-independent FP32 skinning and transform animation.</summary>
public partial class AnimationLabPage : ContentPage
{
    private IPresentationSurfaceSession? session;
    private AnimationLabPresenter? presenter;
    private CancellationTokenSource lifetime = new();
    private Task? configurationTask;
    private float manualTime;

    /// <summary>Initializes the animation laboratory.</summary>
    public AnimationLabPage()
    {
        InitializeComponent();
        SurfaceView.PresentationSessionChanged += OnPresentationSessionChanged;
        SurfaceView.SurfaceError += OnSurfaceError;
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed";
        DetailsLabel.Text = e.Exception.ToString();
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        _ = sender;
        ResetLifetime();
        DisposeRendering();
        session = e.Session;
        if (session is null)
        {
            StatusLabel.Text = "Presentation surface unavailable";
            return;
        }
        StartConfiguration(session);
    }

    private async Task ConfigureAndRunAsync(
        IPresentationSurfaceSession activeSession,
        CancellationToken cancellationToken)
    {
        try
        {
            StatusLabel.Text = "Configuring HDR animation surface…";
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(SurfaceView.PresentationSession, activeSession))
            {
                return;
            }
            presenter = new AnimationLabPresenter(activeSession);
            presenter.Blend = (float)BlendSlider.Value;
            presenter.MorphWeight = (float)MorphSlider.Value;
            TimeSlider.Maximum = presenter.Duration;
            await RunFramesAsync(activeSession, presenter, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusLabel.Text = "Animation Lab failed";
            DetailsLabel.Text = exception.ToString();
            DisposeRendering();
        }
    }

    private async Task RunFramesAsync(
        IPresentationSurfaceSession activeSession,
        AnimationLabPresenter activePresenter,
        CancellationToken cancellationToken)
    {
        Stopwatch clock = Stopwatch.StartNew();
        double previous = clock.Elapsed.TotalSeconds;
        float playbackTime = manualTime;
        int frames = 0;
        while (!cancellationToken.IsCancellationRequested &&
            ReferenceEquals(session, activeSession) &&
            ReferenceEquals(presenter, activePresenter))
        {
            double now = clock.Elapsed.TotalSeconds;
            if (PlaySwitch.IsToggled)
            {
                playbackTime += (float)(now - previous) * (float)SpeedSlider.Value;
                manualTime = playbackTime % activePresenter.Duration;
                playbackTime = manualTime;
                TimeSlider.Value = manualTime;
            }
            else
            {
                playbackTime = manualTime;
            }
            previous = now;
            PresentationSurfaceFrameStatus status = activePresenter.Present(playbackTime);
            if (status is PresentationSurfaceFrameStatus.Outdated or
                PresentationSurfaceFrameStatus.Lost)
            {
                activeSession.Resize(activeSession.Width, activeSession.Height);
            }
            else if (status == PresentationSurfaceFrameStatus.Error)
            {
                throw new InvalidOperationException("wgpu reported an Animation Lab frame error.");
            }
            frames++;
            if (frames == 1 || frames % 60 == 0)
            {
                StatusLabel.Text = $"Animation Lab running — {status}";
                DetailsLabel.Text =
                    "Skin: 2 joints, 4 FP32 weights per vertex\n" +
                    "Palette: FP32 position + inverse-transpose normal matrices\n" +
                    "Paths: visible + shadow + AO depth\n" +
                    "Animation: normalized override blend, quaternion hemisphere alignment\n" +
                    "Morph: FP32 position deltas before skinning\n" +
                    "Output: Rgba16Float preferred; no tone mapping";
            }
            // The loop observes cancellation at the next frame boundary. Avoid throwing for the
            // ordinary page-disappearance path while keeping shutdown latency below one frame.
            await Task.Delay(16);
        }
    }

    private void OnTimeChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        manualTime = (float)e.NewValue;
        TimeLabel.Text = $"Time: {e.NewValue:0.00} s";
    }

    private void OnSpeedChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        SpeedLabel.Text = $"Speed: {e.NewValue:0.00}x";
    }

    private void OnBlendChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        BlendLabel.Text = $"Clip B blend: {e.NewValue:0.00}";
        if (presenter is not null)
        {
            presenter.Blend = (float)e.NewValue;
        }
    }

    private void OnMorphChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        MorphLabel.Text = $"Morph weight: {e.NewValue:0.00}";
        if (presenter is not null)
        {
            presenter.MorphWeight = (float)e.NewValue;
        }
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _ = sender;
        manualTime = 0f;
        TimeSlider.Value = 0;
        SpeedSlider.Value = 1;
        BlendSlider.Value = 0;
        MorphSlider.Value = 0;
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        lifetime.Cancel();
        base.OnDisappearing();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (lifetime.IsCancellationRequested)
        {
            ResetLifetime();
        }
        IPresentationSurfaceSession? current = SurfaceView.PresentationSession;
        if (session is null && current is not null &&
            (configurationTask is null || configurationTask.IsCompleted))
        {
            session = current;
            StartConfiguration(current);
        }
        else if (current is not null &&
            ReferenceEquals(session, current) &&
            presenter is AnimationLabPresenter activePresenter &&
            (configurationTask is null || configurationTask.IsCompleted))
        {
            configurationTask = RunFramesAsync(current, activePresenter, lifetime.Token);
        }
    }

    private void StartConfiguration(IPresentationSurfaceSession activeSession) =>
        configurationTask = ConfigureAndRunAsync(activeSession, lifetime.Token);

    private void ResetLifetime()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new CancellationTokenSource();
        configurationTask = null;
    }

    private void DisposeRendering()
    {
        presenter?.Dispose();
        presenter = null;
        session = null;
    }
}

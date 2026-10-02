using Microsoft.Maui.Animations;
using Mu3D.Maui.Controls;
using MauiAnimation = Microsoft.Maui.Animations.Animation;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates explicit MAUI animation and scroll progress driving a Mu3D viewport.</summary>
public partial class ProgressBridgePage : ContentPage
{
    private IAnimationManager? animationManager;
    private MauiAnimation? animation;
    private bool synchronizingSources;

    /// <summary>Initializes the progress-bridge example.</summary>
    public ProgressBridgePage() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        SceneView.InvalidateScene();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        StopAnimation();
        base.OnDisappearing();
    }

    private void OnSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        if (!synchronizingSources)
        {
            SetProgress(e.NewValue, updateSlider: false);
        }
    }

    private void OnProgressScrolled(object? sender, ScrolledEventArgs e)
    {
        ScrollView scroller = (ScrollView)sender!;
        double range = Math.Max(1d, ScrollTrack.Width - scroller.Width);
        SetProgress(e.ScrollX / range, updateSlider: true);
    }

    private void OnAnimateClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        StopAnimation();
        if (SceneView.Handler?.MauiContext?.Services.GetService(typeof(IAnimationManager))
            is not IAnimationManager manager)
        {
            StatusLabel.Text = "MAUI VSync animation service unavailable";
            return;
        }

        double initialProgress = SceneProgress.Progress;
        double targetProgress = initialProgress < 0.5d ? 1d : 0d;
        MauiAnimation created = new(
            normalizedTime => SetProgress(
                initialProgress + ((targetProgress - initialProgress) * normalizedTime),
                updateSlider: true),
            start: 0d,
            duration: 1.4d,
            easing: Easing.CubicInOut,
            finished: () =>
            {
                animation?.Dispose();
                animation = null;
                animationManager = null;
                StatusLabel.Text = "MAUI animation completed; Mu3D requested only mapped frames.";
            })
        {
            Name = "Mu3D Gallery progress bridge",
        };
        animationManager = manager;
        animation = created;
        StatusLabel.Text = "MAUI VSync animation is driving ProgressTool.";
        created.Commit(manager);
    }

    private void SetProgress(double value, bool updateSlider)
    {
        value = Math.Clamp(value, 0d, 1d);
        SceneProgress.Progress = value;
        ProgressLabel.Text = $"Progress {value:0.000}";
        if (updateSlider)
        {
            synchronizingSources = true;
            ProgressSlider.Value = value;
            synchronizingSources = false;
        }
    }

    private void StopAnimation()
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

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StopAnimation();
        StatusLabel.Text = $"Progress tool {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StopAnimation();
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

using System.Diagnostics;
using System.Numerics;
using Microsoft.Maui.Animations;
using MauiAnimation = Microsoft.Maui.Animations.Animation;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Diagnostics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Diagnostics;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates throttled frame diagnostics over a high-level scene view.</summary>
public partial class FrameStatisticsPage : ContentPage
{
    private readonly Scene scene = new("Frame statistics example");
    private readonly SceneNode animatedNode = new("Animated sample");
    private readonly PerspectiveCamera camera = new(name: "Statistics camera");
    private readonly Stopwatch animationClock = new();
    private readonly MeshGeometry geometry;
    private IAnimationManager? animationManager;
    private MauiAnimation? continuousAnimation;
    private bool isPageVisible;
    private bool resourceCountsRefreshed;

    /// <summary>Initializes the focused frame-statistics example.</summary>
    public FrameStatisticsPage()
    {
        InitializeComponent();

        SceneView.ClearColor = new LinearRgba(
            0.012f,
            0.018f,
            0.035f,
            1f,
            StandardColorSpaces.LinearSrgb);
        geometry = Examples.ToolkitSceneExamples.AddStatisticsVisual(animatedNode);
        scene.Add(animatedNode);
        camera.Transform.Position = new Vector3(0f, 0f, 5f);

        StatisticsOverlay.SnapshotInterval = TimeSpan.FromMilliseconds(IntervalStepper.Value);
        StatisticsOverlay.DrawCallCount = 1;
        StatisticsOverlay.PrimitiveCount = geometry.Indices.Count / 3;
        SceneView.FramePresented += OnFramePresented;
        ContinuousSwitch.Toggled += OnContinuousChanged;
        DisplayModePicker.ItemsSource = new[]
        {
            FrameStatisticsDisplayMode.Compact,
            FrameStatisticsDisplayMode.Normal,
            FrameStatisticsDisplayMode.Detail,
        };
        DisplayModePicker.SetBinding(Picker.SelectedItemProperty,
            static (FrameStatisticsOverlay overlay) => overlay.DisplayMode,
            mode: BindingMode.TwoWay, source: StatisticsOverlay);
        IntervalStepper.ValueChanged += OnIntervalChanged;
        SceneView.Scene = scene;
        SceneView.Camera = camera;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        if (ContinuousSwitch.IsToggled)
        {
            RefreshContinuousRendering();
        }
        else
        {
            StatusLabel.Text = "Rendering paused";
            SceneView.InvalidateScene();
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        isPageVisible = false;
        StopContinuousRendering();
        base.OnDisappearing();
    }

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        _ = sender;
        if (e.Status is not (PresentationSurfaceFrameStatus.PresentedOptimal or
            PresentationSurfaceFrameStatus.PresentedSuboptimal))
        {
            return;
        }

        if (!resourceCountsRefreshed)
        {
            UpdateResourceCounts();
            resourceCountsRefreshed = true;
        }
    }

    private void OnRendererChanged(object? sender, SceneRendererChangedEventArgs e)
    {
        _ = sender;
        if (e.Renderer is null)
        {
            resourceCountsRefreshed = false;
            RefreshContinuousRendering();
            return;
        }
        UpdateResourceCounts();
        resourceCountsRefreshed = false;
        RefreshContinuousRendering();
    }

    private void OnSurfaceSizeChanged(object? sender, SurfaceSizeChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshContinuousRendering();
    }

    private void OnContinuousChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (e.Value && isPageVisible)
        {
            RefreshContinuousRendering();
            return;
        }
        StopContinuousRendering();
        if (!e.Value)
        {
            StatisticsOverlay.PublishSnapshot();
            StatusLabel.Text = "Rendering paused";
        }
    }

    private void OnIntervalChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        double milliseconds = Math.Round(e.NewValue / 50d) * 50d;
        StatisticsOverlay.SnapshotInterval = TimeSpan.FromMilliseconds(milliseconds);
        IntervalLabel.Text = $"{milliseconds:0} ms";
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        animationClock.Restart();
        if (!ContinuousSwitch.IsToggled || !isPageVisible)
        {
            animationClock.Stop();
        }
        animatedNode.Transform.Rotation = Quaternion.Identity;
        StatisticsOverlay.Reset();
        UpdateResourceCounts();
        StatisticsOverlay.PublishSnapshot();
        StatusLabel.Text = ContinuousSwitch.IsToggled
            ? "Statistics reset — continuous rendering active"
            : "Statistics reset — rendering paused";
        SceneView.InvalidateScene();
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StopContinuousRendering();
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }

    private void StartContinuousRendering()
    {
        if (continuousAnimation is not null)
        {
            return;
        }

        if (SceneView.Handler?.MauiContext?.Services.GetService(typeof(IAnimationManager))
            is not IAnimationManager manager)
        {
            StatusLabel.Text = "MAUI VSync animation service unavailable";
            return;
        }

        var animation = new MauiAnimation(
            _ => RenderContinuousFrame(),
            start: 0d,
            duration: 1d,
            easing: Easing.Linear,
            finished: null)
        {
            Name = "Mu3D Gallery frame-statistics VSync loop",
            Repeats = true,
        };

        animationManager = manager;
        continuousAnimation = animation;
        animationClock.Start();
        StatusLabel.Text = "Continuous rendering active";
        animation.Commit(manager);
    }

    private void RefreshContinuousRendering()
    {
        if (!isPageVisible || !ContinuousSwitch.IsToggled)
        {
            StopContinuousRendering();
            return;
        }

        if (SceneView.Renderer is null || SceneView.PixelWidth <= 1 || SceneView.PixelHeight <= 1)
        {
            StopContinuousRendering();
            StatusLabel.Text = "Preparing presentation surface…";
            SceneView.InvalidateScene();
            return;
        }

        StartContinuousRendering();
    }

    private void StopContinuousRendering()
    {
        MauiAnimation? animation = continuousAnimation;
        IAnimationManager? manager = animationManager;
        continuousAnimation = null;
        animationManager = null;

        if (animation is not null)
        {
            manager?.Remove(animation);
            animation.Dispose();
        }

        animationClock.Stop();
    }

    private void RenderContinuousFrame()
    {
        if (!isPageVisible || !ContinuousSwitch.IsToggled)
        {
            return;
        }

        float elapsedSeconds = (float)animationClock.Elapsed.TotalSeconds;
        animatedNode.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(
            elapsedSeconds * 0.9f,
            elapsedSeconds * 0.28f,
            0f);
        SceneView.InvalidateScene();
    }

    private void UpdateResourceCounts()
    {
        SceneRenderer? renderer = SceneView.Renderer;
        long? textureCount = renderer is null
            ? null
            : renderer.CachedMaterialTextureCount + renderer.CachedMaterialDataTextureCount;
        StatisticsOverlay.Collector.UpdateResourceCounts(new RenderResourceCounts(
            meshCount: 1,
            vertexCount: geometry.Positions.Count,
            materialCount: 1,
            textureCount: textureCount));
    }
}

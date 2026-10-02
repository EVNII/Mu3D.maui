using System.Diagnostics;
using System.Numerics;
using Microsoft.Maui.Animations;
using MauiAnimation = Microsoft.Maui.Animations.Animation;
using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates real MAUI UI following moving scene nodes after successful frames.</summary>
public partial class UiAnchorsPage : ContentPage
{
    private readonly Scene scene = new("UI anchor example");
    private readonly SceneNode cyanNode = new("Cyan tracked node");
    private readonly SceneNode magentaNode = new("Magenta tracked node");
    private readonly PerspectiveCamera camera = new(name: "UI anchor camera");
    private readonly Stopwatch animationClock = new();
    private IAnimationManager? animationManager;
    private MauiAnimation? continuousAnimation;
    private bool isPageVisible;
    private bool isPaused;

    /// <summary>Gets the low-level scene node followed by the cyan XAML overlay.</summary>
    public SceneNode CyanNode => cyanNode;

    /// <summary>Gets the low-level scene node followed by the magenta XAML overlay.</summary>
    public SceneNode MagentaNode => magentaNode;

    /// <summary>Initializes the focused UI-anchor example.</summary>
    public UiAnchorsPage()
    {
        InitializeComponent();

        SceneView.ClearColor = new LinearRgba(
            0.01f,
            0.016f,
            0.032f,
            1f,
            StandardColorSpaces.LinearSrgb);
        cyanNode.AddChild(CreateSphere(
            new LinearRgba(0.08f, 0.9f, 1.7f, 1f, StandardColorSpaces.LinearSrgb),
            "Cyan sphere"));
        magentaNode.AddChild(CreateSphere(
            new LinearRgba(1.6f, 0.12f, 0.8f, 1f, StandardColorSpaces.LinearSrgb),
            "Magenta sphere"));
        scene.Add(cyanNode);
        scene.Add(magentaNode);
        camera.Transform.Position = new Vector3(0f, 0f, 6f);

        SceneView.Scene = scene;
        SceneView.Camera = camera;
        UpdateScene(0f);
        StatusLabel.Text = "Preparing the presentation surface…";
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        SceneView.InvalidateScene();
        RefreshAnimation();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        isPageVisible = false;
        StopAnimation();
        base.OnDisappearing();
    }

    private void OnRendererChanged(object? sender, SceneRendererChangedEventArgs e)
    {
        _ = sender;
        if (e.Renderer is null)
        {
            StopAnimation();
            return;
        }
        RefreshAnimation();
    }

    private void OnFrameSnapshotChanged(
        object? sender,
        ViewportFrameSnapshotChangedEventArgs e)
    {
        _ = sender;
        if (e.Snapshot is ViewportFrameSnapshot snapshot && snapshot.FrameId % 30 == 0)
        {
            StatusLabel.Text =
                $"Successful frame {snapshot.FrameId}; " +
                $"{snapshot.PixelWidth}×{snapshot.PixelHeight} px; " +
                $"logical {snapshot.LogicalWidth:0}×{snapshot.LogicalHeight:0}; " +
                $"scale {snapshot.DisplayScaleX:0.00}×{snapshot.DisplayScaleY:0.00}.";
        }
    }

    private void OnPauseClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        isPaused = !isPaused;
        PauseButton.Text = isPaused ? "Resume motion" : "Pause motion";
        RefreshAnimation();
        if (isPaused)
        {
            StatusLabel.Text = "Motion paused; labels retain the last successfully presented positions.";
        }
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StopAnimation();
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StopAnimation();
        StatusLabel.Text = $"Viewport overlay {e.Operation} failed: {e.Exception.Message}";
    }

    private void RefreshAnimation()
    {
        if (!isPageVisible || isPaused || SceneView.Renderer is null)
        {
            StopAnimation();
            return;
        }
        StartAnimation();
    }

    private void StartAnimation()
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

        MauiAnimation animation = new(
            _ => RenderAnimationFrame(),
            start: 0d,
            duration: 1d,
            easing: Easing.Linear,
            finished: null)
        {
            Name = "Mu3D Gallery UI-anchor VSync loop",
            Repeats = true,
        };
        animationManager = manager;
        continuousAnimation = animation;
        animationClock.Start();
        animation.Commit(manager);
    }

    private void StopAnimation()
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

    private void RenderAnimationFrame()
    {
        if (!isPageVisible || isPaused)
        {
            return;
        }
        UpdateScene((float)animationClock.Elapsed.TotalSeconds);
        SceneView.InvalidateScene();
    }

    private void UpdateScene(float time)
    {
        Examples.ToolkitSceneExamples.UpdateAnchors(cyanNode, magentaNode, time);
    }

    private static Mesh CreateSphere(LinearRgba color, string name) =>
        Examples.ToolkitSceneExamples.CreateAnchorSphere(color, name);
}

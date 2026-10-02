using Mu3D.GalleryApp.Examples;
using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates reusable scene assets and independent instances across scene views.</summary>
public partial class SceneAssetPage : ContentPage
{
    private readonly SceneAssetExample example = new();
    /// <summary>Initializes the focused scene-asset example.</summary>
    public SceneAssetPage()
    {
        InitializeComponent();
        LeftSceneView.ClearColor = RightSceneView.ClearColor = SceneAssetExample.ClearColor;
        LeftSceneView.Scene = example.LeftScene; RightSceneView.Scene = example.RightScene;
        LeftSceneView.Camera = example.LeftCamera; RightSceneView.Camera = example.RightCamera;
        UpdateStatus();
    }
    /// <inheritdoc />
    protected override void OnAppearing() { base.OnAppearing(); InvalidateBothScenes(); }
    private void OnMoveClicked(object? sender, EventArgs e) { example.Move(); InvalidateBothScenes(); UpdateStatus(); }
    private void OnRotateClicked(object? sender, EventArgs e) { example.Rotate(); InvalidateBothScenes(); UpdateStatus(); }
    private void OnResetClicked(object? sender, EventArgs e) { example.Reset(); InvalidateBothScenes(); UpdateStatus(); }
    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        string room = ReferenceEquals(sender, LeftSceneView) ? "Room A" : "Room B";
        StatusLabel.Text = $"{room} presentation {e.Operation} failed: {e.Exception.Message}";
    }
    private void InvalidateBothScenes() { LeftSceneView.InvalidateScene(); RightSceneView.InvalidateScene(); }
    private void UpdateStatus() => StatusLabel.Text = example.Status();
}

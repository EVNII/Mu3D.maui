using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates focus-independent first-person camera commands.</summary>
public partial class FlyControlsPage : ContentPage
{
    /// <summary>Initializes the fly-controls example.</summary>
    public FlyControlsPage()
    {
        InitializeComponent();
        ConfigureContinuousKeyboardInput();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        AttachContinuousKeyboardInput();
        StatusLabel.Text =
            "Hold multiple movement/look keys for immediate composite motion; drag the viewport to look.";
        SceneView.InvalidateScene();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        DetachContinuousKeyboardInput();
        base.OnDisappearing();
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Fly feature {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }

    partial void ConfigureContinuousKeyboardInput();

    partial void AttachContinuousKeyboardInput();

    partial void DetachContinuousKeyboardInput();
}

using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates target-centered Orbit camera navigation.</summary>
public partial class OrbitControlsPage : ContentPage
{
    /// <summary>Initializes the orbit-controls example.</summary>
    public OrbitControlsPage() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        StatusLabel.Text = OrbitNavigation.Behavior.IsMouseButtonInputAvailable
            ? "Left drag rotates; right drag, trackpad Pan or two fingers pan; pinch/wheel dollies."
            : "One pointer rotates; two fingers pan while pinch dollies; supported wheels also dolly.";
        SceneView.InvalidateScene();
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Orbit feature {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

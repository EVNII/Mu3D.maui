using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates a finite depth-tested scene-space grid.</summary>
public partial class GridHelperPage : ContentPage
{
    /// <summary>Initializes the grid-helper example.</summary>
    public GridHelperPage() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        SceneView.InvalidateScene();
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Grid feature {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

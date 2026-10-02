using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates an optional interactive orientation axes overlay.</summary>
public partial class AxesHelperPage : ContentPage
{
    /// <summary>Initializes the axes-helper example.</summary>
    public AxesHelperPage() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        SceneView.InvalidateScene();
    }

    private void OnViewRequested(object? sender, AxesViewRequestedEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = e.Axis == Mu3D.Toolkit.Helpers.AxesHelperAxis.Diagonal
            ? $"45° view: {e.Preset}"
            : $"Orthogonal view: {e.Preset}";
    }

    private void OnAxesFailed(object? sender, AxesHelperFailedEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Axes interaction failed: {e.Exception.Message}";
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Axes feature {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

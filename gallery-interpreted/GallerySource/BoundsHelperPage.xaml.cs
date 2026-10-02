using Mu3D.Maui.Controls;
using Mu3D.Toolkit.Rendering;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Compares target-bound world-axis-aligned bounds with a screen-space silhouette.</summary>
public partial class BoundsHelperPage : ContentPage
{
    /// <summary>Initializes the bounds-helper example.</summary>
    public BoundsHelperPage() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        SceneView.InvalidateScene();
    }

    private void OnOverlayToggled(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        TargetBounds.DepthMode = e.Value
            ? BoundsHelperDepthMode.Overlay
            : BoundsHelperDepthMode.SceneDepthTested;
        TargetOutline.DepthMode = e.Value
            ? OutlineHelperDepthMode.Overlay
            : OutlineHelperDepthMode.SceneDepthTested;
        StatusLabel.Text = e.Value
            ? "Overlay mode draws the selected helpers through the foreground sphere."
            : "Scene-depth-tested mode lets the foreground sphere occlude the selected helpers.";
    }

    private void OnIncludeInvisibleToggled(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        TargetBounds.IncludeInvisible = e.Value;
        TargetOutline.IncludeInvisible = e.Value;
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Helper feature {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

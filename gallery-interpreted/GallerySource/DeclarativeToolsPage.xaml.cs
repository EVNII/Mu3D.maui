using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates declarative selection, Orbit, and Transform Gizmo tools.</summary>
public partial class DeclarativeToolsPage : ContentPage
{
    /// <summary>Initializes the declarative tools example.</summary>
    public DeclarativeToolsPage() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        StatusLabel.Text = GizmoTool.IsPointerInputAvailable
            ? $"Ready — tap an object; selected: {SelectionTool.SelectedNode?.Name ?? "none"}"
            : "Scene ready, but native gizmo pointer input is unavailable on this target";
        SceneView.InvalidateScene();
    }

    private void OnSelectionFailed(object? sender, SceneSelectionFailedEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Selection failed: {e.Exception.Message}";
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Tool {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates the first declarative Scene3D XAML composition slice.</summary>
public partial class DeclarativeScenePage : ContentPage
{
    /// <summary>Initializes the declarative scene example.</summary>
    public DeclarativeScenePage() => InitializeComponent();

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        SceneView.InvalidateScene();
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

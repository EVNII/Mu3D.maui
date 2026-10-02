using Mu3D.Gallery.Controls;
using Mu3D.GalleryApp.Examples;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;

namespace Mu3D.Gallery.Pages;

/// <summary>
/// Demonstrates a translucent HDR canvas presented on an unlit sphere over a transparent native
/// surface, so the page content behind the 3D view shows through the low-alpha canvas regions.
/// </summary>
public partial class TranslucentCanvasPage : ContentPage, IGalleryPageActivation
{
    private readonly HdrCanvasExample example = new(translucent: true);
    private bool alphaFallbackApplied;

    /// <summary>Initializes the translucent canvas and requests a transparent native surface.</summary>
    public TranslucentCanvasPage()
    {
        InitializeComponent();
        CanvasMaterial.UnlitMaterial.AlphaMode = MaterialAlphaMode.Blend;
        Host.SceneView.OutputSettings = OutputSettings.Default with
        {
            AlphaMode = SurfaceAlphaMode.Premultiplied,
        };
        Reset();
    }

    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (active) Host.SceneView.InvalidateScene();
    }

    private void OnDabClicked(object? sender, EventArgs e)
    {
        example.AddDab();
        Publish();
    }

    private void OnResetClicked(object? sender, EventArgs e) => Reset();

    private void Reset()
    {
        example.Reset();
        Publish();
    }

    private void Publish()
    {
        CanvasMaterial.Texture = example.Publish(out string status);
        StatusLabel.Text = status;
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        if (!alphaFallbackApplied &&
            Host.SceneView.OutputSettings?.AlphaMode != SurfaceAlphaMode.Automatic)
        {
            alphaFallbackApplied = true;
            Host.SceneView.OutputSettings = OutputSettings.Default with
            {
                AlphaMode = SurfaceAlphaMode.Automatic,
            };
            StatusLabel.Text =
                "Premultiplied alpha is not advertised by this surface; using the platform default instead.";
            return;
        }
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

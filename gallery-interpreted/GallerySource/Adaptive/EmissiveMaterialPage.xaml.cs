using Mu3D.Gallery.Controls;
using Mu3D.GalleryApp.Examples;
using Mu3D.Maui.Controls;

namespace Mu3D.Gallery.Pages;

/// <summary>
/// Demonstrates HDR emissive materials: four spheres share one warm emission color at strengths
/// 0, 1, 4 and 16, so the shared chrome's EV slider and color-space/output pickers show how
/// high-dynamic-range emission survives (or clips through) the selected display view.
/// </summary>
public partial class EmissiveMaterialPage : ContentPage, IGalleryPageActivation
{
    private bool emissionApplied;

    /// <summary>Initializes the emissive material example.</summary>
    public EmissiveMaterialPage()
    {
        InitializeComponent();
    }

    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (active) OnLoadedOnce(this, EventArgs.Empty);
    }

    private void OnLoadedOnce(object? sender, EventArgs e)
    {
        if (emissionApplied)
        {
            return;
        }
        emissionApplied = true;
        // The declarative PbrMaterial3D exposes no emissive properties, so set them on the
        // underlying Core PbrMaterial that each wrapper already owns.
        ApplyEmission(MaterialOff, 0f);
        ApplyEmission(MaterialOne, 1f);
        ApplyEmission(MaterialFour, 4f);
        ApplyEmission(MaterialSixteen, 16f);
        // Core-material mutations bypass declarative change notification; request a new frame.
        Host.SceneView.InvalidateScene();
    }

    private static void ApplyEmission(PbrMaterial3D material, float strength)
    {
        EmissiveMaterialExample.Apply(material.PbrMaterial, strength);
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

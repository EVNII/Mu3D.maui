using Mu3D.GalleryApp.Examples;
using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates the automatic Mu3DView draw callback.</summary>
public partial class CustomDrawPage : ContentPage
{
    private readonly HdrTriangleExample example = new();

    /// <summary>Initializes the custom draw example.</summary>
    public CustomDrawPage() => InitializeComponent();

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        example.Render(e.Device, e.Target);
        DetailsLabel.Text =
            $"{e.Width} x {e.Height} physical pixels · {e.OutputPlan.Output.Format}\n" +
            "Triangle color: extended-linear sRGB (0.05, 0.15, 2.0)";
    }

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Frame: {e.Status}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed";
        DetailsLabel.Text = e.Exception.ToString();
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        _ = sender;
        if (e.Session is null)
        {
            example.Dispose();
        }
    }

    private void OnRedrawClicked(object? sender, EventArgs e)
    {
        _ = sender;
        SurfaceView.InvalidateSurface();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        SurfaceView.InvalidateSurface();
    }
}

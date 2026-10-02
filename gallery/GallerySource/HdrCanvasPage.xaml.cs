using Mu3D.GalleryApp.Examples;
using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates bounded HDR canvas dabs presented through a native Mu3D scene texture.</summary>
public partial class HdrCanvasPage : ContentPage
{
    private readonly HdrCanvasExample example = new();
    /// <summary>Initializes the canvas and XAML scene without owning a surface or input device.</summary>
    public HdrCanvasPage() { InitializeComponent(); Reset(); }
    private void OnDabClicked(object? sender, EventArgs e) { example.AddDab(); Publish(); }
    private void OnResetClicked(object? sender, EventArgs e) => Reset();
    private void Reset() { example.Reset(); Publish(); }
    private void Publish()
    {
        CanvasMaterial.Texture = example.Publish(out string status);
        StatusLabel.Text = status;
    }
    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e) => StatusLabel.Text = e.Exception.Message;
}

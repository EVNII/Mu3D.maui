using Mu3D.Color;
using Mu3D.Creative;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;

namespace Mu3D.Gallery.Pages;

/// <summary>
/// Demonstrates a translucent HDR canvas presented on an unlit sphere over a transparent native
/// surface, so the page content behind the 3D view shows through the low-alpha canvas regions.
/// </summary>
public partial class TranslucentCanvasPage : ContentPage
{
    private readonly HdrCanvas canvas = new(256, 256, StandardColorSpaces.AcesCg,
        new HdrCanvasOptions { MaxStorageBytes = 1024 * 1024, MaxOperationBytes = 1024 * 1024 });
    private int dab;
    private bool alphaFallbackApplied;

    /// <summary>Initializes the translucent canvas and requests a transparent native surface.</summary>
    public TranslucentCanvasPage()
    {
        InitializeComponent();
        Host.SceneView.OutputSettings = OutputSettings.Default with
        {
            AlphaMode = SurfaceAlphaMode.Premultiplied,
        };
        Reset();
    }

    private void OnDabClicked(object? sender, EventArgs e)
    {
        canvas.ApplyDab(new BrushDab(
            32 + dab * 37 % 192,
            48 + dab * 53 % 160,
            35,
            new LinearRgba(4, 0.6f, 0.15f, 0.5f, StandardColorSpaces.AcesCg),
            hardness: 0.2f));
        dab++;
        Publish();
    }

    private void OnResetClicked(object? sender, EventArgs e) => Reset();

    private void Reset()
    {
        canvas.Clear();
        canvas.Fill(
            new CanvasRegion(0, 0, 256, 256),
            new LinearRgba(0.05f, 0.1f, 0.35f, 0.2f, StandardColorSpaces.AcesCg));
        dab = 0;
        Publish();
    }

    private void Publish()
    {
        int changed = canvas.GetDirtyRegions().Count;
        CanvasMaterial.Texture = canvas.Snapshot(name: "Translucent canvas snapshot");
        canvas.ClearDirtyRegions();
        StatusLabel.Text = $"{canvas.Precision} · ACEScg · {canvas.TileCount} tiles · " +
            $"{changed} changed regions · {canvas.AllocatedStorageBytes:N0} bytes. " +
            "Background and dabs carry alpha below 1.";
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

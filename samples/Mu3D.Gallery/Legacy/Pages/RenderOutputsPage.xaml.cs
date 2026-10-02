using Mu3D.GalleryApp.Examples;
using Mu3D.Maui.Controls;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates stable built-in and explicitly registered render outputs.</summary>
public partial class RenderOutputsPage : ContentPage
{
    /// <summary>Gets the Gallery-owned output ID used by the XAML custom toolbar entry.</summary>
    public static RenderOutputId GalleryNormalAlias => RenderOutputsExample.NormalAlias;

    private readonly Scene scene = new("Render-output example");
    private readonly PerspectiveCamera camera = RenderOutputsExample.CreateCamera();
    private CancellationTokenSource lifetime = new();
    private bool configured;

    /// <summary>Initializes the focused render-output example.</summary>
    public RenderOutputsPage()
    {
        InitializeComponent();
        SceneView.Camera = camera;
        SceneView.ClearColor = RenderOutputsExample.ClearColor;
        RenderOutputsExample.RegisterAlias(SceneView.RenderOutputRegistry);
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (configured)
        {
            SceneView.InvalidateScene();
            return;
        }

        if (lifetime.IsCancellationRequested)
        {
            lifetime.Dispose();
            lifetime = new CancellationTokenSource();
        }
        _ = ConfigureAsync(lifetime.Token);
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        lifetime.Cancel();
        base.OnDisappearing();
    }

    private async Task ConfigureAsync(CancellationToken cancellationToken)
    {
        try
        {
            StatusLabel.Text = "Loading the shared HDR environment…";
            EquirectangularHdrEnvironment environment =
                await GalleryEnvironment.LoadStudioAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            BuildScene(environment);
            configured = true;
            SceneView.Scene = scene;
            StatusLabel.Text = "Ready. Output selection is available inside the viewport.";
            SceneView.InvalidateScene();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusLabel.Text = $"Render-output setup failed: {exception.Message}";
        }
    }

    private void BuildScene(EquirectangularHdrEnvironment environment) => RenderOutputsExample.Populate(scene, environment);

    private void OnRendererChanged(object? sender, SceneRendererChangedEventArgs e)
    {
        _ = sender;
        if (e.Renderer is not null)
        {
            e.Renderer.AmbientOcclusionEnabled = true;
            e.Renderer.AmbientOcclusionRadius = 0.7f;
            e.Renderer.AmbientOcclusionStrength = 1f;
        }
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Render-output tool {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }
}

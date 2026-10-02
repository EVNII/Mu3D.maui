using System.Numerics;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Native.UltraHdr;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates switching authored KHR_materials_variants choices.</summary>
public partial class GltfVariantsPage : ContentPage
{
    private GltfScenePresenter? presenter;
    private GltfAsset? asset;
    private CancellationTokenSource lifetime = new();
    private Task? loadingTask;
    private bool isPageVisible;

    /// <summary>Initializes the glTF material-variants example.</summary>
    public GltfVariantsPage()
    {
        InitializeComponent();
        SurfaceView.PresentationSessionChanged += OnPresentationSessionChanged;
        SurfaceView.SurfaceError += OnSurfaceError;
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        _ = sender;
        ResetLifetime();
        DisposePresenter();
        if (isPageVisible && e.Session is not null)
        {
            loadingTask = LoadAsync(e.Session, lifetime.Token);
        }
    }

    private async Task LoadAsync(
        IPresentationSurfaceSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!IsLoadCurrent(session, cancellationToken))
            {
                return;
            }

            byte[] bytes = await GalleryAssets.ReadBytesAsync(
                "GltfSamples/MaterialsVariantsShoe.glb",
                CancellationToken.None);
            if (!IsLoadCurrent(session, cancellationToken))
            {
                return;
            }

            GltfAsset imported = await Task.Run(
                () => GltfImporter.ImportAsset(
                    bytes,
                    name: "Materials Variants Shoe",
                    imageDecoder: new JpegImageDecoder()));
            if (!IsLoadCurrent(session, cancellationToken))
            {
                return;
            }

            var environment = await GalleryEnvironment.LoadStudioAsync(CancellationToken.None);
            if (!IsLoadCurrent(session, cancellationToken))
            {
                return;
            }
            asset = imported;
            presenter = new GltfScenePresenter(
                session.Device,
                imported,
                environment,
                new Vector3(0f, 0f, 3f),
                Vector3.Zero);
            VariantPicker.ItemsSource = new[] { "Default material" }
                .Concat(imported.MaterialVariants.Select(static variant => variant.Name))
                .ToArray();
            VariantPicker.SelectedIndex = 0;
            StatusLabel.Text = "Material variants ready";
            DetailsLabel.Text = $"Authored variants: {imported.MaterialVariants.Count}";
            SurfaceView.InvalidateSurface();
        }
        catch (Exception exception)
        {
            if (!IsLoadCurrent(session, cancellationToken))
            {
                return;
            }
            StatusLabel.Text = "Material variants failed";
            DetailsLabel.Text = exception.ToString();
        }
    }

    private bool IsLoadCurrent(
        IPresentationSurfaceSession session,
        CancellationToken cancellationToken) =>
        isPageVisible &&
        !cancellationToken.IsCancellationRequested &&
        ReferenceEquals(SurfaceView.PresentationSession, session);

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        if (presenter is GltfScenePresenter activePresenter)
        {
            activePresenter.Render(e.Target, e.Width, e.Height);
        }
        else
        {
            GalleryDraw.Clear(e, "glTF variants loading frame");
        }
    }

    private void OnVariantChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (asset is null || VariantPicker.SelectedIndex < 0)
        {
            return;
        }
        asset.ApplyMaterialVariant(
            VariantPicker.SelectedIndex == 0 ? null : VariantPicker.SelectedIndex - 1);
        SurfaceView.InvalidateSurface();
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed";
        DetailsLabel.Text = e.Exception.ToString();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        if (lifetime.IsCancellationRequested)
        {
            ResetLifetime();
        }
        if (presenter is null &&
            SurfaceView.PresentationSession is IPresentationSurfaceSession session &&
            (loadingTask is null || loadingTask.IsCompleted))
        {
            loadingTask = LoadAsync(session, lifetime.Token);
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        isPageVisible = false;
        lifetime.Cancel();
        base.OnDisappearing();
    }

    private void ResetLifetime()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new CancellationTokenSource();
        loadingTask = null;
    }

    private void DisposePresenter()
    {
        presenter?.Dispose();
        presenter = null;
        asset = null;
    }
}

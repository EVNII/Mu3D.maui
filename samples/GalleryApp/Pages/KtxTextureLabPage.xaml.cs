using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Native.Ktx;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Compares decoded and GPU-compressed KTX2 material sampling.</summary>
public partial class KtxTextureLabPage : ContentPage
{
    private KtxTextureLabPresenter? presenter;
    private CancellationTokenSource lifetime = new();
    private Task? loadingTask;

    /// <summary>Initializes the KTX GPU sampling example.</summary>
    public KtxTextureLabPage()
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
        ReloadButton.IsEnabled = e.Session is not null;
        if (e.Session is null)
        {
            StatusLabel.Text = "Presentation surface unavailable";
            return;
        }
        loadingTask = LoadTexturesAsync(e.Session, lifetime.Token);
    }

    private async Task LoadTexturesAsync(
        IPresentationSurfaceSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            StatusLabel.Text = "Reading and transcoding the bundled KTX2 textures…";
            byte[] colorBytes = await GalleryAssets.ReadBytesAsync(
                "KtxSamples/color_grid_basis.ktx2",
                cancellationToken);
            byte[] normalBytes = await GalleryAssets.ReadBytesAsync(
                "KtxSamples/StainedGlassLamp_grill_normal.ktx2",
                cancellationToken);
            KtxTextureLabTextures textures = await Task.Run(
                () => PrepareTextures(colorBytes, normalBytes, session.Device.Capabilities),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(SurfaceView.PresentationSession, session))
            {
                return;
            }

            presenter = new KtxTextureLabPresenter(session.Device, textures);
            StatusLabel.Text = "Decoded and GPU-compressed textures ready";
            DetailsLabel.Text = FormatDetails(session.Device.Capabilities, textures);
            SurfaceView.InvalidateSurface();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusLabel.Text = "KTX GPU sampling failed";
            DetailsLabel.Text = exception.ToString();
        }
    }

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        if (presenter is KtxTextureLabPresenter activePresenter)
        {
            activePresenter.Render(e.Target, e.Width, e.Height);
            return;
        }

        GalleryDraw.Clear(e, "KTX loading frame");
    }

    private static KtxTextureLabTextures PrepareTextures(
        byte[] colorBytes,
        byte[] normalBytes,
        GraphicsCapabilities capabilities)
    {
        Ktx2TextureTranscoder transcoder = new();
        Ktx2ImageDecoder decoder = new();
        return new KtxTextureLabTextures(
            transcoder.TryTranscode(
                colorBytes,
                "image/ktx2",
                CompressedMaterialTextureContent.Color,
                capabilities,
                "Khronos ETC1S color grid"),
            decoder.DecodeColor(colorBytes, "image/ktx2", "Khronos ETC1S color grid"),
            transcoder.TryTranscode(
                normalBytes,
                "image/ktx2",
                CompressedMaterialTextureContent.Data,
                capabilities,
                "Khronos UASTC normal"),
            decoder.DecodeData(normalBytes, "image/ktx2", "Khronos UASTC normal"));
    }

    private static string FormatDetails(
        GraphicsCapabilities capabilities,
        KtxTextureLabTextures textures) =>
        $"GPU compression: ASTC={capabilities.SupportsAstcTextureCompression}, " +
        $"BC={capabilities.SupportsBcTextureCompression}, " +
        $"ETC2={capabilities.SupportsEtc2TextureCompression}\n" +
        $"Color: {Describe(textures.CompressedColor)}\n" +
        $"Normal: {Describe(textures.CompressedNormal)}\n" +
        "Left: decoded RGBA8 · Right: GPU-compressed";

    private static string Describe(CompressedMaterialTexture? texture) => texture is null
        ? "compressed target unavailable; decoded fallback used"
        : $"{texture.Format}, {texture.Width}x{texture.Height}, {texture.MipLevels.Count} mips";

    private void OnReloadClicked(object? sender, EventArgs e)
    {
        _ = sender;
        if (SurfaceView.PresentationSession is not IPresentationSurfaceSession current)
        {
            return;
        }
        ResetLifetime();
        DisposePresenter();
        loadingTask = LoadTexturesAsync(current, lifetime.Token);
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
        if (lifetime.IsCancellationRequested)
        {
            ResetLifetime();
        }
        if (presenter is null &&
            SurfaceView.PresentationSession is IPresentationSurfaceSession session &&
            (loadingTask is null || loadingTask.IsCompleted))
        {
            loadingTask = LoadTexturesAsync(session, lifetime.Token);
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
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
    }
}

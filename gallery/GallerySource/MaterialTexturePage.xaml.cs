using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates metallic/roughness material values and texture sampling.</summary>
public partial class MaterialTexturePage : ContentPage
{
    private MaterialTexturePresenter? presenter;
    private CancellationTokenSource lifetime = new();

    /// <summary>Initializes the material and texture example.</summary>
    public MaterialTexturePage()
    {
        InitializeComponent();
        AddressPicker.ItemsSource = Enum.GetNames<MaterialTextureAddressMode>();
        AddressPicker.SelectedIndex = (int)MaterialTextureAddressMode.Repeat;
        FilterPicker.ItemsSource = Enum.GetNames<MaterialTextureFilter>();
        FilterPicker.SelectedIndex = (int)MaterialTextureFilter.Linear;
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
        if (e.Session is null)
        {
            StatusLabel.Text = "Presentation surface unavailable";
            return;
        }
        _ = ConfigureAsync(e.Session, lifetime.Token);
    }

    private async Task ConfigureAsync(
        IPresentationSurfaceSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            var environment = await GalleryEnvironment.LoadStudioAsync(cancellationToken);
            if (!ReferenceEquals(SurfaceView.PresentationSession, session))
            {
                return;
            }
            presenter = new MaterialTexturePresenter(session.Device, environment);
            ApplyControls();
            StatusLabel.Text = "Material ready";
            DetailsLabel.Text =
                "All maps are assigned through backend-independent PbrMaterial properties.";
            SurfaceView.InvalidateSurface();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusLabel.Text = "Material setup failed";
            DetailsLabel.Text = exception.ToString();
        }
    }

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        if (presenter is MaterialTexturePresenter activePresenter)
        {
            activePresenter.Render(e.Target, e.Width, e.Height);
        }
        else
        {
            GalleryDraw.Clear(e, "Material loading frame");
        }
    }

    private void OnControlChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyControls();
        SurfaceView.InvalidateSurface();
    }

    private void ApplyControls()
    {
        MetallicLabel.Text = $"Metallic: {MetallicSlider.Value:0.00}";
        RoughnessLabel.Text = $"Roughness: {RoughnessSlider.Value:0.00}";
        TilingLabel.Text = $"Texture tiling: {TilingSlider.Value:0.00}x";
        if (presenter is not MaterialTexturePresenter activePresenter ||
            AddressPicker.SelectedIndex < 0 ||
            FilterPicker.SelectedIndex < 0)
        {
            return;
        }
        activePresenter.Metallic = (float)MetallicSlider.Value;
        activePresenter.Roughness = (float)RoughnessSlider.Value;
        activePresenter.UseColorTexture = ColorTextureSwitch.IsToggled;
        activePresenter.UseNormalTexture = NormalTextureSwitch.IsToggled;
        activePresenter.UseOrmTexture = OrmTextureSwitch.IsToggled;
        activePresenter.TextureScale = (float)TilingSlider.Value;
        activePresenter.TextureSampling = new MaterialTextureSampling(
            (MaterialTextureAddressMode)AddressPicker.SelectedIndex,
            (MaterialTextureAddressMode)AddressPicker.SelectedIndex,
            (MaterialTextureFilter)FilterPicker.SelectedIndex);
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
            SurfaceView.PresentationSession is IPresentationSurfaceSession session)
        {
            _ = ConfigureAsync(session, lifetime.Token);
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
    }

    private void DisposePresenter()
    {
        presenter?.Dispose();
        presenter = null;
    }
}

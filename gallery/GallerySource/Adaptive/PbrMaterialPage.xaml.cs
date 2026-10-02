using Mu3D.Gallery.Controls;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;

namespace Mu3D.Gallery.Pages;

/// <summary>Demonstrates metallic/roughness material values and texture sampling.</summary>
public partial class PbrMaterialPage : ContentPage, IGalleryPageActivation
{
    private MaterialTexturePresenter? presenter;
    private CancellationTokenSource lifetime = new();
    private Task? configurationTask;
    private bool navigationActive;

    /// <summary>Initializes the material and texture example.</summary>
    public PbrMaterialPage()
    {
        InitializeComponent();
        AddressPicker.ItemsSource = Enum.GetNames<MaterialTextureAddressMode>();
        AddressPicker.SelectedIndex = (int)MaterialTextureAddressMode.Repeat;
        FilterPicker.ItemsSource = Enum.GetNames<MaterialTextureFilter>();
        FilterPicker.SelectedIndex = (int)MaterialTextureFilter.Linear;
    }

    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (navigationActive == active) return;
        navigationActive = active;
        if (active) OnPageLoaded(this, EventArgs.Empty);
        else OnPageUnloaded(this, EventArgs.Empty);
    }

    private async Task ConfigureAsync(CancellationToken cancellationToken)
    {
        try
        {
            var environment = await SampleEnvironment.LoadStudioAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            presenter = new MaterialTexturePresenter(DeclaredScene.Scene, environment);
            ApplyControls();
            StatusLabel.Text = "Material ready";
            DetailsLabel.Text =
                "All maps are assigned through backend-independent PbrMaterial properties.";
            Host.SceneView.InvalidateScene();
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

    private async Task EnsureConfiguredAsync(CancellationToken cancellationToken)
    {
        // A quick leave/re-enter must not start a second setup while the cancelled one retires.
        if (configurationTask is { IsCompleted: false } previous) await previous;
        if (!navigationActive || cancellationToken.IsCancellationRequested || presenter is not null) return;
        configurationTask = ConfigureAsync(cancellationToken);
        await configurationTask;
    }

    private void OnControlChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyControls();
        Host.SceneView.InvalidateScene();
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

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        if (lifetime.IsCancellationRequested)
        {
            ResetLifetime();
        }
        if (presenter is null)
        {
            _ = EnsureConfiguredAsync(lifetime.Token);
        }
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        lifetime.Cancel();
    }

    private void ResetLifetime()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new CancellationTokenSource();
    }
}

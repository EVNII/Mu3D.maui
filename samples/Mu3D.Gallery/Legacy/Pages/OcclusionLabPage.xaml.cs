using Mu3D.Graphics;
using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates directional shadows and screen-space ambient occlusion.</summary>
public partial class OcclusionLabPage : ContentPage
{
    private OcclusionLabPresenter? presenter;
    private CancellationTokenSource lifetime = new();

    /// <summary>Initializes the occlusion example.</summary>
    public OcclusionLabPage()
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
            presenter = new OcclusionLabPresenter(session.Device, environment);
            ApplyControls();
            StatusLabel.Text = "Occlusion scene ready";
            DetailsLabel.Text = "A shadowed key light plus camera-depth SSAO on indirect diffuse.";
            SurfaceView.InvalidateSurface();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            StatusLabel.Text = "Occlusion setup failed";
            DetailsLabel.Text = exception.ToString();
        }
    }

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        if (presenter is OcclusionLabPresenter activePresenter)
        {
            activePresenter.Render(e.Target, e.Width, e.Height);
        }
        else
        {
            GalleryDraw.Clear(e, "Occlusion loading frame");
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
        AoRadiusLabel.Text = $"AO radius: {AoRadiusSlider.Value:0.00}";
        AoStrengthLabel.Text = $"AO strength: {AoStrengthSlider.Value:0.00}";
        if (presenter is not OcclusionLabPresenter activePresenter)
        {
            return;
        }
        activePresenter.ShadowsEnabled = ShadowSwitch.IsToggled;
        activePresenter.AmbientOcclusionEnabled = AoSwitch.IsToggled;
        activePresenter.AmbientOcclusionRadius = (float)AoRadiusSlider.Value;
        activePresenter.AmbientOcclusionStrength = (float)AoStrengthSlider.Value;
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

using System.Diagnostics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates direct and image-based lighting.</summary>
public partial class LightingLabPage : ContentPage
{
    private const string EnvironmentSourceKey = "app://Hdri/studio_small_02_1k.hdr";
    private const int MaximumEnvironmentSourceByteCount = 2 * 1024 * 1024;
    private const long MaximumEnvironmentOutputByteCount = 8L * 1024 * 1024;

    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly RadianceHdrSourceCache sourceCache = new(MaximumEnvironmentSourceByteCount);
    private LightingLabPresenter? presenter;
    private RadianceHdrEnvironmentAsset? environmentAsset;
    private CancellationTokenSource lifetime = new();
    private CancellationTokenSource? loadCancellation;
    private Task? configurationTask;
    private int configurationVersion;
    private int resolverOpenCount;
    private bool lastLoadUsedCache;
    private TimeSpan lastLoadDuration;
    private TimeSpan lastPreparationDuration;
    private int animationVersion;

    /// <summary>Initializes the lighting example.</summary>
    public LightingLabPage()
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
            SetLoading(false);
            return;
        }
        StartConfiguration(e.Session);
    }

    private async Task ConfigureAsync(
        IPresentationSurfaceSession session,
        int version,
        bool retainEncodedSource,
        CancellationToken cancellationToken)
    {
        try
        {
            StatusLabel.Text = "Loading the Studio Small 02 HDR environment…";
            bool usedCache = sourceCache.Count != 0;
            var loadClock = Stopwatch.StartNew();
            RadianceHdrEnvironmentAsset loadedAsset = await RadianceHdrEnvironmentLoader.LoadAsync(
                EnvironmentSourceKey,
                OpenEnvironmentSourceAsync,
                sourceCache,
                new RadianceHdrEnvironmentLoadOptions(StandardColorSpaces.LinearSrgb)
                {
                    Name = "Studio Small 02 (Poly Haven, CC0, 1K HDR)",
                    MaximumSourceByteCount = MaximumEnvironmentSourceByteCount,
                    MaximumOutputByteCount = MaximumEnvironmentOutputByteCount,
                    RetainEncodedSource = retainEncodedSource,
                },
                cancellationToken);
            loadClock.Stop();

            var preparationClock = Stopwatch.StartNew();
            await SceneRenderer.PrepareImageBasedLightingAsync(
                loadedAsset.Environment,
                cancellationToken);
            preparationClock.Stop();
            if (!IsCurrentConfiguration(session, version, cancellationToken))
            {
                return;
            }

            DisposePresenter();
            environmentAsset = loadedAsset;
            presenter = new LightingLabPresenter(session.Device, loadedAsset.Environment);
            lastLoadUsedCache = usedCache;
            lastLoadDuration = loadClock.Elapsed;
            lastPreparationDuration = preparationClock.Elapsed;
            ApplyControls();
            StatusLabel.Text = "Lighting ready";
            UpdateDetails();
            SetLoading(false);
            SurfaceView.InvalidateSurface();
            if (AnimateSwitch.IsToggled)
            {
                StartAnimation(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (version == configurationVersion)
            {
                StatusLabel.Text = "HDR environment load cancelled";
                SetLoading(false);
            }
        }
        catch (Exception exception)
        {
            if (version == configurationVersion)
            {
                StatusLabel.Text = "Lighting setup failed";
                DetailsLabel.Text = exception.ToString();
                SetLoading(false);
            }
        }
    }

    private async ValueTask<Stream> OpenEnvironmentSourceAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Stream source = await FileSystem.Current.OpenAppPackageFileAsync(
            "Hdri/studio_small_02_1k.hdr");
        if (cancellationToken.IsCancellationRequested)
        {
            await source.DisposeAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
        resolverOpenCount++;
        return source;
    }

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        if (presenter is LightingLabPresenter activePresenter)
        {
            activePresenter.Render(e.Target, e.Width, e.Height, (float)clock.Elapsed.TotalSeconds);
        }
        else
        {
            GalleryDraw.Clear(e, "Lighting loading frame");
        }
    }

    private void OnLightingChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyControls();
        SurfaceView.InvalidateSurface();
    }

    private void OnAnimationChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        ApplyControls();
        if (e.Value)
        {
            StartAnimation(lifetime.Token);
        }
        else
        {
            animationVersion++;
            SurfaceView.InvalidateSurface();
        }
    }

    private void OnRetainEncodedChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        _ = e;
        if (configurationTask is null || configurationTask.IsCompleted)
        {
            StatusLabel.Text = "Encoded-source retention applies on the next HDR reload";
        }
    }

    private void OnReloadClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (SurfaceView.PresentationSession is IPresentationSurfaceSession session)
        {
            StartConfiguration(session);
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        loadCancellation?.Cancel();
    }

    private void OnClearSourceCacheClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        sourceCache.Clear();
        StatusLabel.Text = environmentAsset is null
            ? "Encoded HDR source cache cleared"
            : "Source cache cleared; the decoded environment remains valid";
        UpdateDetails();
    }

    private void StartAnimation(CancellationToken cancellationToken)
    {
        int version = ++animationVersion;
        _ = RunAnimationAsync(cancellationToken, version);
    }

    private async Task RunAnimationAsync(CancellationToken cancellationToken, int version)
    {
        while (!cancellationToken.IsCancellationRequested &&
            version == animationVersion &&
            AnimateSwitch.IsToggled)
        {
            SurfaceView.InvalidateSurface();
            await Task.Delay(16);
        }
    }

    private void ApplyControls()
    {
        if (presenter is not LightingLabPresenter activePresenter)
        {
            return;
        }
        activePresenter.DirectLightEnabled = DirectLightSwitch.IsToggled;
        activePresenter.EnvironmentEnabled = EnvironmentSwitch.IsToggled;
        activePresenter.AnimateLight = AnimateSwitch.IsToggled;
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
            (configurationTask is null || configurationTask.IsCompleted))
        {
            StartConfiguration(session);
        }
        else if (presenter is not null)
        {
            ApplyControls();
            SurfaceView.InvalidateSurface();
            if (AnimateSwitch.IsToggled)
            {
                StartAnimation(lifetime.Token);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        lifetime.Cancel();
        animationVersion++;
        base.OnDisappearing();
    }

    private void StartConfiguration(IPresentationSurfaceSession session)
    {
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        int version = ++configurationVersion;
        DisposePresenter();
        SetLoading(true);
        configurationTask = ConfigureAsync(
            session,
            version,
            RetainEncodedSwitch.IsToggled,
            loadCancellation.Token);
    }

    private void ResetLifetime()
    {
        configurationVersion++;
        loadCancellation?.Cancel();
        loadCancellation?.Dispose();
        loadCancellation = null;
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new CancellationTokenSource();
        configurationTask = null;
    }

    private void DisposePresenter()
    {
        presenter?.Dispose();
        presenter = null;
        environmentAsset = null;
    }

    private bool IsCurrentConfiguration(
        IPresentationSurfaceSession session,
        int version,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        version == configurationVersion &&
        ReferenceEquals(SurfaceView.PresentationSession, session);

    private void SetLoading(bool isLoading)
    {
        ReloadButton.IsEnabled = !isLoading;
        CancelButton.IsEnabled = isLoading;
        RetainEncodedSwitch.IsEnabled = !isLoading;
    }

    private void UpdateDetails()
    {
        long retainedByteCount = environmentAsset?.RetainedEncodedSource.Length ?? 0;
        long outputByteCount = environmentAsset?.OutputByteCount ?? 0;
        DetailsLabel.Text =
            $"Source: {(lastLoadUsedCache ? "cache" : "resolver")} | resolver opens: {resolverOpenCount} | " +
            $"cache: {FormatBytes(sourceCache.TotalByteCount)} / {FormatBytes(sourceCache.MaximumByteCount)} | " +
            $"FP32 output: {FormatBytes(outputByteCount)} | asset-retained source: {FormatBytes(retainedByteCount)} | " +
            $"load: {lastLoadDuration.TotalMilliseconds:F0} ms | IBL prepare: {lastPreparationDuration.TotalMilliseconds:F0} ms";
    }

    private static string FormatBytes(long byteCount) =>
        byteCount >= 1024 * 1024
            ? $"{byteCount / (1024d * 1024d):F2} MiB"
            : $"{byteCount / 1024d:F1} KiB";
}

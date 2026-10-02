using System.Diagnostics;
using System.Numerics;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates selecting and playing imported glTF animation clips.</summary>
public partial class GltfAnimationPage : ContentPage
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private GltfScenePresenter? presenter;
    private GltfAsset? asset;
    private CancellationTokenSource lifetime = new();
    private Task? loadingTask;
    private int playbackVersion;

    /// <summary>Initializes the glTF animation example.</summary>
    public GltfAnimationPage()
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
        if (e.Session is not null)
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

            // Page disappearance is a routine MAUI lifecycle transition. These package-local
            // stages are finite and publish nothing until the current-session checks below, so
            // observe lifecycle cancellation between stages instead of making CopyToAsync or
            // Task.Run throw OperationCanceledException into Visual Studio's async boundary.
            byte[] bytes = await GalleryAssets.ReadBytesAsync(
                "GltfSamples/Fox.glb",
                CancellationToken.None);
            if (!IsLoadCurrent(session, cancellationToken))
            {
                return;
            }

            GltfAsset imported = await Task.Run(
                () => GltfImporter.ImportAsset(bytes, name: "Fox"));
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
                new Vector3(220f, 120f, 220f),
                new Vector3(0f, 38f, -8f));
            ClipPicker.ItemsSource = imported.Animations
                .Select((clip, index) => clip.Name ?? $"Clip {index + 1}")
                .ToArray();
            ClipPicker.SelectedIndex = 0;
            StatusLabel.Text = "Fox animation ready";
            DetailsLabel.Text = $"Imported clips: {imported.Animations.Count}";
            StartPlayback();
        }
        catch (Exception exception)
        {
            if (!IsLoadCurrent(session, cancellationToken))
            {
                return;
            }
            StatusLabel.Text = "glTF animation failed";
            DetailsLabel.Text = exception.ToString();
        }
    }

    private bool IsLoadCurrent(
        IPresentationSurfaceSession session,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        ReferenceEquals(SurfaceView.PresentationSession, session);

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        if (presenter is GltfScenePresenter activePresenter && ClipPicker.SelectedIndex >= 0)
        {
            activePresenter.Render(
                e.Target,
                e.Width,
                e.Height,
                ClipPicker.SelectedIndex,
                (float)clock.Elapsed.TotalSeconds);
        }
        else
        {
            GalleryDraw.Clear(e, "glTF animation loading frame");
        }
    }

    private async Task RunFramesAsync(CancellationToken cancellationToken, int version)
    {
        while (!cancellationToken.IsCancellationRequested &&
            version == playbackVersion &&
            PlaySwitch.IsToggled)
        {
            if (asset is not null)
            {
                SurfaceView.InvalidateSurface();
            }
            await Task.Delay(16);
        }
    }

    private void OnPlayChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (e.Value && asset is not null)
        {
            StartPlayback();
        }
        else
        {
            playbackVersion++;
            SurfaceView.InvalidateSurface();
        }
    }

    private void StartPlayback()
    {
        int version = ++playbackVersion;
        _ = RunFramesAsync(lifetime.Token, version);
    }

    private void OnClipChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        clock.Restart();
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
        else if (presenter is not null)
        {
            if (PlaySwitch.IsToggled)
            {
                StartPlayback();
            }
            else
            {
                SurfaceView.InvalidateSurface();
            }
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        lifetime.Cancel();
        playbackVersion++;
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

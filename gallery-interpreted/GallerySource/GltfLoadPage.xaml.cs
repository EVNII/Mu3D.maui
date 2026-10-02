using System.Numerics;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates bounded asynchronous loading of glTF with external resources.</summary>
public partial class GltfLoadPage : ContentPage
{
    private const int MaximumSourceByteCount = 64 * 1024;
    private const int MaximumExternalResourceByteCount = 32 * 1024;
    private const int MaximumTotalExternalResourceByteCount = 64 * 1024;
    private const int MaximumRetainedSourceByteCount =
        MaximumSourceByteCount + MaximumTotalExternalResourceByteCount;

    private readonly GltfExternalResourceCache externalResourceCache = new(
        MaximumTotalExternalResourceByteCount);
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private GltfScenePresenter? presenter;
    private CancellationTokenSource? loadCancellation;
    private Task? loadingTask;
    private int loadVersion;
    private bool isPageVisible;

    /// <summary>Initializes the glTF loading example.</summary>
    public GltfLoadPage()
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
        InvalidateCurrentLoad();
        DisposePresenter();
        if (isPageVisible && e.Session is not null)
        {
            StartLoad(e.Session);
        }
    }

    private async Task LoadAsync(
        IPresentationSurfaceSession session,
        int version,
        CancellationTokenSource operation,
        bool retainSources)
    {
        bool gateEntered = false;
        try
        {
            await loadGate.WaitAsync();
            gateEntered = true;
            if (!IsCurrent(session, version, operation))
            {
                return;
            }

            int externalResourceCount = 0;
            long externalResourceByteCount = 0;
            async ValueTask<Stream> OpenExternalResourceAsync(
                string uri,
                CancellationToken resourceCancellationToken)
            {
                string resourcePath = uri switch
                {
                    "TextureTransformTest.bin" =>
                        "GltfSamples/TextureTransformTest/TextureTransformTest.bin",
                    "Arrow.png" => "GltfSamples/TextureTransformTest/Arrow.png",
                    "Correct.png" => "GltfSamples/TextureTransformTest/Correct.png",
                    "Error.png" => "GltfSamples/TextureTransformTest/Error.png",
                    "NotSupported.png" => "GltfSamples/TextureTransformTest/NotSupported.png",
                    "UV.png" => "GltfSamples/TextureTransformTest/UV.png",
                    _ => throw new InvalidDataException(
                        $"The Gallery does not allow external glTF URI '{uri}'."),
                };
                resourceCancellationToken.ThrowIfCancellationRequested();
                Stream resolved = await FileSystem.Current.OpenAppPackageFileAsync(resourcePath);
                try
                {
                    resourceCancellationToken.ThrowIfCancellationRequested();
                    externalResourceCount++;
                    if (resolved.CanSeek)
                    {
                        externalResourceByteCount += resolved.Length - resolved.Position;
                    }
                    return resolved;
                }
                catch
                {
                    await resolved.DisposeAsync();
                    throw;
                }
            }

            await using Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(
                "GltfSamples/TextureTransformTest/TextureTransformTest.gltf");
            if (!IsCurrent(session, version, operation))
            {
                return;
            }

            long? sourceByteCount = stream.CanSeek ? stream.Length - stream.Position : null;
            // Shell navigation invalidates the page between finite stages; only the explicit
            // Cancel button reaches the public loader's conventional cancellation contract.
            GltfAsset asset = await GltfAssetLoader.LoadAsync(
                stream,
                new GltfAssetLoadOptions
                {
                    MaximumSourceByteCount = MaximumSourceByteCount,
                    MaximumExternalResourceByteCount = MaximumExternalResourceByteCount,
                    MaximumTotalExternalResourceByteCount = MaximumTotalExternalResourceByteCount,
                    MaximumRetainedSourceByteCount = MaximumRetainedSourceByteCount,
                    ExternalResourceResolver = OpenExternalResourceAsync,
                    ExternalResourceCache = externalResourceCache,
                    SourceRetention = retainSources
                        ? GltfSourceRetentionMode.MainSourceAndExternalResources
                        : GltfSourceRetentionMode.None,
                    ImportOptions = new GltfImportOptions { Name = "TextureTransformTest" },
                },
                operation.Token);
            if (!IsCurrent(session, version, operation))
            {
                return;
            }

            var environment = await GalleryEnvironment.LoadStudioAsync(operation.Token);
            if (!IsCurrent(session, version, operation))
            {
                return;
            }

            presenter = new GltfScenePresenter(
                session.Device,
                asset,
                environment,
                new Vector3(0f, 0f, 4f),
                Vector3.Zero);
            int meshCount = asset.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Count();
            GltfSourceArchive? archive = asset.SourceArchive;
            StatusLabel.Text = "External glTF rendered";
            DetailsLabel.Text =
                $"Meshes: {meshCount}\n" +
                $"Animations: {asset.Animations.Count}\n" +
                $"Shader variants: {asset.MaterialShaderVariants.Variants.Count}\n" +
                $"Source: {(sourceByteCount.HasValue ? $"{sourceByteCount.Value:N0} bytes" : "streamed")}" +
                $" / {MaximumSourceByteCount / 1024} KiB limit\n" +
                $"Resolver this load: {externalResourceCount} resources, " +
                $"{externalResourceByteCount:N0} bytes\n" +
                $"External cache: {externalResourceCache.Count} entries, " +
                $"{externalResourceCache.TotalByteCount:N0} bytes / " +
                $"{MaximumTotalExternalResourceByteCount / 1024} KiB capacity\n" +
                (archive is null
                    ? "Source archive: disabled (temporary encoded buffers released)"
                    : $"Source archive: {archive.TotalByteCount:N0} bytes; " +
                      $"{archive.ExternalResources.Count} external resources");
            SurfaceView.InvalidateSurface();
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested)
        {
            if (IsActiveOperation(session, version, operation))
            {
                StatusLabel.Text = "glTF loading cancelled";
                DetailsLabel.Text = "Press Reload to start a new bounded asynchronous load.";
            }
        }
        catch (Exception exception)
        {
            if (IsCurrent(session, version, operation))
            {
                StatusLabel.Text = "glTF loading failed";
                DetailsLabel.Text = exception.ToString();
            }
        }
        finally
        {
            if (gateEntered)
            {
                loadGate.Release();
            }
            if (version == loadVersion && ReferenceEquals(loadCancellation, operation))
            {
                if (operation.IsCancellationRequested &&
                    IsActiveOperation(session, version, operation))
                {
                    StatusLabel.Text = "glTF loading cancelled";
                    DetailsLabel.Text = "Press Reload to start a new bounded asynchronous load.";
                }
                loadCancellation = null;
                CancelButton.IsEnabled = false;
                ClearCacheButton.IsEnabled = true;
                RetainSourcesSwitch.IsEnabled = true;
            }
            operation.Dispose();
        }
    }

    private void StartLoad(IPresentationSurfaceSession session)
    {
        AbandonCurrentLoad();
        DisposePresenter();
        int version = ++loadVersion;
        CancellationTokenSource operation = new();
        loadCancellation = operation;
        bool retainSources = RetainSourcesSwitch.IsToggled;
        StatusLabel.Text = "Loading TextureTransformTest.gltf…";
        DetailsLabel.Text =
            $"Async .gltf + external BIN/PNG; {MaximumExternalResourceByteCount / 1024} KiB per resource, " +
            $"{MaximumTotalExternalResourceByteCount / 1024} KiB total. " +
            $"Cache before load: {externalResourceCache.Count} entries.";
        CancelButton.IsEnabled = true;
        ClearCacheButton.IsEnabled = false;
        RetainSourcesSwitch.IsEnabled = false;
        loadingTask = LoadAsync(session, version, operation, retainSources);
    }

    private bool IsCurrent(
        IPresentationSurfaceSession session,
        int version,
        CancellationTokenSource operation) =>
        !operation.IsCancellationRequested &&
        IsActiveOperation(session, version, operation);

    private bool IsActiveOperation(
        IPresentationSurfaceSession session,
        int version,
        CancellationTokenSource operation) =>
        isPageVisible &&
        version == loadVersion &&
        ReferenceEquals(loadCancellation, operation) &&
        ReferenceEquals(SurfaceView.PresentationSession, session);

    private void OnReloadClicked(object? sender, EventArgs e)
    {
        _ = sender;
        if (SurfaceView.PresentationSession is IPresentationSurfaceSession session)
        {
            StartLoad(session);
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        _ = sender;
        if (loadCancellation is not null)
        {
            StatusLabel.Text = "Cancelling glTF load…";
            loadCancellation.Cancel();
        }
    }

    private void OnClearCacheClicked(object? sender, EventArgs e)
    {
        _ = sender;
        externalResourceCache.Clear();
        StatusLabel.Text = "External glTF cache cleared";
        DetailsLabel.Text =
            "Cache: 0 entries, 0 bytes. Press Reload to resolve the six external resources again.";
    }

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        _ = sender;
        if (presenter is GltfScenePresenter activePresenter)
        {
            activePresenter.Render(e.Target, e.Width, e.Height);
        }
        else
        {
            GalleryDraw.Clear(e, "glTF loading frame");
        }
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
        if (presenter is null &&
            SurfaceView.PresentationSession is IPresentationSurfaceSession session &&
            (loadingTask is null || loadingTask.IsCompleted))
        {
            StartLoad(session);
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        isPageVisible = false;
        InvalidateCurrentLoad();
        base.OnDisappearing();
    }

    private void InvalidateCurrentLoad()
    {
        loadVersion++;
        AbandonCurrentLoad();
    }

    private void AbandonCurrentLoad()
    {
        loadCancellation = null;
        loadingTask = null;
    }

    private void DisposePresenter()
    {
        presenter?.Dispose();
        presenter = null;
    }
}

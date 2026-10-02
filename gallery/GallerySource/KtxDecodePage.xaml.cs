using System.Diagnostics;
using Mu3D.Color;
using Mu3D.Native.Ktx;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates application-level KTX2 image decoding.</summary>
public partial class KtxDecodePage : ContentPage
{
    private const int MaximumSourceByteCount = 128 * 1024;
    private const int MaximumOutputByteCount = 32 * 1024 * 1024;
    private const string SourceKey = "package/KtxSamples/color_grid_basis.ktx2";

    private readonly Ktx2SourceCache sourceCache = new(MaximumSourceByteCount);
    private CancellationTokenSource lifetime = new();
    private CancellationTokenSource? loadCancellation;
    private Ktx2TextureAsset? loadedAsset;
    private int loadVersion;

    /// <summary>Initializes the KTX decode example.</summary>
    public KtxDecodePage() => InitializeComponent();

    private void OnDecodeClicked(object? sender, EventArgs e)
    {
        _ = sender;
        StartDecode();
    }

    private void StartDecode()
    {
        CancelCurrentLoad();
        int version = ++loadVersion;
        CancellationTokenSource operation = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.Token);
        loadCancellation = operation;
        bool retainEncodedSource = RetainSourceSwitch.IsToggled;
        StatusLabel.Text = "Loading bundled KTX2…";
        DetailsLabel.Text =
            $"Cache before load: {sourceCache.Count} entries, " +
            $"{sourceCache.TotalByteCount:N0} bytes.";
        SetLoadingControls(true);
        _ = DecodeAsync(version, operation, retainEncodedSource);
    }

    private async Task DecodeAsync(
        int version,
        CancellationTokenSource operation,
        bool retainEncodedSource)
    {
        CancellationToken cancellationToken = operation.Token;
        try
        {
            int resolverOpenCount = 0;
            async ValueTask<Stream> OpenSourceAsync(CancellationToken sourceCancellationToken)
            {
                sourceCancellationToken.ThrowIfCancellationRequested();
                Stream stream = await FileSystem.Current.OpenAppPackageFileAsync(
                    "KtxSamples/color_grid_basis.ktx2");
                try
                {
                    sourceCancellationToken.ThrowIfCancellationRequested();
                    resolverOpenCount++;
                    return stream;
                }
                catch
                {
                    await stream.DisposeAsync();
                    throw;
                }
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            Ktx2TextureAsset asset = await Ktx2TextureLoader.LoadAsync(
                SourceKey,
                OpenSourceAsync,
                sourceCache,
                new Ktx2TextureLoadOptions
                {
                    MaximumSourceByteCount = MaximumSourceByteCount,
                    MaximumOutputByteCount = MaximumOutputByteCount,
                    RetainEncodedSource = retainEncodedSource,
                    Name = "Khronos ETC1S color grid",
                },
                cancellationToken);
            stopwatch.Stop();
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrent(version, operation))
            {
                return;
            }
            loadedAsset = asset;
            StatusLabel.Text = "Decoded";
            ShowAssetDetails(asset, resolverOpenCount, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(version, operation))
            {
                StatusLabel.Text = "KTX2 loading cancelled";
                DetailsLabel.Text = "Press Decode / Reload to start another bounded load.";
            }
        }
        catch (Exception exception)
        {
            if (IsCurrent(version, operation))
            {
                StatusLabel.Text = "Decode failed";
                DetailsLabel.Text = exception.ToString();
            }
        }
        finally
        {
            if (version == loadVersion && ReferenceEquals(loadCancellation, operation))
            {
                loadCancellation = null;
                SetLoadingControls(false);
            }
            operation.Dispose();
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        _ = sender;
        if (loadCancellation is not null)
        {
            StatusLabel.Text = "Cancelling KTX2 load…";
            loadCancellation.Cancel();
        }
    }

    private void OnClearCacheClicked(object? sender, EventArgs e)
    {
        _ = sender;
        sourceCache.Clear();
        StatusLabel.Text = loadedAsset is null
            ? "KTX2 source cache cleared"
            : "Source cache cleared; loaded texture remains valid";
        DetailsLabel.Text = loadedAsset is null
            ? "Cache: 0 entries, 0 bytes."
            : $"Loaded output: {loadedAsset.OutputByteCount:N0} bytes\n" +
              $"Asset-retained source: {loadedAsset.RetainedEncodedSource.Length:N0} bytes\n" +
              "Cache: 0 entries, 0 bytes. Reload will reopen the package source.";
    }

    private void ShowAssetDetails(
        Ktx2TextureAsset asset,
        int resolverOpenCount,
        TimeSpan elapsed)
    {
        LinearRgbaImage image = asset.DecodedColorImage!;
        DetailsLabel.Text =
            $"{asset.Width} x {asset.Height} pixels\n" +
            $"Color space: {image.ColorSpace.Name}\n" +
            $"Representation: {asset.Representation}\n" +
            $"FP32 output: {asset.OutputByteCount:N0} bytes / " +
            $"{MaximumOutputByteCount / (1024 * 1024)} MiB limit\n" +
            $"Resolver this load: {resolverOpenCount}\n" +
            $"Source cache: {sourceCache.Count} entry, " +
            $"{sourceCache.TotalByteCount:N0} bytes / " +
            $"{MaximumSourceByteCount / 1024} KiB capacity\n" +
            $"Asset-retained source: {asset.RetainedEncodedSource.Length:N0} bytes\n" +
            $"Load + decode: {elapsed.TotalMilliseconds:F1} ms";
    }

    private bool IsCurrent(int version, CancellationTokenSource operation) =>
        version == loadVersion && ReferenceEquals(loadCancellation, operation);

    private void SetLoadingControls(bool loading)
    {
        DecodeButton.IsEnabled = !loading;
        CancelButton.IsEnabled = loading;
        ClearCacheButton.IsEnabled = !loading;
        RetainSourceSwitch.IsEnabled = !loading;
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (lifetime.IsCancellationRequested)
        {
            lifetime.Dispose();
            lifetime = new CancellationTokenSource();
            SetLoadingControls(false);
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        lifetime.Cancel();
        CancelCurrentLoad();
        loadVersion++;
        loadedAsset = null;
        base.OnDisappearing();
    }

    private void CancelCurrentLoad()
    {
        loadCancellation?.Cancel();
        loadCancellation = null;
    }
}

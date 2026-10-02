using System.Collections.ObjectModel;
using static Mu3D.GalleryApp.Pages.ProductFeedPolicy;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>
/// Identifies the fixed-extent Product Feed list for its scoped Apple handler selection.
/// </summary>
public sealed class ProductFeedCollectionView : CollectionView
{
}

/// <summary>Demonstrates MAUI-owned HDR Surfaces sharing one Mu3D device and renderer.</summary>
public partial class ProductFeedPage : ContentPage
{
    private readonly ProductAssetProvider assetProvider;
    private readonly HashSet<int> loadingIndices = new(MaximumDemoProductCount);
    private readonly Dictionary<int, CancellationTokenSource> loadCancellations =
        new(MaximumDemoProductCount);
    private readonly HashSet<int> promotedIndices = new(MaximumDemoProductCount);
    private readonly HashSet<int> visibleIndices = new(MaximumDemoProductCount);
    private readonly HashSet<int> preloadIndices = new(MaximumDemoProductCount);
    private readonly HashSet<int> warmIndices = new(MaximumDemoProductCount);
    private readonly HashSet<int> nextVisibleIndices = new(MaximumDemoProductCount);
    private readonly HashSet<int> nextPreloadIndices = new(MaximumDemoProductCount);
    private readonly HashSet<int> nextWarmIndices = new(MaximumDemoProductCount);
    private readonly List<int> indexScratch = new(MaximumDemoProductCount);
    private readonly List<Scene> warmSceneScratch = new(MaximumDemoProductCount);
    private readonly PriorityQueue<int, LoadPriority> loadPriorityQueue =
        new(MaximumDemoProductCount);
    private readonly double[] absoluteCardDistances = new double[MaximumDemoProductCount];
    private readonly double[] signedCardDistances = new double[MaximumDemoProductCount];
    private readonly Dictionary<SceneViewProxy, int> realizedProxyIndices =
        new(MaximumDemoProductCount);
    private readonly HashSet<int> presentedIndices = new(MaximumDemoProductCount);
    private readonly Dictionary<int, DateTimeOffset> outsideViewportSince =
        new(MaximumDemoProductCount);
    private readonly IDispatcherTimer layoutUpdateTimer;
    private readonly IDispatcherTimer scrollIdleTimer;
    private readonly IDispatcherTimer retentionTimer;
    private readonly IDispatcherTimer statusUpdateTimer;
    private CancellationTokenSource? pageCancellation;
    private double verticalOffset;
    private int cachedGeometryCount;
    private int cachedMeshCount;
    private int definitionLoadCount;
    private int loadGeneration;
    private long scrollEventCount;
    private long lastScrollEventTick;
    private int statusDispatchPending;
    private int reportedFirstVisibleItemIndex = -1;
    private int reportedLastVisibleItemIndex = -1;
    private int appliedFirstVisibleItemIndex = -1;
    private int appliedLastVisibleItemIndex = -1;
    private string? lastSurfaceError;
    private bool retainedScenesDirty = true;
    private bool isScrollInProgress;
    private bool isFeedActive;
    /// <summary>Initializes the shared-device Surface-proxy product-feed example.</summary>
    public ProductFeedPage()
    {
        InitializeComponent();
        Array.Fill(signedCardDistances, double.NaN);
        layoutUpdateTimer = Dispatcher.CreateTimer();
        layoutUpdateTimer.Interval = TimeSpan.FromMilliseconds(16d);
        layoutUpdateTimer.IsRepeating = false;
        layoutUpdateTimer.Tick += OnLayoutUpdateTimerTick;
        scrollIdleTimer = Dispatcher.CreateTimer();
        scrollIdleTimer.Interval = TimeSpan.FromMilliseconds(50d);
        scrollIdleTimer.IsRepeating = true;
        scrollIdleTimer.Tick += OnScrollIdleTimerTick;
        retentionTimer = Dispatcher.CreateTimer();
        retentionTimer.Interval = TimeSpan.FromMilliseconds(250d);
        retentionTimer.IsRepeating = true;
        retentionTimer.Tick += OnRetentionTimerTick;
        statusUpdateTimer = Dispatcher.CreateTimer();
        statusUpdateTimer.Interval = TimeSpan.FromMilliseconds(200d);
        statusUpdateTimer.IsRepeating = false;
        statusUpdateTimer.Tick += OnStatusUpdateTimerTick;
        assetProvider = new ProductAssetProvider(OnDefinitionLoaded, QueueStatusUpdate);
        Products = [];
        AddProducts(MaximumDemoProductCount);
        ConfigureProductsViewPlatform();
        BindingContext = this;
        UpdateStatus();
    }

    partial void ConfigureProductsViewPlatform();

    /// <summary>Gets lightweight product descriptors used by the native virtualized list.</summary>
    public ObservableCollection<ProductPreview> Products { get; }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        lastSurfaceError = null;
        isScrollInProgress = false;
        scrollIdleTimer.Stop();
        isFeedActive = true;
        outsideViewportSince.Clear();
        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
        pageCancellation = new CancellationTokenSource();
        loadGeneration++;
        loadingIndices.Clear();
        foreach (CancellationTokenSource cancellation in loadCancellations.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        loadCancellations.Clear();
        foreach (ProductPreview product in Products)
        {
            product.CancelLoading();
        }
        ProxyHost.IsRenderingEnabled = true;
        UpdateLayoutState();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        layoutUpdateTimer.Stop();
        scrollIdleTimer.Stop();
        retentionTimer.Stop();
        statusUpdateTimer.Stop();
        isScrollInProgress = false;
        isFeedActive = false;
        loadGeneration++;
        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
        pageCancellation = null;
        loadingIndices.Clear();
        foreach (CancellationTokenSource cancellation in loadCancellations.Values)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        loadCancellations.Clear();
        foreach (ProductPreview product in Products)
        {
            product.CancelLoading();
        }

        // Keep the bounded warm set and one page surface ready for a quick return. Normal MAUI
        // Unloaded/handler teardown still owns final surface/device disposal.
        ApplyRetainedScenes();
        ProxyHost.IsRenderingEnabled = false;
        UpdateStatus();
        base.OnDisappearing();
    }

    private void OnProductsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        _ = sender;
        verticalOffset = e.VerticalOffset;
        lastScrollEventTick = Environment.TickCount64;
        isScrollInProgress = true;
        if (!scrollIdleTimer.IsRunning)
        {
            scrollIdleTimer.Start();
        }
        bool viewportRowsChanged = false;
        if (e.FirstVisibleItemIndex >= 0 &&
            e.LastVisibleItemIndex >= e.FirstVisibleItemIndex)
        {
            reportedFirstVisibleItemIndex = e.FirstVisibleItemIndex;
            reportedLastVisibleItemIndex = e.LastVisibleItemIndex;
            int firstRowIndex = (e.FirstVisibleItemIndex / GridSpan) * GridSpan;
            int lastRowIndex = Math.Min(
                Products.Count - 1,
                (((e.LastVisibleItemIndex / GridSpan) + 1) * GridSpan) - 1);
            viewportRowsChanged =
                firstRowIndex != appliedFirstVisibleItemIndex ||
                lastRowIndex != appliedLastVisibleItemIndex;
        }
        scrollEventCount++;
        if (viewportRowsChanged)
        {
            ScheduleLayoutUpdate();
        }
    }

    private void OnProductsSizeChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (isFeedActive)
        {
            ScheduleLayoutUpdate();
        }
    }

    private void ScheduleLayoutUpdate()
    {
        if (isFeedActive && !layoutUpdateTimer.IsRunning)
        {
            layoutUpdateTimer.Start();
        }
    }

    private void OnLayoutUpdateTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        layoutUpdateTimer.Stop();
        UpdateLayoutState();
    }

    private void OnScrollIdleTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (Environment.TickCount64 - lastScrollEventTick < ScrollIdleDelayMilliseconds)
        {
            return;
        }

        scrollIdleTimer.Stop();
        isScrollInProgress = false;
        UpdateLayoutState();
        UpdateStatus();
    }

    private void OnRetentionTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (!isFeedActive || outsideViewportSince.Count == 0)
        {
            retentionTimer.Stop();
            return;
        }
        UpdateLayoutState();
    }

    private void AddProducts(int count)
    {
        for (int offset = 0; offset < count; offset++)
        {
            int index = Products.Count;
            ProductModel model = ProductModels.All[index % ProductModels.All.Length];
            Products.Add(new ProductPreview(
                $"{model.DisplayName} #{index + 1}",
                $"${49 + ((index * 37) % 450)}",
                index,
                model));
        }
    }

    private void UpdateLayoutState()
    {
        if (!isFeedActive || Products.Count == 0 || ProductsView.Height <= 0d)
        {
            QueueStatusUpdate();
            return;
        }

        UpdateRanges();
        ScheduleLoads();
        UpdateViewportSnapshot();
        QueueStatusUpdate();
    }

    private void UpdateRanges()
    {
        double rowExtent = ItemHeight + VerticalItemSpacing;
        int rowCount = (Products.Count + GridSpan - 1) / GridSpan;
        bool hasReportedVisibleRange =
            reportedFirstVisibleItemIndex >= 0 &&
            reportedFirstVisibleItemIndex < Products.Count &&
            reportedLastVisibleItemIndex >= reportedFirstVisibleItemIndex;
        int firstRow;
        int lastRow;
        if (hasReportedVisibleRange)
        {
            // The native CollectionView layout owns virtualization and is the authority for which
            // items intersect the viewport. VerticalOffset is retained only as diagnostics because
            // its Mac Catalyst value is not suitable for reconstructing GridItemsLayout rows.
            firstRow = Math.Clamp(
                reportedFirstVisibleItemIndex / GridSpan,
                0,
                rowCount - 1);
            lastRow = Math.Clamp(
                Math.Min(reportedLastVisibleItemIndex, Products.Count - 1) / GridSpan,
                firstRow,
                rowCount - 1);
        }
        else
        {
            double fallbackTop = Math.Max(0d, verticalOffset);
            firstRow = Math.Clamp(
                (int)Math.Floor(fallbackTop / rowExtent),
                0,
                rowCount - 1);
            lastRow = Math.Clamp(
                (int)Math.Floor(
                    Math.Max(fallbackTop, fallbackTop + ProductsView.Height - 1d) / rowExtent),
                firstRow,
                rowCount - 1);
        }
        int firstVisible = firstRow * GridSpan;
        int lastVisible = Math.Min(Products.Count - 1, ((lastRow + 1) * GridSpan) - 1);
        appliedFirstVisibleItemIndex = firstVisible;
        appliedLastVisibleItemIndex = lastVisible;
        nextVisibleIndices.Clear();
        for (int index = firstVisible; index <= lastVisible; index++)
        {
            nextVisibleIndices.Add(index);
        }
        double visibleTop = firstRow * rowExtent;
        double viewportBottom = (lastRow * rowExtent) + ItemHeight;
        double preloadDistance = CentimetersToLogicalUnits(PreloadDistanceCentimeters);
        double suspendDistance = CentimetersToLogicalUnits(SuspendDistanceCentimeters);
        double releaseDistance = CentimetersToLogicalUnits(ReleaseDistanceCentimeters);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        nextPreloadIndices.Clear();
        for (int index = 0; index < Products.Count; index++)
        {
            double signedDistance = GetSignedDistanceFromViewport(
                index,
                visibleTop,
                viewportBottom,
                rowExtent);
            signedCardDistances[index] = signedDistance;
            absoluteCardDistances[index] = Math.Abs(signedDistance);
            if (absoluteCardDistances[index] <= preloadDistance)
            {
                nextPreloadIndices.Add(index);
            }
        }

        foreach (int index in nextVisibleIndices)
        {
            outsideViewportSince.Remove(index);
        }
        foreach (int index in warmIndices)
        {
            TrackOutsideViewport(index, now);
        }
        foreach (int index in promotedIndices)
        {
            TrackOutsideViewport(index, now);
        }
        foreach (int index in nextPreloadIndices)
        {
            TrackOutsideViewport(index, now);
        }

        if (!isScrollInProgress)
        {
            CopyIndices(promotedIndices);
            foreach (int index in indexScratch)
            {
                if (nextPreloadIndices.Contains(index) || nextVisibleIndices.Contains(index))
                {
                    continue;
                }
                TimeSpan outsideDuration = now - outsideViewportSince.GetValueOrDefault(index, now);
                if (absoluteCardDistances[index] >= suspendDistance || outsideDuration >= SuspendDelay)
                {
                    promotedIndices.Remove(index);
                    Products[index].MarkWarm();
                }
            }
        }

        nextWarmIndices.Clear();
        nextWarmIndices.UnionWith(warmIndices);
        nextWarmIndices.UnionWith(nextPreloadIndices);
        if (!isScrollInProgress)
        {
            CopyIndices(nextWarmIndices);
            foreach (int index in indexScratch)
            {
                if (nextPreloadIndices.Contains(index) || nextVisibleIndices.Contains(index))
                {
                    continue;
                }
                TimeSpan outsideDuration = now - outsideViewportSince.GetValueOrDefault(index, now);
                if (absoluteCardDistances[index] >= releaseDistance || outsideDuration >= ReleaseDelay)
                {
                    nextWarmIndices.Remove(index);
                    promotedIndices.Remove(index);
                    outsideViewportSince.Remove(index);
                    CancelProductLoad(index);
                    Products[index].ReleaseContent();
                }
            }
        }

        if (!warmIndices.SetEquals(nextWarmIndices))
        {
            retainedScenesDirty = true;
        }
        visibleIndices.Clear();
        visibleIndices.UnionWith(nextVisibleIndices);
        preloadIndices.Clear();
        preloadIndices.UnionWith(nextPreloadIndices);
        warmIndices.Clear();
        warmIndices.UnionWith(nextWarmIndices);
        if (outsideViewportSince.Count != 0 && !retentionTimer.IsRunning)
        {
            retentionTimer.Start();
        }
    }

    private void TrackOutsideViewport(int index, DateTimeOffset now)
    {
        if (!nextVisibleIndices.Contains(index))
        {
            outsideViewportSince.TryAdd(index, now);
        }
    }

    private void CopyIndices(HashSet<int> source)
    {
        indexScratch.Clear();
        foreach (int index in source)
        {
            indexScratch.Add(index);
        }
    }

    private static double GetSignedDistanceFromViewport(
        int itemIndex,
        double viewportTop,
        double viewportBottom,
        double rowExtent)
    {
        int row = itemIndex / GridSpan;
        double itemTop = row * rowExtent;
        double itemBottom = itemTop + ItemHeight;
        if (itemBottom < viewportTop)
        {
            return itemBottom - viewportTop;
        }
        return itemTop > viewportBottom ? itemTop - viewportBottom : 0d;
    }

    private static double CentimetersToLogicalUnits(double centimeters)
    {
#if ANDROID
        const double logicalUnitsPerInch = 160d;
#elif IOS
        const double logicalUnitsPerInch = 163d;
#elif MACCATALYST
        const double logicalUnitsPerInch = 72d;
#else
        const double logicalUnitsPerInch = 96d;
#endif
        return centimeters * logicalUnitsPerInch / 2.54d;
    }

    private void ScheduleLoads()
    {
        CancellationTokenSource? cancellation = pageCancellation;
        if (cancellation is null)
        {
            return;
        }

        int availableSlots = MaximumConcurrentLoads - loadingIndices.Count;
        if (availableSlots <= 0)
        {
            return;
        }

        loadPriorityQueue.Clear();
        for (int index = 0; index < Products.Count; index++)
        {
            if ((!visibleIndices.Contains(index) && !preloadIndices.Contains(index)) ||
                Products[index].Content is not null ||
                loadingIndices.Contains(index))
            {
                continue;
            }

            loadPriorityQueue.Enqueue(
                index,
                new LoadPriority(
                    visibleIndices.Contains(index) ? 0 : 1,
                    absoluteCardDistances[index],
                    index));
        }

        while (availableSlots > 0 && loadPriorityQueue.TryDequeue(out int index, out _))
        {
            ProductPreview product = Products[index];
            loadingIndices.Add(index);
            product.MarkLoading(visibleIndices.Contains(index));
            CancellationTokenSource itemCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
            loadCancellations.Add(index, itemCancellation);
            _ = LoadProductAsync(index, loadGeneration, itemCancellation);
            availableSlots--;
        }
    }

    private void CancelProductLoad(int index)
    {
        if (loadCancellations.TryGetValue(index, out CancellationTokenSource? cancellation))
        {
            cancellation.Cancel();
        }
    }

    private async Task LoadProductAsync(
        int index,
        int generation,
        CancellationTokenSource itemCancellation)
    {
        CancellationToken cancellationToken = itemCancellation.Token;
        ProductSceneContent? content = null;
        Exception? failure = null;
        try
        {
            ProductPreview product = Products[index];
            content = await assetProvider.GetContentAsync(
                product.Index,
                product.Name,
                product.Model,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        Dispatcher.Dispatch(() => CompleteProductLoad(
            index,
            generation,
            itemCancellation,
            content,
            failure));
    }

    private void CompleteProductLoad(
        int index,
        int generation,
        CancellationTokenSource itemCancellation,
        ProductSceneContent? content,
        Exception? failure)
    {
        if (loadCancellations.TryGetValue(index, out CancellationTokenSource? currentCancellation) &&
            ReferenceEquals(currentCancellation, itemCancellation))
        {
            _ = loadCancellations.Remove(index);
        }
        itemCancellation.Dispose();
        if (generation != loadGeneration)
        {
            return;
        }
        loadingIndices.Remove(index);
        ProductPreview product = Products[index];
        if (failure is not null)
        {
            product.MarkFailed(failure);
        }
        else if (content is not null && warmIndices.Contains(index))
        {
            product.SetContent(content);
            retainedScenesDirty = true;
        }
        else
        {
            product.CancelLoading();
        }

        if (isFeedActive)
        {
            ScheduleLoads();
            UpdateViewportSnapshot();
            QueueStatusUpdate();
        }
    }

    private void UpdateViewportSnapshot()
    {
        for (int index = 0; index < Products.Count; index++)
        {
            if (!preloadIndices.Contains(index))
            {
                continue;
            }
            ProductPreview product = Products[index];
            ProductSceneContent? content = product.Content;
            if (content is null)
            {
                product.MarkLoading(loadingIndices.Contains(index));
                continue;
            }

            promotedIndices.Add(index);
            product.MarkLive();
        }

        // Native Surface proxies are positioned entirely by MAUI. The page retains only loading,
        // promotion and GPU-warm policy.
        if (retainedScenesDirty)
        {
            ApplyRetainedScenes();
            retainedScenesDirty = false;
        }
        QueueVisibleAnimatedFrames();
    }

    private void ApplyRetainedScenes()
    {
        warmSceneScratch.Clear();
        for (int index = 0; index < Products.Count; index++)
        {
            if (warmIndices.Contains(index) && Products[index].Content?.Scene is Scene scene)
            {
                warmSceneScratch.Add(scene);
            }
        }
        ProxyHost.SetRetainedScenes(warmSceneScratch);
    }

    private void OnDefinitionLoaded()
    {
        Interlocked.Increment(ref definitionLoadCount);
        QueueStatusUpdate();
    }

    private void OnProxyHostSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        RecordSurfaceError(e.Exception);
    }

    private void OnProxySurfaceError(object? sender, SceneViewProxySurfaceErrorEventArgs e)
    {
        _ = sender;
        RecordSurfaceError(e.Exception);
    }

    private void RecordSurfaceError(Exception exception)
    {
        lastSurfaceError =
            $"{exception.GetType().Name} 0x{exception.HResult:X8}: {exception.Message}";
        UpdateStatus();
    }

    private void QueueStatusUpdate()
    {
        if (!isFeedActive || Interlocked.Exchange(ref statusDispatchPending, 1) != 0)
        {
            return;
        }
        Dispatcher.Dispatch(() =>
        {
            Volatile.Write(ref statusDispatchPending, 0);
            if (!statusUpdateTimer.IsRunning)
            {
                statusUpdateTimer.Start();
            }
        });
    }

    private void OnStatusUpdateTimerTick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        statusUpdateTimer.Stop();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (isScrollInProgress)
        {
            return;
        }

        StatusLabel.Text =
            $"{OutputMode}; one shared device; " +
            $"items {Products.Count}/{MaximumDemoProductCount}; " +
            $"visible {visibleIndices.Count}, live {promotedIndices.Count}, proxies {ProxyHost.ProxyCount}, " +
            $"scroll events {scrollEventCount}, offset {verticalOffset:0}, " +
            $"native range {FormatReportedVisibleRange()}; " +
            GetSurfaceSummary() + ", " +
            $"pending {ProxyHost.PendingFrameCount}; " +
            $"preload <{PreloadDistanceCentimeters:0.#} cm {preloadIndices.Count}, " +
            $"suspend >{SuspendDistanceCentimeters:0.#} cm/{SuspendDelay.TotalSeconds:0.#} s, " +
            $"release >{ReleaseDistanceCentimeters:0.#} cm/{ReleaseDelay.TotalSeconds:0.#} s; " +
            $"warm {warmIndices.Count}, loading {loadingIndices.Count}/{MaximumConcurrentLoads}; " +
            $"definitions {assetProvider.DefinitionCacheCount}/6, managed scenes {assetProvider.WarmContentCount}/16, " +
            $"GPU geometry {cachedGeometryCount}, meshes {cachedMeshCount}; imports {definitionLoadCount}." +
            (lastSurfaceError is null ? string.Empty : $" Surface error: {lastSurfaceError}");
        UpdateDiagnostics();
    }

    private string FormatReportedVisibleRange() =>
        reportedFirstVisibleItemIndex < 0 || reportedLastVisibleItemIndex < reportedFirstVisibleItemIndex
            ? "pending"
            : $"#{reportedFirstVisibleItemIndex + 1}-#{reportedLastVisibleItemIndex + 1}";

    private readonly record struct LoadPriority(
        int VisibilityRank,
        double Distance,
        int Index) : IComparable<LoadPriority>
    {
        public int CompareTo(LoadPriority other)
        {
            int comparison = VisibilityRank.CompareTo(other.VisibilityRank);
            if (comparison != 0)
            {
                return comparison;
            }
            comparison = Distance.CompareTo(other.Distance);
            return comparison != 0 ? comparison : Index.CompareTo(other.Index);
        }
    }

}

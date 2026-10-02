using Mu3D.Gallery.Controls;
using static Mu3D.Gallery.Pages.ProceduralFeedExample;
using System.Collections.ObjectModel;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;

namespace Mu3D.Gallery.Pages;

/// <summary>
/// Demonstrates a two-column product feed where each card hosts a live 3D preview through
/// <see cref="SceneViewProxy"/> instances sharing one <see cref="SceneViewProxyHost"/> device.
/// </summary>
/// <remarks>
/// Reduced port of the GalleryApp product feed: procedural sphere/cone scenes replace the glTF
/// assets, and the scroll lifecycle collapses to three tiers — live rows render, nearby rows keep
/// preloaded content suspended, and distant rows release their scene and surface.
/// </remarks>
public partial class FeedPage : ContentPage, IGalleryPageActivation
{
    private bool navigationActive;

    /// <summary>Initializes the feed with procedurally generated product cards.</summary>
    public FeedPage()
    {
        InitializeComponent();
        Products = [];
        for (int index = 0; index < ProductCount; index++)
        {
            Products.Add(new FeedProduct(index));
        }
        BindingContext = this;
    }

    /// <summary>Gets the product cards shown by the two-column list.</summary>
    public ObservableCollection<FeedProduct> Products { get; }

    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (navigationActive == active) return;
        navigationActive = active;
        if (active) OnPageLoaded(this, EventArgs.Empty);
        else OnPageUnloaded(this, EventArgs.Empty);
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        ProxyHost.IsRenderingEnabled = true;
        ApplyLifecycle(0, Math.Min(ProductCount, 3 * GridSpan) - 1);
        // Proxy registration, the bootstrap device and frame pumping are asynchronous; refresh the
        // diagnostics line once the initial pipeline has settled so a stuck state is visible.
        Dispatcher.DispatchDelayed(TimeSpan.FromSeconds(1), () =>
        {
            if (navigationActive)
            {
                RefreshStatus();
            }
        });
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        ProxyHost.IsRenderingEnabled = false;
        foreach (FeedProduct product in Products)
        {
            product.ReleaseContent();
        }
    }

    private void OnProductsScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        _ = sender;
        if (!navigationActive) return;
        if (e.FirstVisibleItemIndex < 0 || e.LastVisibleItemIndex < e.FirstVisibleItemIndex)
        {
            return;
        }
        ApplyLifecycle(e.FirstVisibleItemIndex, e.LastVisibleItemIndex);
    }

    private void ApplyLifecycle(int firstVisible, int lastVisible)
    {
        (lastLive, lastWarm) = ProceduralFeedExample.ApplyLifecycle(Products, firstVisible, lastVisible);
        RefreshStatus();
    }

    private int lastLive;
    private int lastWarm;

    private void RefreshStatus()
    {
        StatusLabel.Text = $"{ProxyHost.ProxyCount} loaded cells · " +
            $"{ProxyHost.ActiveSurfaceCount} active surfaces · {lastLive} live · {lastWarm} preloaded · " +
            $"shared device {(ProxyHost.HasSharedDevice ? "ready" : "PENDING")} · {ProxyHost.PendingFrameCount} queued";
    }

    private void OnProxyFramePresented(object? sender, SceneViewProxyFramePresentedEventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshStatus();
    }

    private void OnProxySurfaceError(object? sender, SceneViewProxySurfaceErrorEventArgs e)
    {
        _ = sender;
        string message = $"Proxy surface failed: {e.Exception.Message}";
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }

    private void OnProxyHostSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        string message = $"Presentation {e.Operation} failed: {e.Exception.Message}";
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}

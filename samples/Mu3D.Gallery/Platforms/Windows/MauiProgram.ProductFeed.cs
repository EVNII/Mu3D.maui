using Microsoft.Maui.Controls.Handlers.Items;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mu3D.GalleryApp.Pages;
using NativeElement = Microsoft.UI.Xaml.FrameworkElement;
using NativeRect = Windows.Foundation.Rect;

namespace Mu3D.Gallery;

public static partial class MauiProgram
{
    static partial void ConfigurePlatformHandlers(MauiAppBuilder builder) =>
        builder.ConfigureMauiHandlers(handlers =>
        {
            handlers.AddHandler<ProductFeedCollectionView, ProductFeedCollectionViewHandler>();
            handlers.AddHandler<Pages.FeedCollectionView, ProductFeedCollectionViewHandler>();
        });
}

// MAUI 10.0.100 counts realized presenters for a grid rather than resolving their item indexes.
// Virtualization omits earlier containers, so that count can promote offscreen products instead.
internal sealed class ProductFeedCollectionViewHandler : CollectionViewHandler
{
    private ScrollViewer? scrollViewer;

    public ProductFeedCollectionViewHandler() { }

    protected override void ConnectHandler(ListViewBase platformView)
    {
        platformView.Loaded += OnListLoaded;
        platformView.Unloaded += OnListUnloaded;
        WatchTemplateLayout(platformView);
        base.ConnectHandler(platformView);
        BindLoadedScrollViewer(platformView);
    }

    private void OnListLoaded(object sender, RoutedEventArgs args)
    {
        ListViewBase list = (ListViewBase)sender;
        WatchTemplateLayout(list);
        BindLoadedScrollViewer(list);
    }

    private void OnListUnloaded(object sender, RoutedEventArgs args)
    {
        ((ListViewBase)sender).LayoutUpdated -= OnTemplateLayoutUpdated;
    }

    private void WatchTemplateLayout(ListViewBase list)
    {
        list.LayoutUpdated -= OnTemplateLayoutUpdated;
        list.LayoutUpdated += OnTemplateLayoutUpdated;
    }

    private void OnTemplateLayoutUpdated(object? sender, object args)
        => BindLoadedScrollViewer(PlatformView, publishRange: true);

    private void BindLoadedScrollViewer(ListViewBase list, bool publishRange = false)
    {
        // Native Loaded can precede template materialization on first navigation. MAUI's
        // Loaded-only search can then miss the viewer until the page is entered again.
        // Retry at layout boundaries only while pending, and stop as soon as it is bound.
        if (!list.IsLoaded || FindTemplateScrollViewer(list) is not { IsLoaded: true } viewer) return;
        OnScrollViewerFound(viewer);
        if (!publishRange) return;
        // Reattachment can reset the native offset before containers are measured. Publish
        // the first measured range too, so a cached page does not retain its previous rows
        // while waiting for the user to move the scrollbar again.
        ItemsViewScrolledEventArgs initialRange = ComputeVisibleIndexes(new()
        {
            HorizontalOffset = viewer.HorizontalOffset,
            VerticalOffset = viewer.VerticalOffset,
        }, ItemsLayoutOrientation.Vertical, advancing: true);
        if (initialRange.FirstVisibleItemIndex < 0) return;
        list.LayoutUpdated -= OnTemplateLayoutUpdated;
        VirtualView.SendScrolled(initialRange);
    }

    private static ScrollViewer? FindTemplateScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindTemplateScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }
        return null;
    }

    protected override void OnScrollViewerFound(ScrollViewer viewer)
    {
        if (!ReferenceEquals(scrollViewer, viewer))
        {
            if (scrollViewer is not null) scrollViewer.SizeChanged -= OnViewportSizeChanged;
            viewer.SizeChanged += OnViewportSizeChanged;
        }
        scrollViewer = viewer;
        base.OnScrollViewerFound(viewer);
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args)
        => WatchTemplateLayout(PlatformView);

    protected override void DisconnectHandler(ListViewBase platformView)
    {
        platformView.Loaded -= OnListLoaded;
        platformView.Unloaded -= OnListUnloaded;
        platformView.LayoutUpdated -= OnTemplateLayoutUpdated;
        if (scrollViewer is not null) scrollViewer.SizeChanged -= OnViewportSizeChanged;
        scrollViewer = null;
        base.DisconnectHandler(platformView);
    }

    protected override ItemsViewScrolledEventArgs ComputeVisibleIndexes(
        ItemsViewScrolledEventArgs args, ItemsLayoutOrientation orientation, bool advancing)
    {
        if (scrollViewer is not { IsLoaded: true, ActualHeight: > 0, ActualWidth: > 0 } viewport ||
            PlatformView.ItemsPanelRoot is not { } panel)
        {
            return base.ComputeVisibleIndexes(args, orientation, advancing);
        }

        ProductFeedVisibleRange range = new();
        // These two sample templates have fixed heights (260 and 240). Reattached native
        // containers initially measure only their labels (44); that is not a ready card.
        double itemHeight = VirtualView is ProductFeedCollectionView ? ProductFeedPolicy.ItemHeight : 240d;
        // Inspect only realized direct item containers; never force realization or scan model views.
        foreach (UIElement child in panel.Children)
        {
            if (child is not NativeElement { Visibility: Microsoft.UI.Xaml.Visibility.Visible, ActualHeight: > 0 } container)
            {
                continue;
            }
            int index = PlatformView.IndexFromContainer(container);
            if (index < 0)
            {
                continue;
            }
            NativeRect bounds = container.TransformToVisual(viewport).TransformBounds(
                new NativeRect(0, 0, container.ActualWidth, container.ActualHeight));
            range.Include(index, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
                viewport.ActualWidth, viewport.ActualHeight, itemHeight);
        }

        // Unmeasured layout is not a new range. Preserve the last valid page range until layout settles.
        args.FirstVisibleItemIndex = range.First;
        args.LastVisibleItemIndex = range.Last;
        double center = (range.First + range.Last) / 2d;
        args.CenterItemIndex = advancing ? (int)Math.Ceiling(center) : (int)Math.Floor(center);
        return args;
    }
}

#if WINDOWS
using NativeView = Microsoft.UI.Xaml.FrameworkElement;
using NativeRoutedEventArgs = Microsoft.UI.Xaml.RoutedEventArgs;

namespace Mu3D.Gallery.Controls;

internal sealed partial class GalleryPageActivation
{
    private readonly List<WindowsVisibilityObserver> windowsObservers = [];

    private void RegisterWindowsPage(Page page)
        => windowsObservers.Add(new WindowsVisibilityObserver(this, page));

    private void DisposeWindowsObservers()
    {
        foreach (WindowsVisibilityObserver observer in windowsObservers) observer.Dispose();
        windowsObservers.Clear();
    }

    private sealed class WindowsVisibilityObserver : IDisposable
    {
        private readonly GalleryPageActivation owner;
        private readonly Page page;
        private NativeView? view;
        private bool? wasShown;

        internal WindowsVisibilityObserver(GalleryPageActivation owner, Page page)
        {
            this.owner = owner;
            this.page = page;
            page.HandlerChanged += OnHandlerChanged;
            Reconnect();
        }

        private void OnHandlerChanged(object? sender, EventArgs e) => Reconnect();

        private void Reconnect()
        {
            Detach();
            view = page.Handler?.PlatformView as NativeView;
            if (view is not null)
            {
                view.Loaded += OnNativeLoaded;
                view.Unloaded += OnNativeLoaded;
                view.LayoutUpdated += OnLayoutUpdated;
            }
            EvaluateVisibility();
        }

        private void OnNativeLoaded(object sender, NativeRoutedEventArgs e) => EvaluateVisibility();

        private void OnLayoutUpdated(object? sender, object e) => EvaluateVisibility();

        private void EvaluateVisibility()
        {
            if (owner.disposed ||
                (!ReferenceEquals(page, owner.selectedPage) && !ReferenceEquals(page, owner.activePage))) return;
            bool shown = owner.pageVisibility(page);
            // A native Loaded event can precede MAUI's Loaded notification. Layout retries a
            // still-pending selection even if native visibility itself has not changed.
            if (wasShown == shown &&
                (!shown || !page.IsLoaded || ReferenceEquals(page, owner.activePage))) return;
            wasShown = shown;
            owner.OnNativePageVisibilityChanged(page, shown);
        }

        private void Detach()
        {
            if (view is not null)
            {
                view.Loaded -= OnNativeLoaded;
                view.Unloaded -= OnNativeLoaded;
                view.LayoutUpdated -= OnLayoutUpdated;
            }
            view = null;
            wasShown = null;
        }

        public void Dispose()
        {
            page.HandlerChanged -= OnHandlerChanged;
            Detach();
        }
    }
}
#endif

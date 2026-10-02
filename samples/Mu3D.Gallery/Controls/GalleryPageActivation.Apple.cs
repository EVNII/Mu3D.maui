#if IOS || MACCATALYST
using System.ComponentModel;
using NativeView = UIKit.UIView;
using NativeController = UIKit.UIViewController;

namespace Mu3D.Gallery.Controls;

internal sealed partial class GalleryPageActivation
{
    private readonly List<NativeVisibilityObserver> appleObservers = [];

    private void RegisterApplePage(Page page)
        => appleObservers.Add(new NativeVisibilityObserver(this, page));

    private void DisposeAppleObservers()
    {
        foreach (NativeVisibilityObserver observer in appleObservers) observer.Dispose();
        appleObservers.Clear();
    }

    private static bool IsApplePageShown(Page page)
    {
        if (page.Handler?.PlatformView is not NativeView view || view.Handle == IntPtr.Zero || view.Window is null)
            return false;
        for (NativeView? ancestor = view; ancestor is not null; ancestor = ancestor.Superview)
            if (ancestor.Hidden) return false;

        NativeController? controller = null;
        for (UIKit.UIResponder? responder = view; responder is not null; responder = responder.NextResponder)
        {
            if (responder is not NativeController found) continue;
            controller = found;
            break;
        }
        if (controller is null) return false;
        // Window attachment alone does not distinguish a cached landing controller from the
        // child above it. Check containment selection, without treating a presented modal as
        // another Gallery destination.
        for (; controller is not null; controller = controller.ParentViewController)
        {
            NativeController? parent = controller.ParentViewController;
            if (parent is UIKit.UINavigationController navigation
                && navigation.TopViewController?.Handle != controller.Handle) return false;
            if (parent is UIKit.UITabBarController tabs
                && tabs.SelectedViewController?.Handle != controller.Handle) return false;
        }
        return true;
    }

    private sealed class NativeVisibilityObserver : NativeView
    {
        private readonly GalleryPageActivation owner;
        private readonly Page page;
        private NativeView? pageView;
        private bool? wasShown;
        private bool detached;
        private bool reconnectQueued;
        private bool observerDisposed;

        internal NativeVisibilityObserver(GalleryPageActivation owner, Page page)
        {
            this.owner = owner;
            this.page = page;
            // This zero-size observer never participates in page layout or pointer input.
            UserInteractionEnabled = false;
            BackgroundColor = UIKit.UIColor.Clear;
            page.HandlerChanged += OnHandlerChanged;
            page.PropertyChanged += OnPagePropertyChanged;
            Reconnect();
        }

        private void OnHandlerChanged(object? sender, EventArgs e) => QueueReconnect();

        private void OnPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ContentPage.Content)) QueueReconnect();
        }

        private void QueueReconnect()
        {
            if (observerDisposed || reconnectQueued) return;
            reconnectQueued = true;
            // MAUI clears all native subviews when Content changes. Reattach after its mapping
            // completes, coalescing the source drawer's Content=null / Content=chrome sequence.
            if (!page.Dispatcher.Dispatch(() =>
                {
                    reconnectQueued = false;
                    if (!observerDisposed) Reconnect();
                })) reconnectQueued = false;
        }

        private void Reconnect()
        {
            if (observerDisposed) return;
            detached = true;
            RemoveFromSuperview();
            pageView = page.Handler?.PlatformView as NativeView;
            wasShown = null;
            detached = false;
            pageView?.AddSubview(this);
            EvaluateVisibility();
        }

        public override void MovedToWindow()
        {
            base.MovedToWindow();
            EvaluateVisibility();
        }

        public override void MovedToSuperview()
        {
            base.MovedToSuperview();
            EvaluateVisibility();
        }

        public override void LayoutSubviews()
        {
            base.LayoutSubviews();
            EvaluateVisibility();
        }

        private void EvaluateVisibility()
        {
            if (detached || observerDisposed) return;
            bool shown = IsApplePageShown(page);
            if (wasShown == shown) return;
            wasShown = shown;
            owner.OnNativePageVisibilityChanged(page, shown);
        }

        protected override void Dispose(bool disposing)
        {
            observerDisposed = true;
            if (disposing)
            {
                detached = true;
                page.HandlerChanged -= OnHandlerChanged;
                page.PropertyChanged -= OnPagePropertyChanged;
                RemoveFromSuperview();
                pageView = null;
            }
            base.Dispose(disposing);
        }
    }
}
#endif

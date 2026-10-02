#if ANDROID
using NativeView = Android.Views.View;
using NativeViewTreeObserver = Android.Views.ViewTreeObserver;

namespace Mu3D.Gallery.Controls;

internal sealed partial class GalleryPageActivation
{
    private readonly List<NativeVisibilityObserver> androidObservers = [];

    private void RegisterAndroidPage(Page page)
        => androidObservers.Add(new NativeVisibilityObserver(this, page));

    private void DisposeAndroidObservers()
    {
        foreach (NativeVisibilityObserver observer in androidObservers) observer.Dispose();
        androidObservers.Clear();
    }

    private sealed class NativeVisibilityObserver : Java.Lang.Object, NativeViewTreeObserver.IOnGlobalLayoutListener
    {
        private readonly GalleryPageActivation owner;
        private readonly Page page;
        private NativeView? view;
        private NativeViewTreeObserver? observer;
        private bool? wasShown;

        internal NativeVisibilityObserver(GalleryPageActivation owner, Page page)
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
            observer = view?.ViewTreeObserver;
            if (IsLive(observer)) observer!.AddOnGlobalLayoutListener(this);
            EvaluateVisibility();
        }

        public void OnGlobalLayout() => EvaluateVisibility();

        private void EvaluateVisibility()
        {
            bool shown = view is { IsAttachedToWindow: true, IsShown: true };
            if (wasShown == shown) return;
            wasShown = shown;
            owner.OnNativePageVisibilityChanged(page, shown);
        }

        private void Detach()
        {
            // Attaching a View merges its initial observer into the window observer. Remove
            // the same listener from the current observer, rather than a dead pre-attach one.
            NativeViewTreeObserver? current = view is not null && view.Handle != IntPtr.Zero
                ? view.ViewTreeObserver : observer;
            if (IsLive(current)) current!.RemoveOnGlobalLayoutListener(this);
            observer = null;
            view = null;
            wasShown = null;
        }

        private static bool IsLive(NativeViewTreeObserver? value)
            => value is not null && value.Handle != IntPtr.Zero && value.IsAlive;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                page.HandlerChanged -= OnHandlerChanged;
                Detach();
            }
            base.Dispose(disposing);
        }
    }
}
#endif

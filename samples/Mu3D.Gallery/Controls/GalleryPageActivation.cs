using Microsoft.Maui;
using Mu3D.Maui.Toolkit.Controls;

namespace Mu3D.Gallery.Controls;

/// <summary>Coordinates only the pages actually created by the sample's navigation templates.</summary>
internal sealed partial class GalleryPageActivation : IDisposable
{
    private readonly Dictionary<Page, PageState> pages = [];
    private Page? selectedPage;
    private Page? activePage;
    private bool disposed;

    /// <summary>Registers a created page and suspends its rendering until it is selected and loaded.</summary>
    internal void Register(Page page)
        => Register(page, landing: false);

    /// <summary>Registers a native group's landing page without suppressing its list content.</summary>
    internal void RegisterLanding(Page page)
        => Register(page, landing: true);

    private void Register(Page page, bool landing)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(page);
        if (pages.ContainsKey(page)) return;
        PageState state = new(page, landing);
        pages.Add(page, state);
        state.SuspendRendering();
        page.Loaded += OnPageLoaded;
        page.Unloaded += OnPageUnloaded;
#if ANDROID
        RegisterAndroidPage(page);
#elif IOS || MACCATALYST
        RegisterApplePage(page);
#endif
    }

    /// <summary>Selects a page; a not-yet-loaded page starts only when its own Loaded event arrives.</summary>
    internal void Activate(Page? page)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (page is not null) Register(page);
        selectedPage = page;
        if (!ReferenceEquals(activePage, page)) DeactivateActivePage();
        if (page is not null && page.IsLoaded && IsPageShown(page)) ActivateSelectedPage();
    }

    /// <summary>Clears navigation activation when no destination is selected or the window stops.</summary>
    internal void Deactivate()
    {
        selectedPage = null;
        DeactivateActivePage();
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        if (disposed) return;
        if (sender is not Page page || !IsPageShown(page)) return;
        // A cached landing page can load below a navigation stack's visible child. Only the
        // platform visibility observer may treat it as a navigation destination.
        if (!pages[page].IsLanding && ReferenceEquals(page, selectedPage)) ActivateSelectedPage();
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        if (disposed) return;
        if (sender is Page page && ReferenceEquals(page, activePage)) DeactivateActivePage();
    }

    private void ActivateSelectedPage()
    {
        if (selectedPage is not Page page || ReferenceEquals(page, activePage)) return;
        activePage = page;
        // Set up scene/pass state before making native presentation views visible again.
        NotifyActivation(page, true);
        pages[page].ResumeRendering();
    }

    private static bool IsPageShown(Page page)
    {
#if ANDROID
        return page.Handler?.PlatformView is global::Android.Views.View view
            && view.IsAttachedToWindow && view.IsShown;
#elif IOS || MACCATALYST
        return IsApplePageShown(page);
#elif WINDOWS
        if (page.Handler?.PlatformView is not global::Microsoft.UI.Xaml.FrameworkElement view || !view.IsLoaded)
            return false;
        for (global::Microsoft.UI.Xaml.DependencyObject? ancestor = view; ancestor is not null;
            ancestor = global::Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(ancestor))
            if (ancestor is global::Microsoft.UI.Xaml.UIElement element
                && element.Visibility != global::Microsoft.UI.Xaml.Visibility.Visible) return false;
        return true;
#else
        return page.IsLoaded;
#endif
    }

#if ANDROID || IOS || MACCATALYST
    private void OnNativePageVisibilityChanged(Page page, bool shown)
    {
        if (disposed) return;
        if (pages[page].IsLanding)
        {
            // A landing may become visible while the selected child is still being pushed.
            // Stop the current frame owner without losing that pending selection.
            if (shown) DeactivateActivePage();
        }
        else if (!shown && ReferenceEquals(page, activePage)) DeactivateActivePage();
        else if (shown && page.IsLoaded && ReferenceEquals(page, selectedPage)) ActivateSelectedPage();
    }
#endif

    private void DeactivateActivePage()
    {
        if (activePage is not Page page) return;
        activePage = null;
        // Capture user rendering intent before the page's shutdown code changes its switches.
        pages[page].SuspendRendering();
        NotifyActivation(page, false);
    }

    private static void NotifyActivation(Page page, bool active)
    {
        if (page is IGalleryPageActivation receiver) receiver.SetNavigationActive(active);
        else if (active) ((IPageController)page).SendAppearing();
        else ((IPageController)page).SendDisappearing();
    }

    /// <summary>Stops the active page and releases the coordinator's lifecycle subscriptions.</summary>
    public void Dispose()
    {
        if (disposed) return;
        Deactivate();
        disposed = true;
#if ANDROID
        DisposeAndroidObservers();
#elif IOS || MACCATALYST
        DisposeAppleObservers();
#endif
        foreach (Page page in pages.Keys)
        {
            page.Loaded -= OnPageLoaded;
            page.Unloaded -= OnPageUnloaded;
        }
        pages.Clear();
    }

    private sealed class PageState
    {
        private readonly Page page;
        private readonly Grid? renderingGate;
        private readonly View? originalContent;
        private readonly Dictionary<SceneViewProxyHost, bool> proxyRendering = [];
        private bool suspended;

        internal PageState(Page page, bool landing)
        {
            this.page = page;
            IsLanding = landing;
            if (!landing && page is not Pages.ExampleCatalogPage
                && page is ContentPage contentPage && contentPage.Content is View content)
            {
                // Own only this container. Detaching its original content stops control-owned
                // surface lifecycles without altering any content visibility binding or value.
                originalContent = content;
                contentPage.Content = null;
                renderingGate = new Grid();
                renderingGate.Add(content);
                contentPage.Content = renderingGate;
            }
        }

        internal bool IsLanding { get; }

        internal void SuspendRendering()
        {
            if (suspended) return;
            suspended = true;
            foreach (IVisualTreeElement element in Descendants((IVisualTreeElement?)originalContent ?? page))
            {
                if (element is SceneViewProxyHost host)
                {
                    proxyRendering[host] = host.IsRenderingEnabled;
                    host.IsRenderingEnabled = false;
                }
            }
            if (renderingGate is not null)
            {
                if (originalContent is not null) renderingGate.Children.Remove(originalContent);
                renderingGate.IsVisible = false;
            }
        }

        internal void ResumeRendering()
        {
            if (!suspended) return;
            suspended = false;
            foreach ((SceneViewProxyHost host, bool wasRendering) in proxyRendering) host.IsRenderingEnabled = wasRendering;
            if (renderingGate is not null)
            {
                if (originalContent is not null) renderingGate.Add(originalContent);
                renderingGate.IsVisible = true;
            }
            proxyRendering.Clear();
        }

        private static IEnumerable<IVisualTreeElement> Descendants(IVisualTreeElement element)
        {
            yield return element;
            foreach (IVisualTreeElement child in element.GetVisualChildren())
                foreach (IVisualTreeElement descendant in Descendants(child)) yield return descendant;
        }
    }
}

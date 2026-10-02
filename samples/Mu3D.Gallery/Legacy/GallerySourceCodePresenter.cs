using System.Runtime.CompilerServices;

namespace Mu3D.GalleryApp;

internal static class GallerySourceCodePresenter
{
    private static readonly ConditionalWeakTable<ContentPage, Registration> Registrations = new();

    internal static void Attach(ContentPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        // The source drawer is desktop authoring chrome. Mobile packages keep the focused
        // example page untouched and do not pay the extra layout or source-loading cost.
        if (DeviceInfo.Current.Idiom != DeviceIdiom.Desktop)
        {
            return;
        }

        _ = Registrations.GetValue(page, static target => new Registration(target));
    }

    private sealed class Registration
    {
        private readonly ContentPage page;
        private bool isInstalled;

        internal Registration(ContentPage page)
        {
            this.page = page;
            page.Loaded += OnPageLoaded;
            page.Dispatcher.Dispatch(Install);
        }

        private void OnPageLoaded(object? sender, EventArgs e)
        {
            _ = sender;
            _ = e;
            Install();
        }

        private void Install()
        {
            if (isInstalled || page.Content is null)
            {
                return;
            }

            View exampleContent = page.Content;
            page.Content = null;

            GallerySourceCodePanel panel = new(page.GetType())
            {
                IsVisible = true,
            };
            Grid chrome = new()
            {
                RowDefinitions =
                {
                    new RowDefinition(GridLength.Star),
                    new RowDefinition(GridLength.Auto),
                },
            };
            chrome.Add(exampleContent);
            chrome.Add(panel, 0, 1);
            page.Content = chrome;
            isInstalled = true;
        }
    }
}

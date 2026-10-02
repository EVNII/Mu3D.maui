namespace Mu3D.GalleryApp;

/// <summary>Coordinates navigation from the compact Gallery examples hub.</summary>
public static class GalleryNavigationService
{
    /// <summary>Navigates to a focused example on a fresh Examples stack.</summary>
    public static async Task NavigateToExampleAsync(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        if (Shell.Current is AppShell galleryShell)
        {
            galleryShell.SelectCategory(GalleryNavigationCategory.Examples);
        }

        await Shell.Current.GoToAsync(route);
    }
}

/// <summary>Identifies the three stable Gallery navigation categories.</summary>
public enum GalleryNavigationCategory
{
    /// <summary>The focused example catalog.</summary>
    Examples,

    /// <summary>The advanced material-conformance surface.</summary>
    Advanced,

    /// <summary>Application and license information.</summary>
    About,
}

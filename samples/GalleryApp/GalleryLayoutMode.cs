namespace Mu3D.GalleryApp;

/// <summary>Identifies the navigation placement selected for the current Gallery window.</summary>
public enum GalleryLayoutMode
{
    /// <summary>Places the complete Gallery navigation in a persistent leading sidebar.</summary>
    SideNavigation,

    /// <summary>Places the three primary Gallery categories as text actions in the Shell navigation bar.</summary>
    TopNavigation,

    /// <summary>Places the three primary Gallery categories in an icon-and-text bottom bar.</summary>
    BottomNavigation,
}

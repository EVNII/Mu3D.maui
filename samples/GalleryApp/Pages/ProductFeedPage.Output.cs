namespace Mu3D.GalleryApp.Pages;

/// <summary>Describes the active platform's native product-feed output path.</summary>
public partial class ProductFeedPage
{
#if WINDOWS
    private const string OutputMode =
        "Windows synchronized HDR-mask probe: transparent cards render one FP16 RGBA frame, copy its color to direct HDR, and replay the MAUI underlay from the same frame alpha; opaque cards use direct HDR Surfaces";
#elif IOS || MACCATALYST
    private const string OutputMode =
        "Apple native HDR-alpha probe: transparent cards render one premultiplied FP16 RGBA frame and convert it on-GPU to Metal straight alpha; opaque cards use direct HDR Surfaces";
#elif ANDROID
    private const string OutputMode =
        "Android native HDR-alpha probe: transparent cards use the compositor-inherited alpha mode; opaque cards use direct HDR Surfaces";
#else
    private const string OutputMode =
        "Native HDR-alpha product outputs";
#endif

    /// <summary>Gets the platform-specific native output heading.</summary>
    public string OutputHeading
    {
        get
        {
#if WINDOWS
            return "Synchronized HDR mask product outputs";
#else
            return "Native HDR alpha product outputs";
#endif
        }
    }

    private string GetSurfaceSummary()
    {
#if WINDOWS
        return $"direct HDR {ProxyHost.ActiveDirectSurfaceCount}, mask FP16 {ProxyHost.ActiveMaskSurfaceCount}";
#else
        return $"native HDR {ProxyHost.ActiveDirectSurfaceCount}";
#endif
    }
}

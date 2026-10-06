namespace Mu3D.Graphics;

/// <summary>Identifies the native platform object used to create a presentation surface.</summary>
public enum NativeSurfaceKind
{
    /// <summary>A Core Animation CAMetalLayer.</summary>
    MetalLayer,

    /// <summary>An Android ANativeWindow.</summary>
    AndroidNativeWindow,

    /// <summary>A Win32 HWND and its module HINSTANCE.</summary>
    WindowsHwnd,

    /// <summary>A WinUI 3 ISwapChainPanelNative interface.</summary>
    WindowsSwapChainPanel,
}

/// <summary>
/// Carries an ABI-neutral native presentation source from a platform host to a graphics backend.
/// The host owns the referenced native objects and must keep them alive for the surface lifetime.
/// </summary>
/// <param name="Kind">The kind of native platform object.</param>
/// <param name="Handle">The CAMetalLayer, ANativeWindow, or HWND handle.</param>
/// <param name="AuxiliaryHandle">The HINSTANCE for Windows; zero on other platforms.</param>
public readonly record struct NativeSurfaceSource(
    NativeSurfaceKind Kind,
    nint Handle,
    nint AuxiliaryHandle = 0)
{
    // Only a platform-owned carrier may supply an inherited association. An arbitrary
    // ANativeWindow does not identify its consumer's pixel association or ownership.
    private nint PremultipliedAndroidCarrierHandle { get; init; }
    private int AndroidCarrierLifetimeToken { get; init; }
    private bool AndroidTextureViewSupportsHdr { get; init; }

    private bool IsKnownAndroidCarrier =>
        Kind == NativeSurfaceKind.AndroidNativeWindow && Handle != 0 &&
        Handle == PremultipliedAndroidCarrierHandle && AuxiliaryHandle == 0;

    internal SurfaceAlphaMode InheritedAlphaAssociation =>
        IsKnownAndroidCarrier
            ? SurfaceAlphaMode.Premultiplied
            : SurfaceAlphaMode.Unknown;

    internal IDisposable? AcquireLifetime() => IsKnownAndroidCarrier && AndroidCarrierLifetimeToken != 0
        ? NativeSurfaceLifetime.Acquire(AndroidCarrierLifetimeToken)
        : null;

    internal SurfaceCapabilities ConstrainCapabilities(SurfaceCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!IsKnownAndroidCarrier || AndroidCarrierLifetimeToken == 0 || AndroidTextureViewSupportsHdr)
        {
            return capabilities;
        }
        // A float producer alone does not establish HDR through the app-window consumer.
        return capabilities with
        {
            Formats = capabilities.Formats.Where(format => format != PresentationFormat.Rgba16Float).ToArray(),
            SupportsRgba16Float = false,
            FormatCapabilities = capabilities.FormatCapabilities
                .Where(capability => capability.Format != PresentationFormat.Rgba16Float).ToArray(),
        };
    }

    /// <summary>Creates a source backed by a Core Animation CAMetalLayer.</summary>
    public static NativeSurfaceSource FromMetalLayer(nint layer) =>
        Create(NativeSurfaceKind.MetalLayer, layer);

    /// <summary>Creates a source backed by a retained Android ANativeWindow.</summary>
    public static NativeSurfaceSource FromAndroidNativeWindow(nint window) =>
        Create(NativeSurfaceKind.AndroidNativeWindow, window);

    internal static NativeSurfaceSource FromAndroidSurfaceView(nint window) =>
        FromAndroidNativeWindow(window) with
        {
            // SurfaceView never sets SurfaceControl.NON_PREMULTIPLIED; its buffer layer
            // therefore retains Android's default premultiplied association.
            // https://android.googlesource.com/platform/frameworks/base/+/refs/tags/android-16.0.0_r1/core/java/android/view/SurfaceView.java
            // https://android.googlesource.com/platform/frameworks/base/+/refs/tags/android-16.0.0_r1/core/java/android/view/SurfaceControl.java
            PremultipliedAndroidCarrierHandle = window,
        };

    internal static NativeSurfaceSource FromAndroidTextureView(
        nint window,
        NativeSurfaceLifetime lifetime,
        bool supportsHdr)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        return FromAndroidNativeWindow(window) with
        {
            // HWUI imports TextureView's AHardwareBuffer using kPremul_SkAlphaType.
            // https://android.googlesource.com/platform/frameworks/base/+/refs/tags/android-16.0.0_r1/libs/hwui/DeferredLayerUpdater.cpp
            PremultipliedAndroidCarrierHandle = window,
            AndroidCarrierLifetimeToken = lifetime.Token,
            AndroidTextureViewSupportsHdr = supportsHdr,
        };
    }

    /// <summary>Creates a source backed by a Win32 window and module handle.</summary>
    public static NativeSurfaceSource FromWindowsHwnd(nint hwnd, nint hinstance)
    {
        ArgumentOutOfRangeException.ThrowIfZero(hwnd);
        ArgumentOutOfRangeException.ThrowIfZero(hinstance);
        return new NativeSurfaceSource(NativeSurfaceKind.WindowsHwnd, hwnd, hinstance);
    }

    /// <summary>Creates a source backed by a retained WinUI 3 ISwapChainPanelNative interface.</summary>
    public static NativeSurfaceSource FromWindowsSwapChainPanel(nint panelNative) =>
        Create(NativeSurfaceKind.WindowsSwapChainPanel, panelNative);

    private static NativeSurfaceSource Create(NativeSurfaceKind kind, nint handle)
    {
        ArgumentOutOfRangeException.ThrowIfZero(handle);
        return new NativeSurfaceSource(kind, handle);
    }
}

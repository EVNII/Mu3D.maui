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
    /// <summary>Creates a source backed by a Core Animation CAMetalLayer.</summary>
    public static NativeSurfaceSource FromMetalLayer(nint layer) =>
        Create(NativeSurfaceKind.MetalLayer, layer);

    /// <summary>Creates a source backed by a retained Android ANativeWindow.</summary>
    public static NativeSurfaceSource FromAndroidNativeWindow(nint window) =>
        Create(NativeSurfaceKind.AndroidNativeWindow, window);

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

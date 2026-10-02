using System.ComponentModel;
using System.Runtime.InteropServices;
using Mu3D.Graphics;

namespace Mu3D.Native.Wgpu.Tests;

internal sealed partial class WindowsHiddenSurfaceWindow : IDisposable
{
    private const uint WindowStylePopup = 0x80000000;
    private nint window;

    internal WindowsHiddenSurfaceWindow(int width, int height)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The hidden HWND surface probe requires Windows.");
        }

        nint instance = GetModuleHandle(null);
        if (instance == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        window = CreateWindowEx(
            0,
            "STATIC",
            "Mu3D hidden presentation probe",
            WindowStylePopup,
            0,
            0,
            width,
            height,
            0,
            0,
            instance,
            0);
        if (window == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        Source = NativeSurfaceSource.FromWindowsHwnd(window, instance);
    }

    internal NativeSurfaceSource Source { get; }

    public void Dispose()
    {
        nint value = Interlocked.Exchange(ref window, 0);
        if (value != 0)
        {
            _ = DestroyWindow(value);
        }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true,
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);
}

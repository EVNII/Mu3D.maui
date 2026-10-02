#if WINDOWS
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Mu3D.GalleryApp.Pages;

public sealed partial class PointerInputBridge
{
    private const uint WmSetCursor = 0x0020;
    private const int IdcArrow = 32512;
    private static long nextCursorSubclassId;
    private FrameworkElement? platformPointerView;
    private Microsoft.UI.Xaml.Input.Pointer? activePlatformPointer;
    private WindowSubclassProcedure? platformCursorSubclassProcedure;
    private nint platformWindowHandle;
    private nuint platformCursorSubclassId;
    private bool platformSystemCursorHidden;

    private partial void AttachPlatformInput()
    {
        if (SceneView?.Handler?.PlatformView is not FrameworkElement nativeView)
        {
            return;
        }
        platformPointerView = nativeView;
        AttachPlatformCursorSubclass();
        nativeView.PointerPressed += OnPlatformPointerPressed;
        nativeView.PointerMoved += OnPlatformPointerMoved;
        nativeView.PointerReleased += OnPlatformPointerReleased;
        nativeView.PointerExited += OnPlatformPointerExited;
        nativeView.PointerCanceled += OnPlatformPointerCanceled;
        nativeView.PointerCaptureLost += OnPlatformPointerCaptureLost;
    }

    private partial void DetachPlatformInput()
    {
        PublishCanceled();
        PublishExited();
        FrameworkElement? nativeView = platformPointerView;
        if (nativeView is not null)
        {
            nativeView.PointerPressed -= OnPlatformPointerPressed;
            nativeView.PointerMoved -= OnPlatformPointerMoved;
            nativeView.PointerReleased -= OnPlatformPointerReleased;
            nativeView.PointerExited -= OnPlatformPointerExited;
            nativeView.PointerCanceled -= OnPlatformPointerCanceled;
            nativeView.PointerCaptureLost -= OnPlatformPointerCaptureLost;
        }
        ReleasePlatformPointer();
        SetPlatformSystemCursorHidden(false);
        DetachPlatformCursorSubclass();
        platformPointerView = null;
    }

    private void OnPlatformPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        FrameworkElement? nativeView = platformPointerView;
        if (nativeView is null || activePlatformPointer is not null ||
            ShouldIgnoreDirectTouch(e))
        {
            return;
        }
        PointerPoint point = e.GetCurrentPoint(nativeView);
        if (!IsPrimaryContact(e, point) ||
            !PublishPoint(ApplicationPointerPhase.Pressed, e, point, nativeView))
        {
            return;
        }
        SetPlatformSystemCursorHidden(e.Pointer.PointerDeviceType == PointerDeviceType.Pen);
        if (!nativeView.CapturePointer(e.Pointer))
        {
            PublishCanceled();
            SetPlatformSystemCursorHidden(false);
            return;
        }
        activePlatformPointer = e.Pointer;
        e.Handled = true;
    }

    private void OnPlatformPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        if (ShouldIgnoreDirectTouch(e))
        {
            return;
        }
        FrameworkElement? nativeView = platformPointerView;
        if (nativeView is null)
        {
            return;
        }
        PointerPoint point = e.GetCurrentPoint(nativeView);
        if (!IsActivePointer(e))
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Pen && !point.IsInContact)
            {
                SetPlatformSystemCursorHidden(true);
                if (PublishPoint(ApplicationPointerPhase.Hovered, e, point, nativeView))
                {
                    e.Handled = true;
                }
                else
                {
                    PublishExited();
                    SetPlatformSystemCursorHidden(false);
                }
            }
            else if (platformSystemCursorHidden)
            {
                PublishExited();
                SetPlatformSystemCursorHidden(false);
            }
            return;
        }
        SetPlatformSystemCursorHidden(e.Pointer.PointerDeviceType == PointerDeviceType.Pen);
        if (!PublishPoint(
            ApplicationPointerPhase.Moved,
            e,
            point,
            nativeView))
        {
            PublishCanceled();
            ReleasePlatformPointer();
            SetPlatformSystemCursorHidden(false);
        }
        e.Handled = true;
    }

    private void OnPlatformPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        if (ShouldIgnoreDirectTouch(e))
        {
            return;
        }
        FrameworkElement? nativeView = platformPointerView;
        if (!IsActivePointer(e) || nativeView is null)
        {
            return;
        }
        if (!PublishPoint(
            ApplicationPointerPhase.Released,
            e,
            e.GetCurrentPoint(nativeView),
            nativeView))
        {
            PublishCanceled();
        }
        ReleasePlatformPointer();
        e.Handled = true;
    }

    private void OnPlatformPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        if (ShouldIgnoreDirectTouch(e))
        {
            return;
        }
        bool wasDrawingCursor = platformSystemCursorHidden;
        PublishExited();
        if (IsActivePointer(e))
        {
            ReleasePlatformPointer();
        }
        SetPlatformSystemCursorHidden(false);
        if (wasDrawingCursor)
        {
            e.Handled = true;
        }
    }

    private void OnPlatformPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        if (ShouldIgnoreDirectTouch(e))
        {
            return;
        }
        if (!IsActivePointer(e))
        {
            return;
        }
        PublishCanceled();
        ReleasePlatformPointer();
        SetPlatformSystemCursorHidden(false);
        e.Handled = true;
    }

    private void OnPlatformPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        if (ShouldIgnoreDirectTouch(e))
        {
            return;
        }
        if (!IsActivePointer(e))
        {
            return;
        }
        activePlatformPointer = null;
        PublishCanceled();
        SetPlatformSystemCursorHidden(false);
        e.Handled = true;
    }

    private bool PublishPoint(
        ApplicationPointerPhase phase,
        PointerRoutedEventArgs e,
        PointerPoint point,
        FrameworkElement nativeView)
    {
        ApplicationPointerDeviceKind deviceKind = e.Pointer.PointerDeviceType switch
        {
            PointerDeviceType.Mouse => ApplicationPointerDeviceKind.Mouse,
            PointerDeviceType.Touch => ApplicationPointerDeviceKind.Touch,
            PointerDeviceType.Pen => ApplicationPointerDeviceKind.Pen,
            _ => ApplicationPointerDeviceKind.Unknown,
        };
        PointerPointProperties properties = point.Properties;
        float? pressure = deviceKind is ApplicationPointerDeviceKind.Pen or
            ApplicationPointerDeviceKind.Touch
            ? properties.Pressure
            : null;
        float? tiltX = deviceKind == ApplicationPointerDeviceKind.Pen ? properties.XTilt : null;
        float? tiltY = deviceKind == ApplicationPointerDeviceKind.Pen ? properties.YTilt : null;
        return PublishPlatformSample(
            phase,
            deviceKind,
            point.Position.X,
            point.Position.Y,
            nativeView.ActualWidth,
            nativeView.ActualHeight,
            pressure,
            tiltX,
            tiltY,
            properties.IsEraser,
            properties.IsBarrelButtonPressed);
    }

    private static bool IsPrimaryContact(PointerRoutedEventArgs e, PointerPoint point) =>
        e.Pointer.PointerDeviceType switch
        {
            PointerDeviceType.Mouse => point.Properties.IsLeftButtonPressed,
            PointerDeviceType.Touch or PointerDeviceType.Pen => point.IsInContact,
            _ => false,
        };

    private bool IsActivePointer(PointerRoutedEventArgs e) =>
        activePlatformPointer is { } pointer && pointer.PointerId == e.Pointer.PointerId;

    private bool ShouldIgnoreDirectTouch(PointerRoutedEventArgs e) =>
        e.Pointer.PointerDeviceType == PointerDeviceType.Touch &&
        IsDirectTouchPressSuppressed;

    private void ReleasePlatformPointer()
    {
        Microsoft.UI.Xaml.Input.Pointer? pointer = activePlatformPointer;
        activePlatformPointer = null;
        if (pointer is not null)
        {
            platformPointerView?.ReleasePointerCapture(pointer);
        }
    }

    private void SetPlatformSystemCursorHidden(bool hidden)
    {
        if (hidden && platformWindowHandle == 0)
        {
            AttachPlatformCursorSubclass();
        }
        if (platformSystemCursorHidden == hidden)
        {
            if (hidden)
            {
                _ = NativeSetCursor(0);
            }
            return;
        }
        platformSystemCursorHidden = hidden;
        if (hidden)
        {
            _ = NativeSetCursor(0);
        }
        else
        {
            _ = NativeSetCursor(NativeLoadCursor(0, IdcArrow));
        }
    }

    private void AttachPlatformCursorSubclass()
    {
        if (platformWindowHandle != 0 ||
            SceneView?.Window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
        {
            return;
        }
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
        if (windowHandle == 0)
        {
            return;
        }
        WindowSubclassProcedure procedure = OnPlatformWindowMessage;
        nuint subclassId = unchecked((nuint)Interlocked.Increment(ref nextCursorSubclassId));
        if (!NativeSetWindowSubclass(windowHandle, procedure, subclassId, 0))
        {
            return;
        }
        platformWindowHandle = windowHandle;
        platformCursorSubclassId = subclassId;
        platformCursorSubclassProcedure = procedure;
    }

    private void DetachPlatformCursorSubclass()
    {
        nint windowHandle = platformWindowHandle;
        WindowSubclassProcedure? procedure = platformCursorSubclassProcedure;
        if (windowHandle != 0 && procedure is not null)
        {
            _ = NativeRemoveWindowSubclass(
                windowHandle,
                procedure,
                platformCursorSubclassId);
        }
        platformCursorSubclassProcedure = null;
        platformCursorSubclassId = 0;
        platformWindowHandle = 0;
    }

    private nint OnPlatformWindowMessage(
        nint windowHandle,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == WmSetCursor && platformSystemCursorHidden)
        {
            _ = NativeSetCursor(0);
            return 1;
        }
        return NativeDefSubclassProcedure(
            windowHandle,
            message,
            wordParameter,
            longParameter);
    }

    [DllImport("user32.dll", EntryPoint = "SetCursor")]
    private static extern nint NativeSetCursor(nint cursor);

    [DllImport("user32.dll", EntryPoint = "LoadCursorW")]
    private static extern nint NativeLoadCursor(nint instance, nint cursorName);

    [DllImport("comctl32.dll", EntryPoint = "SetWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeSetWindowSubclass(
        nint windowHandle,
        WindowSubclassProcedure procedure,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", EntryPoint = "RemoveWindowSubclass")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeRemoveWindowSubclass(
        nint windowHandle,
        WindowSubclassProcedure procedure,
        nuint subclassId);

    [DllImport("comctl32.dll", EntryPoint = "DefSubclassProc")]
    private static extern nint NativeDefSubclassProcedure(
        nint windowHandle,
        uint message,
        nuint wordParameter,
        nint longParameter);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowSubclassProcedure(
        nint windowHandle,
        uint message,
        nuint wordParameter,
        nint longParameter,
        nuint subclassId,
        nuint referenceData);
}
#endif

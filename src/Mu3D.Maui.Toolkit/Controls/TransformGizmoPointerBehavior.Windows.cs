#if WINDOWS
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Mu3D.Maui.Toolkit.Controls;

public sealed partial class TransformGizmoPointerBehavior
{
    private FrameworkElement? platformPointerView;
    private Microsoft.UI.Xaml.Input.Pointer? activePlatformPointer;

    private partial void AttachPlatformInput()
    {
        DetachPlatformInput();
        if (sceneView?.Handler?.PlatformView is not FrameworkElement nativeView)
        {
            return;
        }

        platformPointerView = nativeView;
        nativeView.PointerPressed += OnPlatformPointerPressed;
        nativeView.PointerMoved += OnPlatformPointerMoved;
        nativeView.PointerReleased += OnPlatformPointerReleased;
        nativeView.PointerCanceled += OnPlatformPointerCanceled;
        nativeView.PointerCaptureLost += OnPlatformPointerCaptureLost;
    }

    private partial void DetachPlatformInput()
    {
        FrameworkElement? nativeView = platformPointerView;
        if (nativeView is not null)
        {
            nativeView.PointerPressed -= OnPlatformPointerPressed;
            nativeView.PointerMoved -= OnPlatformPointerMoved;
            nativeView.PointerReleased -= OnPlatformPointerReleased;
            nativeView.PointerCanceled -= OnPlatformPointerCanceled;
            nativeView.PointerCaptureLost -= OnPlatformPointerCaptureLost;
        }
        ReleasePlatformPointerCapture();
        platformPointerView = null;
    }

    private partial void ReleasePlatformPointerCapture()
    {
        Microsoft.UI.Xaml.Input.Pointer? pointer = activePlatformPointer;
        activePlatformPointer = null;
        if (pointer is not null)
        {
            platformPointerView?.ReleasePointerCapture(pointer);
        }
    }

    private static partial bool GetPlatformPointerInputAvailable() => true;

    private void OnPlatformPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        FrameworkElement? nativeView = platformPointerView;
        if (nativeView is null || activePlatformPointer is not null ||
            !IsSupportedPrimaryPointer(e, nativeView) ||
            !TryMapPlatformPosition(e, nativeView, out Vector2 position) ||
            !ProcessPointerPressed(position))
        {
            return;
        }

        if (!nativeView.CapturePointer(e.Pointer))
        {
            ProcessPointerCanceled();
            return;
        }
        activePlatformPointer = e.Pointer;
        e.Handled = true;
    }

    private void OnPlatformPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        FrameworkElement? nativeView = platformPointerView;
        if (!IsActivePlatformPointer(e) || nativeView is null)
        {
            return;
        }
        if (!TryMapPlatformPosition(e, nativeView, out Vector2 position) ||
            !ProcessPointerMoved(position))
        {
            ProcessPointerCanceled();
            ReleasePlatformPointerCapture();
            return;
        }
        e.Handled = true;
    }

    private void OnPlatformPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        FrameworkElement? nativeView = platformPointerView;
        if (!IsActivePlatformPointer(e) || nativeView is null)
        {
            return;
        }
        bool mapped = TryMapPlatformPosition(e, nativeView, out Vector2 position);
        if (mapped)
        {
            ProcessPointerReleased(position);
        }
        else
        {
            ProcessPointerCanceled();
        }
        ReleasePlatformPointerCapture();
        e.Handled = true;
    }

    private void OnPlatformPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        if (!IsActivePlatformPointer(e))
        {
            return;
        }
        ProcessPointerCanceled();
        ReleasePlatformPointerCapture();
        e.Handled = true;
    }

    private void OnPlatformPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        if (!IsActivePlatformPointer(e))
        {
            return;
        }
        activePlatformPointer = null;
        ProcessPointerCanceled();
        e.Handled = true;
    }

    private bool IsActivePlatformPointer(PointerRoutedEventArgs e) =>
        activePlatformPointer is Microsoft.UI.Xaml.Input.Pointer pointer &&
        pointer.PointerId == e.Pointer.PointerId;

    private static bool IsSupportedPrimaryPointer(
        PointerRoutedEventArgs e,
        FrameworkElement nativeView)
    {
        var point = e.GetCurrentPoint(nativeView);
        return e.Pointer.PointerDeviceType switch
        {
            Microsoft.UI.Input.PointerDeviceType.Mouse => point.Properties.IsLeftButtonPressed,
            Microsoft.UI.Input.PointerDeviceType.Touch => point.IsInContact,
            _ => false,
        };
    }

    private bool TryMapPlatformPosition(
        PointerRoutedEventArgs e,
        FrameworkElement nativeView,
        out Vector2 position)
    {
        position = default;
        if (sceneView is null || nativeView.ActualWidth <= 0d || nativeView.ActualHeight <= 0d ||
            sceneView.PixelWidth == 0 || sceneView.PixelHeight == 0)
        {
            return false;
        }
        Windows.Foundation.Point point = e.GetCurrentPoint(nativeView).Position;
        try
        {
            position = TransformGizmoPointerState.MapViewportPosition(
                point.X,
                point.Y,
                nativeView.ActualWidth,
                nativeView.ActualHeight,
                sceneView.PixelWidth,
                sceneView.PixelHeight);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
#endif

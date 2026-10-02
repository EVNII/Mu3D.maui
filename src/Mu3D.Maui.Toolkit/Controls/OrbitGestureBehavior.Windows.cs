#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using PlatformPointer = Microsoft.UI.Xaml.Input.Pointer;

namespace Mu3D.Maui.Toolkit.Controls;

public sealed partial class ViewportNavigationBehavior
{
    private UIElement? platformInputView;
    private PlatformPointer? platformButtonDragPointer;
    private ViewportDragAction platformOnePointerDragAction;
    private bool restorePlatformTabStop;

    private partial void AttachPlatformInput()
    {
        DetachPlatformInput();
        if (sceneView?.Handler?.PlatformView is not UIElement nativeView)
        {
            return;
        }

        platformInputView = nativeView;
        if (IsMouseWheelEnabled)
        {
            nativeView.PointerWheelChanged += OnPlatformPointerWheelChanged;
        }
        if (KeyboardInput.IsEnabled)
        {
            restorePlatformTabStop = !nativeView.IsTabStop;
            if (restorePlatformTabStop)
            {
                nativeView.IsTabStop = true;
            }
            nativeView.KeyDown += OnPlatformKeyDown;
        }
        nativeView.PointerPressed += OnPlatformPointerPressed;
        if (HasMouseButtonDrag)
        {
            nativeView.PointerMoved += OnPlatformPointerMoved;
            nativeView.PointerReleased += OnPlatformPointerReleased;
            nativeView.PointerCanceled += OnPlatformPointerCanceled;
            nativeView.PointerCaptureLost += OnPlatformPointerCaptureLost;
        }
    }

    private partial void DetachPlatformInput()
    {
        UIElement? nativeView = platformInputView;
        platformInputView = null;
        PlatformPointer? capturedPointer = platformButtonDragPointer;
        platformButtonDragPointer = null;
        platformOnePointerDragAction = ViewportDragAction.None;
        if (nativeView is not null && capturedPointer is not null)
        {
            ProcessActivePointerButtonDrag(GestureStatus.Canceled, 0d, 0d);
            nativeView.ReleasePointerCapture(capturedPointer);
        }
        if (nativeView is null)
        {
            restorePlatformTabStop = false;
            return;
        }

        nativeView.PointerWheelChanged -= OnPlatformPointerWheelChanged;
        nativeView.PointerPressed -= OnPlatformPointerPressed;
        nativeView.PointerMoved -= OnPlatformPointerMoved;
        nativeView.PointerReleased -= OnPlatformPointerReleased;
        nativeView.PointerCanceled -= OnPlatformPointerCanceled;
        nativeView.PointerCaptureLost -= OnPlatformPointerCaptureLost;
        nativeView.KeyDown -= OnPlatformKeyDown;
        if (restorePlatformTabStop && nativeView.IsTabStop)
        {
            nativeView.IsTabStop = false;
        }
        restorePlatformTabStop = false;
    }

    private static partial bool GetPlatformWheelInputAvailable() => true;

    private static partial bool GetPlatformKeyboardInputAvailable() => true;

    private static partial bool GetPlatformMouseButtonInputAvailable() => true;

    private partial bool GetPlatformSystemNavigationGestureActive() => false;

    private partial ViewportDragAction GetPlatformOnePointerDragAction() =>
        platformOnePointerDragAction;

    private static partial bool GetPlatformUsesNativePinchInput() => false;

    private void OnPlatformPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        UIElement? nativeView = platformInputView;
        if (nativeView is null)
        {
            return;
        }
        var point = e.GetCurrentPoint(nativeView);
        platformOnePointerDragAction = e.Pointer.PointerDeviceType switch
        {
            Microsoft.UI.Input.PointerDeviceType.Touch when TouchscreenInput.IsEnabled =>
                TouchscreenInput.OneFingerDragAction,
            Microsoft.UI.Input.PointerDeviceType.Mouse when MouseInput.IsEnabled =>
                MouseInput.LeftButtonDragAction,
            _ => ViewportDragAction.None,
        };
        if (KeyboardInput.IsEnabled)
        {
            if (!nativeView.Focus(FocusState.Pointer))
            {
                _ = nativeView.Focus(FocusState.Programmatic);
            }
        }
        if (platformButtonDragPointer is not null)
        {
            return;
        }

        ViewportDragAction buttonAction = point.Properties.IsRightButtonPressed
            ? MouseInput.RightButtonDragAction
            : point.Properties.IsMiddleButtonPressed
                ? MouseInput.MiddleButtonDragAction
                : ViewportDragAction.None;
        if (!MouseInput.IsEnabled || buttonAction == ViewportDragAction.None ||
            !nativeView.CapturePointer(e.Pointer))
        {
            return;
        }
        if (!ProcessPointerButtonDrag(
            GestureStatus.Started,
            point.Position.X,
            point.Position.Y,
            buttonAction))
        {
            nativeView.ReleasePointerCapture(e.Pointer);
            return;
        }

        platformButtonDragPointer = e.Pointer;
        e.Handled = true;
    }

    private void OnPlatformPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        UIElement? nativeView = platformInputView;
        if (nativeView is null || platformButtonDragPointer?.PointerId != e.Pointer.PointerId)
        {
            return;
        }
        var point = e.GetCurrentPoint(nativeView);
        _ = ProcessActivePointerButtonDrag(GestureStatus.Running, point.Position.X, point.Position.Y);
        e.Handled = true;
    }

    private void OnPlatformPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        UIElement? nativeView = platformInputView;
        PlatformPointer? pointer = platformButtonDragPointer;
        if (nativeView is null || pointer?.PointerId != e.Pointer.PointerId)
        {
            return;
        }
        var point = e.GetCurrentPoint(nativeView);
        _ = ProcessActivePointerButtonDrag(GestureStatus.Running, point.Position.X, point.Position.Y);
        _ = ProcessActivePointerButtonDrag(GestureStatus.Completed, point.Position.X, point.Position.Y);
        platformButtonDragPointer = null;
        nativeView.ReleasePointerCapture(pointer);
        e.Handled = true;
    }

    private void OnPlatformPointerCanceled(object sender, PointerRoutedEventArgs e) =>
        CancelPlatformButtonDrag(e.Pointer);

    private void OnPlatformPointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        CancelPlatformButtonDrag(e.Pointer);

    private void CancelPlatformButtonDrag(PlatformPointer pointer)
    {
        if (platformButtonDragPointer?.PointerId != pointer.PointerId)
        {
            return;
        }
        platformButtonDragPointer = null;
        _ = ProcessActivePointerButtonDrag(GestureStatus.Canceled, 0d, 0d);
    }

    private void OnPlatformPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        _ = sender;
        UIElement? nativeView = platformInputView;
        if (nativeView is null)
        {
            return;
        }
        int wheelDelta = e.GetCurrentPoint(nativeView).Properties.MouseWheelDelta;
        float detents = ViewportNavigationInputState.NormalizeWheelDelta(wheelDelta, 120f);
        if (detents != 0f && ProcessWheel(detents))
        {
            e.Handled = true;
        }
    }

    private void OnPlatformKeyDown(object sender, KeyRoutedEventArgs e)
    {
        _ = sender;
        ViewportKey? key = MapPlatformKeyboardKey(e.Key) ??
            MapPlatformKeyboardKey(e.OriginalKey);
        if (key is ViewportKey mapped && ProcessKeyboardKey(mapped))
        {
            e.Handled = true;
        }
    }

    private static ViewportKey? MapPlatformKeyboardKey(VirtualKey key)
    {
        ViewportKey? mapped = key switch
        {
            VirtualKey.Left => ViewportKey.LeftArrow,
            VirtualKey.Right => ViewportKey.RightArrow,
            VirtualKey.Up => ViewportKey.UpArrow,
            VirtualKey.Down => ViewportKey.DownArrow,
            VirtualKey.PageUp => ViewportKey.PageUp,
            VirtualKey.PageDown => ViewportKey.PageDown,
            VirtualKey.Add => ViewportKey.Add,
            VirtualKey.Subtract => ViewportKey.Subtract,
            _ => null,
        };
        if (mapped is not null)
        {
            return mapped;
        }

        int value = (int)key;
        int firstLetter = (int)VirtualKey.A;
        int lastLetter = (int)VirtualKey.Z;
        return value >= firstLetter && value <= lastLetter
            ? (ViewportKey)((int)ViewportKey.A + (value - firstLetter))
            : null;
    }

    private bool IsMouseWheelEnabled =>
        MouseInput.IsEnabled && MouseInput.WheelAction == ViewportScalarAction.Dolly;

    private bool HasMouseButtonDrag =>
        MouseInput.IsEnabled &&
        (MouseInput.MiddleButtonDragAction != ViewportDragAction.None ||
            MouseInput.RightButtonDragAction != ViewportDragAction.None);
}
#endif

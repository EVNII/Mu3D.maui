#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Mu3D.Maui.Toolkit.Controls;
using Windows.System;
using NativeWindow = Microsoft.UI.Xaml.Window;

namespace Mu3D.GalleryApp.Pages;

internal sealed partial class HeldKeyboardInputSession
{
    private UIElement? eventRoot;
    private NativeWindow? nativeWindow;
    private KeyEventHandler? keyDownHandler;
    private KeyEventHandler? keyUpHandler;

    partial void TryAttachPlatformInput()
    {
        if (!attachRequested || eventRoot is not null ||
            windowProvider()?.Handler?.PlatformView is not NativeWindow window ||
            window.Content is not UIElement root)
        {
            return;
        }

        keyDownHandler = OnKeyDown;
        keyUpHandler = OnKeyUp;
        eventRoot = root;
        nativeWindow = window;
        root.AddHandler(UIElement.KeyDownEvent, keyDownHandler, handledEventsToo: true);
        root.AddHandler(UIElement.KeyUpEvent, keyUpHandler, handledEventsToo: true);
        window.Activated += OnWindowActivated;
    }

    partial void DetachPlatformInput()
    {
        UIElement? root = eventRoot;
        if (root is not null)
        {
            if (keyDownHandler is not null)
            {
                root.RemoveHandler(UIElement.KeyDownEvent, keyDownHandler);
            }
            if (keyUpHandler is not null)
            {
                root.RemoveHandler(UIElement.KeyUpEvent, keyUpHandler);
            }
        }
        if (nativeWindow is not null)
        {
            nativeWindow.Activated -= OnWindowActivated;
        }

        eventRoot = null;
        nativeWindow = null;
        keyDownHandler = null;
        keyUpHandler = null;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        _ = sender;
        ViewportKey key = MapNativeKey(e.Key);
        if (key == ViewportKey.None)
        {
            key = MapNativeKey(e.OriginalKey);
        }
        e.Handled = HandleKeyDown(key);
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        _ = sender;
        ViewportKey key = MapNativeKey(e.Key);
        if (key == ViewportKey.None)
        {
            key = MapNativeKey(e.OriginalKey);
        }
        e.Handled = HandleKeyUp(key);
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        _ = sender;
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            ClearPressedState();
        }
    }

    private static ViewportKey MapNativeKey(VirtualKey key) => key switch
    {
        VirtualKey.Left => ViewportKey.LeftArrow,
        VirtualKey.Right => ViewportKey.RightArrow,
        VirtualKey.Up => ViewportKey.UpArrow,
        VirtualKey.Down => ViewportKey.DownArrow,
        VirtualKey.PageUp => ViewportKey.PageUp,
        VirtualKey.PageDown => ViewportKey.PageDown,
        VirtualKey.Add => ViewportKey.Add,
        VirtualKey.Subtract => ViewportKey.Subtract,
        >= VirtualKey.A and <= VirtualKey.Z =>
            (ViewportKey)(
                (int)ViewportKey.A + ((int)key - (int)VirtualKey.A)),
        _ => ViewportKey.None,
    };
}
#endif

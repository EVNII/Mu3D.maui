#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using Mu3D.Maui.Toolkit.Controls;
using UIKit;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace Mu3D.GalleryApp.Pages;

internal sealed partial class HeldKeyboardInputSession
{
    private MauiWindow? observedWindow;
    private UIView? keyboardSinkOwner;
    private AppleKeyboardSinkView? keyboardSink;

    partial void TryAttachPlatformInput()
    {
        AttachWindowLifecycle();
        AttachKeyboardSink();
    }

    partial void DetachPlatformInput()
    {
        DetachWindowLifecycle();
        DetachKeyboardSink();
    }

    private void AttachWindowLifecycle()
    {
        MauiWindow? window = windowProvider();
        if (ReferenceEquals(observedWindow, window))
        {
            return;
        }

        DetachWindowLifecycle();
        observedWindow = window;
        if (window is not null)
        {
            window.Deactivated += OnWindowDeactivated;
            window.Activated += OnWindowActivated;
        }
    }

    private void DetachWindowLifecycle()
    {
        MauiWindow? window = observedWindow;
        observedWindow = null;
        if (window is not null)
        {
            window.Deactivated -= OnWindowDeactivated;
            window.Activated -= OnWindowActivated;
        }
    }

    private void OnWindowDeactivated(object? sender, EventArgs e) =>
        ClearApplePressedStateOnUiThread();

    private void OnWindowActivated(object? sender, EventArgs e) => FocusKeyboardSink();

    private void AttachKeyboardSink()
    {
        if (animationOwner.Handler?.PlatformView is not UIView nativeView)
        {
            return;
        }
        if (keyboardSink is not null && ReferenceEquals(keyboardSinkOwner, nativeView))
        {
            FocusKeyboardSink();
            return;
        }

        DetachKeyboardSink();

        keyboardSinkOwner = nativeView;
        keyboardSink = new AppleKeyboardSinkView(ProcessUIKitKey)
        {
            AccessibilityElementsHidden = true,
            BackgroundColor = UIColor.Clear,
            Frame = CGRect.Empty,
            UserInteractionEnabled = false,
        };
        nativeView.AddSubview(keyboardSink);
        FocusKeyboardSink();
    }

    private void DetachKeyboardSink()
    {
        AppleKeyboardSinkView? sink = keyboardSink;
        keyboardSink = null;
        keyboardSinkOwner = null;
        if (sink is null)
        {
            return;
        }
        if (sink.IsFirstResponder)
        {
            sink.ResignFirstResponder();
        }
        sink.RemoveFromSuperview();
        sink.Dispose();
    }

    private void FocusKeyboardSink()
    {
        if (animationOwner.Dispatcher.IsDispatchRequired)
        {
            animationOwner.Dispatcher.Dispatch(FocusKeyboardSink);
            return;
        }

        AppleKeyboardSinkView? sink = keyboardSink;
        if (sink is not null && !sink.IsFirstResponder)
        {
            sink.BecomeFirstResponder();
        }
    }

    private void ClearApplePressedStateOnUiThread()
    {
        if (animationOwner.Dispatcher.IsDispatchRequired)
        {
            animationOwner.Dispatcher.Dispatch(ClearApplePressedStateOnUiThread);
            return;
        }

        ClearPressedState();
    }

    private bool ProcessUIKitKey(UIKey key, bool pressed)
    {
        if ((key.ModifierFlags & (
            UIKeyModifierFlags.Alternate |
            UIKeyModifierFlags.Command |
            UIKeyModifierFlags.Control)) != 0)
        {
            return false;
        }

        ViewportKey mappedKey = MapNativeKey(key.KeyCode);
        if (!acceptsKey(mappedKey))
        {
            return false;
        }

        SetKeyState(mappedKey, pressed);
        return true;
    }

    private static ViewportKey MapNativeKey(UIKeyboardHidUsage keyCode)
    {
        long usage = (long)keyCode;
        long keyA = (long)UIKeyboardHidUsage.KeyboardA;
        if (usage >= keyA && usage <= (long)UIKeyboardHidUsage.KeyboardZ)
        {
            return (ViewportKey)(
                (int)ViewportKey.A + (int)(usage - keyA));
        }

        return keyCode switch
        {
            UIKeyboardHidUsage.KeyboardLeftArrow => ViewportKey.LeftArrow,
            UIKeyboardHidUsage.KeyboardRightArrow => ViewportKey.RightArrow,
            UIKeyboardHidUsage.KeyboardUpArrow => ViewportKey.UpArrow,
            UIKeyboardHidUsage.KeyboardDownArrow => ViewportKey.DownArrow,
            UIKeyboardHidUsage.KeyboardPageUp => ViewportKey.PageUp,
            UIKeyboardHidUsage.KeyboardPageDown => ViewportKey.PageDown,
            UIKeyboardHidUsage.KeyboardEqualSign or UIKeyboardHidUsage.KeypadPlus =>
                ViewportKey.Add,
            UIKeyboardHidUsage.KeyboardHyphen or UIKeyboardHidUsage.KeypadHyphen =>
                ViewportKey.Subtract,
            _ => ViewportKey.None,
        };
    }

    private sealed class AppleKeyboardSinkView(
        Func<UIKey, bool, bool> processKey) : UIView
    {
        public override bool CanBecomeFirstResponder => true;

        public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt)
        {
            if (!ProcessPresses(presses, pressed: true))
            {
                base.PressesBegan(presses, evt);
            }
        }

        public override void PressesEnded(NSSet<UIPress> presses, UIPressesEvent evt)
        {
            if (!ProcessPresses(presses, pressed: false))
            {
                base.PressesEnded(presses, evt);
            }
        }

        public override void PressesCancelled(NSSet<UIPress> presses, UIPressesEvent evt)
        {
            if (!ProcessPresses(presses, pressed: false))
            {
                base.PressesCancelled(presses, evt);
            }
        }

        private bool ProcessPresses(NSSet<UIPress> presses, bool pressed)
        {
            bool handled = false;
            foreach (UIPress press in presses)
            {
                if (press.Key is UIKey key && processKey(key, pressed))
                {
                    handled = true;
                }
            }
            return handled;
        }
    }
}
#endif

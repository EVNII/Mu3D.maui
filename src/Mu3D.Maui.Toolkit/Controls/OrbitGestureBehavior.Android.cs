#if ANDROID
using Android.Views;
using PlatformView = Android.Views.View;

namespace Mu3D.Maui.Toolkit.Controls;

public sealed partial class ViewportNavigationBehavior
{
    private PlatformView? platformInputView;
    private bool restorePlatformFocusable;
    private bool restorePlatformFocusableInTouchMode;
    private bool platformSystemNavigationGestureActive;
    private bool platformButtonDragActive;
    private ViewportDragAction platformOnePointerDragAction;

    private partial void AttachPlatformInput()
    {
        DetachPlatformInput();
        if (sceneView?.Handler?.PlatformView is not PlatformView nativeView)
        {
            return;
        }

        platformInputView = nativeView;
        nativeView.Touch += OnPlatformTouch;
        if (MouseInput.IsEnabled && MouseInput.WheelAction == ViewportScalarAction.Dolly)
        {
            nativeView.GenericMotion += OnPlatformGenericMotion;
        }
        if (KeyboardInput.IsEnabled)
        {
            restorePlatformFocusable = !nativeView.Focusable;
            restorePlatformFocusableInTouchMode = !nativeView.FocusableInTouchMode;
            nativeView.Focusable = true;
            nativeView.FocusableInTouchMode = true;
            nativeView.KeyPress += OnPlatformKeyPress;
        }
    }

    private partial void DetachPlatformInput()
    {
        PlatformView? nativeView = platformInputView;
        platformInputView = null;
        if (nativeView is null)
        {
            restorePlatformFocusable = false;
            restorePlatformFocusableInTouchMode = false;
            platformSystemNavigationGestureActive = false;
            platformButtonDragActive = false;
            platformOnePointerDragAction = ViewportDragAction.None;
            return;
        }

        if (platformButtonDragActive)
        {
            _ = ProcessActivePointerButtonDrag(GestureStatus.Canceled, 0d, 0d);
        }
        nativeView.GenericMotion -= OnPlatformGenericMotion;
        nativeView.KeyPress -= OnPlatformKeyPress;
        nativeView.Touch -= OnPlatformTouch;
        if (restorePlatformFocusableInTouchMode && nativeView.FocusableInTouchMode)
        {
            nativeView.FocusableInTouchMode = false;
        }
        if (restorePlatformFocusable && nativeView.Focusable)
        {
            nativeView.Focusable = false;
        }
        restorePlatformFocusable = false;
        restorePlatformFocusableInTouchMode = false;
        platformSystemNavigationGestureActive = false;
        platformButtonDragActive = false;
    }

    private static partial bool GetPlatformWheelInputAvailable() => true;

    private static partial bool GetPlatformKeyboardInputAvailable() => true;

    private static partial bool GetPlatformMouseButtonInputAvailable() => true;

    private partial bool GetPlatformSystemNavigationGestureActive() =>
        platformSystemNavigationGestureActive;

    private partial ViewportDragAction GetPlatformOnePointerDragAction() =>
        platformOnePointerDragAction;

    private static partial bool GetPlatformUsesNativePinchInput() => false;

    private void OnPlatformTouch(object? sender, PlatformView.TouchEventArgs e)
    {
        _ = sender;
        MotionEvent? motion = e.Event;
        if (motion is null)
        {
            return;
        }

        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
                platformOnePointerDragAction = motion.GetToolType(0) switch
                {
                    MotionEventToolType.Finger when TouchscreenInput.IsEnabled =>
                        TouchscreenInput.OneFingerDragAction,
                    MotionEventToolType.Mouse when MouseInput.IsEnabled =>
                        MouseInput.LeftButtonDragAction,
                    _ => ViewportDragAction.None,
                };
                ViewportDragAction buttonAction = motion.IsButtonPressed(MotionEventButtonState.Secondary)
                    ? MouseInput.RightButtonDragAction
                    : motion.IsButtonPressed(MotionEventButtonState.Tertiary)
                        ? MouseInput.MiddleButtonDragAction
                        : ViewportDragAction.None;
                if (MouseInput.IsEnabled && buttonAction != ViewportDragAction.None &&
                    ProcessPointerButtonDrag(
                        GestureStatus.Started,
                        motion.GetX(),
                        motion.GetY(),
                        buttonAction))
                {
                    platformButtonDragActive = true;
                    e.Handled = true;
                    return;
                }
                platformSystemNavigationGestureActive =
                    platformInputView is PlatformView nativeView &&
                    AndroidSystemGestureArbitration.StartsAtSystemBackEdge(nativeView, motion);
                break;
            case MotionEventActions.Move when platformButtonDragActive:
                _ = ProcessActivePointerButtonDrag(GestureStatus.Running, motion.GetX(), motion.GetY());
                e.Handled = true;
                break;
            case MotionEventActions.Up:
                if (platformButtonDragActive)
                {
                    _ = ProcessActivePointerButtonDrag(GestureStatus.Running, motion.GetX(), motion.GetY());
                    _ = ProcessActivePointerButtonDrag(GestureStatus.Completed, motion.GetX(), motion.GetY());
                    platformButtonDragActive = false;
                    e.Handled = true;
                }
                platformSystemNavigationGestureActive = false;
                break;
            case MotionEventActions.Cancel:
                if (platformButtonDragActive)
                {
                    _ = ProcessActivePointerButtonDrag(GestureStatus.Canceled, motion.GetX(), motion.GetY());
                    platformButtonDragActive = false;
                    e.Handled = true;
                }
                platformSystemNavigationGestureActive = false;
                break;
        }
    }

    private void OnPlatformGenericMotion(object? sender, PlatformView.GenericMotionEventArgs e)
    {
        _ = sender;
        MotionEvent? motion = e.Event;
        if (motion is null || motion.ActionMasked != MotionEventActions.Scroll)
        {
            return;
        }

        float wheelDelta = motion.GetAxisValue(Axis.Vscroll);
        float detents = ViewportNavigationInputState.NormalizeWheelDelta(wheelDelta, 1f);
        if (detents != 0f && ProcessWheel(detents))
        {
            if (KeyboardInput.IsEnabled)
            {
                platformInputView?.RequestFocus();
            }
            e.Handled = true;
        }
    }

    private void OnPlatformKeyPress(object? sender, PlatformView.KeyEventArgs e)
    {
        _ = sender;
        KeyEvent? keyEvent = e.Event;
        if (keyEvent is null || keyEvent.Action != KeyEventActions.Down ||
            keyEvent.IsAltPressed || keyEvent.IsCtrlPressed || keyEvent.IsMetaPressed)
        {
            return;
        }

        ViewportKey? key = MapPlatformKeyboardKey(keyEvent.KeyCode);
        if (key is ViewportKey mapped && ProcessKeyboardKey(mapped))
        {
            e.Handled = true;
        }
    }

    private static ViewportKey? MapPlatformKeyboardKey(Keycode key)
    {
        ViewportKey? mapped = key switch
        {
            Keycode.DpadLeft => ViewportKey.LeftArrow,
            Keycode.DpadRight => ViewportKey.RightArrow,
            Keycode.DpadUp => ViewportKey.UpArrow,
            Keycode.DpadDown => ViewportKey.DownArrow,
            Keycode.PageUp => ViewportKey.PageUp,
            Keycode.PageDown => ViewportKey.PageDown,
            Keycode.NumpadAdd => ViewportKey.Add,
            Keycode.NumpadSubtract => ViewportKey.Subtract,
            _ => null,
        };
        if (mapped is not null)
        {
            return mapped;
        }

        int value = (int)key;
        int firstLetter = (int)Keycode.A;
        int lastLetter = (int)Keycode.Z;
        return value >= firstLetter && value <= lastLetter
            ? (ViewportKey)((int)ViewportKey.A + (value - firstLetter))
            : null;
    }
}
#endif

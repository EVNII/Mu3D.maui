#if ANDROID
using Android.Views;
using PlatformView = Android.Views.View;

namespace Mu3D.GalleryApp.Pages;

public sealed partial class PointerInputBridge
{
    private PlatformView? platformPointerView;
    private int activePlatformPointerId = -1;
    private PointerIcon? platformPreviousPointerIcon;
    private bool platformSystemCursorHidden;

    private partial void AttachPlatformInput()
    {
        if (SceneView?.Handler?.PlatformView is not PlatformView nativeView)
        {
            return;
        }
        platformPointerView = nativeView;
        nativeView.Touch += OnPlatformTouch;
        nativeView.Hover += OnPlatformHover;
    }

    private partial void DetachPlatformInput()
    {
        PublishCanceled();
        PublishExited();
        if (platformPointerView is { } nativeView)
        {
            nativeView.Touch -= OnPlatformTouch;
            nativeView.Hover -= OnPlatformHover;
        }
        ReleasePlatformPointer();
        SetPlatformSystemCursorHidden(false);
        platformPointerView = null;
    }

    private void OnPlatformTouch(object? sender, PlatformView.TouchEventArgs e)
    {
        _ = sender;
        MotionEvent? motion = e.Event;
        PlatformView? nativeView = platformPointerView;
        if (motion is null || nativeView is null)
        {
            return;
        }

        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
                ProcessPointerDown(motion, nativeView, e);
                break;
            case MotionEventActions.Move:
                ProcessPointerMove(motion, nativeView, e);
                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                ProcessPointerUp(motion, nativeView, e);
                break;
            case MotionEventActions.Cancel:
                if (activePlatformPointerId >= 0)
                {
                    PublishCanceled();
                    ReleasePlatformPointer();
                    SetPlatformSystemCursorHidden(false);
                    e.Handled = true;
                }
                break;
            default:
                e.Handled = activePlatformPointerId >= 0;
                break;
        }
    }

    private void ProcessPointerDown(
        MotionEvent motion,
        PlatformView nativeView,
        PlatformView.TouchEventArgs e)
    {
        int index = motion.ActionIndex;
        if (activePlatformPointerId >= 0 ||
            (IsDirectTouchPressSuppressed &&
                motion.GetToolType(index) == MotionEventToolType.Finger) ||
            !IsSupportedPrimaryContact(motion, index) ||
            !PublishMotion(ApplicationPointerPhase.Pressed, motion, index, nativeView))
        {
            return;
        }
        activePlatformPointerId = motion.GetPointerId(index);
        SetPlatformSystemCursorHidden(IsPenTool(motion.GetToolType(index)));
        nativeView.Parent?.RequestDisallowInterceptTouchEvent(true);
        e.Handled = true;
    }

    private void ProcessPointerMove(
        MotionEvent motion,
        PlatformView nativeView,
        PlatformView.TouchEventArgs e)
    {
        if (activePlatformPointerId < 0)
        {
            return;
        }
        int index = motion.FindPointerIndex(activePlatformPointerId);
        if (index < 0 ||
            !PublishMotion(ApplicationPointerPhase.Moved, motion, index, nativeView))
        {
            PublishCanceled();
            ReleasePlatformPointer();
            SetPlatformSystemCursorHidden(false);
        }
        else
        {
            SetPlatformSystemCursorHidden(IsPenTool(motion.GetToolType(index)));
        }
        e.Handled = true;
    }

    private void OnPlatformHover(object? sender, PlatformView.HoverEventArgs e)
    {
        _ = sender;
        MotionEvent? motion = e.Event;
        PlatformView? nativeView = platformPointerView;
        if (motion is null || nativeView is null)
        {
            return;
        }
        if (motion.ActionMasked == MotionEventActions.HoverExit)
        {
            bool wasDrawingCursor = platformSystemCursorHidden;
            PublishExited();
            SetPlatformSystemCursorHidden(false);
            e.Handled = wasDrawingCursor;
            return;
        }
        if (motion.ActionMasked is not MotionEventActions.HoverEnter and
            not MotionEventActions.HoverMove)
        {
            return;
        }
        int index = Math.Max(0, motion.ActionIndex);
        if (index >= motion.PointerCount || !IsPenTool(motion.GetToolType(index)))
        {
            PublishExited();
            SetPlatformSystemCursorHidden(false);
            return;
        }
        SetPlatformSystemCursorHidden(true);
        if (!PublishMotion(ApplicationPointerPhase.Hovered, motion, index, nativeView))
        {
            PublishExited();
            SetPlatformSystemCursorHidden(false);
            return;
        }
        e.Handled = true;
    }

    private void ProcessPointerUp(
        MotionEvent motion,
        PlatformView nativeView,
        PlatformView.TouchEventArgs e)
    {
        if (activePlatformPointerId < 0)
        {
            return;
        }
        int index = motion.ActionIndex;
        if (motion.GetPointerId(index) == activePlatformPointerId)
        {
            if (!PublishMotion(ApplicationPointerPhase.Released, motion, index, nativeView))
            {
                PublishCanceled();
            }
            ReleasePlatformPointer();
        }
        e.Handled = true;
    }

    private bool PublishMotion(
        ApplicationPointerPhase phase,
        MotionEvent motion,
        int index,
        PlatformView nativeView)
    {
        MotionEventToolType toolType = motion.GetToolType(index);
        ApplicationPointerDeviceKind deviceKind = toolType switch
        {
            MotionEventToolType.Mouse => ApplicationPointerDeviceKind.Mouse,
            MotionEventToolType.Finger => ApplicationPointerDeviceKind.Touch,
            MotionEventToolType.Stylus or MotionEventToolType.Eraser =>
                ApplicationPointerDeviceKind.Pen,
            _ => ApplicationPointerDeviceKind.Unknown,
        };
        float? pressure = deviceKind == ApplicationPointerDeviceKind.Mouse
            ? null
            : motion.GetPressure(index);
        float? tiltX = null;
        float? tiltY = null;
        if (deviceKind == ApplicationPointerDeviceKind.Pen)
        {
            float tilt = motion.GetAxisValue(Axis.Tilt, index) * (180f / MathF.PI);
            float orientation = motion.GetOrientation(index);
            tiltX = tilt * MathF.Sin(orientation);
            tiltY = tilt * MathF.Cos(orientation);
        }
        MotionEventButtonState buttons = motion.ButtonState;
        bool barrelButton = (buttons & (MotionEventButtonState.StylusPrimary |
            MotionEventButtonState.StylusSecondary)) != 0;
        return PublishPlatformSample(
            phase,
            deviceKind,
            motion.GetX(index),
            motion.GetY(index),
            nativeView.Width,
            nativeView.Height,
            pressure,
            tiltX,
            tiltY,
            toolType == MotionEventToolType.Eraser,
            barrelButton);
    }

    private static bool IsSupportedPrimaryContact(MotionEvent motion, int index)
    {
        MotionEventToolType toolType = motion.GetToolType(index);
        return toolType switch
        {
            MotionEventToolType.Finger or MotionEventToolType.Stylus or MotionEventToolType.Eraser =>
                true,
            MotionEventToolType.Mouse =>
                (motion.ButtonState & MotionEventButtonState.Primary) != 0,
            _ => false,
        };
    }

    private static bool IsPenTool(MotionEventToolType toolType) =>
        toolType is MotionEventToolType.Stylus or MotionEventToolType.Eraser;

    private void ReleasePlatformPointer()
    {
        activePlatformPointerId = -1;
        platformPointerView?.Parent?.RequestDisallowInterceptTouchEvent(false);
    }

    private void SetPlatformSystemCursorHidden(bool hidden)
    {
        PlatformView? nativeView = platformPointerView;
        if (nativeView is null || platformSystemCursorHidden == hidden)
        {
            return;
        }
        if (hidden)
        {
            Android.Content.Context? context = nativeView.Context;
            if (context is null)
            {
                return;
            }
            platformPreviousPointerIcon = nativeView.PointerIcon;
            nativeView.PointerIcon = PointerIcon.GetSystemIcon(
                context,
                PointerIconType.Null);
        }
        else
        {
            nativeView.PointerIcon = platformPreviousPointerIcon;
            platformPreviousPointerIcon = null;
        }
        platformSystemCursorHidden = hidden;
    }
}
#endif

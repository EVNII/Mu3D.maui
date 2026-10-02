#if ANDROID
using System.Numerics;
using Android.Views;
using PlatformView = Android.Views.View;

namespace Mu3D.Maui.Toolkit.Controls;

public sealed partial class TransformGizmoPointerBehavior
{
    private PlatformView? platformPointerView;
    private int activePlatformPointerId = -1;

    private partial void AttachPlatformInput()
    {
        DetachPlatformInput();
        if (sceneView?.Handler?.PlatformView is not PlatformView nativeView)
        {
            return;
        }
        platformPointerView = nativeView;
        nativeView.Touch += OnPlatformTouch;
    }

    private partial void DetachPlatformInput()
    {
        PlatformView? nativeView = platformPointerView;
        if (nativeView is not null)
        {
            nativeView.Touch -= OnPlatformTouch;
        }
        ReleasePlatformPointerCapture();
        platformPointerView = null;
    }

    private partial void ReleasePlatformPointerCapture()
    {
        activePlatformPointerId = -1;
        platformPointerView?.Parent?.RequestDisallowInterceptTouchEvent(false);
    }

    private static partial bool GetPlatformPointerInputAvailable() => true;

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
                ProcessPlatformPointerDown(motion, nativeView, e);
                break;
            case MotionEventActions.Move:
                ProcessPlatformPointerMove(motion, nativeView, e);
                break;
            case MotionEventActions.Up:
            case MotionEventActions.PointerUp:
                ProcessPlatformPointerUp(motion, nativeView, e);
                break;
            case MotionEventActions.Cancel:
                if (activePlatformPointerId >= 0)
                {
                    ProcessPointerCanceled();
                    ReleasePlatformPointerCapture();
                    e.Handled = true;
                }
                break;
            default:
                if (activePlatformPointerId >= 0)
                {
                    e.Handled = true;
                }
                break;
        }
    }

    private void ProcessPlatformPointerDown(
        MotionEvent motion,
        PlatformView nativeView,
        PlatformView.TouchEventArgs e)
    {
        int index = motion.ActionIndex;
        if (activePlatformPointerId >= 0 || !IsSupportedPointer(motion, index) ||
            !TryMapPlatformPosition(motion, index, nativeView, out Vector2 position) ||
            !ProcessPointerPressed(position))
        {
            return;
        }
        activePlatformPointerId = motion.GetPointerId(index);
        nativeView.Parent?.RequestDisallowInterceptTouchEvent(true);
        e.Handled = true;
    }

    private void ProcessPlatformPointerMove(
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
            !TryMapPlatformPosition(motion, index, nativeView, out Vector2 position) ||
            !ProcessPointerMoved(position))
        {
            ProcessPointerCanceled();
            ReleasePlatformPointerCapture();
            e.Handled = true;
            return;
        }
        e.Handled = true;
    }

    private void ProcessPlatformPointerUp(
        MotionEvent motion,
        PlatformView nativeView,
        PlatformView.TouchEventArgs e)
    {
        if (activePlatformPointerId < 0)
        {
            return;
        }
        int index = motion.ActionIndex;
        if (motion.GetPointerId(index) != activePlatformPointerId)
        {
            e.Handled = true;
            return;
        }

        if (TryMapPlatformPosition(motion, index, nativeView, out Vector2 position))
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

    private static bool IsSupportedPointer(MotionEvent motion, int index)
    {
        MotionEventToolType toolType = motion.GetToolType(index);
        if (toolType == MotionEventToolType.Finger)
        {
            return true;
        }
        return toolType == MotionEventToolType.Mouse &&
            (motion.ButtonState & MotionEventButtonState.Primary) != 0;
    }

    private bool TryMapPlatformPosition(
        MotionEvent motion,
        int index,
        PlatformView nativeView,
        out Vector2 position)
    {
        position = default;
        if (sceneView is null || nativeView.Width <= 0 || nativeView.Height <= 0 ||
            sceneView.PixelWidth == 0 || sceneView.PixelHeight == 0)
        {
            return false;
        }
        try
        {
            position = TransformGizmoPointerState.MapViewportPosition(
                motion.GetX(index),
                motion.GetY(index),
                nativeView.Width,
                nativeView.Height,
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

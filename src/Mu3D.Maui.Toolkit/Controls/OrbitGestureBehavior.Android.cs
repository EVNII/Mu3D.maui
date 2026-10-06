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
    private readonly ViewportTouchTransformState platformTouchTransform = new();
    private bool platformTouchSequenceActive;
    private bool platformNativeTouchSequence;
    private bool platformNativeMultiTouchActive;
    private bool platformNativeOnePointerDragActive;
    private int platformNativeOnePointerId = -1;
    private bool platformParentInterceptionDisallowed;

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
        EndPlatformTouchSequence(GestureStatus.Canceled);
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

    private static partial bool GetPlatformUsesNativePinchInput() => true;

    private void OnPlatformTouch(object? sender, PlatformView.TouchEventArgs e)
    {
        _ = sender;
        MotionEvent? motion = e.Event;
        if (motion is null)
        {
            return;
        }
        if (!inputAttached || sceneView is not { IsEnabled: true, InputTransparent: false })
        {
            if (platformButtonDragActive)
            {
                _ = ProcessActivePointerButtonDrag(GestureStatus.Canceled, 0d, 0d);
                platformButtonDragActive = false;
            }
            EndPlatformTouchSequence(GestureStatus.Canceled);
            return;
        }

        switch (motion.ActionMasked)
        {
            case MotionEventActions.Down:
                EndPlatformTouchSequence(GestureStatus.Canceled);
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
                platformTouchSequenceActive =
                    motion.GetToolType(0) == MotionEventToolType.Finger &&
                    TouchscreenInput.IsEnabled && !platformSystemNavigationGestureActive &&
                    (TouchscreenInput.OneFingerDragAction != ViewportDragAction.None ||
                     TouchscreenInput.TwoFingerDragAction != ViewportDragAction.None ||
                     TouchscreenInput.PinchAction == ViewportScalarAction.Dolly);
                if (platformTouchSequenceActive)
                {
                    // A pinch-only setup has no MAUI recognizer to consume Down. Keep this
                    // native view in the touch sequence so it receives the second pointer.
                    e.Handled = true;
                    if (TouchscreenInput.OneFingerDragAction != ViewportDragAction.None)
                    {
                        SetPlatformParentInterception(true);
                    }
                }
                break;
            case MotionEventActions.PointerDown when platformTouchSequenceActive:
                UpdatePlatformFingerInput(motion, excludedIndex: -1, rebase: true);
                e.Handled |= platformNativeTouchSequence;
                break;
            case MotionEventActions.Move when platformButtonDragActive:
                _ = ProcessActivePointerButtonDrag(GestureStatus.Running, motion.GetX(), motion.GetY());
                e.Handled = true;
                break;
            case MotionEventActions.Move when platformTouchSequenceActive:
                UpdatePlatformFingerInput(motion, excludedIndex: -1, rebase: false);
                e.Handled |= platformNativeTouchSequence;
                break;
            case MotionEventActions.PointerUp when platformTouchSequenceActive:
                // Android includes the departing pointer in this event's PointerCount.
                UpdatePlatformFingerInput(motion, motion.ActionIndex, rebase: true);
                e.Handled |= platformNativeTouchSequence;
                break;
            case MotionEventActions.Up:
                if (platformButtonDragActive)
                {
                    _ = ProcessActivePointerButtonDrag(GestureStatus.Running, motion.GetX(), motion.GetY());
                    _ = ProcessActivePointerButtonDrag(GestureStatus.Completed, motion.GetX(), motion.GetY());
                    platformButtonDragActive = false;
                    e.Handled = true;
                }
                else if (platformNativeOnePointerDragActive)
                {
                    UpdatePlatformFingerInput(motion, excludedIndex: -1, rebase: false);
                }
                e.Handled |= platformNativeTouchSequence || platformTouchSequenceActive;
                EndPlatformTouchSequence(GestureStatus.Completed);
                platformSystemNavigationGestureActive = false;
                break;
            case MotionEventActions.Cancel:
                if (platformButtonDragActive)
                {
                    _ = ProcessActivePointerButtonDrag(GestureStatus.Canceled, motion.GetX(), motion.GetY());
                    platformButtonDragActive = false;
                    e.Handled = true;
                }
                e.Handled |= platformNativeTouchSequence || platformTouchSequenceActive;
                EndPlatformTouchSequence(GestureStatus.Canceled);
                platformSystemNavigationGestureActive = false;
                break;
        }
    }

    private void UpdatePlatformFingerInput(MotionEvent motion, int excludedIndex, bool rebase)
    {
        if (!TouchscreenInput.IsEnabled || platformSystemNavigationGestureActive)
        {
            EndPlatformTouchSequence(GestureStatus.Canceled);
            return;
        }
        if ((platformNativeMultiTouchActive && !multiTouchGestureState.IsActive) ||
            (platformNativeOnePointerDragActive && !gestureState.IsActive))
        {
            // A higher-priority tool revoked the camera lease. Do not reacquire it during
            // this same touch sequence, or leave a ScrollView interception request behind.
            EndPlatformTouchSequence(GestureStatus.Canceled);
            platformNativeTouchSequence = true;
            return;
        }

        int firstIndex = FindPlatformFinger(motion, platformTouchTransform.FirstPointerId, excludedIndex);
        int secondIndex = FindPlatformFinger(motion, platformTouchTransform.SecondPointerId, excludedIndex);
        if (firstIndex < 0 || secondIndex < 0)
        {
            firstIndex = -1;
            secondIndex = -1;
            for (int index = 0; index < motion.PointerCount; index++)
            {
                if (index == excludedIndex || motion.GetToolType(index) != MotionEventToolType.Finger)
                {
                    continue;
                }
                if (firstIndex < 0)
                {
                    firstIndex = index;
                }
                else
                {
                    secondIndex = index;
                    break;
                }
            }
        }

        double density = platformInputView?.Resources?.DisplayMetrics?.Density ?? 1d;
        if (!double.IsFinite(density) || density <= 0d)
        {
            density = 1d;
        }
        if (secondIndex >= 0 &&
            (TouchscreenInput.TwoFingerDragAction != ViewportDragAction.None ||
             TouchscreenInput.PinchAction == ViewportScalarAction.Dolly))
        {
            if (!platformTouchTransform.TryUpdate(
                motion.GetPointerId(firstIndex), motion.GetX(firstIndex) / density, motion.GetY(firstIndex) / density,
                motion.GetPointerId(secondIndex), motion.GetX(secondIndex) / density, motion.GetY(secondIndex) / density,
                rebase, out ViewportTouchTransformSample sample))
            {
                return;
            }
            if (!platformNativeTouchSequence || platformNativeOnePointerDragActive)
            {
                ProcessPan(GestureStatus.Canceled, 0d, 0d);
                platformNativeOnePointerDragActive = false;
                platformNativeOnePointerId = -1;
                platformNativeTouchSequence = true;
            }
            if (sample.IsRebased)
            {
                ProcessTwoFingerPan(GestureStatus.Started, sample.CenterX, sample.CenterY);
                ProcessPinch(GestureStatus.Started, 1d, isIncremental: true);
                platformNativeMultiTouchActive = multiTouchGestureState.IsActive;
            }
            else if (platformNativeMultiTouchActive)
            {
                // Both components describe this exact MotionEvent and share one camera
                // lease. No ScaleGestureDetector can suppress the centroid movement.
                ProcessTwoFingerPan(GestureStatus.Running, sample.CenterX, sample.CenterY);
                ProcessPinch(GestureStatus.Running, sample.ScaleRatio, isIncremental: true);
            }
            SetPlatformParentInterception(platformNativeMultiTouchActive);
            return;
        }

        if (platformNativeMultiTouchActive)
        {
            ProcessTwoFingerPan(GestureStatus.Completed, 0d, 0d);
            ProcessPinch(GestureStatus.Completed, 1d, isIncremental: true);
            platformNativeMultiTouchActive = false;
        }
        platformTouchTransform.Reset();
        if (!platformNativeTouchSequence)
        {
            return;
        }
        if (firstIndex < 0 || TouchscreenInput.OneFingerDragAction == ViewportDragAction.None)
        {
            if (platformNativeOnePointerDragActive)
            {
                ProcessPan(GestureStatus.Completed, 0d, 0d);
                platformNativeOnePointerDragActive = false;
            }
            SetPlatformParentInterception(false);
            return;
        }

        int pointerId = motion.GetPointerId(firstIndex);
        double x = motion.GetX(firstIndex) / density;
        double y = motion.GetY(firstIndex) / density;
        if (!platformNativeOnePointerDragActive || platformNativeOnePointerId != pointerId || rebase)
        {
            // Resume a remaining finger from its current position, never from MAUI's
            // totals that predate the two-finger gesture or from a different pointer ID.
            ProcessPan(GestureStatus.Completed, 0d, 0d);
            ProcessPan(GestureStatus.Started, x, y);
            platformNativeOnePointerDragActive = gestureState.IsActive;
            platformNativeOnePointerId = pointerId;
        }
        else
        {
            ProcessPan(GestureStatus.Running, x, y);
        }
        SetPlatformParentInterception(platformNativeOnePointerDragActive);
    }

    private static int FindPlatformFinger(MotionEvent motion, int pointerId, int excludedIndex)
    {
        int index = pointerId >= 0 ? motion.FindPointerIndex(pointerId) : -1;
        return index >= 0 && index != excludedIndex &&
            motion.GetToolType(index) == MotionEventToolType.Finger ? index : -1;
    }

    private void EndPlatformTouchSequence(GestureStatus status)
    {
        if (platformNativeMultiTouchActive)
        {
            ProcessTwoFingerPan(status, 0d, 0d);
            ProcessPinch(status, 1d, isIncremental: true);
        }
        if (platformNativeOnePointerDragActive)
        {
            ProcessPan(status, 0d, 0d);
        }
        else if (platformTouchSequenceActive && !platformNativeMultiTouchActive)
        {
            ProcessPan(status, 0d, 0d);
        }
        ClearPlatformTouchSequence();
    }

    private void ClearPlatformTouchSequence()
    {
        SetPlatformParentInterception(false);
        platformTouchTransform.Reset();
        platformTouchSequenceActive = false;
        platformNativeTouchSequence = false;
        platformNativeMultiTouchActive = false;
        platformNativeOnePointerDragActive = false;
        platformNativeOnePointerId = -1;
    }

    private void SetPlatformParentInterception(bool disallow)
    {
        if (platformParentInterceptionDisallowed == disallow)
        {
            return;
        }
        platformInputView?.Parent?.RequestDisallowInterceptTouchEvent(disallow);
        platformParentInterceptionDisallowed = disallow;
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

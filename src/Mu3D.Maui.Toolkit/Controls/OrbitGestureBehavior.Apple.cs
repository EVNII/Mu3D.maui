#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;

namespace Mu3D.Maui.Toolkit.Controls;

public sealed partial class ViewportNavigationBehavior
{
    private const double AppleScrollPointsPerDetent = 40d;
    private UIView? platformInputView;
    private AppleSystemGestureArbitration.AppleSystemNavigationBlocker? platformNavigationBlocker;
    private UIPanGestureRecognizer? platformOnePointerInputObserver;
    private AppleOnePointerInputDelegate? platformOnePointerInputDelegate;
    private UIPinchGestureRecognizer? platformPinchRecognizer;
    private ApplePinchGestureDelegate? platformPinchDelegate;
    private UIPanGestureRecognizer? platformScrollRecognizer;
    private UIPanGestureRecognizer? platformTrackpadPanRecognizer;
    private AppleScrollGestureDelegate? platformScrollDelegate;
    private AppleRightButtonPanGestureRecognizer? platformRightButtonRecognizer;
    private AppleRightButtonGestureDelegate? platformRightButtonDelegate;
    private UITapGestureRecognizer? platformFocusRecognizer;
    private AppleFocusGestureDelegate? platformFocusDelegate;
    private AppleKeyboardResponderView? platformKeyboardResponder;

    private partial void AttachPlatformInput()
    {
        DetachPlatformInput();
        if (sceneView?.Handler?.PlatformView is not UIView nativeView)
        {
            return;
        }

        platformInputView = nativeView;
        platformNavigationBlocker = AppleSystemGestureArbitration.CreateNavigationBlocker(
            nativeView,
            blocksDirectPan: TouchscreenInput.IsEnabled &&
                TouchscreenInput.OneFingerDragAction != ViewportDragAction.None,
            blocksIndirectPan: TrackpadInput.IsEnabled &&
                TrackpadInput.TwoFingerDragAction != ViewportDragAction.None);
        if ((MouseInput.IsEnabled &&
                MouseInput.LeftButtonDragAction != ViewportDragAction.None) ||
            (TouchscreenInput.IsEnabled &&
                TouchscreenInput.OneFingerDragAction != ViewportDragAction.None))
        {
            platformOnePointerInputDelegate = new AppleOnePointerInputDelegate(this);
            platformOnePointerInputObserver = new UIPanGestureRecognizer
            {
                CancelsTouchesInView = false,
                Delegate = platformOnePointerInputDelegate,
                MaximumNumberOfTouches = 1,
                MinimumNumberOfTouches = 1,
            };
            nativeView.AddGestureRecognizer(platformOnePointerInputObserver);
        }
        if ((TrackpadInput.IsEnabled &&
                TrackpadInput.PinchAction == ViewportScalarAction.Dolly) ||
            (TouchscreenInput.IsEnabled &&
                TouchscreenInput.PinchAction == ViewportScalarAction.Dolly))
        {
            platformPinchDelegate = new ApplePinchGestureDelegate();
            platformPinchRecognizer = new UIPinchGestureRecognizer(OnPlatformPinch)
            {
                CancelsTouchesInView = false,
                Delegate = platformPinchDelegate,
            };
            nativeView.AddGestureRecognizer(platformPinchRecognizer);
        }
        if (MouseInput.IsEnabled && MouseInput.WheelAction == ViewportScalarAction.Dolly)
        {
            platformScrollDelegate = new AppleScrollGestureDelegate(nativeView);
            platformScrollRecognizer = new UIPanGestureRecognizer(OnPlatformScroll)
            {
                AllowedScrollTypesMask = !TrackpadInput.IsEnabled ||
                    TrackpadInput.TwoFingerDragAction == ViewportDragAction.None
                    ? UIScrollTypeMask.Continuous | UIScrollTypeMask.Discrete
                    : UIScrollTypeMask.Discrete,
                CancelsTouchesInView = false,
                Delegate = platformScrollDelegate,
            };
            nativeView.AddGestureRecognizer(platformScrollRecognizer);
        }
        if (TrackpadInput.IsEnabled &&
            TrackpadInput.TwoFingerDragAction != ViewportDragAction.None)
        {
            platformScrollDelegate ??= new AppleScrollGestureDelegate(nativeView);
            platformTrackpadPanRecognizer = new UIPanGestureRecognizer(OnPlatformTrackpadPan)
            {
                AllowedScrollTypesMask = UIScrollTypeMask.Continuous,
                CancelsTouchesInView = false,
                Delegate = platformScrollDelegate,
            };
            nativeView.AddGestureRecognizer(platformTrackpadPanRecognizer);
        }
        if (KeyboardInput.IsEnabled)
        {
            platformKeyboardResponder = new AppleKeyboardResponderView(ProcessKeyboardKey)
            {
                AccessibilityElementsHidden = true,
                BackgroundColor = UIColor.Clear,
                Frame = CGRect.Empty,
                UserInteractionEnabled = false,
            };
            nativeView.AddSubview(platformKeyboardResponder);
            platformFocusDelegate = new AppleFocusGestureDelegate();
            platformFocusRecognizer = new UITapGestureRecognizer(FocusPlatformKeyboard)
            {
                CancelsTouchesInView = false,
                DelaysTouchesBegan = false,
                DelaysTouchesEnded = false,
                Delegate = platformFocusDelegate,
            };
            nativeView.AddGestureRecognizer(platformFocusRecognizer);
        }
        if (MouseInput.IsEnabled &&
            MouseInput.RightButtonDragAction != ViewportDragAction.None)
        {
            platformRightButtonDelegate = new AppleRightButtonGestureDelegate();
            platformRightButtonRecognizer = new AppleRightButtonPanGestureRecognizer(
                OnPlatformRightButtonDrag)
            {
                CancelsTouchesInView = false,
                Delegate = platformRightButtonDelegate,
                MaximumNumberOfTouches = 1,
                MinimumNumberOfTouches = 1,
            };
            nativeView.AddGestureRecognizer(platformRightButtonRecognizer);
            UIGestureRecognizer[]? existingRecognizers = nativeView.GestureRecognizers;
            if (existingRecognizers is not null)
            {
                foreach (UIGestureRecognizer existing in existingRecognizers)
                {
                    if (existing is UIPanGestureRecognizer and not UIScreenEdgePanGestureRecognizer &&
                        !ReferenceEquals(existing, platformRightButtonRecognizer) &&
                        !ReferenceEquals(existing, platformScrollRecognizer) &&
                        !ReferenceEquals(existing, platformTrackpadPanRecognizer))
                    {
                        existing.RequireGestureRecognizerToFail(platformRightButtonRecognizer);
                    }
                }
            }
        }
    }

    private partial void DetachPlatformInput()
    {
        UIView? nativeView = platformInputView;
        platformInputView = null;

        platformNavigationBlocker?.Dispose();
        platformNavigationBlocker = null;

        UIPanGestureRecognizer? primaryInputObserver = platformOnePointerInputObserver;
        platformOnePointerInputObserver = null;
        if (primaryInputObserver is not null)
        {
            nativeView?.RemoveGestureRecognizer(primaryInputObserver);
            primaryInputObserver.Dispose();
        }
        platformOnePointerInputDelegate?.Dispose();
        platformOnePointerInputDelegate = null;

        UIPinchGestureRecognizer? pinchRecognizer = platformPinchRecognizer;
        platformPinchRecognizer = null;
        if (pinchRecognizer is not null)
        {
            multiTouchGestureState.EndPinch();
            nativeView?.RemoveGestureRecognizer(pinchRecognizer);
            pinchRecognizer.Dispose();
        }
        platformPinchDelegate?.Dispose();
        platformPinchDelegate = null;

        UIPanGestureRecognizer? scrollRecognizer = platformScrollRecognizer;
        platformScrollRecognizer = null;
        if (scrollRecognizer is not null)
        {
            nativeView?.RemoveGestureRecognizer(scrollRecognizer);
            scrollRecognizer.Dispose();
        }

        UIPanGestureRecognizer? trackpadPanRecognizer = platformTrackpadPanRecognizer;
        platformTrackpadPanRecognizer = null;
        if (trackpadPanRecognizer is not null)
        {
            _ = ProcessActivePointerButtonDrag(GestureStatus.Canceled, 0d, 0d);
            nativeView?.RemoveGestureRecognizer(trackpadPanRecognizer);
            trackpadPanRecognizer.Dispose();
        }
        platformScrollDelegate?.Dispose();
        platformScrollDelegate = null;

        AppleRightButtonPanGestureRecognizer? rightButtonRecognizer = platformRightButtonRecognizer;
        platformRightButtonRecognizer = null;
        if (rightButtonRecognizer is not null)
        {
            _ = ProcessActivePointerButtonDrag(GestureStatus.Canceled, 0d, 0d);
            nativeView?.RemoveGestureRecognizer(rightButtonRecognizer);
            rightButtonRecognizer.Dispose();
        }
        platformRightButtonDelegate?.Dispose();
        platformRightButtonDelegate = null;

        UITapGestureRecognizer? focusRecognizer = platformFocusRecognizer;
        platformFocusRecognizer = null;
        if (focusRecognizer is not null)
        {
            nativeView?.RemoveGestureRecognizer(focusRecognizer);
            focusRecognizer.Dispose();
        }
        platformFocusDelegate?.Dispose();
        platformFocusDelegate = null;

        AppleKeyboardResponderView? keyboardResponder = platformKeyboardResponder;
        platformKeyboardResponder = null;
        if (keyboardResponder is not null)
        {
            if (keyboardResponder.IsFirstResponder)
            {
                keyboardResponder.ResignFirstResponder();
            }
            keyboardResponder.RemoveFromSuperview();
            keyboardResponder.Dispose();
        }
    }

    private static partial bool GetPlatformWheelInputAvailable() => true;

    private static partial bool GetPlatformKeyboardInputAvailable() => true;

    private static partial bool GetPlatformMouseButtonInputAvailable() => true;

    private partial bool GetPlatformSystemNavigationGestureActive() => false;

    private partial ViewportDragAction GetPlatformOnePointerDragAction()
    {
        if (platformOnePointerInputDelegate?.CurrentAction is { } currentAction)
        {
            return currentAction;
        }
#if MACCATALYST
        return MouseInput.IsEnabled
            ? MouseInput.LeftButtonDragAction
            : ViewportDragAction.None;
#else
        return TouchscreenInput.IsEnabled
            ? TouchscreenInput.OneFingerDragAction
            : ViewportDragAction.None;
#endif
    }

    private static partial bool GetPlatformUsesNativePinchInput() => true;

    private void OnPlatformPinch()
    {
        UIPinchGestureRecognizer? recognizer = platformPinchRecognizer;
        if (recognizer is null)
        {
            return;
        }

#if MACCATALYST
        const bool isTrackpad = true;
#else
        bool isTrackpad = platformPinchDelegate?.ReceivedIndirectPointer == true;
#endif
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                ProcessPinch(GestureStatus.Started, recognizer.Scale, isTrackpad);
                break;
            case UIGestureRecognizerState.Changed:
                ProcessPinch(GestureStatus.Running, recognizer.Scale, isTrackpad);
                break;
            case UIGestureRecognizerState.Ended:
                ProcessPinch(GestureStatus.Running, recognizer.Scale, isTrackpad);
                ProcessPinch(GestureStatus.Completed, recognizer.Scale, isTrackpad);
                platformPinchDelegate?.ResetInputKind();
                break;
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                ProcessPinch(GestureStatus.Canceled, recognizer.Scale, isTrackpad);
                platformPinchDelegate?.ResetInputKind();
                break;
        }
    }

    private void OnPlatformScroll()
    {
        UIPanGestureRecognizer? recognizer = platformScrollRecognizer;
        UIView? nativeView = platformInputView;
        if (recognizer is null || nativeView is null)
        {
            return;
        }

        CGPoint translation = recognizer.TranslationInView(nativeView);
        recognizer.SetTranslation(CGPoint.Empty, nativeView);
        float detents = ViewportNavigationInputState.NormalizeWheelDelta(
            (float)translation.Y,
            (float)AppleScrollPointsPerDetent,
            invert: true);
        if (detents != 0f && ProcessWheel(detents) &&
            KeyboardInput.IsEnabled)
        {
            FocusPlatformKeyboard();
        }
    }

    private void OnPlatformTrackpadPan()
    {
        UIPanGestureRecognizer? recognizer = platformTrackpadPanRecognizer;
        UIView? nativeView = platformInputView;
        if (recognizer is null || nativeView is null)
        {
            return;
        }

        CGPoint translation = recognizer.TranslationInView(nativeView);
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                _ = ProcessPointerButtonDrag(
                    GestureStatus.Started,
                    0d,
                    0d,
                    TrackpadInput.TwoFingerDragAction);
                break;
            case UIGestureRecognizerState.Changed:
                _ = ProcessPointerButtonDrag(
                    GestureStatus.Running,
                    translation.X,
                    translation.Y,
                    TrackpadInput.TwoFingerDragAction);
                break;
            case UIGestureRecognizerState.Ended:
                _ = ProcessPointerButtonDrag(
                    GestureStatus.Running,
                    translation.X,
                    translation.Y,
                    TrackpadInput.TwoFingerDragAction);
                _ = ProcessPointerButtonDrag(
                    GestureStatus.Completed,
                    translation.X,
                    translation.Y,
                    TrackpadInput.TwoFingerDragAction);
                break;
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                _ = ProcessPointerButtonDrag(
                    GestureStatus.Canceled,
                    translation.X,
                    translation.Y,
                    TrackpadInput.TwoFingerDragAction);
                break;
        }
    }

    private void FocusPlatformKeyboard()
    {
        AppleKeyboardResponderView? responder = platformKeyboardResponder;
        if (responder is not null && !responder.IsFirstResponder)
        {
            responder.BecomeFirstResponder();
        }
    }

    private void OnPlatformRightButtonDrag()
    {
        UIPanGestureRecognizer? recognizer = platformRightButtonRecognizer;
        UIView? nativeView = platformInputView;
        if (recognizer is null || nativeView is null)
        {
            return;
        }

        CGPoint position = recognizer.LocationInView(nativeView);
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                _ = ProcessPointerButtonDrag(
                    GestureStatus.Started,
                    position.X,
                    position.Y,
                    MouseInput.RightButtonDragAction);
                break;
            case UIGestureRecognizerState.Changed:
                _ = ProcessActivePointerButtonDrag(GestureStatus.Running, position.X, position.Y);
                break;
            case UIGestureRecognizerState.Ended:
                _ = ProcessActivePointerButtonDrag(GestureStatus.Running, position.X, position.Y);
                _ = ProcessActivePointerButtonDrag(GestureStatus.Completed, position.X, position.Y);
                break;
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                _ = ProcessActivePointerButtonDrag(GestureStatus.Canceled, position.X, position.Y);
                break;
        }
    }

    private sealed class AppleKeyboardResponderView(
        Func<ViewportKey, bool> processKey) : UIView
    {
        public override bool CanBecomeFirstResponder => true;

        public override void PressesBegan(NSSet<UIPress> presses, UIPressesEvent evt)
        {
            bool handled = false;
            foreach (UIPress press in presses)
            {
                UIKey? key = press.Key;
                if (key is null ||
                    (key.ModifierFlags & (
                        UIKeyModifierFlags.Alternate |
                        UIKeyModifierFlags.Command |
                        UIKeyModifierFlags.Control)) != 0)
                {
                    continue;
                }

                ViewportKey? mappedKey = MapPlatformKeyboardKey(key.KeyCode);
                if (mappedKey is ViewportKey command && processKey(command))
                {
                    handled = true;
                }
            }
            if (!handled)
            {
                base.PressesBegan(presses, evt);
            }
        }

        private static ViewportKey? MapPlatformKeyboardKey(UIKeyboardHidUsage key)
        {
            ViewportKey? mapped = key switch
            {
                UIKeyboardHidUsage.KeyboardLeftArrow => ViewportKey.LeftArrow,
                UIKeyboardHidUsage.KeyboardRightArrow => ViewportKey.RightArrow,
                UIKeyboardHidUsage.KeyboardUpArrow => ViewportKey.UpArrow,
                UIKeyboardHidUsage.KeyboardDownArrow => ViewportKey.DownArrow,
                UIKeyboardHidUsage.KeyboardPageUp => ViewportKey.PageUp,
                UIKeyboardHidUsage.KeyboardPageDown => ViewportKey.PageDown,
                _ => null,
            };
            if (mapped is not null)
            {
                return mapped;
            }

            int value = (int)key;
            int firstLetter = (int)UIKeyboardHidUsage.KeyboardA;
            int lastLetter = (int)UIKeyboardHidUsage.KeyboardZ;
            return value >= firstLetter && value <= lastLetter
                ? (ViewportKey)((int)ViewportKey.A + (value - firstLetter))
                : null;
        }
    }

    private sealed class AppleScrollGestureDelegate(UIView nativeView) :
        UIGestureRecognizerDelegate
    {
        public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch)
        {
            _ = recognizer;
            _ = touch;
            return false;
        }

        public override bool ShouldRecognizeSimultaneously(
            UIGestureRecognizer gestureRecognizer,
            UIGestureRecognizer otherGestureRecognizer)
        {
            _ = gestureRecognizer;
            return AppleSystemGestureArbitration.IsGestureInsideView(
                otherGestureRecognizer,
                nativeView);
        }
    }

    private sealed class AppleOnePointerInputDelegate(ViewportNavigationBehavior owner) :
        UIGestureRecognizerDelegate
    {
        internal ViewportDragAction? CurrentAction { get; private set; }

        public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch)
        {
            _ = recognizer;
            CurrentAction = touch.Type == UITouchType.IndirectPointer
                ? owner.MouseInput.IsEnabled
                    ? owner.MouseInput.LeftButtonDragAction
                    : ViewportDragAction.None
                : owner.TouchscreenInput.IsEnabled
                    ? owner.TouchscreenInput.OneFingerDragAction
                    : ViewportDragAction.None;
            return false;
        }
    }

    private sealed class ApplePinchGestureDelegate : UIGestureRecognizerDelegate
    {
        internal bool ReceivedIndirectPointer { get; private set; }

        public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch)
        {
            _ = recognizer;
            ReceivedIndirectPointer |= touch.Type == UITouchType.IndirectPointer;
            return true;
        }

        internal void ResetInputKind() => ReceivedIndirectPointer = false;

        public override bool ShouldRecognizeSimultaneously(
            UIGestureRecognizer gestureRecognizer,
            UIGestureRecognizer otherGestureRecognizer)
        {
            _ = gestureRecognizer;
            _ = otherGestureRecognizer;
            return true;
        }
    }

    private sealed class AppleFocusGestureDelegate : UIGestureRecognizerDelegate
    {
        public override bool ShouldRecognizeSimultaneously(
            UIGestureRecognizer gestureRecognizer,
            UIGestureRecognizer otherGestureRecognizer)
        {
            _ = gestureRecognizer;
            _ = otherGestureRecognizer;
            return true;
        }
    }

    private sealed class AppleRightButtonGestureDelegate : UIGestureRecognizerDelegate
    {
        public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch)
        {
            _ = recognizer;
            return touch.Type == UITouchType.IndirectPointer;
        }

        public override bool ShouldBegin(UIGestureRecognizer recognizer) =>
            recognizer is AppleRightButtonPanGestureRecognizer rightButton &&
            (rightButton.IsRightButtonGesture ||
                (rightButton.ButtonMask & UIEventButtonMask.Secondary) != 0);

        public override bool ShouldRecognizeSimultaneously(
            UIGestureRecognizer gestureRecognizer,
            UIGestureRecognizer otherGestureRecognizer)
        {
            _ = gestureRecognizer;
            _ = otherGestureRecognizer;
            return true;
        }
    }

    private sealed class AppleRightButtonPanGestureRecognizer(Action action) :
        UIPanGestureRecognizer(action)
    {
        internal bool IsRightButtonGesture { get; private set; }

        public override void TouchesBegan(NSSet touches, UIEvent evt)
        {
            IsRightButtonGesture =
                (evt.ButtonMask & UIEventButtonMask.Secondary) != 0;
            base.TouchesBegan(touches, evt);
        }

        public override void Reset()
        {
            base.Reset();
            IsRightButtonGesture = false;
        }
    }
}
#endif

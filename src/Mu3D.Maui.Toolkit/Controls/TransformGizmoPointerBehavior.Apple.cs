#if IOS || MACCATALYST
using System.Numerics;
using CoreGraphics;
using UIKit;

namespace Mu3D.Maui.Toolkit.Controls;

public sealed partial class TransformGizmoPointerBehavior
{
    private UIView? platformPointerView;
    private UIPanGestureRecognizer? platformPointerRecognizer;
    private AppleGizmoPanDelegate? platformPointerDelegate;

    private partial void AttachPlatformInput()
    {
        DetachPlatformInput();
        if (sceneView?.Handler?.PlatformView is not UIView nativeView)
        {
            return;
        }

        platformPointerView = nativeView;
        platformPointerDelegate = new AppleGizmoPanDelegate(this);
        platformPointerRecognizer = new UIPanGestureRecognizer(OnPlatformPointerPan)
        {
            CancelsTouchesInView = true,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false,
            MinimumNumberOfTouches = 1,
            MaximumNumberOfTouches = 1,
            Delegate = platformPointerDelegate,
        };
        nativeView.AddGestureRecognizer(platformPointerRecognizer);
    }

    private partial void DetachPlatformInput()
    {
        UIView? nativeView = platformPointerView;
        UIPanGestureRecognizer? recognizer = platformPointerRecognizer;
        platformPointerRecognizer = null;
        if (recognizer is not null)
        {
            nativeView?.RemoveGestureRecognizer(recognizer);
            recognizer.Dispose();
        }
        platformPointerDelegate?.Dispose();
        platformPointerDelegate = null;
        platformPointerView = null;
    }

    private partial void ReleasePlatformPointerCapture()
    {
        UIPanGestureRecognizer? recognizer = platformPointerRecognizer;
        if (recognizer?.State is UIGestureRecognizerState.Began or UIGestureRecognizerState.Changed)
        {
            recognizer.Enabled = false;
            recognizer.Enabled = true;
        }
    }

    private static partial bool GetPlatformPointerInputAvailable() => true;

    private bool TryBeginPlatformPointer(UIGestureRecognizer recognizer)
    {
        UIView? nativeView = platformPointerView;
        if (nativeView is null || recognizer.NumberOfTouches != 1)
        {
            return false;
        }
        CGPoint location = recognizer.LocationInView(nativeView);
        if (recognizer is UIPanGestureRecognizer pan)
        {
            CGPoint translation = pan.TranslationInView(nativeView);
            location = new CGPoint(location.X - translation.X, location.Y - translation.Y);
        }
        return TryMapPlatformPosition(location, nativeView, out Vector2 position) &&
            ProcessPointerPressed(position);
    }

    private void OnPlatformPointerPan()
    {
        UIPanGestureRecognizer? recognizer = platformPointerRecognizer;
        UIView? nativeView = platformPointerView;
        if (recognizer is null || nativeView is null)
        {
            return;
        }
        CGPoint location = recognizer.LocationInView(nativeView);
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
            case UIGestureRecognizerState.Changed:
                if (!TryMapPlatformPosition(location, nativeView, out Vector2 moved) ||
                    !ProcessPointerMoved(moved))
                {
                    ProcessPointerCanceled();
                    ReleasePlatformPointerCapture();
                }
                break;
            case UIGestureRecognizerState.Ended:
                if (TryMapPlatformPosition(location, nativeView, out Vector2 released))
                {
                    ProcessPointerReleased(released);
                }
                else
                {
                    ProcessPointerCanceled();
                }
                break;
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                ProcessPointerCanceled();
                break;
        }
    }

    private bool TryMapPlatformPosition(
        CGPoint point,
        UIView nativeView,
        out Vector2 position)
    {
        position = default;
        CGSize bounds = nativeView.Bounds.Size;
        if (sceneView is null || bounds.Width <= 0d || bounds.Height <= 0d ||
            sceneView.PixelWidth == 0 || sceneView.PixelHeight == 0)
        {
            return false;
        }
        try
        {
            position = TransformGizmoPointerState.MapViewportPosition(
                point.X,
                point.Y,
                bounds.Width,
                bounds.Height,
                sceneView.PixelWidth,
                sceneView.PixelHeight);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private sealed class AppleGizmoPanDelegate(TransformGizmoPointerBehavior owner)
        : UIGestureRecognizerDelegate
    {
        public override bool ShouldBegin(UIGestureRecognizer recognizer) =>
            owner.TryBeginPlatformPointer(recognizer);

        public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch)
        {
            _ = recognizer;
            return touch.Type != UITouchType.Stylus;
        }
    }
}
#endif

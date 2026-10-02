#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using UIKit;

namespace Mu3D.GalleryApp.Pages;

public sealed partial class PointerInputBridge
{
    private UIView? platformPointerView;
    private UILongPressGestureRecognizer? platformPointerRecognizer;
    private UIHoverGestureRecognizer? platformHoverRecognizer;
    private ApplePointerDelegate? platformPointerDelegate;
    private ApplePenHoverDelegate? platformHoverDelegate;
    private ApplePointerStyleDelegate? platformPointerStyleDelegate;
    private UIPointerInteraction? platformPointerInteraction;
    private UITouch? platformTouch;
    private bool platformPenContactActive;
    private bool platformPenHoverAccepted;
    private float? platformLastPenHoverDistanceNormalized;
    private bool platformSystemCursorHidden;

    private partial void AttachPlatformInput()
    {
        if (SceneView?.Handler?.PlatformView is not UIView nativeView)
        {
            return;
        }
        platformPointerView = nativeView;
        platformPointerDelegate = new ApplePointerDelegate(this);
        platformPointerRecognizer = new UILongPressGestureRecognizer(OnPlatformPointerChanged)
        {
            MinimumPressDuration = 0d,
            AllowableMovement = (System.Runtime.InteropServices.NFloat)100000d,
            NumberOfTouchesRequired = 1,
            CancelsTouchesInView = true,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false,
            Delegate = platformPointerDelegate,
        };
        platformHoverDelegate = new ApplePenHoverDelegate(this);
        platformHoverRecognizer = new UIHoverGestureRecognizer(OnPlatformHoverChanged)
        {
            AllowedTouchTypes =
            [
                NSNumber.FromInt32((int)UITouchType.Stylus),
            ],
            CancelsTouchesInView = false,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false,
            Delegate = platformHoverDelegate,
        };
        platformPointerStyleDelegate = new ApplePointerStyleDelegate(this);
        platformPointerInteraction = new UIPointerInteraction(platformPointerStyleDelegate);
        nativeView.AddGestureRecognizer(platformPointerRecognizer);
        nativeView.AddGestureRecognizer(platformHoverRecognizer);
        nativeView.AddInteraction(platformPointerInteraction);
    }

    private partial void DetachPlatformInput()
    {
        PublishCanceled();
        PublishExited();
        platformPenContactActive = false;
        platformPenHoverAccepted = false;
        platformLastPenHoverDistanceNormalized = null;
        SetPlatformSystemCursorHidden(false);
        UILongPressGestureRecognizer? recognizer = platformPointerRecognizer;
        platformPointerRecognizer = null;
        if (recognizer is not null)
        {
            platformPointerView?.RemoveGestureRecognizer(recognizer);
            recognizer.Dispose();
        }
        UIHoverGestureRecognizer? hoverRecognizer = platformHoverRecognizer;
        platformHoverRecognizer = null;
        if (hoverRecognizer is not null)
        {
            platformPointerView?.RemoveGestureRecognizer(hoverRecognizer);
            hoverRecognizer.Dispose();
        }
        UIPointerInteraction? pointerInteraction = platformPointerInteraction;
        platformPointerInteraction = null;
        if (pointerInteraction is not null)
        {
            platformPointerView?.RemoveInteraction(pointerInteraction);
            pointerInteraction.Dispose();
        }
        platformPointerDelegate?.Dispose();
        platformPointerDelegate = null;
        platformHoverDelegate?.Dispose();
        platformHoverDelegate = null;
        platformPointerStyleDelegate?.Dispose();
        platformPointerStyleDelegate = null;
        platformPointerView = null;
        platformTouch = null;
    }

    private void OnPlatformPointerChanged()
    {
        UILongPressGestureRecognizer? recognizer = platformPointerRecognizer;
        UIView? nativeView = platformPointerView;
        if (recognizer is null || nativeView is null)
        {
            return;
        }
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
                platformPenContactActive = IsApplePenTouch(platformTouch);
                SetPlatformSystemCursorHidden(platformPenContactActive);
                _ = PublishAppleSample(ApplicationPointerPhase.Pressed, recognizer, nativeView);
                break;
            case UIGestureRecognizerState.Changed:
                if (IsApplePenTouch(platformTouch))
                {
                    platformPenContactActive = true;
                    SetPlatformSystemCursorHidden(true);
                }
                _ = PublishAppleSample(ApplicationPointerPhase.Moved, recognizer, nativeView);
                break;
            case UIGestureRecognizerState.Ended:
                bool wasPenContact =
                    platformPenContactActive || IsApplePenTouch(platformTouch);
                bool released = PublishAppleSample(
                    ApplicationPointerPhase.Released,
                    recognizer,
                    nativeView);
                if (!released)
                {
                    PublishCanceled();
                }
                platformPenContactActive = false;
                if (!wasPenContact || !released)
                {
                    PublishExited();
                    SetPlatformSystemCursorHidden(false);
                }
                else
                {
                    // A Pencil release transitions back to the hover recognizer. Preserve the
                    // released sample and hidden cursor until hover resumes or reports a real exit.
                    SetPlatformSystemCursorHidden(true);
                }
                platformTouch = null;
                break;
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                PublishCanceled();
                platformPenContactActive = false;
                SetPlatformSystemCursorHidden(false);
                platformTouch = null;
                break;
        }
    }

    private void OnPlatformHoverChanged()
    {
        UIHoverGestureRecognizer? recognizer = platformHoverRecognizer;
        UIView? nativeView = platformPointerView;
        if (recognizer is null || nativeView is null)
        {
            return;
        }
        switch (recognizer.State)
        {
            case UIGestureRecognizerState.Began:
            case UIGestureRecognizerState.Changed:
                if (!platformPenHoverAccepted)
                {
                    PublishExited();
                    SetPlatformSystemCursorHidden(false);
                    return;
                }
                if (platformPenContactActive)
                {
                    return;
                }
                SetPlatformSystemCursorHidden(true);
                if (!PublishAppleHoverSample(recognizer, nativeView))
                {
                    PublishExited();
                    SetPlatformSystemCursorHidden(false);
                }
                break;
            case UIGestureRecognizerState.Ended:
                bool isContactTransition =
                    platformPenContactActive ||
                    IsApplePenTouch(platformTouch) ||
                    platformLastPenHoverDistanceNormalized is float distance &&
                    distance <= 0.08f;
                if (!isContactTransition)
                {
                    PublishExited();
                    SetPlatformSystemCursorHidden(false);
                }
                platformPenHoverAccepted = false;
                break;
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                if (!platformPenContactActive)
                {
                    PublishExited();
                    SetPlatformSystemCursorHidden(false);
                }
                platformPenHoverAccepted = false;
                break;
        }
    }

    private bool PublishAppleSample(
        ApplicationPointerPhase phase,
        UIGestureRecognizer recognizer,
        UIView nativeView)
    {
        UITouch? touch = platformTouch;
        CGPoint location = recognizer.LocationInView(nativeView);
        CGSize size = nativeView.Bounds.Size;
        ApplicationPointerDeviceKind deviceKind = touch?.Type switch
        {
            UITouchType.Stylus => ApplicationPointerDeviceKind.Pen,
            UITouchType.Direct => ApplicationPointerDeviceKind.Touch,
            UITouchType.Indirect or UITouchType.IndirectPointer =>
                ApplicationPointerDeviceKind.Mouse,
            _ => ApplicationPointerDeviceKind.Unknown,
        };
        float? pressure = null;
        float? tiltX = null;
        float? tiltY = null;
        if (touch is not null &&
            deviceKind is ApplicationPointerDeviceKind.Pen or ApplicationPointerDeviceKind.Touch &&
            touch.MaximumPossibleForce > 0d)
        {
            pressure = (float)(touch.Force / touch.MaximumPossibleForce);
        }
        if (touch is not null && deviceKind == ApplicationPointerDeviceKind.Pen)
        {
            float tilt = 90f - (float)(touch.AltitudeAngle * 180d / Math.PI);
            float azimuth = (float)touch.GetAzimuthAngle(nativeView);
            tiltX = tilt * MathF.Cos(azimuth);
            tiltY = tilt * MathF.Sin(azimuth);
        }
        return PublishPlatformSample(
            phase,
            deviceKind,
            location.X,
            location.Y,
            size.Width,
            size.Height,
            pressure,
            tiltX,
            tiltY,
            isEraser: false,
            isBarrelButtonPressed: false);
    }

    private bool PublishAppleHoverSample(
        UIHoverGestureRecognizer recognizer,
        UIView nativeView)
    {
        CGPoint location = recognizer.LocationInView(nativeView);
        CGSize size = nativeView.Bounds.Size;
        float? tiltX = null;
        float? tiltY = null;
        float? hoverDistance = null;
        bool? hoverToolPreviewPreferred = null;
#if IOS
        if (OperatingSystem.IsIOSVersionAtLeast(16, 4))
#else
        if (OperatingSystem.IsMacCatalystVersionAtLeast(16, 4))
#endif
        {
            double altitudeRadians = (double)recognizer.AltitudeAngle;
            if (double.IsFinite(altitudeRadians) && altitudeRadians > 0d)
            {
                float tilt = 90f - (float)(altitudeRadians * 180d / Math.PI);
                float azimuth = (float)recognizer.GetAzimuthAngle(nativeView);
                tiltX = tilt * MathF.Cos(azimuth);
                tiltY = tilt * MathF.Sin(azimuth);
            }
        }
#if IOS
        if (OperatingSystem.IsIOSVersionAtLeast(16, 1))
        {
            double normalizedDistance = (double)recognizer.ZOffset;
            if (double.IsFinite(normalizedDistance))
            {
                hoverDistance = (float)normalizedDistance;
                platformLastPenHoverDistanceNormalized = Math.Clamp(
                    hoverDistance.Value,
                    0f,
                    1f);
            }
        }
        if (OperatingSystem.IsIOSVersionAtLeast(17, 5))
        {
            hoverToolPreviewPreferred = UIPencilInteraction.PrefersHoverToolPreview;
        }
#endif
        return PublishPlatformSample(
            ApplicationPointerPhase.Hovered,
            ApplicationPointerDeviceKind.Pen,
            location.X,
            location.Y,
            size.Width,
            size.Height,
            pressure: null,
            tiltX,
            tiltY,
            isEraser: false,
            isBarrelButtonPressed: false,
            hoverDistanceNormalized: hoverDistance,
            isHoverToolPreviewPreferred: hoverToolPreviewPreferred);
    }

    private static bool IsApplePenTouch(UITouch? touch) =>
        touch?.Type == UITouchType.Stylus;

    private void SetPlatformSystemCursorHidden(bool hidden)
    {
        if (platformSystemCursorHidden == hidden)
        {
            return;
        }
        platformSystemCursorHidden = hidden;
        platformPointerInteraction?.Invalidate();
    }

    private sealed class ApplePointerDelegate(PointerInputBridge owner)
        : UIGestureRecognizerDelegate
    {
        public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch)
        {
            _ = recognizer;
            if (touch.Type == UITouchType.Direct && owner.IsDirectTouchPressSuppressed)
            {
                return false;
            }
            owner.platformTouch = touch;
            return true;
        }
    }

    private sealed class ApplePenHoverDelegate(PointerInputBridge owner)
        : UIGestureRecognizerDelegate
    {
        public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch)
        {
            _ = recognizer;
            owner.platformPenHoverAccepted = IsApplePenTouch(touch);
            if (!owner.platformPenHoverAccepted && !owner.platformPenContactActive)
            {
                owner.PublishExited();
                owner.SetPlatformSystemCursorHidden(false);
            }
            return owner.platformPenHoverAccepted;
        }
    }

    private sealed class ApplePointerStyleDelegate(PointerInputBridge owner)
        : UIPointerInteractionDelegate
    {
        private readonly UIPointerStyle hiddenPointerStyle =
            UIPointerStyle.CreateHiddenPointerStyle();
        private readonly UIPointerStyle systemPointerStyle =
            UIPointerStyle.CreateSystemPointerStyle();

        public override UIPointerStyle GetStyleForRegion(
            UIPointerInteraction interaction,
            UIPointerRegion region)
        {
            _ = interaction;
            _ = region;
            return owner.platformSystemCursorHidden ? hiddenPointerStyle : systemPointerStyle;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hiddenPointerStyle.Dispose();
                systemPointerStyle.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
#endif

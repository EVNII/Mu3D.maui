#if IOS || MACCATALYST
using System.Runtime.CompilerServices;
using UIKit;

namespace Mu3D.Maui.Toolkit.Controls;

internal static class AppleSystemGestureArbitration
{
    private static readonly ConditionalWeakTable<UIGestureRecognizer, NavigationGestureState>
        NavigationGestureStates = new();

    internal static AppleSystemNavigationBlocker CreateNavigationBlocker(
        UIView nativeView,
        bool blocksDirectPan,
        bool blocksIndirectPan) =>
        new(
            nativeView,
            FindNavigationController(nativeView)?.InteractivePopGestureRecognizer,
            blocksDirectPan,
            blocksIndirectPan);

    internal static bool IsGestureInsideView(
        UIGestureRecognizer recognizer,
        UIView nativeView) =>
        recognizer.View is { } recognizerView &&
        (recognizerView.Handle == nativeView.Handle ||
            recognizerView.IsDescendantOfView(nativeView));

    private static UINavigationController? FindNavigationController(UIView nativeView)
    {
        UIResponder? responder = nativeView;
        while (responder is not null)
        {
            if (responder is UINavigationController navigationController)
            {
                return navigationController;
            }
            if (responder is UIViewController viewController &&
                viewController.NavigationController is { } owner)
            {
                return owner;
            }
            responder = responder.NextResponder;
        }
        return FindOwningNavigationController(
            nativeView.Window?.RootViewController,
            nativeView);
    }

    private static UINavigationController? FindOwningNavigationController(
        UIViewController? controller,
        UIView nativeView)
    {
        if (controller is null)
        {
            return null;
        }
        if (controller is UINavigationController navigationController &&
            navigationController.View is { } navigationView &&
            nativeView.IsDescendantOfView(navigationView))
        {
            return navigationController;
        }
        if (FindOwningNavigationController(controller.PresentedViewController, nativeView)
            is { } presentedOwner)
        {
            return presentedOwner;
        }
        foreach (UIViewController child in controller.ChildViewControllers)
        {
            if (FindOwningNavigationController(child, nativeView) is { } childOwner)
            {
                return childOwner;
            }
        }
        return null;
    }

    private static void Acquire(UIGestureRecognizer navigationGesture)
    {
        lock (NavigationGestureStates)
        {
            if (!NavigationGestureStates.TryGetValue(
                navigationGesture,
                out NavigationGestureState? state))
            {
                state = new NavigationGestureState();
                NavigationGestureStates.Add(navigationGesture, state);
            }
            if (state.LeaseCount == 0)
            {
                state.WasEnabled = navigationGesture.Enabled;
                navigationGesture.Enabled = false;
            }
            state.LeaseCount++;
        }
    }

    private static void Release(UIGestureRecognizer navigationGesture)
    {
        lock (NavigationGestureStates)
        {
            if (!NavigationGestureStates.TryGetValue(navigationGesture, out NavigationGestureState? state) ||
                state.LeaseCount == 0)
            {
                return;
            }
            state.LeaseCount--;
            if (state.LeaseCount != 0)
            {
                return;
            }
            navigationGesture.Enabled = state.WasEnabled;
            NavigationGestureStates.Remove(navigationGesture);
        }
    }

    internal sealed class AppleSystemNavigationBlocker : IDisposable
    {
        private UIView? nativeView;
        private UIGestureRecognizer? navigationGesture;
        private UIGestureRecognizer? localRecognizer;
        private LocalGestureDelegate? localDelegate;
        private bool navigationLeaseHeld;

        internal AppleSystemNavigationBlocker(
            UIView nativeView,
            UIGestureRecognizer? navigationGesture,
            bool blocksDirectPan,
            bool blocksIndirectPan)
        {
            this.nativeView = nativeView;
            this.navigationGesture = navigationGesture;
            if (navigationGesture is null)
            {
                return;
            }

#if MACCATALYST
            if (blocksIndirectPan)
            {
                localDelegate = new LocalGestureDelegate(
                    nativeView,
                    directTouchOnly: false);
                localRecognizer = new UIHoverGestureRecognizer(OnLocalGestureChanged)
                {
                    CancelsTouchesInView = false,
                    DelaysTouchesBegan = false,
                    DelaysTouchesEnded = false,
                    Delegate = localDelegate,
                };
            }
#else
            if (blocksDirectPan)
            {
                localDelegate = new LocalGestureDelegate(
                    nativeView,
                    directTouchOnly: true);
                localRecognizer = new UILongPressGestureRecognizer(OnLocalGestureChanged)
                {
                    MinimumPressDuration = 0d,
                    AllowableMovement = (System.Runtime.InteropServices.NFloat)100000d,
                    NumberOfTouchesRequired = 1,
                    CancelsTouchesInView = false,
                    DelaysTouchesBegan = false,
                    DelaysTouchesEnded = false,
                    Delegate = localDelegate,
                };
            }
#endif
            if (localRecognizer is not null)
            {
                nativeView.AddGestureRecognizer(localRecognizer);
            }
        }

        public void Dispose()
        {
            SetNavigationBlocked(false);
            UIGestureRecognizer? recognizer = localRecognizer;
            localRecognizer = null;
            if (recognizer is not null)
            {
                nativeView?.RemoveGestureRecognizer(recognizer);
                recognizer.Dispose();
            }
            localDelegate?.Dispose();
            localDelegate = null;
            nativeView = null;
            navigationGesture = null;
        }

        private void OnLocalGestureChanged()
        {
            UIGestureRecognizer? recognizer = localRecognizer;
            if (recognizer is null)
            {
                return;
            }
            switch (recognizer.State)
            {
                case UIGestureRecognizerState.Began:
                case UIGestureRecognizerState.Changed:
                    SetNavigationBlocked(true);
                    break;
                case UIGestureRecognizerState.Ended:
                case UIGestureRecognizerState.Cancelled:
                case UIGestureRecognizerState.Failed:
                    SetNavigationBlocked(false);
                    break;
            }
        }

        private void SetNavigationBlocked(bool blocked)
        {
            UIGestureRecognizer? current = navigationGesture;
            if (current is null || navigationLeaseHeld == blocked)
            {
                return;
            }
            navigationLeaseHeld = blocked;
            if (blocked)
            {
                Acquire(current);
            }
            else
            {
                Release(current);
            }
        }

        private sealed class LocalGestureDelegate(
            UIView nativeView,
            bool directTouchOnly) :
            UIGestureRecognizerDelegate
        {
            public override bool ShouldReceiveTouch(
                UIGestureRecognizer recognizer,
                UITouch touch)
            {
                _ = recognizer;
                return !directTouchOnly ||
                    touch.Type is UITouchType.Direct or UITouchType.Stylus;
            }

            public override bool ShouldRecognizeSimultaneously(
                UIGestureRecognizer gestureRecognizer,
                UIGestureRecognizer otherGestureRecognizer)
            {
                _ = gestureRecognizer;
                return IsGestureInsideView(otherGestureRecognizer, nativeView);
            }
        }
    }

    private sealed class NavigationGestureState
    {
        internal int LeaseCount { get; set; }

        internal bool WasEnabled { get; set; }
    }
}
#endif

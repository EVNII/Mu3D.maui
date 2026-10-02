#if ANDROID
using Android.Views;
using AndroidX.Core.View;
using AndroidView = Android.Views.View;

namespace Mu3D.Maui.Toolkit.Controls;

internal static class AndroidSystemGestureArbitration
{
    internal static bool StartsAtSystemBackEdge(AndroidView nativeView, MotionEvent motion)
    {
        WindowInsetsCompat? windowInsets = ViewCompat.GetRootWindowInsets(nativeView);
        AndroidX.Core.Graphics.Insets? gestureInsets = windowInsets?.GetInsets(
            WindowInsetsCompat.Type.SystemGestures());
        if (gestureInsets is null)
        {
            return false;
        }

        float x = motion.GetX();
        return (gestureInsets.Left > 0 && x <= gestureInsets.Left) ||
            (gestureInsets.Right > 0 && x >= nativeView.Width - gestureInsets.Right);
    }
}
#endif

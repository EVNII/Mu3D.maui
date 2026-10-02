#if IOS || MACCATALYST
using CoreAnimation;
using Foundation;
using UIKit;
#else
using Microsoft.Maui.Animations;
using MauiAnimation = Microsoft.Maui.Animations.Animation;
#endif

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>
/// Drives a per-frame callback at the display's native refresh rate for continuous viewport
/// rendering. On iOS and Mac Catalyst the source owns a <c>CADisplayLink</c> whose
/// <c>PreferredFrameRateRange</c> follows the window screen's <c>MaximumFramesPerSecond</c>, so
/// ProMotion displays are not capped at MAUI's 60 Hz default ticker. Other platforms use the MAUI
/// VSync animation service, which already follows the display.
/// </summary>
/// <remarks>
/// The application still owns scheduling policy: start the source while a continuously animated
/// view is visible and stop it when the view hides or pauses. iPhone apps also need the
/// <c>CADisableMinimumFrameDurationOnPhone</c> Info.plist opt-in before iOS lifts the system
/// 60 Hz clamp. The source is not thread-safe; call it from the UI thread. The callback runs on
/// the UI thread at display rate.
/// </remarks>
public sealed class VsyncFrameSource : IDisposable
{
    private readonly Action tick;

    /// <summary>Creates a frame source invoking <paramref name="tick"/> once per display refresh.</summary>
    /// <param name="tick">The UI-thread callback, typically an Invalidate call on the animated view.</param>
    public VsyncFrameSource(Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        this.tick = tick;
    }

    /// <summary>Gets whether the source is currently driving callbacks.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Gets the measured callbacks per second over the most recent second; zero when not running.</summary>
    public double MeasuredTicksPerSecond { get; private set; }

    /// <summary>Gets the screen's reported refresh ceiling at the latest <see cref="Start"/>, or zero before it.</summary>
    /// <remarks>Non-Apple platforms report zero: the MAUI ticker already follows the display and no
    /// separate ceiling is negotiated.</remarks>
    public int ScreenMaximumFramesPerSecond { get; private set; }

    /// <summary>Starts callbacks. Returns false when no frame source is available for the element.</summary>
    /// <param name="anchor">A loaded element whose window selects the Apple screen to follow.</param>
    public bool Start(VisualElement anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ObjectDisposedException.ThrowIf(disposed, this);
        if (IsRunning) return true;
#if IOS || MACCATALYST
        // The window's screen reports the real ceiling (e.g. 120 on ProMotion). A range past the
        // hardware limit is clamped by Core Animation, so unknown screens safely default to 120.
        float maximumFramesPerSecond = 120;
        if (anchor.Window?.Handler?.PlatformView is UIWindow window)
        {
            maximumFramesPerSecond = (float)window.Screen.MaximumFramesPerSecond;
        }
        ScreenMaximumFramesPerSecond = (int)maximumFramesPerSecond;
        link = CADisplayLink.Create(OnTick);
        link.PreferredFrameRateRange = CAFrameRateRange.Create(
            30,
            maximumFramesPerSecond,
            maximumFramesPerSecond);
        link.AddToRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
#else
        if (anchor.Handler?.MauiContext?.Services.GetService(typeof(IAnimationManager))
            is not IAnimationManager manager)
        {
            return false;
        }
        animationManager = manager;
        animation = new MauiAnimation(_ => OnTick(), 0, 1, Easing.Linear, null)
        {
            Name = "Mu3D Toolkit VSync frame source",
            Repeats = true,
        };
        animation.Commit(manager);
#endif
        tickCount = 0;
        tickWindowStart = Environment.TickCount64;
        IsRunning = true;
        return true;
    }

    private void OnTick()
    {
        tick();
        tickCount++;
        long now = Environment.TickCount64;
        long elapsed = now - tickWindowStart;
        if (elapsed >= 1000)
        {
            MeasuredTicksPerSecond = tickCount * 1000.0 / elapsed;
            tickCount = 0;
            tickWindowStart = now;
        }
    }

    /// <summary>Stops callbacks and releases the underlying display link or animation.</summary>
    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        MeasuredTicksPerSecond = 0;
#if IOS || MACCATALYST
        link?.RemoveFromRunLoop(NSRunLoop.Main, NSRunLoopMode.Common);
        link?.Dispose();
        link = null;
#else
        MauiAnimation? previous = animation;
        animation = null;
        if (previous is not null)
        {
            animationManager?.Remove(previous);
            previous.Dispose();
        }
        animationManager = null;
#endif
    }

    /// <summary>Stops callbacks. The anchor and application-owned view remain usable.</summary>
    public void Dispose()
    {
        disposed = true;
        Stop();
    }

#if IOS || MACCATALYST
    private CADisplayLink? link;
#else
    private IAnimationManager? animationManager;
    private MauiAnimation? animation;
#endif
    private bool disposed;
    private int tickCount;
    private long tickWindowStart;
}

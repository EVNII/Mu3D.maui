#if WINDOWS || IOS || MACCATALYST
using System.Diagnostics;
using Microsoft.Maui.Animations;
using Mu3D.Maui.Toolkit.Controls;
using MauiAnimation = Microsoft.Maui.Animations.Animation;
using MauiWindow = Microsoft.Maui.Controls.Window;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Tracks one page-owned key chord and advances it on MAUI VSync.</summary>
internal sealed partial class HeldKeyboardInputSession : IDisposable
{
    private const float InitialFrameSeconds = 1f / 60f;
    private const float MaximumFrameSeconds = 0.05f;

    private readonly VisualElement animationOwner;
    private readonly Func<MauiWindow?> windowProvider;
    private readonly Func<ViewportKey, bool> acceptsKey;
    private readonly Action<HeldKeyboardInputSession, float> advance;
    private readonly HashSet<ViewportKey> pressedKeys = [];
    private IAnimationManager? animationManager;
    private MauiAnimation? animation;
    private long lastFrameTimestamp;
    private bool attachRequested;
    private bool disposed;

    internal HeldKeyboardInputSession(
        VisualElement animationOwner,
        Func<MauiWindow?> windowProvider,
        Func<ViewportKey, bool> acceptsKey,
        Action<HeldKeyboardInputSession, float> advance)
    {
        this.animationOwner = animationOwner ?? throw new ArgumentNullException(nameof(animationOwner));
        this.windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
        this.acceptsKey = acceptsKey ?? throw new ArgumentNullException(nameof(acceptsKey));
        this.advance = advance ?? throw new ArgumentNullException(nameof(advance));
    }

    internal bool IsPressed(ViewportKey key) =>
        key != ViewportKey.None && pressedKeys.Contains(key);

    internal void Attach()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        attachRequested = true;
        animationOwner.Loaded -= OnOwnerLoaded;
        animationOwner.Loaded += OnOwnerLoaded;
        TryAttachPlatformInput();
    }

    internal void Detach()
    {
        attachRequested = false;
        animationOwner.Loaded -= OnOwnerLoaded;
        DetachPlatformInput();
        ClearPressedState();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Detach();
    }

    private void OnOwnerLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        TryAttachPlatformInput();
    }

    private bool HandleKeyDown(ViewportKey key)
    {
        if (!attachRequested || !acceptsKey(key))
        {
            return false;
        }

        if (pressedKeys.Add(key))
        {
            advance(this, InitialFrameSeconds);
            StartFrameClock();
        }

        return true;
    }

    private bool HandleKeyUp(ViewportKey key)
    {
        if (!acceptsKey(key))
        {
            return false;
        }

        pressedKeys.Remove(key);
        if (pressedKeys.Count == 0)
        {
            StopFrameClock();
        }

        return true;
    }

    private void SetKeyState(ViewportKey key, bool pressed)
    {
        if (animationOwner.Dispatcher.IsDispatchRequired)
        {
            animationOwner.Dispatcher.Dispatch(() => SetKeyState(key, pressed));
            return;
        }

        if (pressed)
        {
            HandleKeyDown(key);
        }
        else
        {
            HandleKeyUp(key);
        }
    }

    private void StartFrameClock()
    {
        if (animation is not null || !attachRequested ||
            animationOwner.Handler?.MauiContext?.Services.GetService(typeof(IAnimationManager))
                is not IAnimationManager manager)
        {
            return;
        }

        MauiAnimation created = new(
            _ => ProcessFrame(),
            start: 0d,
            duration: 1d,
            easing: Easing.Linear,
            finished: null)
        {
            Name = "Mu3D Gallery held-key VSync loop",
            Repeats = true,
        };
        animationManager = manager;
        animation = created;
        lastFrameTimestamp = Stopwatch.GetTimestamp();
        created.Commit(manager);
    }

    private void ProcessFrame()
    {
        if (!attachRequested || pressedKeys.Count == 0)
        {
            return;
        }

        long currentTimestamp = Stopwatch.GetTimestamp();
        float elapsedSeconds = Math.Clamp(
            (float)Stopwatch.GetElapsedTime(lastFrameTimestamp, currentTimestamp).TotalSeconds,
            0f,
            MaximumFrameSeconds);
        lastFrameTimestamp = currentTimestamp;
        if (elapsedSeconds > 0f)
        {
            advance(this, elapsedSeconds);
        }
    }

    private void ClearPressedState()
    {
        pressedKeys.Clear();
        StopFrameClock();
    }

    private void StopFrameClock()
    {
        MauiAnimation? current = animation;
        IAnimationManager? manager = animationManager;
        animation = null;
        animationManager = null;
        lastFrameTimestamp = 0;
        if (current is not null)
        {
            manager?.Remove(current);
            current.Dispose();
        }
    }

    partial void TryAttachPlatformInput();

    partial void DetachPlatformInput();
}
#endif

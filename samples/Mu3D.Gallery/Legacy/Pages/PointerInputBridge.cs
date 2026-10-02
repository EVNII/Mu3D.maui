using System.Numerics;
using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Identifies the application-facing device that produced one Gallery pointer sample.</summary>
public enum ApplicationPointerDeviceKind
{
    /// <summary>The native device type was not recognized.</summary>
    Unknown,

    /// <summary>A mouse or trackpad pointer produced the sample.</summary>
    Mouse,

    /// <summary>A direct touch contact produced the sample.</summary>
    Touch,

    /// <summary>A pen or stylus produced the sample.</summary>
    Pen,
}

/// <summary>Identifies one phase in an application-owned pointer contact.</summary>
public enum ApplicationPointerPhase
{
    /// <summary>A pen is hovering over the viewport without contact.</summary>
    Hovered,

    /// <summary>A primary contact began.</summary>
    Pressed,

    /// <summary>The active contact moved.</summary>
    Moved,

    /// <summary>The active contact ended normally.</summary>
    Released,

    /// <summary>The platform canceled or lost the active contact.</summary>
    Canceled,

    /// <summary>The hovering pointer left the viewport.</summary>
    Exited,
}

/// <summary>Handles one transient, allocation-free application pointer sample.</summary>
public delegate void ApplicationPointerSampledEventHandler(
    object? sender,
    ApplicationPointerSample sample);

/// <summary>Provides one normalized application-owned pointer or pen sample.</summary>
public readonly struct ApplicationPointerSample
{
    internal ApplicationPointerSample(
        ApplicationPointerPhase phase,
        ApplicationPointerDeviceKind deviceKind,
        Point logicalPosition,
        Vector2 positionPixels,
        float? pressure,
        float? tiltXDegrees,
        float? tiltYDegrees,
        bool isEraser,
        bool isBarrelButtonPressed,
        float? hoverDistanceNormalized,
        bool? isHoverToolPreviewPreferred)
    {
        Phase = phase;
        DeviceKind = deviceKind;
        LogicalPosition = logicalPosition;
        PositionPixels = positionPixels;
        Pressure = pressure;
        TiltXDegrees = tiltXDegrees;
        TiltYDegrees = tiltYDegrees;
        IsEraser = isEraser;
        IsBarrelButtonPressed = isBarrelButtonPressed;
        HoverDistanceNormalized = hoverDistanceNormalized;
        IsHoverToolPreviewPreferred = isHoverToolPreviewPreferred;
    }

    /// <summary>Gets the contact phase.</summary>
    public ApplicationPointerPhase Phase { get; }

    /// <summary>Gets the normalized device kind.</summary>
    public ApplicationPointerDeviceKind DeviceKind { get; }

    /// <summary>Gets the top-left-origin position in MAUI logical units.</summary>
    public Point LogicalPosition { get; }

    /// <summary>Gets the top-left-origin position in the Mu3D physical viewport.</summary>
    public Vector2 PositionPixels { get; }

    /// <summary>Gets normalized pressure in [0, 1], or null when the platform did not supply it.</summary>
    public float? Pressure { get; }

    /// <summary>Gets platform-normalized X tilt in degrees, or null when unavailable.</summary>
    public float? TiltXDegrees { get; }

    /// <summary>Gets platform-normalized Y tilt in degrees, or null when unavailable.</summary>
    public float? TiltYDegrees { get; }

    /// <summary>Gets whether the platform reports an eraser tip.</summary>
    public bool IsEraser { get; }

    /// <summary>Gets whether the pen barrel button is pressed.</summary>
    public bool IsBarrelButtonPressed { get; }

    /// <summary>
    /// Gets the normalized hover distance, where zero approaches the screen and one is the
    /// platform's maximum hover distance, or null when unavailable.
    /// </summary>
    public float? HoverDistanceNormalized { get; }

    /// <summary>
    /// Gets the platform user preference for a hover tool preview, or null when unavailable.
    /// </summary>
    public bool? IsHoverToolPreviewPreferred { get; }
}

/// <summary>
/// Gallery-owned native input adapter that demonstrates how an application can feed pen samples
/// into backend-independent Mu3D hit testing without creating a Core input model.
/// </summary>
/// <remarks>
/// This type belongs to the sample application, not a Mu3D package. It follows MAUI handler and
/// Loaded/Unloaded lifetime, captures only one primary contact, and releases every native event on
/// unload or detach. Applications remain responsible for coalescing, stroke policy, undo, and edits.
/// </remarks>
public sealed partial class PointerInputBridge : Behavior<Mu3DSceneView>
{
    /// <summary>Identifies the opt-in pen-priority direct-touch suppression setting.</summary>
    public static readonly BindableProperty SuppressDirectTouchPressWhilePenActiveProperty =
        BindableProperty.Create(
            nameof(SuppressDirectTouchPressWhilePenActive),
            typeof(bool),
            typeof(PointerInputBridge),
            false);

    private Mu3DSceneView? sceneView;
    private ApplicationPointerSample? lastSample;
    private bool contactInProgress;
    private bool penProximityActive;

    /// <summary>Occurs for normalized primary mouse, touch, or pen contact samples.</summary>
    public event ApplicationPointerSampledEventHandler? Sampled;

    /// <summary>
    /// Gets or sets whether this primary-pointer adapter rejects direct-touch presses while a pen
    /// is hovering or contacting. The native touch stream is not consumed, so separate multi-touch
    /// Pan/Pinch recognizers on the same SceneView remain available.
    /// </summary>
    public bool SuppressDirectTouchPressWhilePenActive
    {
        get => (bool)GetValue(SuppressDirectTouchPressWhilePenActiveProperty);
        set => SetValue(SuppressDirectTouchPressWhilePenActiveProperty, value);
    }

    internal Mu3DSceneView? SceneView => sceneView;

    internal bool IsDirectTouchPressSuppressed =>
        SuppressDirectTouchPressWhilePenActive && penProximityActive;

    /// <inheritdoc />
    protected override void OnAttachedTo(Mu3DSceneView bindable)
    {
        base.OnAttachedTo(bindable);
        sceneView = bindable;
        bindable.HandlerChanged += OnHandlerChanged;
        bindable.Loaded += OnLoaded;
        bindable.Unloaded += OnUnloaded;
        RefreshPlatformInput();
    }

    /// <inheritdoc />
    protected override void OnDetachingFrom(Mu3DSceneView bindable)
    {
        bindable.HandlerChanged -= OnHandlerChanged;
        bindable.Loaded -= OnLoaded;
        bindable.Unloaded -= OnUnloaded;
        DetachPlatformInput();
        lastSample = null;
        contactInProgress = false;
        penProximityActive = false;
        sceneView = null;
        base.OnDetachingFrom(bindable);
    }

    internal bool PublishPlatformSample(
        ApplicationPointerPhase phase,
        ApplicationPointerDeviceKind deviceKind,
        double nativeX,
        double nativeY,
        double nativeWidth,
        double nativeHeight,
        float? pressure,
        float? tiltXDegrees,
        float? tiltYDegrees,
        bool isEraser,
        bool isBarrelButtonPressed,
        float? hoverDistanceNormalized = null,
        bool? isHoverToolPreviewPreferred = null)
    {
        Mu3DSceneView? view = sceneView;
        if (view is null ||
            !double.IsFinite(nativeX) || !double.IsFinite(nativeY) ||
            !double.IsFinite(nativeWidth) || !double.IsFinite(nativeHeight) ||
            nativeWidth <= 0d || nativeHeight <= 0d ||
            nativeX < 0d || nativeY < 0d || nativeX > nativeWidth || nativeY > nativeHeight ||
            !double.IsFinite(view.Width) || !double.IsFinite(view.Height) ||
            view.Width <= 0d || view.Height <= 0d ||
            view.PixelWidth == 0 || view.PixelHeight == 0)
        {
            return false;
        }

        float horizontal = (float)(nativeX / nativeWidth);
        float vertical = (float)(nativeY / nativeHeight);
        Point logicalPosition = new(horizontal * view.Width, vertical * view.Height);
        Vector2 physicalPosition = new(
            horizontal * view.PixelWidth,
            vertical * view.PixelHeight);
        if (!float.IsFinite(physicalPosition.X) || !float.IsFinite(physicalPosition.Y))
        {
            return false;
        }

        ApplicationPointerSample sample = new(
            phase,
            deviceKind,
            logicalPosition,
            physicalPosition,
            NormalizePressure(pressure),
            NormalizeFinite(tiltXDegrees),
            NormalizeFinite(tiltYDegrees),
            isEraser,
            isBarrelButtonPressed,
            NormalizeUnitInterval(hoverDistanceNormalized),
            isHoverToolPreviewPreferred);
        if (phase == ApplicationPointerPhase.Pressed)
        {
            contactInProgress = true;
        }
        else if (phase is ApplicationPointerPhase.Released or ApplicationPointerPhase.Canceled)
        {
            contactInProgress = false;
        }
        if (deviceKind == ApplicationPointerDeviceKind.Pen)
        {
            penProximityActive = phase is
                ApplicationPointerPhase.Hovered or
                ApplicationPointerPhase.Pressed or
                ApplicationPointerPhase.Moved or
                ApplicationPointerPhase.Released;
        }
        lastSample = phase is ApplicationPointerPhase.Canceled or ApplicationPointerPhase.Exited
            ? null
            : sample;
        Sampled?.Invoke(this, sample);
        return true;
    }

    internal void PublishCanceled()
    {
        ApplicationPointerSample? previous = lastSample;
        if (!contactInProgress || previous is null)
        {
            return;
        }
        contactInProgress = false;
        if (previous.Value.DeviceKind == ApplicationPointerDeviceKind.Pen)
        {
            penProximityActive = false;
        }
        lastSample = null;
        Sampled?.Invoke(this, CopyWithPhase(previous.Value, ApplicationPointerPhase.Canceled));
    }

    internal void PublishExited()
    {
        if (contactInProgress)
        {
            PublishCanceled();
            return;
        }
        ApplicationPointerSample? previous = lastSample;
        lastSample = null;
        if (previous is not null)
        {
            if (previous.Value.DeviceKind == ApplicationPointerDeviceKind.Pen)
            {
                penProximityActive = false;
            }
            Sampled?.Invoke(this, CopyWithPhase(previous.Value, ApplicationPointerPhase.Exited));
        }
    }

    private static ApplicationPointerSample CopyWithPhase(
        ApplicationPointerSample previous,
        ApplicationPointerPhase phase) =>
        new(
            phase,
            previous.DeviceKind,
            previous.LogicalPosition,
            previous.PositionPixels,
            previous.Pressure,
            previous.TiltXDegrees,
            previous.TiltYDegrees,
            previous.IsEraser,
            previous.IsBarrelButtonPressed,
            previous.HoverDistanceNormalized,
            previous.IsHoverToolPreviewPreferred);

    private static float? NormalizePressure(float? pressure) =>
        pressure is float value && float.IsFinite(value)
            ? Math.Clamp(value, 0f, 1f)
            : null;

    private static float? NormalizeUnitInterval(float? value) =>
        value is float number && float.IsFinite(number)
            ? Math.Clamp(number, 0f, 1f)
            : null;

    private static float? NormalizeFinite(float? value) =>
        value is float number && float.IsFinite(number) ? number : null;

    private void OnHandlerChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshPlatformInput();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshPlatformInput();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        DetachPlatformInput();
        lastSample = null;
        contactInProgress = false;
        penProximityActive = false;
    }

    private void RefreshPlatformInput()
    {
        DetachPlatformInput();
        if (sceneView?.IsLoaded == true)
        {
            AttachPlatformInput();
        }
    }

    private partial void AttachPlatformInput();

    private partial void DetachPlatformInput();
}

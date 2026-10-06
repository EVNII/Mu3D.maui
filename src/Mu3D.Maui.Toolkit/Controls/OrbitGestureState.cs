using System.Numerics;
using Mu3D.Toolkit.Controls;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Identifies the normalized action emitted by a configured viewport drag route.</summary>
public enum ViewportDragAction
{
    /// <summary>Rotate an orbit camera or change a first-person look direction.</summary>
    Rotate,

    /// <summary>Translate in the controller's horizontal/vertical navigation plane.</summary>
    Pan,

    /// <summary>Disables the configured drag route.</summary>
    None,
}

/// <summary>Identifies a normalized scalar viewport-input route.</summary>
public enum ViewportScalarAction
{
    /// <summary>Change orbit distance or move a first-person camera forward/backward.</summary>
    Dolly,

    /// <summary>Do not consume this input.</summary>
    None,
}

internal sealed class ViewportGestureState : IDisposable
{
    private const string LeaseOwnerName = "MAUI viewport navigation gesture";
    private ViewportControlLease? controlLease;
    private GestureKind activeGesture;
    private double previousPanX;
    private double previousPanY;
    private double previousPinchScale = 1d;

    internal bool IsActive => activeGesture != GestureKind.None;

    internal bool BeginPan(double totalX, double totalY, ViewportControlArbiter? arbiter)
    {
        if (!double.IsFinite(totalX) || !double.IsFinite(totalY))
        {
            throw new ArgumentOutOfRangeException(nameof(totalX), "Gesture totals must be finite.");
        }
        if (!Begin(GestureKind.Pan, arbiter))
        {
            return false;
        }
        previousPanX = totalX;
        previousPanY = totalY;
        return true;
    }

    internal bool TryGetPanDelta(double totalX, double totalY, out double deltaX, out double deltaY)
    {
        deltaX = 0d;
        deltaY = 0d;
        if (activeGesture != GestureKind.Pan)
        {
            return false;
        }
        deltaX = totalX - previousPanX;
        deltaY = totalY - previousPanY;
        previousPanX = totalX;
        previousPanY = totalY;
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            throw new ArgumentOutOfRangeException(nameof(totalX), "Gesture totals must be finite.");
        }
        return true;
    }

    internal void EndPan()
    {
        if (activeGesture == GestureKind.Pan)
        {
            End();
        }
    }

    internal bool BeginPinch(ViewportControlArbiter? arbiter)
    {
        if (!Begin(GestureKind.Pinch, arbiter))
        {
            return false;
        }
        previousPinchScale = 1d;
        return true;
    }

    internal bool TryGetPinchRatio(double scale, out double ratio)
    {
        ratio = 1d;
        if (activeGesture != GestureKind.Pinch)
        {
            return false;
        }
        if (!double.IsFinite(scale) || scale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Pinch scale must be positive and finite.");
        }
        ratio = scale / previousPinchScale;
        previousPinchScale = scale;
        return true;
    }

    internal void EndPinch()
    {
        if (activeGesture == GestureKind.Pinch)
        {
            End();
        }
    }

    internal void End()
    {
        activeGesture = GestureKind.None;
        previousPanX = 0d;
        previousPanY = 0d;
        previousPinchScale = 1d;
        ViewportControlLease? lease = controlLease;
        controlLease = null;
        if (lease is not null)
        {
            lease.Revoked -= OnControlLeaseRevoked;
            lease.Dispose();
        }
    }

    public void Dispose() => End();

    internal static Vector2 MapDragDelta(
        double deltaX,
        double deltaY,
        double width,
        double height,
        ViewportDragAction action,
        float rotationRadiansPerViewport,
        float panUnitsPerViewport,
        bool useIsotropicNormalization)
    {
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            throw new ArgumentOutOfRangeException(nameof(deltaX), "Gesture deltas must be finite.");
        }
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0d || height <= 0d)
        {
            return Vector2.Zero;
        }
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
        if (!float.IsFinite(rotationRadiansPerViewport) || rotationRadiansPerViewport <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(rotationRadiansPerViewport));
        }
        if (!float.IsFinite(panUnitsPerViewport) || panUnitsPerViewport <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(panUnitsPerViewport));
        }

        double referenceX = useIsotropicNormalization ? Math.Min(width, height) : width;
        double referenceY = useIsotropicNormalization ? Math.Min(width, height) : height;
        Vector2 viewportDelta = new(
            (float)(deltaX / referenceX),
            (float)(deltaY / referenceY));
        return action switch
        {
            ViewportDragAction.None => Vector2.Zero,
            ViewportDragAction.Rotate => new Vector2(
                -viewportDelta.X * rotationRadiansPerViewport,
                -viewportDelta.Y * rotationRadiansPerViewport),
            ViewportDragAction.Pan => new Vector2(
                -viewportDelta.X * panUnitsPerViewport,
                viewportDelta.Y * panUnitsPerViewport),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }

    private bool Begin(GestureKind gesture, ViewportControlArbiter? arbiter)
    {
        if (activeGesture != GestureKind.None)
        {
            return activeGesture == gesture;
        }
        ViewportControlLease? lease = arbiter?.TryAcquire(
            LeaseOwnerName,
            ViewportControlPriorities.Camera);
        if (arbiter is not null && lease is null)
        {
            return false;
        }
        controlLease = lease;
        if (controlLease is not null)
        {
            controlLease.Revoked += OnControlLeaseRevoked;
        }
        activeGesture = gesture;
        return true;
    }

    private void OnControlLeaseRevoked(object? sender, EventArgs e)
    {
        if (controlLease is not ViewportControlLease revoked || !ReferenceEquals(sender, revoked))
        {
            return;
        }
        revoked.Revoked -= OnControlLeaseRevoked;
        controlLease = null;
        activeGesture = GestureKind.None;
        previousPanX = 0d;
        previousPanY = 0d;
        previousPinchScale = 1d;
    }

    private enum GestureKind
    {
        None,
        Pan,
        Pinch,
    }
}

internal sealed class ViewportMultiTouchGestureState : IDisposable
{
    private const string LeaseOwnerName = "MAUI viewport multi-touch gesture";
    private ViewportControlLease? controlLease;
    private bool panActive;
    private bool pinchActive;
    private double previousPanX;
    private double previousPanY;
    private double previousPinchScale = 1d;

    internal bool IsActive => panActive || pinchActive;

    internal bool BeginPan(double totalX, double totalY, ViewportControlArbiter? arbiter)
    {
        if (!double.IsFinite(totalX) || !double.IsFinite(totalY))
        {
            throw new ArgumentOutOfRangeException(nameof(totalX), "Gesture totals must be finite.");
        }
        if (!TryBegin(arbiter))
        {
            return false;
        }
        panActive = true;
        previousPanX = totalX;
        previousPanY = totalY;
        return true;
    }

    internal bool TryGetPanDelta(double totalX, double totalY, out double deltaX, out double deltaY)
    {
        deltaX = 0d;
        deltaY = 0d;
        if (!panActive)
        {
            return false;
        }
        deltaX = totalX - previousPanX;
        deltaY = totalY - previousPanY;
        previousPanX = totalX;
        previousPanY = totalY;
        if (!double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            throw new ArgumentOutOfRangeException(nameof(totalX), "Gesture totals must be finite.");
        }
        return true;
    }

    internal bool BeginPinch(ViewportControlArbiter? arbiter)
    {
        if (!TryBegin(arbiter))
        {
            return false;
        }
        pinchActive = true;
        previousPinchScale = 1d;
        return true;
    }

    internal bool TryGetPinchRatio(double scale, out double ratio, bool isIncremental = false)
    {
        ratio = 1d;
        if (!pinchActive)
        {
            return false;
        }
        if (!double.IsFinite(scale) || scale <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "Pinch scale must be positive and finite.");
        }
        ratio = isIncremental ? scale : scale / previousPinchScale;
        if (!isIncremental)
        {
            previousPinchScale = scale;
        }
        return true;
    }

    internal void EndPan()
    {
        panActive = false;
        previousPanX = 0d;
        previousPanY = 0d;
        ReleaseIfIdle();
    }

    internal void EndPinch()
    {
        pinchActive = false;
        previousPinchScale = 1d;
        ReleaseIfIdle();
    }

    internal void End()
    {
        panActive = false;
        pinchActive = false;
        previousPanX = 0d;
        previousPanY = 0d;
        previousPinchScale = 1d;
        ReleaseLease();
    }

    public void Dispose() => End();

    private bool TryBegin(ViewportControlArbiter? arbiter)
    {
        if (IsActive)
        {
            return true;
        }
        ViewportControlLease? lease = arbiter?.TryAcquire(
            LeaseOwnerName,
            ViewportControlPriorities.Camera);
        if (arbiter is not null && lease is null)
        {
            return false;
        }
        controlLease = lease;
        if (controlLease is not null)
        {
            controlLease.Revoked += OnControlLeaseRevoked;
        }
        return true;
    }

    private void ReleaseIfIdle()
    {
        if (!IsActive)
        {
            ReleaseLease();
        }
    }

    private void ReleaseLease()
    {
        ViewportControlLease? lease = controlLease;
        controlLease = null;
        if (lease is null)
        {
            return;
        }
        lease.Revoked -= OnControlLeaseRevoked;
        lease.Dispose();
    }

    private void OnControlLeaseRevoked(object? sender, EventArgs e)
    {
        if (controlLease is not ViewportControlLease revoked || !ReferenceEquals(sender, revoked))
        {
            return;
        }
        revoked.Revoked -= OnControlLeaseRevoked;
        controlLease = null;
        panActive = false;
        pinchActive = false;
        previousPanX = 0d;
        previousPanY = 0d;
        previousPinchScale = 1d;
    }
}

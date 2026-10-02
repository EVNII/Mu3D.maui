using System.Numerics;
using Mu3D.Toolkit.Controls;

namespace Mu3D.Maui.Toolkit.Controls;

internal enum ViewportKeyboardAction
{
    RotateLeft,
    RotateRight,
    RotateUp,
    RotateDown,
    PanLeft,
    PanRight,
    PanUp,
    PanDown,
    DollyIn,
    DollyOut,
}

internal static class ViewportNavigationInputState
{
    private const string LeaseOwnerName = "MAUI viewport navigation input";

    internal static float NormalizeWheelDelta(
        float platformDelta,
        float platformUnitsPerDetent,
        bool invert = false)
    {
        if (!float.IsFinite(platformDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(platformDelta));
        }
        ThrowIfPositiveFinite(platformUnitsPerDetent, nameof(platformUnitsPerDetent));
        float detents = platformDelta / platformUnitsPerDetent;
        if (invert)
        {
            detents = -detents;
        }
        if (!float.IsFinite(detents))
        {
            throw new ArgumentOutOfRangeException(
                nameof(platformDelta),
                "The normalized wheel delta must be finite.");
        }
        return detents;
    }

    internal static bool TryApplyWheel(
        IViewportNavigationController controller,
        ViewportControlArbiter? arbiter,
        float detents,
        float dollyStep)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (!float.IsFinite(detents))
        {
            throw new ArgumentOutOfRangeException(nameof(detents));
        }
        ThrowIfPositiveFinite(dollyStep, nameof(dollyStep));
        if (detents == 0f)
        {
            return false;
        }
        return TryApply(
            controller,
            arbiter,
            Vector2.Zero,
            Vector2.Zero,
            detents * dollyStep);
    }

    internal static bool TryApplyKeyboard(
        IViewportNavigationController controller,
        ViewportControlArbiter? arbiter,
        ViewportKeyboardAction action,
        float rotationStepRadians,
        float panStep,
        float dollyStep)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
        ThrowIfPositiveFinite(rotationStepRadians, nameof(rotationStepRadians));
        ThrowIfPositiveFinite(panStep, nameof(panStep));
        ThrowIfPositiveFinite(dollyStep, nameof(dollyStep));

        Vector2 rotation = action switch
        {
            ViewportKeyboardAction.RotateLeft => new Vector2(-rotationStepRadians, 0f),
            ViewportKeyboardAction.RotateRight => new Vector2(rotationStepRadians, 0f),
            ViewportKeyboardAction.RotateUp => new Vector2(0f, -rotationStepRadians),
            ViewportKeyboardAction.RotateDown => new Vector2(0f, rotationStepRadians),
            _ => Vector2.Zero,
        };
        Vector2 pan = action switch
        {
            ViewportKeyboardAction.PanLeft => new Vector2(-panStep, 0f),
            ViewportKeyboardAction.PanRight => new Vector2(panStep, 0f),
            ViewportKeyboardAction.PanUp => new Vector2(0f, panStep),
            ViewportKeyboardAction.PanDown => new Vector2(0f, -panStep),
            _ => Vector2.Zero,
        };
        float dolly = action switch
        {
            ViewportKeyboardAction.DollyIn => dollyStep,
            ViewportKeyboardAction.DollyOut => -dollyStep,
            _ => 0f,
        };
        return TryApply(controller, arbiter, rotation, pan, dolly);
    }

    internal static bool TryApplyNavigation(
        IViewportNavigationController controller,
        ViewportControlArbiter? arbiter,
        OrbitNavigationAction action,
        float rotationStepRadians,
        float panStep,
        float dollyStep)
    {
        ArgumentNullException.ThrowIfNull(controller);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
        ThrowIfPositiveFinite(rotationStepRadians, nameof(rotationStepRadians));
        ThrowIfPositiveFinite(panStep, nameof(panStep));
        ThrowIfPositiveFinite(dollyStep, nameof(dollyStep));

        Vector2 rotation = action switch
        {
            OrbitNavigationAction.RotateLeft => new Vector2(-rotationStepRadians, 0f),
            OrbitNavigationAction.RotateRight => new Vector2(rotationStepRadians, 0f),
            OrbitNavigationAction.RotateUp => new Vector2(0f, -rotationStepRadians),
            OrbitNavigationAction.RotateDown => new Vector2(0f, rotationStepRadians),
            _ => Vector2.Zero,
        };
        Vector2 pan = action switch
        {
            OrbitNavigationAction.PanLeft => new Vector2(-panStep, 0f),
            OrbitNavigationAction.PanRight => new Vector2(panStep, 0f),
            OrbitNavigationAction.PanUp => new Vector2(0f, panStep),
            OrbitNavigationAction.PanDown => new Vector2(0f, -panStep),
            _ => Vector2.Zero,
        };
        float dolly = action switch
        {
            OrbitNavigationAction.DollyIn => dollyStep,
            OrbitNavigationAction.DollyOut => -dollyStep,
            _ => 0f,
        };
        return TryApply(controller, arbiter, rotation, pan, dolly);
    }

    internal static bool TryApplyNavigationDelta(
        IViewportNavigationController controller,
        ViewportControlArbiter? arbiter,
        Vector2 rotationRadians,
        Vector2 panViewportDelta,
        float dollyDelta)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ValidateNavigationDelta(rotationRadians, panViewportDelta, dollyDelta);
        if (rotationRadians == Vector2.Zero &&
            panViewportDelta == Vector2.Zero &&
            dollyDelta == 0f)
        {
            return false;
        }
        return TryApply(
            controller,
            arbiter,
            rotationRadians,
            panViewportDelta,
            dollyDelta);
    }

    internal static void ValidateNavigationDelta(
        Vector2 rotationRadians,
        Vector2 panViewportDelta,
        float dollyDelta)
    {
        if (!float.IsFinite(rotationRadians.X) || !float.IsFinite(rotationRadians.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(rotationRadians));
        }
        if (!float.IsFinite(panViewportDelta.X) || !float.IsFinite(panViewportDelta.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(panViewportDelta));
        }
        if (!float.IsFinite(dollyDelta))
        {
            throw new ArgumentOutOfRangeException(nameof(dollyDelta));
        }
    }

    private static bool TryApply(
        IViewportNavigationController controller,
        ViewportControlArbiter? arbiter,
        Vector2 rotation,
        Vector2 pan,
        float dolly)
    {
        ViewportControlLease? lease = arbiter?.TryAcquire(
            LeaseOwnerName,
            ViewportControlPriorities.Camera);
        if (arbiter is not null && lease is null)
        {
            return false;
        }
        using (lease)
        {
            bool accepted = false;
            if (rotation != Vector2.Zero)
            {
                accepted |= controller.Rotate(rotation);
            }
            if (pan != Vector2.Zero)
            {
                accepted |= controller.Pan(pan);
            }
            if (dolly != 0f)
            {
                accepted |= controller.Dolly(dolly);
            }
            return accepted;
        }
    }

    private static void ThrowIfPositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "The input step must be positive and finite.");
        }
    }
}

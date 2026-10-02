using System.Numerics;
using Mu3D.Toolkit.Controls;

namespace Mu3D.Maui.Toolkit.Controls;

internal static class FlyNavigationState
{
    private const string CommandOwnerName = "FlyTool command";

    internal static bool TryApply(
        FlyController controller,
        ViewportControlArbiter arbiter,
        FlyNavigationAction action,
        float movementStep,
        float lookStepRadians)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(arbiter);
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }
        ThrowIfPositiveFinite(movementStep, nameof(movementStep));
        ThrowIfPositiveFinite(lookStepRadians, nameof(lookStepRadians));

        (Vector3 movement, Vector2 look) = action switch
        {
            FlyNavigationAction.MoveForward =>
                (new Vector3(0f, 0f, movementStep), Vector2.Zero),
            FlyNavigationAction.MoveBackward =>
                (new Vector3(0f, 0f, -movementStep), Vector2.Zero),
            FlyNavigationAction.MoveLeft =>
                (new Vector3(-movementStep, 0f, 0f), Vector2.Zero),
            FlyNavigationAction.MoveRight =>
                (new Vector3(movementStep, 0f, 0f), Vector2.Zero),
            FlyNavigationAction.MoveUp =>
                (new Vector3(0f, movementStep, 0f), Vector2.Zero),
            FlyNavigationAction.MoveDown =>
                (new Vector3(0f, -movementStep, 0f), Vector2.Zero),
            FlyNavigationAction.LookLeft =>
                (Vector3.Zero, new Vector2(-lookStepRadians, 0f)),
            FlyNavigationAction.LookRight =>
                (Vector3.Zero, new Vector2(lookStepRadians, 0f)),
            FlyNavigationAction.LookUp =>
                (Vector3.Zero, new Vector2(0f, lookStepRadians)),
            FlyNavigationAction.LookDown =>
                (Vector3.Zero, new Vector2(0f, -lookStepRadians)),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        return TryApplyDelta(controller, arbiter, movement, look);
    }

    internal static bool TryApplyDelta(
        FlyController controller,
        ViewportControlArbiter? arbiter,
        Vector3 localMovement,
        Vector2 lookDeltaRadians)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ValidateDelta(localMovement, lookDeltaRadians);
        if (localMovement == Vector3.Zero && lookDeltaRadians == Vector2.Zero)
        {
            return false;
        }

        using ViewportControlLease? lease = arbiter?.TryAcquire(
            CommandOwnerName,
            ViewportControlPriorities.Camera);
        if (arbiter is not null && lease is null)
        {
            return false;
        }
        return controller.Navigate(localMovement, lookDeltaRadians);
    }

    internal static void ValidateDelta(Vector3 localMovement, Vector2 lookDeltaRadians)
    {
        if (!float.IsFinite(localMovement.X) ||
            !float.IsFinite(localMovement.Y) ||
            !float.IsFinite(localMovement.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(localMovement));
        }
        if (!float.IsFinite(lookDeltaRadians.X) || !float.IsFinite(lookDeltaRadians.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(lookDeltaRadians));
        }
    }

    private static void ThrowIfPositiveFinite(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

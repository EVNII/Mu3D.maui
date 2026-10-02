#if WINDOWS || IOS || MACCATALYST
using System.Numerics;
using Mu3D.Maui.Toolkit.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Composes the Fly Gallery's configurable desktop held-key chord.</summary>
public partial class FlyControlsPage
{
    private const float KeyboardCommandsPerSecond = 12f;
    private HeldKeyboardInputSession? keyboardInput;

    partial void ConfigureContinuousKeyboardInput()
    {
        KeyboardInput keys = FlyNavigation.Input.Keyboard;
        ConfigureMenuHint(MoveForwardMenuItem, "Move forward", keys.DollyInKey);
        ConfigureMenuHint(MoveBackwardMenuItem, "Move backward", keys.DollyOutKey);
        ConfigureMenuHint(MoveLeftMenuItem, "Strafe left", keys.PanLeftKey);
        ConfigureMenuHint(MoveRightMenuItem, "Strafe right", keys.PanRightKey);
        ConfigureMenuHint(MoveUpMenuItem, "Move up", keys.PanUpKey);
        ConfigureMenuHint(MoveDownMenuItem, "Move down", keys.PanDownKey);
        ConfigureMenuHint(LookLeftMenuItem, "Look left", keys.RotateLeftKey);
        ConfigureMenuHint(LookRightMenuItem, "Look right", keys.RotateRightKey);
        ConfigureMenuHint(LookUpMenuItem, "Look up", keys.RotateUpKey);
        ConfigureMenuHint(LookDownMenuItem, "Look down", keys.RotateDownKey);
    }

    partial void AttachContinuousKeyboardInput()
    {
        keyboardInput ??= new HeldKeyboardInputSession(
            SceneView,
            () => Window,
            IsFlyKeyBound,
            ApplyFlyKeyboardFrame);
        keyboardInput.Attach();
    }

    partial void DetachContinuousKeyboardInput() => keyboardInput?.Detach();

    private bool IsFlyKeyBound(ViewportKey key)
    {
        KeyboardInput keys = FlyNavigation.Input.Keyboard;
        return key != ViewportKey.None &&
            (key == keys.DollyInKey ||
             key == keys.DollyOutKey ||
             key == keys.PanLeftKey ||
             key == keys.PanRightKey ||
             key == keys.PanUpKey ||
             key == keys.PanDownKey ||
             key == keys.RotateLeftKey ||
             key == keys.RotateRightKey ||
             key == keys.RotateUpKey ||
             key == keys.RotateDownKey);
    }

    private void ApplyFlyKeyboardFrame(
        HeldKeyboardInputSession input,
        float elapsedSeconds)
    {
        KeyboardInput keys = FlyNavigation.Input.Keyboard;
        Vector3 movementAxis = new(
            Axis(input, keys.PanRightKey) - Axis(input, keys.PanLeftKey),
            Axis(input, keys.PanUpKey) - Axis(input, keys.PanDownKey),
            Axis(input, keys.DollyInKey) - Axis(input, keys.DollyOutKey));
        if (movementAxis.LengthSquared() > 1f)
        {
            movementAxis = Vector3.Normalize(movementAxis);
        }

        Vector2 lookAxis = new(
            Axis(input, keys.RotateRightKey) - Axis(input, keys.RotateLeftKey),
            Axis(input, keys.RotateUpKey) - Axis(input, keys.RotateDownKey));
        if (lookAxis.LengthSquared() > 1f)
        {
            lookAxis = Vector2.Normalize(lookAxis);
        }

        FlyNavigation.TryNavigate(
            movementAxis * FlyNavigation.MovementStep *
                KeyboardCommandsPerSecond * elapsedSeconds,
            lookAxis * FlyNavigation.LookStepRadians *
                KeyboardCommandsPerSecond * elapsedSeconds);
    }

    private static void ConfigureMenuHint(
        MenuFlyoutItem item,
        string label,
        ViewportKey key)
    {
        item.KeyboardAccelerators.Clear();
        item.Text = key == ViewportKey.None ? $"{label} (unbound)" : $"{label} ({key})";
    }

    private static float Axis(HeldKeyboardInputSession input, ViewportKey key) =>
        input.IsPressed(key) ? 1f : 0f;
}
#endif

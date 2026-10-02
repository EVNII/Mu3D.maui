#if WINDOWS || IOS || MACCATALYST
using System.Numerics;
using Mu3D.Maui.Toolkit.Controls;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Composes the Map Gallery's configurable desktop held-key chord.</summary>
public partial class MapControlsPage
{
    private const float KeyboardCommandsPerSecond = 12f;
    private HeldKeyboardInputSession? keyboardInput;

    partial void AttachContinuousKeyboardInput()
    {
        keyboardInput ??= new HeldKeyboardInputSession(
            SceneView,
            () => Window,
            IsMapKeyBound,
            ApplyMapKeyboardFrame);
        keyboardInput.Attach();
    }

    partial void DetachContinuousKeyboardInput() => keyboardInput?.Detach();

    private bool IsMapKeyBound(ViewportKey key) =>
        key != ViewportKey.None &&
        (key == MapKeyboardInput.RotateLeftKey ||
         key == MapKeyboardInput.RotateRightKey ||
         key == MapKeyboardInput.RotateUpKey ||
         key == MapKeyboardInput.RotateDownKey ||
         key == MapKeyboardInput.DollyInKey ||
         key == MapKeyboardInput.DollyOutKey ||
         key == MapKeyboardInput.AlternateDollyInKey ||
         key == MapKeyboardInput.AlternateDollyOutKey);

    private void ApplyMapKeyboardFrame(
        HeldKeyboardInputSession input,
        float elapsedSeconds)
    {
        Vector2 rotationAxis = new(
            Axis(input, MapKeyboardInput.RotateRightKey) -
                Axis(input, MapKeyboardInput.RotateLeftKey),
            Axis(input, MapKeyboardInput.RotateDownKey) -
                Axis(input, MapKeyboardInput.RotateUpKey));
        if (rotationAxis.LengthSquared() > 1f)
        {
            rotationAxis = Vector2.Normalize(rotationAxis);
        }

        float dollyAxis = Math.Clamp(
            (IsDollyInPressed(input) ? 1f : 0f) -
                (IsDollyOutPressed(input) ? 1f : 0f),
            -1f,
            1f);
        MapNavigation.TryNavigate(
            rotationAxis * MapNavigation.NavigationRotationStepRadians *
                KeyboardCommandsPerSecond * elapsedSeconds,
            Vector2.Zero,
            dollyAxis * MapNavigation.NavigationDollyStep *
                KeyboardCommandsPerSecond * elapsedSeconds);
    }

    private bool IsDollyInPressed(HeldKeyboardInputSession input) =>
        input.IsPressed(MapKeyboardInput.DollyInKey) ||
        input.IsPressed(MapKeyboardInput.AlternateDollyInKey);

    private bool IsDollyOutPressed(HeldKeyboardInputSession input) =>
        input.IsPressed(MapKeyboardInput.DollyOutKey) ||
        input.IsPressed(MapKeyboardInput.AlternateDollyOutKey);

    private static float Axis(HeldKeyboardInputSession input, ViewportKey key) =>
        input.IsPressed(key) ? 1f : 0f;
}
#endif

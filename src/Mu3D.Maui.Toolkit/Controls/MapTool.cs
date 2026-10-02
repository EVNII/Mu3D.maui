using Mu3D.Toolkit.Controls;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Provides a map/CAD preset for the configurable orbit navigation tool.</summary>
/// <remarks>
/// Left-button and direct one-finger drag pan across the world-up plane; right-button, native
/// trackpad two-finger drag and direct two-finger drag rotate around the target; pinch and supported
/// pointer wheels dolly. <see cref="OrbitTool.NavigationCommand"/> provides focus-independent
/// discrete navigation, while the optional focused hardware-key adapter can supply the same
/// rotate/dolly actions. Map changes only declarative Orbit defaults; it does not introduce a
/// separate input or controller implementation.
/// </remarks>
public sealed class MapTool : OrbitTool
{
    /// <summary>Initializes the default map input mapping.</summary>
    public MapTool()
    {
        PanMode = OrbitPanMode.WorldUpPlane;
        MaximumPolarAngle = MathF.PI / 2f - 0.0001f;
        Input.Mouse.LeftButtonDragAction = ViewportDragAction.Pan;
        Input.Mouse.RightButtonDragAction = ViewportDragAction.Rotate;
        Input.Trackpad.TwoFingerDragAction = ViewportDragAction.Rotate;
        Input.Touchscreen.OneFingerDragAction = ViewportDragAction.Pan;
        Input.Touchscreen.TwoFingerDragAction = ViewportDragAction.Rotate;

        Input.Keyboard.RotateLeftKey = ViewportKey.A;
        Input.Keyboard.RotateRightKey = ViewportKey.D;
        Input.Keyboard.RotateUpKey = ViewportKey.W;
        Input.Keyboard.RotateDownKey = ViewportKey.S;
        Input.Keyboard.DollyInKey = ViewportKey.E;
        Input.Keyboard.DollyOutKey = ViewportKey.Q;
        Input.Keyboard.AlternateDollyInKey = ViewportKey.PageUp;
        Input.Keyboard.AlternateDollyOutKey = ViewportKey.PageDown;
    }
}

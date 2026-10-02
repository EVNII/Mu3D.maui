using System.Numerics;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Gizmos;

namespace Mu3D.Maui.Toolkit.Controls;

// Retain the internal MAUI adapter boundary; all projection/drag math is shared with other hosts.
internal sealed class TransformGizmoPointerState : IDisposable
{
    private readonly TransformGizmoDragController controller = new();
    internal event Action<Exception>? InteractionFailed
    {
        add => controller.InteractionFailed += value;
        remove => controller.InteractionFailed -= value;
    }
    internal event Action? InteractionRevoked
    {
        add => controller.InteractionRevoked += value;
        remove => controller.InteractionRevoked -= value;
    }
    internal bool IsActive => controller.IsActive;
    internal bool Begin(TransformGizmo gizmo, TransformGizmoHit hit, PerspectiveCamera camera,
        uint width, uint height, Vector2 position, ViewportControlArbiter? arbiter,
        float scaleFactorPerGizmoLength) =>
        controller.Begin(gizmo, hit, camera, width, height, position, arbiter, scaleFactorPerGizmoLength);
    internal bool Update(Vector2 position) => controller.Update(position);
    internal void Complete() => controller.Complete();
    internal void Cancel() => controller.Cancel();
    public void Dispose() => controller.Dispose();
    internal static Vector2 MapViewportPosition(double x, double y, double width, double height,
        uint viewportWidth, uint viewportHeight) =>
        TransformGizmoDragController.MapViewportPosition(x, y, width, height, viewportWidth, viewportHeight);
}

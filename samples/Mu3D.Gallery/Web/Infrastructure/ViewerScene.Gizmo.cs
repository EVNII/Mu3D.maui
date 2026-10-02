using System.Numerics;
using Mu3D.Rendering;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Rendering;

namespace Mu3D.GalleryApp.Web.Infrastructure;

internal sealed partial class ViewerScene
{
    private TransformGizmo gizmo = null!;
    private TransformGizmoRenderPass gizmoPass = null!;
    private readonly TransformGizmoDragController drag = new();
    private readonly TransformGizmoHitTester hitTester = new();
    private uint viewportWidth, viewportHeight;
    private bool gizmoEnabled = true;

    private void InitializeGizmo()
    {
        gizmo = new(model) { ScreenSizePixels = 84 };
        gizmoPass = new(gizmo);
    }

    internal void SetViewport(uint width, uint height)
    {
        if (width != viewportWidth || height != viewportHeight) drag.Cancel();
        viewportWidth = width; viewportHeight = height;
        camera.AspectRatio = (float)width / height;
    }

    internal void ConfigureGizmo(int mode, bool local, bool enabled, bool resetModel)
    {
        if (!Enum.IsDefined((TransformGizmoMode)mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        drag.Cancel();
        gizmo.Mode = (TransformGizmoMode)mode;
        gizmo.Space = local ? TransformGizmoSpace.Local : TransformGizmoSpace.World;
        gizmoEnabled = enabled;
        if (resetModel) ResetSelectedPose();
    }

    internal bool BeginDrag(float x, float y)
    {
        SynchronizeSelectionTargets();
        if (!gizmoEnabled || drag.IsActive || viewportWidth == 0 || viewportHeight == 0 ||
            !float.IsFinite(x) || !float.IsFinite(y) || x is < 0 or > 1 || y is < 0 or > 1) return false;
        Vector2 point = new(x * viewportWidth, y * viewportHeight);
        TransformGizmoHit? hit = hitTester.HitTest(gizmo, camera, viewportWidth, viewportHeight, point);
        return hit is not null && drag.Begin(gizmo, hit, camera, viewportWidth, viewportHeight, point, null, 2);
    }

    internal void UpdateDrag(float x, float y) => drag.Update(new(x * viewportWidth, y * viewportHeight));
    internal void EndDrag(bool cancel) { if (cancel) drag.Cancel(); else drag.Complete(); }
    internal void RenderGizmo(RenderPassContext context) { SynchronizeSelectionTargets(); if (gizmoEnabled) gizmoPass.Execute(context); }
}

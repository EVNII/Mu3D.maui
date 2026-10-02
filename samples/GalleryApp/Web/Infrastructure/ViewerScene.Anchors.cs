using System.Numerics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Application-chosen IDs connect borrowed Core nodes to arbitrary HTML elements. They are not
// universal screen positions on SceneNode. The JS adapter consumes this viewport-specific batch.
internal sealed record ViewerAnchorPoint(string Id, bool Projected, bool InsideViewport,
    double X, double Y, float Depth);
internal sealed record ViewerAnchorFrame(int FrameId, double Width, double Height, ViewerAnchorPoint[] Points);

internal sealed partial class ViewerScene
{
    private Scene anchorScene = null!;
    private (string Id, SceneNode Node, Vector3 LocalPosition)[] anchors = [];

    private void InitializeAnchors(Scene scene, Mesh[] meshes)
    {
        anchorScene = scene;
        anchors = [("blue", meshes[0], new(0, 0.8f, 0)),
            ("cone", meshes[1], new(0, 0.85f, 0)), ("green", meshes[2], new(0, 0.8f, 0))];
    }

    internal ViewerAnchorFrame CaptureAnchors(int frame, double logicalWidth, double logicalHeight)
    {
        // Called immediately after successful submission, with no intervening scene mutation.
        // Reuses MAUI's exact projection math; WebGPU does not acknowledge physical presentation.
        ViewportFrameSnapshot snapshot = new((ulong)frame, camera.ViewMatrix, camera.ProjectionMatrix,
            viewportWidth, viewportHeight, logicalWidth, logicalHeight);
        HashSet<SceneNode> visible = [.. anchorScene.EnumerateVisible(camera.VisibilityMask)];
        ViewerAnchorPoint[] points = new ViewerAnchorPoint[anchors.Length];
        for (int i = 0; i < anchors.Length; i++)
        {
            var (id, node, localPosition) = anchors[i];
            ViewportProjection projection = default;
            bool projected = visible.Contains(node) &&
                snapshot.TryProject(Vector3.Transform(localPosition, node.WorldMatrix), out projection);
            points[i] = new(id, projected, projected && projection.IsInsideViewport,
                projected ? projection.LogicalPosition.X : 0, projected ? projection.LogicalPosition.Y : 0,
                projected ? projection.Depth : 0);
        }
        return new(frame, logicalWidth, logicalHeight, points);
    }
}

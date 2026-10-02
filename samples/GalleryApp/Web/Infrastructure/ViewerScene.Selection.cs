using System.Numerics;
using Mu3D.Color;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Rendering;
using Mu3D.Toolkit.Selection;

namespace Mu3D.GalleryApp.Web.Infrastructure;

internal sealed record ViewerSelectionCandidate(string Token, float Distance, int TriangleIndex,
    float[] WorldPosition, float[] LocalPosition);
internal sealed record ViewerSelectionCandidates(string SourceId, ViewerSelectionCandidate[] Candidates);
internal sealed record ViewerSelectionReport(long Version, string? Token, bool TargetVisible,
    bool HighlightEnabled, float[]? Position, float[]? Rotation, float[]? Scale);

internal sealed partial class ViewerScene
{
    private readonly SceneRaycaster selectionRaycaster = new();
    private readonly OutlineHelper selectionOutline = new();
    private readonly Dictionary<SceneNode, TransformGizmoPose> initialSelectionPoses = new(ReferenceEqualityComparer.Instance);
    private OutlineHelperRenderPass selectionOutlinePass = null!;
    private Scene? selectionScene;
    private SceneNode? selectedNode;
    private string? selectedToken;
    private long selectionVersion;
    private bool highlightEnabled = true;

    internal SceneNode? SelectedNode => selectedNode;

    private void InitializeSelection(Scene scene)
    {
        selectionScene = scene;
        foreach (SceneNode node in nodeHandles.Values)
            initialSelectionPoses.Add(node, TransformGizmoPose.Capture(node));
        // Match the native outline adapter's #FF38E8FF sRGB default in linear scene color.
        static float Decode(float value) => value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
        OutlineHelperRenderStyle style = new(new LinearRgba(Decode(56f / 255f), Decode(232f / 255f), 1, 1,
            StandardColorSpaces.LinearSrgb));
        selectionOutlinePass = new(selectionOutline, style, StandardColorSpaces.LinearSrgb, "Selected object outline");
        selectedNode = model;
        selectedToken = NodeCatalog.Nodes.Single(node => node.Key == "model").Token;
        SynchronizeSelectionTargets();
    }

    internal ViewerSelectionCandidates HitTestSelection(double x, double y, uint mask)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 ||
            x > viewportWidth || y > viewportHeight)
            throw new ArgumentOutOfRangeException(nameof(x), "Selection positions must be inside the physical viewport.");
        Scene scene = selectionScene ?? throw new ObjectDisposedException(nameof(ViewerScene));
        if (viewportWidth == 0 || viewportHeight == 0 || (mask & 1) == 0)
            return new(NodeCatalog.SourceId, []);
        List<ViewerSelectionCandidate> candidates = [];
        // This fixture declares each editable PBR mesh as an owner/category 1; reference patches
        // remain unselectable. A consuming application supplies its own owner/ranking policy.
        foreach (SceneRaycastHit hit in selectionRaycaster.HitTest(scene, camera, viewportWidth, viewportHeight,
            new((float)x, (float)y)))
        {
            string? token = nodeHandles.FirstOrDefault(pair => ReferenceEquals(pair.Value, hit.Mesh)).Key;
            if (token is null) continue;
            candidates.Add(new(token, hit.Distance, hit.TriangleIndex,
                [hit.WorldPosition.X, hit.WorldPosition.Y, hit.WorldPosition.Z],
                [hit.LocalPosition.X, hit.LocalPosition.Y, hit.LocalPosition.Z]));
        }
        return new(NodeCatalog.SourceId, candidates.ToArray());
    }

    internal ViewerSelectionReport SelectNode(string token)
    {
        ObjectDisposedException.ThrowIf(selectionScene is null, this);
        SceneNode? node = token.Length == 0 ? null : nodeHandles.GetValueOrDefault(token) ??
            throw new ArgumentException("The selected node does not belong to this scene source.", nameof(token));
        if (!ReferenceEquals(node, selectedNode))
        {
            drag.Cancel();
            selectedNode = node;
            selectedToken = node is null ? null : token;
            selectionVersion++;
        }
        return CaptureSelection();
    }

    internal void ConfigureHighlight(bool enabled) => highlightEnabled = enabled;

    internal ViewerSelectionReport CaptureSelection()
    {
        SynchronizeSelectionTargets();
        if (selectedNode is null) return new(selectionVersion, null, false, highlightEnabled, null, null, null);
        Vector3 position = selectedNode.Transform.Position, scale = selectedNode.Transform.Scale;
        Quaternion rotation = selectedNode.Transform.Rotation;
        return new(selectionVersion, selectedToken, gizmo.Target is not null, highlightEnabled,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W],
            [scale.X, scale.Y, scale.Z]);
    }

    private void SynchronizeSelectionTargets()
    {
        SceneNode? effective = selectedNode is not null && selectionScene is not null &&
            selectionScene.EnumerateVisible(camera.VisibilityMask).Any(node => ReferenceEquals(node, selectedNode))
            ? selectedNode : null;
        if (!ReferenceEquals(gizmo.Target, effective))
        {
            drag.Cancel();
            gizmo.Target = effective;
        }
        // A masked group can still contain independently layered, visible children. The shared
        // outline pass checks scene membership/ancestry and filters every contributing mesh.
        selectionOutline.Target = selectedNode;
    }

    private void ResetSelectedPose()
    {
        if (selectedNode is not null && initialSelectionPoses.TryGetValue(selectedNode, out TransformGizmoPose pose))
            pose.ApplyTo(selectedNode);
    }

    internal void RenderSelectionHighlight(RenderPassContext context)
    {
        SynchronizeSelectionTargets();
        if (highlightEnabled && selectionOutline.Target is not null) selectionOutlinePass.Execute(context);
    }

    private void DisposeSelection()
    {
        selectionOutlinePass.Dispose();
        selectionOutline.Target = null;
        initialSelectionPoses.Clear();
        selectedNode = null;
        selectedToken = null;
        selectionScene = null;
    }
}

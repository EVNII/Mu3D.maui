using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.GalleryApp.Web.Infrastructure;
using static ViewerTestChecks;

internal static class ViewerSelectionChecks
{
    internal static void Validate(Action<bool, string> check)
    {
        ValidateCandidateBridge(check);
        ValidateSelectionBinding(check);
        ValidateRetainedTarget(check);
        ValidateRenderFeatureWiring(check);
    }

    private static void ValidateCandidateBridge(Action<bool, string> check)
    {
        Scene scene = SharedValidationScene.Create();
        Mesh[] meshes = scene.Root.Children.OfType<Mesh>().Where(mesh => mesh.Material is PbrMaterial).ToArray();
        Mesh[] patches = scene.Root.Children.OfType<Mesh>().Where(mesh => mesh.Material is UnlitMaterial).ToArray();
        PerspectiveCamera camera = new();
        using ViewerScene viewer = new(scene, camera);
        viewer.SetViewport(1000, 600);
        string blueToken = Token(viewer, "blue"), greenToken = Token(viewer, "green");
        ViewerSelectionReport initial = viewer.CaptureSelection();
        check(initial.Token == Token(viewer, "model") && initial.TargetVisible && viewer.SelectedNode == meshes[0].Parent!.Parent,
            "The viewer explicitly preserves the existing editable-group Gizmo target as its initial selection.");
        foreach (Mesh mesh in meshes)
        {
            Vector2 point = Project(camera, mesh, 1000, 600);
            ViewerSelectionCandidates hits = viewer.HitTestSelection(point.X, point.Y, uint.MaxValue);
            check(hits.SourceId == viewer.NodeCatalog.SourceId && hits.Candidates.Length > 0 &&
                hits.Candidates[0].Token == Token(viewer, mesh == meshes[0] ? "blue" : mesh == meshes[1] ? "cone" : "green"),
                "Projected mesh centers produce the actual shared raycaster's matching editable-node proposal.");
        }
        check(viewer.CaptureSelection().Version == initial.Version && viewer.CaptureSelection().Token == initial.Token,
            "Geometric hit testing proposes candidates without committing application-owned selection.");
        Vector2 patchPoint = Project(camera, patches[0], 1000, 600);
        check(viewer.HitTestSelection(patchPoint.X, patchPoint.Y, uint.MaxValue).Candidates.Length == 0,
            "HDR reference patches do not become editable selection candidates.");
        Vector2 bluePoint = Project(camera, meshes[0], 1000, 600);
        check(viewer.HitTestSelection(bluePoint.X, bluePoint.Y, 0).Candidates.Length == 0,
            "An empty application selection mask excludes every geometric proposal.");
        meshes[0].Transform.Position = new(0, .3f, 1);
        meshes[1].Transform.Position = new(4, .3f, 0);
        meshes[2].Transform.Position = new(0, .3f, -1);
        Vector2 overlapPoint = Project(camera, meshes[0], 1000, 600);
        ViewerSelectionCandidates overlap = viewer.HitTestSelection(overlapPoint.X, overlapPoint.Y, uint.MaxValue);
        check(overlap.Candidates.Select(candidate => candidate.Token).SequenceEqual([blueToken, greenToken]),
            "The host preserves every shared hit's scoped token for application ranking policy.");
        string json = JsonSerializer.Serialize(overlap, ViewerSelectionJsonContext.Default.ViewerSelectionCandidates);
        using JsonDocument parsed = JsonDocument.Parse(json);
        check(parsed.RootElement.GetProperty("SourceId").GetString() == viewer.NodeCatalog.SourceId &&
            parsed.RootElement.GetProperty("Candidates")[0].GetProperty("Token").GetString() == blueToken,
            "Bounded source-generated serialization preserves the managed selection proposal contract.");
    }

    private static void ValidateSelectionBinding(Action<bool, string> check)
    {
        Scene scene = SharedValidationScene.Create();
        Mesh blue = scene.Root.Children.OfType<Mesh>().First();
        PerspectiveCamera camera = new();
        using ViewerScene viewer = new(scene, camera);
        viewer.SetViewport(1000, 1000);
        ViewerSelectionReport original = viewer.CaptureSelection();
        check(viewer.BeginDrag(.542f, .5f), "The existing group translation handle is available before retargeting.");
        viewer.UpdateDrag(.58f, .5f);
        check(viewer.Report(1).ModelPosition[0] > .1f, "The retarget test first modifies the active group drag.");
        check(Throws<ArgumentException>(() => viewer.SelectNode("foreign-node")) &&
            viewer.Report(2).Dragging && viewer.CaptureSelection().Version == original.Version,
            "Unknown scoped tokens leave selection and an active edit intact.");
        string blueToken = Token(viewer, "blue");
        viewer.SelectNode(blueToken);
        ViewerSelectionReport selected = viewer.CaptureSelection();
        check(selected.Token == blueToken && selected.Version > original.Version && ReferenceEquals(viewer.SelectedNode, blue) &&
            !viewer.Report(3).Dragging && viewer.Report(3).ModelPosition.All(value => value == 0),
            "Retargeting cancels and restores the old drag before borrowing the selected leaf as the Gizmo target.");
        viewer.SelectNode(blueToken);
        check(viewer.CaptureSelection().Version == selected.Version,
            "Selecting an unchanged node does not create a duplicate observable revision.");
        Vector2 point = Project(camera, blue, 1000, 1000);
        check(viewer.BeginDrag(point.X / 1000 + .042f, point.Y / 1000),
            "The selected leaf receives its own projected translation handle.");
        viewer.UpdateDrag(point.X / 1000 + .08f, point.Y / 1000);
        viewer.EndDrag(false);
        check(blue.Transform.Position.X > -1.4f && viewer.Report(4).ModelPosition.All(value => value == 0),
            "A selected leaf edit changes that leaf while the group pose stays intact.");
        viewer.ConfigureGizmo(0, false, true, true);
        check(blue.Transform.Position == new Vector3(-1.55f, .3f, 0) && blue.Transform.Rotation == Quaternion.Identity &&
            blue.Transform.Scale == Vector3.One,
            "Reset restores the selected node's original local pose rather than resetting an unrelated group.");
        check(selected.Position!.SequenceEqual([-1.55f, .3f, 0]) && selected.Scale!.SequenceEqual([1f, 1f, 1f]),
            "Previously delivered pose arrays retain the selection state they captured.");
        viewer.ConfigureHighlight(false);
        check(!viewer.CaptureSelection().HighlightEnabled && viewer.CaptureSelection().Version == selected.Version,
            "Highlight visibility is independent of selection identity and pose.");
        viewer.SelectNode("");
        ViewerSelectionReport none = viewer.CaptureSelection();
        check(none.Token is null && !none.TargetVisible && viewer.SelectedNode is null && none.Position is null &&
            none.Rotation is null && none.Scale is null && !viewer.BeginDrag(.542f, .5f),
            "An explicit empty selection has no target, pose or interactive Gizmo.");
        viewer.SelectNode("");
        check(viewer.CaptureSelection().Version == none.Version, "Repeated deselection is an effective no-op.");
        string json = JsonSerializer.Serialize(none, ViewerSelectionJsonContext.Default.ViewerSelectionReport);
        using JsonDocument parsed = JsonDocument.Parse(json);
        check(parsed.RootElement.GetProperty("Token").ValueKind == JsonValueKind.Null &&
            !parsed.RootElement.GetProperty("TargetVisible").GetBoolean(),
            "Source-generated selection snapshots preserve explicit null and unavailable pose values.");
    }

    private static void ValidateRetainedTarget(Action<bool, string> check)
    {
        Scene scene = SharedValidationScene.Create();
        Mesh blue = scene.Root.Children.OfType<Mesh>().First();
        PerspectiveCamera camera = new();
        using ViewerScene viewer = new(scene, camera);
        viewer.SetViewport(1000, 1000);
        string token = Token(viewer, "blue");
        viewer.SelectNode(token);
        ViewerSelectionReport visible = viewer.CaptureSelection();
        blue.IsVisible = false;
        AssertRetainedHidden(viewer, token, visible.Version, blue, check, "Hidden selected nodes");
        blue.IsVisible = true;
        camera.VisibilityMask = SceneVisibilityMask.None;
        AssertRetainedHidden(viewer, token, visible.Version, blue, check, "Camera-masked selected nodes");
        camera.VisibilityMask = SceneVisibilityMask.Default;
        blue.VisibilityMask = SceneVisibilityMask.None;
        AssertRetainedHidden(viewer, token, visible.Version, blue, check, "Node-masked selected nodes");
        blue.VisibilityMask = SceneVisibilityMask.Default;
        SceneNode parent = blue.Parent!;
        parent.IsVisible = false;
        AssertRetainedHidden(viewer, token, visible.Version, blue, check, "Selected nodes below hidden ancestors");
        parent.IsVisible = true;
        check(viewer.CaptureSelection().TargetVisible, "Restoring visibility restores the retained selected target.");
        parent.RemoveChild(blue);
        AssertRetainedHidden(viewer, token, visible.Version, blue, check, "Removed selected nodes");
        parent.AddChild(blue);
        check(viewer.CaptureSelection().TargetVisible && viewer.CaptureSelection().Version == visible.Version,
            "Reattaching the same borrowed node restores its effective target without inventing a selection change.");
    }

    private static void ValidateRenderFeatureWiring(Action<bool, string> check)
    {
        Scene scene = SharedValidationScene.Create();
        Mesh blue = scene.Root.Children.OfType<Mesh>().First();
        PerspectiveCamera camera = new();
        using RecordingGraphicsDevice device = new();
        using GraphicsTexture color = device.CreateTexture(new GraphicsTextureDescriptor(new GraphicsExtent3D(1000, 600),
            GraphicsTextureFormat.Rgba16Float, GraphicsTextureUsage.RenderAttachment, label: "selection HDR color"));
        using GraphicsTexture depth = device.CreateTexture(new GraphicsTextureDescriptor(new GraphicsExtent3D(1000, 600),
            GraphicsTextureFormat.Depth32Float, GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
            label: "selection sampleable scene depth"));
        using ViewerScene viewer = new(scene, camera);
        viewer.SetViewport(1000, 600);
        RenderPassContext context = new(scene, camera, color, depth, StandardColorSpaces.LinearSrgb,
            colorTargetInitialized: true, depthTargetInitialized: true);
        void Draw() { viewer.RenderSelectionHighlight(context); viewer.RenderGizmo(context); }
        Draw();
        check(device.SubmittedPipelineLabels.SequenceEqual(["Selected object outline mask pipeline",
            "Selected object outline composite pipeline", "Transform Gizmo pipeline"]),
            "The real selected-group outline submits its private mask and HDR composite before the interactive Gizmo.");
        SceneNode group = viewer.SelectedNode!;
        ViewerSelectionReport groupSelection = viewer.CaptureSelection();
        group.VisibilityMask = SceneVisibilityMask.None;
        device.SubmittedPipelineLabels.Clear(); Draw();
        ViewerSelectionReport maskedGroup = viewer.CaptureSelection();
        check(!maskedGroup.TargetVisible &&
            maskedGroup.Token == groupSelection.Token && maskedGroup.Version == groupSelection.Version &&
            device.SubmittedPipelineLabels.SequenceEqual(["Selected object outline mask pipeline",
                "Selected object outline composite pipeline"]) && !viewer.BeginDrag(.542f, .5f),
            "Masked groups retain application selection and outline independently layered descendants without an invisible interactive Gizmo.");
        group.VisibilityMask = SceneVisibilityMask.Default;
        scene.Remove(group);
        device.SubmittedPipelineLabels.Clear(); Draw();
        check(device.SubmittedPipelineLabels.Count == 0 && viewer.CaptureSelection().Token == groupSelection.Token,
            "A detached selected group keeps application state without stale outline or Gizmo submissions.");
        scene.Add(group);
        device.SubmittedPipelineLabels.Clear(); Draw();
        check(device.SubmittedPipelineLabels.Count == 3 && ReferenceEquals(viewer.SelectedNode, group),
            "Reattaching a selected group restores both rendering adapters with the retained selection.");
        viewer.SelectNode(Token(viewer, "blue"));
        device.SubmittedPipelineLabels.Clear(); Draw();
        check(ReferenceEquals(viewer.SelectedNode, blue) &&
            device.SubmittedPipelineLabels.SequenceEqual(["Selected object outline mask pipeline",
                "Selected object outline composite pipeline", "Transform Gizmo pipeline"]),
            "Retargeting the application binding keeps both rendering adapters connected to the selected leaf.");
        viewer.ConfigureHighlight(false); device.SubmittedPipelineLabels.Clear(); Draw();
        check(device.SubmittedPipelineLabels.SequenceEqual(["Transform Gizmo pipeline"]),
            "Disabling selected-object highlighting leaves the selected object's Gizmo available.");
        viewer.ConfigureHighlight(true);
        blue.IsVisible = false; device.SubmittedPipelineLabels.Clear(); Draw();
        check(device.SubmittedPipelineLabels.Count == 0 && viewer.CaptureSelection().Token == Token(viewer, "blue"),
            "Hidden selected meshes retain application state and submit neither stale outlines nor Gizmos.");
        blue.IsVisible = true; camera.VisibilityMask = SceneVisibilityMask.None; Draw();
        check(device.SubmittedPipelineLabels.Count == 0, "Camera masking suppresses both selected rendering adapters.");
        camera.VisibilityMask = SceneVisibilityMask.Default; viewer.SelectNode(""); Draw();
        check(device.SubmittedPipelineLabels.Count == 0, "Null selection submits no highlight or Gizmo commands.");
    }

    private static void AssertRetainedHidden(ViewerScene viewer, string token, long version, SceneNode node,
        Action<bool, string> check, string reason)
    {
        ViewerSelectionReport state = viewer.CaptureSelection();
        check(state.Token == token && state.Version == version && !state.TargetVisible && ReferenceEquals(viewer.SelectedNode, node) &&
            !viewer.BeginDrag(.542f, .5f), $"{reason} retain application selection but release the effective Gizmo/highlight target.");
    }

    private static string Token(ViewerScene viewer, string key) => viewer.NodeCatalog.Nodes.Single(node => node.Key == key).Token;

    private static Vector2 Project(PerspectiveCamera camera, SceneNode node, uint width, uint height)
    {
        ViewportFrameSnapshot frame = new(1, camera.ViewMatrix, camera.ProjectionMatrix, width, height, width, height);
        if (!frame.TryProject(Vector3.Transform(Vector3.Zero, node.WorldMatrix), out ViewportProjection projection) ||
            !projection.IsInsideViewport) throw new InvalidOperationException("The selection regression target must be inside the viewport.");
        return projection.PixelPosition;
    }
}

[JsonSerializable(typeof(ViewerSelectionReport))]
[JsonSerializable(typeof(ViewerSelectionCandidates))]
internal partial class ViewerSelectionJsonContext : JsonSerializerContext;

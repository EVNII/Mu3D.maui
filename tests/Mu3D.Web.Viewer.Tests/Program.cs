using System.Numerics;
using static ViewerTestChecks;
using Mu3D.SceneGraph;
using Mu3D.GalleryApp.Web.Infrastructure;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
static (Scene, ViewerScene) Open()
{
    Scene scene = SharedValidationScene.Create();
    return (scene, new ViewerScene(scene, new PerspectiveCamera()));
}
static ViewerInput Input() => new(1f / 60, 1f / 60, true, 0, 0, 0, 0.35f, 0.28f, false);

ValidateViewerProperties();
ValidateGizmoInput();
ValidateAnchorBridge();
ValidateBenchmark();
ViewerStatisticsChecks.Validate(Check);
ViewerNodeAnchorChecks.Validate(Check);
ViewerSelectionChecks.Validate(Check);
CanvasViewHandlerChecks.Validate(Check);
GalleryPortChecks.Validate(Check);
NewGalleryCaseChecks.Validate(Check);
GalleryCatalogChecks.Validate(Check);
#if MU3D_WEB_CONTRACTS
SceneNodeAnchorSourceChecks.Validate(Check);
await SceneNodeAnchorSourceChecks.ValidateBorrowedReferences(Check);
#endif
Console.WriteLine("Viewer host checks passed: property/input mapping, selection/node binding, Canvas lifecycle, submitted-frame diagnostics, serialization and validation benchmark accounting.");

static void ValidateViewerProperties()
{
    var (scene, viewer) = Open();
    using ViewerScene ownedViewer = viewer;
    ViewerInput step = Input();
    SceneNode[] patches = scene.Root.Children.OfType<Mesh>().Where(mesh => mesh.Material is UnlitMaterial).ToArray();
    Matrix4x4[] original = patches.Select(p => p.WorldMatrix).ToArray();
    viewer.Apply(step);
    float angle = viewer.Report(1).Angle;
    viewer.Apply(step with { Playing = false, RotateX = 0.3f, Metallic = 1, Roughness = 0.12f });
    ViewerFrameReport paused = viewer.Report(2);
    Check(paused.Angle == angle && Math.Abs(paused.CameraPosition[0]) > 0.1f, "Paused animation must still accept Orbit input.");
    Check(paused.Metallic == 1 && paused.Roughness == 0.12f, "Paused material edits must reach PbrMaterial.");
    Check(patches.Select(p => p.WorldMatrix).SequenceEqual(original), "Model animation must not move reference patches.");
    viewer.Apply(step with { ResetCamera = true, Playing = false });
    Check(Vector3.Distance(new(viewer.Report(3).CameraPosition), new(0, 0, 5.6f)) < 0.0001f, "Reset restores the application-configured camera pose.");
}

static void ValidateGizmoInput()
{
    var (_, editor) = Open();
    using ViewerScene ownedEditor = editor;
    ViewerInput step = Input();
    editor.SetViewport(1000, 1000);
    editor.Apply(step with { Playing = false });
    Check(!editor.BeginDrag(0.05f, 0.05f), "Empty space must remain available to Orbit.");
    Check(editor.BeginDrag(0.542f, 0.5f), "The shared hit tester must accept the X translation handle.");
    editor.UpdateDrag(0.58f, 0.5f);
    ViewerFrameReport translated = editor.Report(1);
    Check(translated.Dragging && translated.ModelPosition[0] != 0 && translated.ActiveAxis == "X",
        "Normalized host coordinates reach the shared X handle and update the bound model.");
    editor.Apply(step with { RotateX = 1, Dolly = 1 });
    Check(editor.Report(2).Angle == 0 && editor.Report(2).CameraPosition.SequenceEqual(translated.CameraPosition),
        "Gizmo capture must suppress animation and camera commands.");
    editor.EndDrag(true);
    Check(editor.Report(3).ModelPosition.All(v => v == 0), "Cancel restores the original model pose.");
    Check(editor.BeginDrag(0.542f, 0.5f), "A canceled drag must release ownership.");
    editor.UpdateDrag(0.58f, 0.5f); editor.EndDrag(false);
    editor.Apply(step);
    Check(editor.Report(4).ModelPosition[0] == translated.ModelPosition[0], "Animation preserves the edited parent transform.");
    editor.ConfigureGizmo(2, false, true, true);
    Check(editor.BeginDrag(0.5f, 0.5f), "Scale center accepts a uniform drag.");
    editor.UpdateDrag(0.54f, 0.46f); editor.EndDrag(false);
    float[] scale = editor.Report(5).ModelScale;
    Check(scale.Any(value => value != 1), "Scale mode configuration forwards edits to the bound model scale.");
    editor.ConfigureGizmo(1, false, true, true);
    Check(editor.BeginDrag(0.5445f, 0.4555f), "Shared hit testing accepts the face-on Z rotation ring.");
    editor.UpdateDrag(0.52f, 0.43f); editor.EndDrag(false);
    Check(editor.Report(6).ModelRotation[2] != 0, "Rotation mode configuration forwards edits to the bound model rotation.");
    editor.ConfigureGizmo(0, true, true, true);
    Check(editor.BeginDrag(0.542f, 0.5f), "Local translation can start after mode changes.");
    editor.UpdateDrag(0.58f, 0.5f); editor.SetViewport(800, 600);
    Check(!editor.Report(7).Dragging && editor.Report(7).ModelPosition.All(v => v == 0), "Resize cancels and restores an active drag.");
    editor.ConfigureGizmo(0, false, false, false);
    Check(!editor.BeginDrag(0.55f, 0.5f), "Disabled Gizmo never claims a contact.");
}

static void ValidateAnchorBridge()
{
    Scene anchorScene = SharedValidationScene.Create();
    Mesh blue = anchorScene.Root.Children.OfType<Mesh>().First();
    using ViewerScene tracking = new(anchorScene, new PerspectiveCamera());
    tracking.SetViewport(1000, 600);
    ViewerAnchorFrame initial = tracking.CaptureAnchors(1, 500, 300);
    Check(initial.Points.All(p => p.Projected && p.InsideViewport), "All three initial anchors are visible.");
    Check(initial.Points.Select(point => point.Id).SequenceEqual(["blue", "cone", "green"]),
        "Node IDs must match blue/cone/green scene order.");
    tracking.SetViewport(2000, 1200);
    Check(tracking.CaptureAnchors(2, 500, 300).Points.SequenceEqual(initial.Points),
        "Doubling backing pixels cannot double HTML coordinates.");
    ViewerAnchorFrame enlarged = tracking.CaptureAnchors(3, 1000, 600);
    Check(Math.Abs(enlarged.Points[0].X - 2 * initial.Points[0].X) < 0.001,
        "CSS extent changes must scale the logical projection.");
    Check(initial.FrameId == 1 && initial.Width == 500 && initial.Points[1].X == 250,
        "Old host batches retain their frame identity and logical extent after resize.");
    blue.IsVisible = false;
    Check(!tracking.CaptureAnchors(6, 500, 300).Points[0].Projected, "Invisible node anchors disappear.");
    blue.IsVisible = true;
    blue.Parent!.RemoveChild(blue);
    Check(!tracking.CaptureAnchors(10, 500, 300).Points[0].Projected, "Removed nodes cannot leave stale labels.");
}

static void ValidateBenchmark()
{
    ViewerBenchmark benchmark = new(1);
    Check(Throws<InvalidOperationException>(() => benchmark.Finish("test")),
        "Incomplete benchmark windows must not produce results.");
    benchmark.BeginFrame();
    byte[] allocation = new byte[4096];
    Check(Throws<InvalidOperationException>(() => benchmark.Mark(ViewerBenchmarkStage.RenderSubmit)),
        "Out-of-order stages must not corrupt timing attribution.");
    foreach (ViewerBenchmarkStage stage in Enum.GetValues<ViewerBenchmarkStage>()) benchmark.Mark(stage);
    GC.KeepAlive(allocation);
    benchmark.CompleteFrame();
    ViewerBenchmarkReport measurement = benchmark.Finish("test");
    Check(measurement.TotalAllocatedBytes[0] >= 4096 &&
        measurement.TotalAllocatedBytes[0] == measurement.StageAllocatedBytes.Sum(stage => stage[0]),
        "Per-stage allocation counts must reconcile with the frame total.");
    Check(Math.Abs(measurement.TotalMilliseconds[0] - measurement.StageMilliseconds.Sum(stage => stage[0])) < 0.000001,
        "Per-stage timings must reconcile with the frame total.");
    Check(Throws<InvalidOperationException>(() => benchmark.BeginFrame()),
        "Completed benchmark windows cannot overwrite samples.");
}

internal static class ViewerTestChecks
{
    internal static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}

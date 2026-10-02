using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mu3D.Color;
using Mu3D.GalleryApp.Examples;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Diagnostics;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Overlays;
using Mu3D.Toolkit.Rendering;
using Mu3D.Toolkit.Selection;

namespace Mu3D.GalleryApp.Web.Infrastructure;

internal sealed class ToolkitGalleryExample : IDisposable
{
    internal string Id { get; }
    internal Scene Scene { get; }
    internal PerspectiveCamera Camera { get; }
    internal NativeGalleryScene? Definition { get; }
    internal OrbitController? Orbit { get; }
    internal FlyController? Fly { get; }
    internal GridHelper? Grid { get; }
    internal AxesHelper? Axes { get; }
    internal BoundsHelper? Bounds { get; }
    internal OutlineHelper? Outline { get; }
    internal TransformGizmo? Gizmo { get; }
    internal TransformGizmoDragController Drag { get; } = new();
    internal FrameStatisticsCollector Statistics { get; } = new();
    internal SceneNodeAnchorSource? Anchors { get; }
    internal bool FeaturesEnabled = true, GridVisible = true, AxesInteractive = true, AxesVisible = true, BoundsVisible = true, OutlineVisible = true;
    internal bool OverlayDepth = false, Continuous = false, StatisticsVisible = false, StatisticsDetailed = false;
    internal bool SelectionEnabled = true, GizmoEnabled = true;
    internal double SnapshotInterval = 100;
    internal string Status { get; private set; } = "Ready";
    internal bool HasPendingMotion => Orbit?.HasPendingMotion == true || Fly?.HasPendingMotion == true;
    private readonly LinearRgba clear;
    private readonly List<IDisposable> owned = [];
    private readonly Dictionary<string, SceneNodeAnchorRegistration> registrations = [];
    private readonly Dictionary<string, SceneNode> anchorNodes = [];
    private readonly SceneNode? animated, cyan, magenta;
    private readonly TransformGizmoHitTester gizmoHitTester = new();
    private readonly AxesHelperHitTester axesHitTester = new();
    private readonly SceneRaycaster raycaster = new();
    private SceneRenderer? renderer;
    private GridHelperRenderPass? gridPass;
    private AxesHelperRenderPass? axesPass;
    private BoundsHelperRenderPass? boundsPass;
    private OutlineHelperRenderPass? outlinePass;
    private TransformGizmoRenderPass? gizmoPass;
    private bool? lastOverlayDepth;
    private uint width, height;
    private float elapsed;
    private long? lastSubmission, lastPublication;

    internal ToolkitGalleryExample(string id)
    {
        Id = id;
        clear = new(0, 0, 0, 1, StandardColorSpaces.LinearSrgb);
        if (id is "transform-gizmo" or "frame-statistics" or "ui-anchors")
        {
            Scene = new(id == "transform-gizmo" ? "Transform gizmo example" : id == "frame-statistics" ? "Frame statistics example" : "UI anchor example");
            Camera = new(name: id == "transform-gizmo" ? "Gizmo camera" : id == "frame-statistics" ? "Statistics camera" : "UI anchor camera");
            clear = new(id == "ui-anchors" ? .01f : .012f, id == "ui-anchors" ? .016f : .018f,
                id == "ui-anchors" ? .032f : .035f, 1, StandardColorSpaces.LinearSrgb);
            if (id == "transform-gizmo")
            {
                animated = new("Transform target"); ToolkitSceneExamples.AddGizmoVisual(animated); Scene.Add(animated);
                Camera.Transform.Position = new(3.2f, 2.4f, 6.2f);
                Orbit = new(Camera, Vector3.Zero) { DampingEnabled = true, DampingTime = .1f, MinimumDistance = 2.5f, MaximumDistance = 15 };
                Gizmo = CreateGizmo(animated);
            }
            else if (id == "frame-statistics")
            {
                animated = new("Animated sample"); ToolkitSceneExamples.AddStatisticsVisual(animated); Scene.Add(animated);
                Camera.Transform.Position = new(0, 0, 5); Continuous = StatisticsVisible = true;
            }
            else
            {
                cyan = new("Cyan tracked node"); magenta = new("Magenta tracked node");
                cyan.AddChild(ToolkitSceneExamples.CreateAnchorSphere(new(.08f, .9f, 1.7f, 1, StandardColorSpaces.LinearSrgb), "Cyan sphere"));
                magenta.AddChild(ToolkitSceneExamples.CreateAnchorSphere(new(1.6f, .12f, .8f, 1, StandardColorSpaces.LinearSrgb), "Magenta sphere"));
                Scene.Add(cyan); Scene.Add(magenta); Camera.Transform.Position = new(0, 0, 6);
                Anchors = new(Scene); anchorNodes.Add("cyan", cyan); anchorNodes.Add("magenta", magenta);
                Continuous = true; ToolkitSceneExamples.UpdateAnchors(cyan, magenta, 0);
            }
        }
        else
        {
            string page = id switch {
                "orbit-controls" => "OrbitControlsPage", "map-controls" => "MapControlsPage", "fly-controls" => "FlyControlsPage",
                "axes-helper" => "AxesHelperPage", "grid-helper" => "GridHelperPage", "bounds-helper" => "BoundsHelperPage",
                "declarative-scene" => "DeclarativeScenePage", "declarative-tools" => "DeclarativeToolsPage",
                _ => throw new ArgumentOutOfRangeException(nameof(id)),
            };
            Definition = NativeGalleryScene.Read(page); Scene = Definition.Scene; Camera = Definition.Camera;
            if (id == "fly-controls") Fly = new(Camera) { DampingEnabled = true, DampingTime = .1f };
            else if (id != "declarative-scene")
            {
                var tool = Definition.Find(id == "map-controls" ? "MapTool" : "OrbitTool");
                Vector3 target = new(NativeGalleryScene.Number(tool, "TargetX", 0), NativeGalleryScene.Number(tool, "TargetY", 0), NativeGalleryScene.Number(tool, "TargetZ", 0));
                Orbit = id == "map-controls" ? new MapController(Camera, target) : new OrbitController(Camera, target);
                Orbit.DampingEnabled = NativeGalleryScene.Boolean(tool, "DampingEnabled", false);
                Orbit.DampingTime = NativeGalleryScene.Number(tool, "DampingTime", .12f);
                Orbit.MinimumDistance = NativeGalleryScene.Number(tool, "MinimumDistance", .0001f);
                Orbit.MaximumDistance = NativeGalleryScene.Number(tool, "MaximumDistance", float.MaxValue);
            }
            if (Definition.Find("GridHelper") is { } grid)
                Grid = new() { Size = NativeGalleryScene.Number(grid, "Size", 10), Divisions = (int)NativeGalleryScene.Number(grid, "Divisions", 20),
                    MajorLineEvery = (int)NativeGalleryScene.Number(grid, "MajorLineEvery", 5), ShowCenterAxes = NativeGalleryScene.Boolean(grid, "ShowCenterAxes", true) };
            if (Definition.Find("AxesHelper") is { } axes)
                Axes = new() { ScreenSizePixels = NativeGalleryScene.Number(axes, "ScreenSize", 112), MarginPixels = NativeGalleryScene.Number(axes, "Margin", 14) };
            if (id == "bounds-helper")
            {
                animated = Definition.NamedNodes["BoundedCone"];
                Bounds = new() { Target = animated, Padding = .08f, Precise = true };
                Outline = new() { Target = animated };
            }
            if (id == "declarative-tools") { Gizmo = CreateGizmo(Definition.NamedNodes["TargetCone"]); StatisticsVisible = false; }
        }
        if (Orbit is not null) owned.Add(Orbit);
        if (Fly is not null) owned.Add(Fly);
        owned.Add(Drag); if (Anchors is not null) owned.Add(Anchors);
    }

    private static TransformGizmo CreateGizmo(SceneNode target) => new(target) {
        ScreenSizePixels = 110, IsRotateEnabled = true, IsScaleEnabled = true,
        TranslationSnap = .1f, RotationSnapRadians = MathF.PI / 12, ScaleSnap = .1f,
    };

    internal void Navigate(float rotateX, float rotateY, float panX, float panY, float dolly, int keys, float delta)
    {
        if (!FeaturesEnabled || Drag.IsActive) return;
        static float Key(int mask, int bit) => (mask & (1 << bit)) != 0 ? 1 : 0;
        if (Fly is not null)
        {
            Vector3 movement = new(Key(keys, 3) - Key(keys, 2), Key(keys, 4) - Key(keys, 5), Key(keys, 0) - Key(keys, 1));
            if (movement.LengthSquared() > 1) movement = Vector3.Normalize(movement);
            Vector2 look = new(Key(keys, 7) - Key(keys, 6), Key(keys, 8) - Key(keys, 9));
            if (look.LengthSquared() > 1) look = Vector2.Normalize(look);
            Fly.Navigate(movement * .75f * 12 * delta + new Vector3(panX * 4, panY * 4, dolly * .75f),
                look * (MathF.PI / 36) * 12 * delta + new Vector2(rotateX, rotateY));
            Fly.Update(delta);
        }
        else if (Orbit is not null)
        {
            Vector2 rotation = new(Key(keys, 1) - Key(keys, 0), Key(keys, 3) - Key(keys, 2));
            if (rotation.LengthSquared() > 1) rotation = Vector2.Normalize(rotation);
            Orbit.Rotate(new Vector2(rotateX, rotateY) + rotation * (MathF.PI / 36) * 12 * delta);
            Orbit.Pan(new(panX, panY));
            Orbit.Dolly(dolly + (Key(keys, 4) - Key(keys, 5)) * .12f * 12 * delta);
            Orbit.Update(delta);
        }
    }

    internal void Render(GraphicsDevice device, GraphicsTexture target, GraphicsTexture? depth, float delta)
    {
        if (width != target.Descriptor.Size.Width || height != target.Descriptor.Size.Height) Drag.Cancel();
        width = target.Descriptor.Size.Width; height = target.Descriptor.Size.Height; Camera.AspectRatio = (float)width / height;
        if (Continuous) elapsed += delta;
        if (Id == "frame-statistics" && animated is not null) animated.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(elapsed * .9f, elapsed * .28f, 0);
        if (cyan is not null && magenta is not null) ToolkitSceneExamples.UpdateAnchors(cyan, magenta, elapsed);
        renderer ??= Own(new SceneRenderer(device, GraphicsTextureFormat.Rgba16Float));
        renderer.Render(Scene, Camera, target, depth!, clear);
        if (!FeaturesEnabled) return;
        RenderPassContext context = new(Scene, Camera, target, depth, StandardColorSpaces.LinearSrgb, colorTargetInitialized: true, depthTargetInitialized: true);
        if (Grid is not null && GridVisible) { gridPass ??= Own(new GridHelperRenderPass(Grid)); gridPass.Execute(context); }
        if (Bounds is not null && Outline is not null)
        {
            if (lastOverlayDepth != OverlayDepth)
            {
                if (boundsPass is not null) { owned.Remove(boundsPass); boundsPass.Dispose(); }
                if (outlinePass is not null) { owned.Remove(outlinePass); outlinePass.Dispose(); }
                boundsPass = Own(new BoundsHelperRenderPass(Bounds, new(NativeGalleryScene.Color("#FFFF9D24"), 3,
                    OverlayDepth ? BoundsHelperDepthMode.Overlay : BoundsHelperDepthMode.SceneDepthTested), StandardColorSpaces.LinearSrgb));
                outlinePass = Own(new OutlineHelperRenderPass(Outline, new(NativeGalleryScene.Color("#FF38E8FF"), 4,
                    OverlayDepth ? OutlineHelperDepthMode.Overlay : OutlineHelperDepthMode.SceneDepthTested), StandardColorSpaces.LinearSrgb));
                lastOverlayDepth = OverlayDepth;
            }
            if (BoundsVisible) boundsPass!.Execute(context);
            if (OutlineVisible) outlinePass!.Execute(context);
        }
        if (Gizmo is not null && GizmoEnabled) { gizmoPass ??= Own(new TransformGizmoRenderPass(Gizmo)); gizmoPass.Execute(context); }
        if (Axes is not null && AxesVisible) { axesPass ??= Own(new AxesHelperRenderPass(Axes)); axesPass.Execute(context); }
    }

    private T Own<T>(T resource) where T : IDisposable { owned.Add(resource); return resource; }

    internal int BeginContact(double x, double y)
    {
        if (!FeaturesEnabled || width == 0 || height == 0) return 0;
        Vector2 point = new((float)x * width, (float)y * height);
        if (AxesVisible && AxesInteractive && Axes is not null && Orbit is not null && axesHitTester.HitTest(Axes, Camera, width, height, point) is { } axis)
        { Orbit.SetViewDirection(axis.CameraDirection); Status = $"{(axis.IsDiagonal ? "45°" : "Orthogonal")} view: {axis.Preset}"; return 2; }
        if (GizmoEnabled && Gizmo is not null && gizmoHitTester.HitTest(Gizmo, Camera, width, height, point) is { } hit &&
            Drag.Begin(Gizmo, hit, Camera, width, height, point, null, 2))
        { Status = $"Dragging {hit.Axis} ({hit.Mode}, {Gizmo.Space})"; return 1; }
        return 0;
    }
    internal void UpdateContact(double x, double y) => Drag.Update(new((float)x * width, (float)y * height));
    internal void EndContact(bool cancel)
    {
        if (!Drag.IsActive) return;
        if (cancel) Drag.Cancel(); else Drag.Complete();
        Status = cancel ? "Interaction canceled; initial transform restored" : "Transform committed";
    }
    internal void SelectAt(double x, double y, uint mask)
    {
        if (Id != "declarative-tools" || !SelectionEnabled || Definition is null || Gizmo is null || !FeaturesEnabled || width == 0 || height == 0) return;
        SceneNode? selected = raycaster.HitTest(Scene, Camera, width, height, new((float)x * width, (float)y * height))
            .FirstOrDefault(hit => (Definition.SelectionMasks.GetValueOrDefault(hit.Mesh) & mask) != 0)?.Mesh;
        Drag.Cancel(); Gizmo.Target = selected; Status = $"Selected: {selected?.Name ?? "none"}";
    }
    internal void ResetPose()
    {
        Drag.Cancel(); if (Gizmo?.Target is not { } target) return;
        if (Definition is not null)
        {
            var source = Definition.Source.Descendants().First(element => (string?)element.Attribute("Name") == target.Name);
            NativeGalleryScene.ApplyTransform(target, source);
        }
        else { target.Transform.Position = Vector3.Zero; target.Transform.Rotation = Quaternion.Identity; target.Transform.Scale = Vector3.One; }
        Status = "Transform reset";
    }

    internal string Pose => Gizmo?.Target is { } target ? $"Position {target.Transform.Position}\nRotation {target.Transform.Rotation}\nScale {target.Transform.Scale}" : "No selected target";
    internal void ResetStatistics() { Statistics.Reset(); elapsed = 0; lastSubmission = lastPublication = null; }
    internal string CompleteFrame(ulong frame, double logicalWidth, double logicalHeight, bool clockBreak)
    {
        long now = Stopwatch.GetTimestamp();
        if (clockBreak || !Continuous && !Drag.IsActive && !HasPendingMotion) lastSubmission = null;
        if (lastSubmission is long previous && Stopwatch.GetElapsedTime(previous, now) is { } duration && duration > TimeSpan.Zero)
            Statistics.RecordFrame(new(duration, rendererTimings: renderer?.LastFrameTimings,
                drawCallCount: Id == "frame-statistics" ? 1 : null,
                primitiveCount: Id == "frame-statistics" ? Scene.Root.Children.OfType<SceneNode>().SelectMany(node => node.Children).OfType<Mesh>().Sum(mesh => mesh.Geometry.Indices.Count / 3) : null));
        lastSubmission = now;
        string? statistics = null;
        if (StatisticsVisible && (lastPublication is null || Stopwatch.GetElapsedTime(lastPublication.Value, now).TotalMilliseconds >= SnapshotInterval))
        {
            Mesh[] meshes = Scene.EnumerateVisible(Camera.VisibilityMask).OfType<Mesh>().ToArray();
            Statistics.UpdateResourceCounts(new(meshCount: meshes.Length, vertexCount: meshes.Sum(mesh => mesh.Geometry.Positions.Count),
                materialCount: meshes.Select(mesh => mesh.Material).Distinct().Count(), textureCount: renderer is null ? null : renderer.CachedMaterialTextureCount + renderer.CachedMaterialDataTextureCount));
            statistics = JsonSerializer.Serialize(Statistics.CaptureSnapshot(), ToolkitJsonContext.Default.FrameStatisticsSnapshot);
            lastPublication = now;
        }
        string? anchors = Anchors is null ? null : JsonSerializer.Serialize(Anchors.CaptureFrame(new(frame, Camera.ViewMatrix, Camera.ProjectionMatrix,
            width, height, logicalWidth, logicalHeight), Camera.VisibilityMask), ToolkitAnchorJsonContext.Default.SceneNodeAnchorFrame);
        return JsonSerializer.Serialize(new ToolkitFrameReport(Continuous || HasPendingMotion || Drag.IsActive, statistics, anchors, Status), ToolkitJsonContext.Default.ToolkitFrameReport);
    }
    internal string ConfigureAnchor(string id, string target, double x, double y, double z)
    {
        SceneNodeAnchorSource source = Anchors ?? throw new InvalidOperationException("This page has no anchored nodes.");
        SceneNode? node = string.IsNullOrEmpty(target) ? null : anchorNodes.GetValueOrDefault(target) ?? throw new ArgumentException("Foreign node handle.");
        if (id.Length == 0) { var created = source.Register(node, new((float)x, (float)y, (float)z)); id = created.Id; registrations.Add(id, created); }
        else registrations[id].Update(node, new((float)x, (float)y, (float)z));
        return JsonSerializer.Serialize(new ToolkitAnchorRegistration(id, source.Revision, source.SourceId), ToolkitJsonContext.Default.ToolkitAnchorRegistration);
    }
    internal long RemoveAnchor(string id) { if (registrations.Remove(id, out var registration)) registration.Dispose(); return Anchors!.Revision; }
    public void Dispose() { foreach (var registration in registrations.Values) registration.Dispose(); registrations.Clear(); for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose(); owned.Clear(); }
}

internal sealed record ToolkitFrameReport(bool Continuous, string? Statistics, string? Anchors, string Status);
internal sealed record ToolkitAnchorRegistration(string Id, long Revision, string SourceId);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ToolkitFrameReport))]
[JsonSerializable(typeof(ToolkitAnchorRegistration))]
[JsonSerializable(typeof(FrameStatisticsSnapshot))]
internal partial class ToolkitJsonContext : JsonSerializerContext;
[JsonSerializable(typeof(SceneNodeAnchorFrame))]
internal partial class ToolkitAnchorJsonContext : JsonSerializerContext;

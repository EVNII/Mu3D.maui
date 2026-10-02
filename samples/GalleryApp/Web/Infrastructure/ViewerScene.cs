using System.Numerics;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Overlays;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Browser events are normalized by the host. This state uses the existing portable Toolkit.
internal sealed record ViewerInput(float DeltaSeconds, float FrameIntervalSeconds, bool Playing,
    float RotateX, float RotateY, float Dolly, float Metallic, float Roughness, bool ResetCamera, float PixelRatio = 1,
    double LogicalWidth = 0, double LogicalHeight = 0);

internal sealed record ViewerFrameReport(int Frame, float Angle, float Distance, float[] CameraPosition,
    float Metallic, float Roughness, double FramesPerSecond, float[] ModelPosition, float[] ModelRotation,
    float[] ModelScale, bool Dragging, string? ActiveAxis, ViewerAnchorFrame? Anchors,
    long StatisticsVersion, SceneNodeAnchorFrame? NodeAnchors = null, ViewerSelectionReport? Selection = null);

internal sealed partial class ViewerScene : IDisposable
{
    private readonly SceneNode model = new("editable PBR group");
    private readonly SceneNode spin = new("rotating PBR group");
    private readonly PbrMaterial[] materials;
    private readonly PerspectiveCamera camera;
    private OrbitController orbit;
    private float angle;

    internal ViewerScene(Scene scene, PerspectiveCamera camera)
    {
        this.camera = camera;
        Mesh[] meshes = scene.Root.Children.OfType<Mesh>().Where(mesh => mesh.Material is PbrMaterial).ToArray();
        materials = meshes.Select(mesh => (PbrMaterial)mesh.Material).ToArray();
        foreach (Mesh mesh in meshes)
        {
            scene.Root.RemoveChild(mesh);
            spin.AddChild(mesh);
        }
        model.AddChild(spin);
        scene.Root.AddChild(model);
        InitializeAnchors(scene, meshes);
        InitializeNodeAnchors(scene, meshes);
        orbit = CreateOrbit();
        InitializeGizmo();
        InitializeSelection(scene);
    }

    private OrbitController CreateOrbit()
    {
        camera.Transform.Position = new(0, 0, 5.6f);
        camera.Transform.Rotation = Quaternion.Identity;
        return new(camera, Vector3.Zero) { MinimumDistance = 3.2f, MaximumDistance = 14 };
    }

    internal void Apply(ViewerInput input)
    {
        SynchronizeSelectionTargets();
        float[] finite = [input.DeltaSeconds, input.FrameIntervalSeconds, input.RotateX, input.RotateY,
            input.Dolly, input.Metallic, input.Roughness, input.PixelRatio];
        if (finite.Any(value => !float.IsFinite(value)) || input.DeltaSeconds < 0 || input.FrameIntervalSeconds < 0 ||
            input.Metallic is < 0 or > 1 || input.Roughness is < 0.04f or > 1 || input.PixelRatio <= 0 ||
            !double.IsFinite(input.LogicalWidth) || !double.IsFinite(input.LogicalHeight) ||
            input.LogicalWidth < 0 || input.LogicalHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        if (input.ResetCamera)
        {
            drag.Cancel();
            orbit.Dispose();
            orbit = CreateOrbit();
        }
        gizmo.ScreenSizePixels = 84 * input.PixelRatio;
        hitTester.HitTolerancePixels = 8 * input.PixelRatio;
        if (!drag.IsActive)
        {
            orbit.Rotate(new(input.RotateX, input.RotateY));
            orbit.Dolly(input.Dolly);
            if (input.Playing) angle = (angle + Math.Min(input.DeltaSeconds, 0.1f) * 0.45f) % MathF.Tau;
        }
        spin.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(angle, 0, 0);
        foreach (PbrMaterial material in materials)
        {
            material.Metallic = input.Metallic;
            material.Roughness = input.Roughness;
        }
    }

    internal ViewerFrameReport Report(int frame, ViewerAnchorFrame? anchors = null, SceneNodeAnchorFrame? nodeAnchors = null)
    {
        ViewerSelectionReport selection = CaptureSelection();
        Vector3 eye = camera.Transform.Position;
        Vector3 position = model.Transform.Position, scale = model.Transform.Scale;
        Quaternion rotation = model.Transform.Rotation;
        return new(frame, angle, orbit.Distance, [eye.X, eye.Y, eye.Z], materials[0].Metallic,
            materials[0].Roughness, LatestStatisticsReport.Snapshot.FramesPerSecond,
            [position.X, position.Y, position.Z], [rotation.X, rotation.Y, rotation.Z, rotation.W],
            [scale.X, scale.Y, scale.Z], drag.IsActive, gizmo.ActiveAxis?.ToString(), anchors,
            LatestStatisticsVersion, nodeAnchors, selection);
    }

    public void Dispose() { DisposeStatistics(); DisposeSelection(); DisposeNodeAnchors(); drag.Dispose(); gizmoPass.Dispose(); gizmo.Dispose(); orbit.Dispose(); }
}

using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Examples;

// The native view and Canvas host share the authored scene and explicit alias factory.
internal sealed class RenderOutputsExample : IDisposable
{
    internal static RenderOutputId NormalAlias { get; } = new("gallery.normal-alias");
    internal static LinearRgba ClearColor { get; } = new(.008f, .012f, .025f, 1, StandardColorSpaces.LinearSrgb);
    internal static PerspectiveCamera CreateCamera() => new(name: "Render-output camera")
        { Transform = { Position = new(0, .35f, 6.2f) } };
    internal static void RegisterAlias(RenderOutputRegistry registry) => registry.Register(NormalAlias,
        static (renderer, options, name) => new SceneRenderOutputPass(renderer, RenderOutputIds.SurfaceNormal, options, name));
    private readonly GraphicsDevice device;
    private readonly Scene scene = new("Render-output example");
    private readonly PerspectiveCamera camera = CreateCamera();
    private readonly RenderOutputRegistry registry = RenderOutputRegistry.CreateDefault();
    private readonly SceneRenderPassOptions options = new(GraphicsLoadOperation.Clear, ClearColor);
    private SceneRenderer? renderer;
    private IRenderPass? pass;
    private RenderOutputId selected;

    internal RenderOutputsExample(GraphicsDevice device, EquirectangularHdrEnvironment environment)
    { this.device = device; RegisterAlias(registry); Populate(scene, environment); }
    internal void Draw(GraphicsTexture target, GraphicsTexture? depth, RenderOutputId output)
    {
        camera.AspectRatio = (float)target.Descriptor.Size.Width / target.Descriptor.Size.Height;
        renderer ??= new SceneRenderer(device, target.Descriptor.Format)
            { AmbientOcclusionEnabled = true, AmbientOcclusionRadius = .7f, AmbientOcclusionStrength = 1 };
        if (pass is null || selected != output)
        { pass = registry.CreatePass(output, renderer, options, "Gallery render output"); selected = output; }
        pass.Execute(new(scene, camera, target, depth, StandardColorSpaces.LinearSrgb));
    }
    public void Dispose() { renderer?.Dispose(); renderer = null; pass = null; }

    internal static void Populate(Scene scene, EquirectangularHdrEnvironment environment)
    {
        DirectionalLight light = new(
            new LinearRgba(1.05f, 0.96f, 0.86f, 1f, StandardColorSpaces.LinearSrgb),
            2.2f,
            "Warm key")
        {
            CastsShadows = true,
        };
        light.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-0.55f, -0.65f, 0f);
        scene.Add(light);
        scene.Add(new ImageBasedLight(environment, 0.65f, "Studio environment"));

        PbrMaterial blueMetal = new(
            new LinearRgba(0.08f, 0.28f, 0.95f, 1f, StandardColorSpaces.LinearSrgb),
            metallic: 0.85f,
            roughness: 0.2f,
            name: "Blue metal");
        Mesh sphere = new(
            MeshPrimitives.CreateUvSphere(0.9f, 48, 24),
            blueMetal,
            "Reflective sphere");
        sphere.Transform.Position = new Vector3(-1.05f, -0.2f, 0.15f);
        scene.Add(sphere);

        PbrMaterial orangeCeramic = new(
            new LinearRgba(0.95f, 0.22f, 0.045f, 1f, StandardColorSpaces.LinearSrgb),
            metallic: 0.05f,
            roughness: 0.5f,
            name: "Orange ceramic");
        Mesh cone = new(
            MeshPrimitives.CreateCone(0.85f, 1.9f, 48),
            orangeCeramic,
            "Ceramic cone");
        cone.Transform.Position = new Vector3(1.15f, -0.35f, -0.15f);
        scene.Add(cone);
        scene.Add(CreateFloor());
    }

    private static Mesh CreateFloor()
    {
        MeshGeometry geometry = new(
            [
                new Vector3(-4f, -1.25f, -3f),
                new Vector3(-4f, -1.25f, 3f),
                new Vector3(4f, -1.25f, 3f),
                new Vector3(4f, -1.25f, -3f),
            ],
            [0u, 1u, 2u, 0u, 2u, 3u],
            [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY]);
        PbrMaterial material = new(
            new LinearRgba(0.32f, 0.34f, 0.4f, 1f, StandardColorSpaces.LinearSrgb),
            metallic: 0f,
            roughness: 0.82f,
            name: "Matte floor");
        return new Mesh(geometry, material, "Floor");
    }

}

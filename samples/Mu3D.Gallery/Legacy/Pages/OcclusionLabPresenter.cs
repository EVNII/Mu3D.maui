using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

internal sealed class OcclusionLabPresenter : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Scene scene = new("Occlusion example");
    private readonly PerspectiveCamera camera = new(name: "Occlusion camera");
    private readonly DirectionalLight light;
    private SceneRenderer? renderer;
    private GraphicsTexture? depthTexture;
    private GraphicsExtent3D depthExtent;

    internal OcclusionLabPresenter(
        GraphicsDevice device,
        EquirectangularHdrEnvironment environment)
    {
        this.device = device ?? throw new ArgumentNullException(nameof(device));
        camera.Transform.Position = new Vector3(0f, 1f, 6.5f);

        light = new DirectionalLight(
            new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb),
            1.5f,
            "Shadow light")
        {
            CastsShadows = true,
            AngularDiameterRadians = 0.08f,
        };
        light.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-0.55f, -0.6f, 0f);
        scene.Add(light);
        scene.Add(new ImageBasedLight(environment, 0.55f, "Studio environment"));

        PbrMaterial material = new(
            new LinearRgba(0.75f, 0.78f, 0.85f, 1f, StandardColorSpaces.LinearSrgb),
            0f,
            0.55f,
            "Occlusion material");
        Mesh sphere = new(MeshPrimitives.CreateUvSphere(0.8f, 48, 24), material);
        sphere.Transform.Position = new Vector3(-0.8f, -0.35f, 0f);
        scene.Add(sphere);
        Mesh cone = new(MeshPrimitives.CreateCone(0.8f, 1.8f, 48), material);
        cone.Transform.Position = new Vector3(1f, -0.45f, 0.3f);
        scene.Add(cone);
        scene.Add(CreateFloor());
    }

    internal bool ShadowsEnabled { get; set; } = true;

    internal bool AmbientOcclusionEnabled { get; set; } = true;

    internal float AmbientOcclusionRadius { get; set; } = 0.7f;

    internal float AmbientOcclusionStrength { get; set; } = 1f;

    internal void Render(GraphicsTexture target, uint width, uint height)
    {
        light.CastsShadows = ShadowsEnabled;
        camera.AspectRatio = (float)width / height;
        renderer ??= new SceneRenderer(device, target.Descriptor.Format);
        renderer.AmbientOcclusionEnabled = AmbientOcclusionEnabled;
        renderer.AmbientOcclusionRadius = AmbientOcclusionRadius;
        renderer.AmbientOcclusionStrength = AmbientOcclusionStrength;
        renderer.Render(scene, camera, target, EnsureDepthTexture(width, height));
    }

    public void Dispose()
    {
        depthTexture?.Dispose();
        depthTexture = null;
        renderer?.Dispose();
        renderer = null;
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
            new LinearRgba(0.35f, 0.35f, 0.35f, 1f, StandardColorSpaces.LinearSrgb),
            0f,
            0.8f,
            "Floor");
        return new Mesh(geometry, material);
    }

    private GraphicsTexture EnsureDepthTexture(uint width, uint height)
    {
        GraphicsExtent3D extent = new(width, height);
        if (depthTexture is not null && depthExtent == extent)
        {
            return depthTexture;
        }
        depthTexture?.Dispose();
        depthTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "Occlusion depth"));
        depthExtent = extent;
        return depthTexture;
    }
}

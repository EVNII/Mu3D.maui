using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

internal sealed class LightingLabPresenter : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Scene scene = new("Lighting example");
    private readonly PerspectiveCamera camera = new(name: "Lighting camera");
    private readonly DirectionalLight directLight;
    private readonly ImageBasedLight environmentLight;
    private SceneRenderer? renderer;
    private GraphicsTexture? depthTexture;
    private GraphicsExtent3D depthExtent;

    internal LightingLabPresenter(
        GraphicsDevice device,
        EquirectangularHdrEnvironment environment)
    {
        this.device = device ?? throw new ArgumentNullException(nameof(device));
        camera.Transform.Position = new Vector3(0f, 0.5f, 6.5f);

        directLight = new DirectionalLight(
            new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb),
            1.5f,
            "Direct light");
        directLight.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-0.45f, -0.55f, 0f);
        scene.Add(directLight);

        environmentLight = new ImageBasedLight(environment, 0.7f, "Studio Small 02 HDRI");
        environmentLight.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(0.35f, 0f, 0f);
        scene.Add(environmentLight);

        MeshGeometry sphere = MeshPrimitives.CreateUvSphere(0.75f, 48, 24);
        AddSphere(sphere, -1.7f, 0.12f);
        AddSphere(sphere, 0f, 0.45f);
        AddSphere(sphere, 1.7f, 0.82f);
    }

    internal bool DirectLightEnabled { get; set; } = true;

    internal bool EnvironmentEnabled { get; set; } = true;

    internal bool AnimateLight { get; set; }

    internal void Render(GraphicsTexture target, uint width, uint height, float elapsedSeconds)
    {
        directLight.Intensity = DirectLightEnabled ? 1.5f : 0f;
        environmentLight.Intensity = EnvironmentEnabled ? 0.7f : 0f;
        if (AnimateLight)
        {
            directLight.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(
                -0.45f + MathF.Sin(elapsedSeconds * 0.7f) * 0.55f,
                -0.55f,
                0f);
        }
        camera.AspectRatio = (float)width / height;
        renderer ??= new SceneRenderer(device, target.Descriptor.Format);
        renderer.Render(scene, camera, target, EnsureDepthTexture(width, height));
    }

    public void Dispose()
    {
        depthTexture?.Dispose();
        depthTexture = null;
        renderer?.Dispose();
        renderer = null;
    }

    private void AddSphere(MeshGeometry geometry, float x, float roughness)
    {
        PbrMaterial material = new(
            new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb),
            0f,
            roughness,
            $"Roughness {roughness:0.00}");
        Mesh sphere = new(geometry, material);
        sphere.Transform.Position = new Vector3(x, 0f, 0f);
        scene.Add(sphere);
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
            label: "Lighting depth"));
        depthExtent = extent;
        return depthTexture;
    }
}

using System.Numerics;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

internal sealed class GltfScenePresenter : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly GltfAsset asset;
    private readonly Scene scene;
    private readonly PerspectiveCamera camera = new();
    private SceneRenderer? renderer;
    private GraphicsTexture? depthTexture;
    private GraphicsExtent3D depthExtent;

    internal GltfScenePresenter(
        GraphicsDevice device,
        GltfAsset asset,
        EquirectangularHdrEnvironment environment,
        Vector3 cameraPosition,
        Vector3 cameraTarget)
    {
        this.device = device ?? throw new ArgumentNullException(nameof(device));
        this.asset = asset ?? throw new ArgumentNullException(nameof(asset));
        scene = asset.Scene;
        SetCameraLookAt(cameraPosition, cameraTarget);

        DirectionalLight light = new(
            new LinearRgba(1f, 0.95f, 0.9f, 1f, StandardColorSpaces.LinearSrgb),
            2f,
            "glTF key light");
        light.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-0.55f, -0.6f, 0f);
        scene.Add(light);
        scene.Add(new ImageBasedLight(environment, 0.7f, "Studio environment"));
    }

    internal void Render(
        GraphicsTexture target,
        uint width,
        uint height,
        int? animationIndex = null,
        float animationTime = 0f)
    {
        if (animationIndex is int index)
        {
            asset.Animations[index].Apply(animationTime, AnimationWrapMode.Loop);
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

    private void SetCameraLookAt(Vector3 position, Vector3 target)
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(position, target, Vector3.UnitY);
        if (!Matrix4x4.Invert(view, out Matrix4x4 world) ||
            !Matrix4x4.Decompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 eye))
        {
            throw new InvalidOperationException("The glTF example camera is not invertible.");
        }
        camera.Transform.Position = eye;
        camera.Transform.Rotation = rotation;
        camera.Transform.Scale = scale;
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
            label: "glTF example depth"));
        depthExtent = extent;
        return depthTexture;
    }
}

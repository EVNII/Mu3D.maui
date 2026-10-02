using System.Numerics;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

internal static class GltfInstancesExample
{
    internal static (GltfSceneAsset Definition, GltfSceneInstance Left, GltfSceneInstance Right) Create(GltfAsset asset)
    {
        GltfSceneAsset reusable = asset.CreateSceneAsset("Shoe product definition");
        GltfSceneInstance left = reusable.CreateInstance("Left shoe");
        GltfSceneInstance right = reusable.CreateInstance("Right shoe");
        left.Root.Transform.Position = new Vector3(-0.72f, 0f, 0f);
        right.Root.Transform.Position = new Vector3(0.72f, 0f, 0f);
        left.Root.Transform.Scale = new Vector3(0.72f);
        right.Root.Transform.Scale = new Vector3(0.72f);
        left.Root.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.18f);
        right.Root.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, -0.18f);
        return (reusable, left, right);
    }
    internal static void AddLighting(Scene scene, EquirectangularHdrEnvironment environment)
    {
        scene.Add(new DirectionalLight(
            new LinearRgba(1f, 0.95f, 0.9f, 1f, StandardColorSpaces.LinearSrgb),
            2f,
            "Product key light")
        {
            Transform =
            {
                Rotation = Quaternion.CreateFromYawPitchRoll(-0.55f, -0.6f, 0f),
            },
        });
        scene.Add(new ImageBasedLight(environment, 0.7f, "Studio environment"));
    }
    internal static PerspectiveCamera CreateCamera()
    {
        PerspectiveCamera result = new(fieldOfViewRadians: MathF.PI / 3f, name: "Product camera");
        Matrix4x4 view = Matrix4x4.CreateLookAt(
            new Vector3(0f, 0.1f, 3.4f),
            Vector3.Zero,
            Vector3.UnitY);
        if (!Matrix4x4.Invert(view, out Matrix4x4 world) ||
            !Matrix4x4.Decompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 position))
        {
            throw new InvalidOperationException("The Gallery product camera is not invertible.");
        }
        result.Transform.Position = position;
        result.Transform.Rotation = rotation;
        result.Transform.Scale = scale;
        return result;
    }
}

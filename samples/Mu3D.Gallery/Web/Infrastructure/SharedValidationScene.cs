using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Web.Infrastructure;

internal static class SharedValidationScene
{
    // This scene uses only shared public Mu3D APIs; no browser or backend types.
    internal static Scene Create()
    {
        Scene scene = new("shared browser first frame");
        scene.Root.AddChild(new Mesh(MeshPrimitives.CreateUvSphere(0.72f),
            new PbrMaterial(Rgb(0.035f, 0.32f, 0.8f), metallic: 0.1f, roughness: 0.35f)) { Transform = { Position = new(-1.55f, 0.3f, 0) } });
        scene.Root.AddChild(new Mesh(MeshPrimitives.CreateCone(0.65f, 1.5f),
            new PbrMaterial(Rgb(0.95f, 0.2f, 0.04f), metallic: 0.05f, roughness: 0.5f)) { Transform = { Position = new(0, 0.3f, 0) } });
        scene.Root.AddChild(new Mesh(MeshPrimitives.CreateUvSphere(0.72f),
            new PbrMaterial(Rgb(0.1f, 0.7f, 0.3f), metallic: 0.3f, roughness: 0.2f)) { Transform = { Position = new(1.55f, 0.3f, 0) } });
        scene.Root.AddChild(new DirectionalLight(Rgb(1, 1, 1), 3) { Transform = { Rotation = Quaternion.CreateFromYawPitchRoll(-0.4f, -0.5f, 0) } });
        scene.Root.AddChild(new DirectionalLight(Rgb(0.35f, 0.5f, 1), 0.6f) { Transform = { Rotation = Quaternion.CreateFromYawPitchRoll(0.9f, 0.2f, 0) } });
        MeshGeometry patch = new([new(-0.38f, -0.22f, 0), new(0.38f, -0.22f, 0), new(0.38f, 0.22f, 0), new(-0.38f, 0.22f, 0)], [0, 1, 2, 0, 2, 3]);
        float[] values = [0.25f, 1, 2, 4];
        for (int i = 0; i < values.Length; i++)
            scene.Root.AddChild(new Mesh(patch, new UnlitMaterial(Rgb(values[i], values[i], values[i]))) { Transform = { Position = new((i - 1.5f) * 1.05f, -1.1f, 0.1f) } });
        return scene;
    }
    private static LinearRgba Rgb(float r, float g, float b) => new(r, g, b, 1, StandardColorSpaces.LinearSrgb);
}

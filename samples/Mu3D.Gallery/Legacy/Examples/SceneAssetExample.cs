using System.Numerics;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Examples;

// One asset, two isolated instances; UI hosts only choose when to invalidate their views.
internal sealed class SceneAssetExample
{
    private readonly Scene leftScene = new("Room A");
    private readonly Scene rightScene = new("Room B");
    private readonly SceneInstance blueLamp;
    private readonly SceneInstance goldLamp;
    private bool blueLampIsRight;
    private float blueRotation;

    internal Scene LeftScene => leftScene;
    internal Scene RightScene => rightScene;
    internal PerspectiveCamera LeftCamera { get; } = CreateCamera("Room A camera");
    internal PerspectiveCamera RightCamera { get; } = CreateCamera("Room B camera");
    internal static LinearRgba ClearColor => CreateClearColor();
    internal SceneAssetExample()
    {
        SceneAsset lampAsset = CreateLampAsset();
        blueLamp = lampAsset.CreateInstance("Blue lamp instance");
        goldLamp = lampAsset.CreateInstance("Gold lamp instance");
        SetInstanceColor(blueLamp, new LinearRgba(
            0.12f,
            0.45f,
            1.4f,
            1f,
            StandardColorSpaces.LinearSrgb));
        SetInstanceColor(goldLamp, new LinearRgba(
            1.5f,
            0.62f,
            0.08f,
            1f,
            StandardColorSpaces.LinearSrgb));

        blueLamp.AttachTo(leftScene);
        goldLamp.AttachTo(rightScene);

    }
    internal void Move()
    {
        blueLampIsRight = !blueLampIsRight;
        blueLamp.Root.Transform.Position = blueLampIsRight
            ? new Vector3(-1.15f, 0f, 0f)
            : Vector3.Zero;
        goldLamp.Root.Transform.Position = blueLampIsRight
            ? new Vector3(1.15f, 0f, 0f)
            : Vector3.Zero;
        blueLamp.AttachTo(blueLampIsRight ? rightScene : leftScene);
    }
    internal void Rotate()
    {
        blueRotation += MathF.PI / 6f;
        blueLamp.Root.Transform.Rotation = Quaternion.CreateFromAxisAngle(
            Vector3.UnitZ,
            blueRotation);
    }
    internal void Reset()
    {
        blueLampIsRight = false;
        blueRotation = 0f;
        ResetTransform(blueLamp.Root);
        ResetTransform(goldLamp.Root);
        blueLamp.AttachTo(leftScene);
        goldLamp.AttachTo(rightScene);
    }
    internal string Status()
    {
        Mesh blueMesh = blueLamp.Root.EnumerateDepthFirst().OfType<Mesh>().First();
        Mesh goldMesh = goldLamp.Root.EnumerateDepthFirst().OfType<Mesh>().First();
        return
            $"Blue lamp: {(blueLampIsRight ? "Room B" : "Room A")}; " +
            $"blue Z rotation: {blueRotation * 180f / MathF.PI:0}°. " +
            $"Geometry shared: {ReferenceEquals(blueMesh.Geometry, goldMesh.Geometry)}; " +
            $"mutable materials isolated: {!ReferenceEquals(blueMesh.Material, goldMesh.Material)}.";
    }

    private static SceneAsset CreateLampAsset()
    {
        Scene source = new("Reusable lamp asset");
        UnlitMaterial material = new(
            new LinearRgba(0.2f, 0.5f, 1.2f, 1f, StandardColorSpaces.LinearSrgb),
            "Lamp finish");
        SceneNode lamp = new("Lamp");
        Mesh shade = new(
            MeshPrimitives.CreateCone(radius: 0.72f, height: 1.15f, radialSegments: 36),
            material,
            "Shade");
        shade.Transform.Position = new Vector3(0f, 0.5f, 0f);
        Mesh baseMesh = new(
            MeshPrimitives.CreateUvSphere(radius: 0.42f, longitudeSegments: 32, latitudeSegments: 16),
            material,
            "Base");
        baseMesh.Transform.Position = new Vector3(0f, -0.72f, 0f);
        baseMesh.Transform.Scale = new Vector3(1.15f, 0.4f, 1.15f);
        lamp.AddChild(shade);
        lamp.AddChild(baseMesh);
        source.Add(lamp);
        return new SceneAsset(source);
    }

    private static PerspectiveCamera CreateCamera(string name)
    {
        PerspectiveCamera camera = new(name: name);
        camera.Transform.Position = new Vector3(0f, 0.15f, 4.5f);
        return camera;
    }

    private static LinearRgba CreateClearColor() => new(
        0.012f,
        0.018f,
        0.035f,
        1f,
        StandardColorSpaces.LinearSrgb);

    private static void SetInstanceColor(SceneInstance instance, LinearRgba color)
    {
        HashSet<Material> visited = new(ReferenceEqualityComparer.Instance);
        foreach (Mesh mesh in instance.Root.EnumerateDepthFirst().OfType<Mesh>())
        {
            if (mesh.Material is UnlitMaterial material && visited.Add(material))
            {
                material.Color = color;
            }
        }
    }

    private static void ResetTransform(SceneNode node)
    {
        node.Transform.Position = Vector3.Zero;
        node.Transform.Rotation = Quaternion.Identity;
        node.Transform.Scale = Vector3.One;
    }
}

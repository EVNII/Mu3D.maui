using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Examples;

// C#-authored native Gallery scene operations shared verbatim with the Web host.
internal static class ToolkitSceneExamples
{
    internal static void AddGizmoVisual(SceneNode target) => target.AddChild(new Mesh(
        MeshPrimitives.CreateCone(radius: .72f, height: 1.8f, radialSegments: 48),
        new UnlitMaterial(new(.18f, .55f, 1.35f, 1, StandardColorSpaces.LinearSrgb), "HDR blue target"), "Centered cone"));

    internal static MeshGeometry AddStatisticsVisual(SceneNode target)
    {
        MeshGeometry geometry = MeshPrimitives.CreateCone(radius: .8f, height: 2, radialSegments: 48);
        target.AddChild(new Mesh(geometry, new UnlitMaterial(new(.12f, .48f, 1.6f, 1, StandardColorSpaces.LinearSrgb),
            "HDR statistics material"), "Statistics cone"));
        return geometry;
    }

    internal static Mesh CreateAnchorSphere(LinearRgba color, string name) => new(
        MeshPrimitives.CreateUvSphere(radius: .42f, longitudeSegments: 32, latitudeSegments: 16),
        new UnlitMaterial(color, $"{name} material"), name);

    internal static void UpdateAnchors(SceneNode cyan, SceneNode magenta, float time)
    {
        cyan.Transform.Position = new(MathF.Sin(time * 1.1f) * 1.8f, MathF.Cos(time * 1.6f) * .65f, MathF.Sin(time * .7f) * .6f);
        magenta.Transform.Position = new(MathF.Cos(time * .85f) * 1.55f, MathF.Sin(time * 1.35f) * .8f, MathF.Cos(time * .65f) * .45f);
    }
}

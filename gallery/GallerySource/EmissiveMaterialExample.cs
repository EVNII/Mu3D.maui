using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Examples;

// The declarative scene stays in the native page; both hosts apply these same FP32 material values.
internal static class EmissiveMaterialExample
{
    internal static void Apply(PbrMaterial material, float strength)
    {
        material.EmissiveColor = new LinearRgba(4f, 1.2f, 0.5f, 1f, StandardColorSpaces.LinearSrgb);
        material.EmissiveStrength = strength;
    }
}

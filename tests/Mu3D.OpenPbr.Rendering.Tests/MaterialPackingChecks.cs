using System.Numerics;
using Mu3D.Color;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

internal static class MaterialPackingChecks
{
    internal static int Run()
    {
        int checks = 0;
        OpenPbrMaterial material = new(new());
        foreach (StandardRgbColorSpaceReference space in new[] { StandardColorSpaces.LinearSrgb,
            StandardColorSpaces.LinearDisplayP3, StandardColorSpaces.AcesCg })
        {
            material.Surface.BaseColor = new(1, 1, 1, 1, space);
            Vector4 packed = OpenPbrGpuMaterial.Pack(material, 1)[0];
            if (packed != Vector4.One) throw new InvalidOperationException("White-point adaptation must preserve valid unit reflectance.");
            checks++;
        }
        material.Surface.EmissionColor = new(2, 1, 1, 1, StandardColorSpaces.AcesCg);
        material.Surface.EmissionLuminance = float.MaxValue;
        material.NitsPerSceneUnit = float.MaxValue;
        Reject(() => OpenPbrGpuMaterial.Pack(material, 1), "Upstream emission multiplication overflow must be rejected even when later division would reduce it.");
        material.Surface.EmissionLuminance = 1;
        material.NitsPerSceneUnit = float.Epsilon;
        Reject(() => OpenPbrGpuMaterial.Pack(material, 1), "Scene emission unit conversion overflow must be rejected.");
        material.NitsPerSceneUnit = 1;
        material.Surface.BaseColor = new(1.001f, 1, 1, 1, StandardColorSpaces.AcesCg);
        Reject(() => OpenPbrGpuMaterial.Pack(material, 1), "Above-unit reflectance must not be silently clipped.");
        return checks;

        void Reject(Action action, string message)
        {
            try { action(); }
            catch (ArgumentOutOfRangeException) { checks++; return; }
            throw new InvalidOperationException(message);
        }
    }
}

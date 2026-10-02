using System.Numerics;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

internal static class OpenPbrMaterialChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Expect(bool condition, string message)
        {
            checks++;
            if (!condition) throw new InvalidOperationException(message);
        }
        T Throws<T>(Action action, string message) where T : Exception
        {
            checks++;
            try { action(); }
            catch (T exception) { return exception; }
            throw new InvalidOperationException(message);
        }

        OpenPbrSurface surface = new()
        {
            Name = "original surface", MetersPerUnit = 0.01f,
            BaseWeight = 0.13f, BaseMetalness = 0.27f, BaseDiffuseRoughness = 0.34f,
            BaseColor = new(-0.1f, 0.45f, 1.3f, 1f, StandardColorSpaces.AcesCg),
            SpecularWeight = 1.7f, SpecularRoughness = 0.23f, SpecularIor = 1.8f,
            SpecularRoughnessAnisotropy = 0.37f,
            SpecularColor = new(0.8f, 0.3f, 0.5f, 1f, StandardColorSpaces.LinearRec2020),
            TransmissionWeight = 0.49f, TransmissionDepth = 2.3f,
            TransmissionColor = new(0.2f, 0.3f, 0.4f, 1f, StandardColorSpaces.AcesCg),
            TransmissionScatter = new(0.7f, 0.8f, 0.9f, 1f, StandardColorSpaces.LinearSrgb),
            TransmissionScatterAnisotropy = -0.3f,
            TransmissionDispersionScale = 0.62f, TransmissionDispersionAbbeNumber = 42f,
            SubsurfaceWeight = 0.31f, SubsurfaceRadius = 0.7f,
            SubsurfaceColor = new(0.1f, 0.8f, 0.4f, 1f, StandardColorSpaces.LinearRec2020),
            SubsurfaceRadiusScale = new(2f, 0.7f, 0.2f), SubsurfaceScatterAnisotropy = -0.4f,
            FuzzWeight = 0.57f, FuzzRoughness = 0.68f,
            FuzzColor = new(0.5f, 0.7f, 0.2f, 1f, StandardColorSpaces.AcesCg),
            CoatWeight = 0.73f, CoatRoughness = 0.14f, CoatRoughnessAnisotropy = 0.41f,
            CoatIor = 1.9f, CoatDarkening = 0.82f,
            CoatColor = new(0.3f, 0.7f, 0.9f, 1f, StandardColorSpaces.AcesCg),
            ThinFilmWeight = 0.51f, ThinFilmThickness = 0.31f, ThinFilmIor = 1.7f,
            EmissionLuminance = 900f,
            EmissionColor = new(4f, 2f, 0.5f, 1f, StandardColorSpaces.LinearRec2020),
            GeometryOpacity = 0.91f, GeometryThinWalled = true,
            GeometryNormal = new(0f, 2f, 1f), GeometryTangent = new(3f, 1f, 0f),
            GeometryCoatNormal = new(0.2f, 0.4f, 2f), GeometryCoatTangent = null,
        };
        surface.Graph = new(new Dictionary<OpenPbrInput, OpenPbrNode> { [OpenPbrInput.SpecularRoughness] = OpenPbrNode.Float(.4f) });
        OpenPbrSurface copied = surface.Clone();
        Expect(!ReferenceEquals(surface, copied), "Cloning creates independent OpenPBR authoring identity");
        // Inspect all public authoring inputs so additions cannot silently escape snapshot coverage.
        var inputs = typeof(OpenPbrSurface).GetProperties().Where(property => property.CanWrite).ToArray();
        Expect(inputs.Length == 44, "Pinned surface contains 41 inputs, two metadata properties and immutable graph connections");
        foreach (var input in inputs)
        {
            Expect(Equals(input.GetValue(surface), input.GetValue(copied)),
                $"Snapshot preserves {input.Name} without unit, color or inherited-vector conversion");
        }
        copied.BaseMetalness = 0.99f;
        copied.GeometryNormal = null;
        copied.Name = "copy";
        Expect(surface.BaseMetalness == 0.27f && surface.GeometryNormal == new Vector3(0f, 2f, 1f) &&
            surface.Name == "original surface", "Changes to snapshot author inputs cannot mutate original");

        Throws<ArgumentNullException>(() => new OpenPbrMaterial(null!), "Null source constructor rejection");
        OpenPbrMaterial material = new(surface)
        {
            NitsPerSceneUnit = 100f, AlphaMode = MaterialAlphaMode.Blend, AlphaCutoff = 0.21f,
            IsDoubleSided = true,
        };
        Expect(material.Name == "original surface" && ReferenceEquals(material.Surface, surface),
            "Material explicitly borrows surface and snapshots default display name");
        Expect(material.BaseModel == MaterialBaseModel.OpenPbr &&
            (int)MaterialBaseModel.Custom == 0 && (int)MaterialBaseModel.Unlit == 1 &&
            (int)MaterialBaseModel.PbrMetallicRoughness == 2 && (int)MaterialBaseModel.OpenPbr == 3,
            "OpenPBR is an additive material root, separate from glTF");
        Throws<ArgumentNullException>(() => material.Surface = null!, "Null replacement rejection");
        Expect(ReferenceEquals(material.Surface, surface), "Failed source replacement retains existing surface");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => material.NitsPerSceneUnit = invalid,
                "Luminance conversion rejects nonpositive or nonfinite input");
        }
        Expect(material.NitsPerSceneUnit == 100f, "Failed luminance edits preserve valid conversion");
        material.NitsPerSceneUnit = null;
        Expect(material.NitsPerSceneUnit is null && surface.EmissionLuminance == 900f,
            "Unset conversion retains emission authoring for an evaluator to validate");
        material.NitsPerSceneUnit = 80f;

        MeshGeometry geometry = MeshPrimitives.CreateCone();
        Scene scene = new("OpenPBR assets");
        scene.Add(new Mesh(geometry, material, "first"));
        scene.Add(new Mesh(geometry, material, "second"));
        SceneAsset asset = new(scene);
        surface.BaseWeight = 0.9f;
        material.NitsPerSceneUnit = 120f;
        material.Name = "edited original";
        SceneInstance firstInstance = asset.CreateInstance();
        SceneInstance secondInstance = asset.CreateInstance();
        Mesh[] firstMeshes = firstInstance.Root.EnumerateDepthFirst().OfType<Mesh>().ToArray();
        Mesh secondMesh = secondInstance.Root.EnumerateDepthFirst().OfType<Mesh>().First();
        OpenPbrMaterial firstMaterial = (OpenPbrMaterial)firstMeshes[0].Material;
        OpenPbrMaterial secondMaterial = (OpenPbrMaterial)secondMesh.Material;
        Expect(ReferenceEquals(firstMeshes[0].Material, firstMeshes[1].Material) &&
            ReferenceEquals(firstMeshes[0].Geometry, geometry),
            "Scene instance preserves shared material aliases and immutable geometry sharing");
        Expect(!ReferenceEquals(firstMaterial, material) && !ReferenceEquals(firstMaterial, secondMaterial) &&
            !ReferenceEquals(firstMaterial.Surface, surface) &&
            !ReferenceEquals(firstMaterial.Surface, secondMaterial.Surface),
            "Asset snapshot and instances isolate all mutable material and authoring objects");
        Expect(firstMaterial.Surface.BaseWeight == 0.13f && firstMaterial.NitsPerSceneUnit == 80f &&
            firstMaterial.Name == "original surface" && firstMaterial.AlphaMode == MaterialAlphaMode.Blend &&
            firstMaterial.AlphaCutoff == 0.21f && firstMaterial.IsDoubleSided,
            "Asset captures original OpenPBR data, scale and common material state");
        firstMaterial.Surface.TransmissionDepth = 8f;
        firstMaterial.Surface.GeometryCoatTangent = Vector3.UnitX;
        firstMaterial.NitsPerSceneUnit = 60f;
        Expect(secondMaterial.Surface.TransmissionDepth == 2.3f &&
            secondMaterial.Surface.GeometryCoatTangent is null && secondMaterial.NitsPerSceneUnit == 80f,
            "Instance authoring edits do not leak across assets or sibling instances");

        MaterialShaderVariant variant = MaterialShaderVariant.FromMaterial(material);
        Expect(variant.BaseModel == MaterialBaseModel.OpenPbr && variant.Extensions == PbrMaterialExtensions.None &&
            variant.TextureBindings == PbrMaterialTextureBindings.None,
            "Manifest retains OpenPBR identity without falsely translating lobe flags");
        MaterialVariantManifest manifest = MaterialVariantManifest.FromScene(scene);
        Expect(manifest.Variants.Count == 1 && manifest.Variants[0] == variant,
            "Shared OpenPBR material resolves to one distinct manifest entry");
        Throws<ArgumentException>(() => new MaterialShaderVariant(MaterialBaseModel.OpenPbr,
            PbrMaterialExtensions.Clearcoat, PbrMaterialTextureBindings.None, MaterialAlphaMode.Opaque, false),
            "OpenPBR does not accept glTF closure flags");
        Throws<ArgumentException>(() => new MaterialShaderVariant(MaterialBaseModel.OpenPbr,
            PbrMaterialExtensions.None, PbrMaterialTextureBindings.BaseColor, MaterialAlphaMode.Opaque, false),
            "OpenPBR does not accept glTF texture flags");

        using RecordingGraphicsDevice device = new();
        using SceneRenderer renderer = new(device, GraphicsTextureFormat.Rgba16Float);
        using RecordingGraphicsTexture target = (RecordingGraphicsTexture)device.CreateTexture(new(
            new GraphicsExtent3D(16, 16), GraphicsTextureFormat.Rgba16Float, GraphicsTextureUsage.RenderAttachment));
        using GraphicsTexture depth = device.CreateTexture(new(
            new GraphicsExtent3D(16, 16), GraphicsTextureFormat.Depth32Float, GraphicsTextureUsage.RenderAttachment));
        NotSupportedException prewarm = Throws<NotSupportedException>(() => renderer.PrewarmMaterialVariants(manifest),
            "Default renderer cannot prewarm OpenPBR as a glTF variant");
        Expect(prewarm.Message.Contains("OpenPBR render pass", StringComparison.Ordinal),
            "Prewarm failure directs callers to the correct rendering boundary");
        PerspectiveCamera camera = new();
        camera.Transform.Position = new(0f, 0f, 4f);
        NotSupportedException render = Throws<NotSupportedException>(() => renderer.Render(scene, camera, target, depth),
            "Default renderer rejects real OpenPBR instead of silently converting authoring values");
        Expect(render.Message.Contains("OpenPBR render pass", StringComparison.Ordinal) &&
            target.IndexedDrawCount == 0, "Unsupported root fails before any mesh draw");
        return checks;
    }
}

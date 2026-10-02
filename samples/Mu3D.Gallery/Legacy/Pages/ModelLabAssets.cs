using System.Numerics;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

internal static class ModelLabAssets
{
    internal static ModelResourceCounts CountResources(GltfAsset asset)
    {
        Mesh[] meshes = asset.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().ToArray();
        LinearRgbaImage[] colorTextures = meshes
            .SelectMany(static mesh => mesh.Material is PbrMaterial material
                ? new[] { material.BaseColorTexture, material.EmissiveTexture }
                : [])
            .OfType<LinearRgbaImage>()
            .Distinct()
            .ToArray();
        NormalizedRgbaDataImage[] dataTextures = meshes
            .SelectMany(static mesh => mesh.Material is PbrMaterial material
                ? new[]
                {
                    material.NormalTexture,
                    material.OcclusionRoughnessMetallicTexture,
                    material.ClearcoatTexture,
                    material.ClearcoatRoughnessTexture,
                    material.ClearcoatNormalTexture,
                    material.AnisotropyTexture,
                }
                : [])
            .OfType<NormalizedRgbaDataImage>()
            .Distinct()
            .ToArray();
        CompressedMaterialTexture[] compressedColorTextures = meshes
            .SelectMany(static mesh => mesh.Material is PbrMaterial material
                ? new[]
                {
                    material.CompressedBaseColorTexture,
                    material.CompressedEmissiveTexture,
                }
                : [])
            .OfType<CompressedMaterialTexture>()
            .Distinct()
            .ToArray();
        CompressedMaterialTexture[] compressedDataTextures = meshes
            .SelectMany(static mesh => mesh.Material is PbrMaterial material
                ? new[]
                {
                    material.CompressedNormalTexture,
                    material.CompressedOcclusionRoughnessMetallicTexture,
                    material.CompressedClearcoatTexture,
                    material.CompressedClearcoatRoughnessTexture,
                    material.CompressedClearcoatNormalTexture,
                    material.CompressedAnisotropyTexture,
                }
                : [])
            .OfType<CompressedMaterialTexture>()
            .Distinct()
            .ToArray();
        ulong compressedBytes = compressedColorTextures.Concat(compressedDataTextures)
            .Aggregate(0ul, static (total, texture) => checked(
                total + texture.MipLevels.Aggregate(
                    0ul,
                    static (mipTotal, mip) => checked(mipTotal + (ulong)mip.Data.Length))));
        return new ModelResourceCounts(
            meshes.Length,
            meshes.Sum(static mesh => mesh.Geometry.Positions.Count),
            meshes.Sum(static mesh => mesh.Geometry.Indices.Count / 3),
            meshes.Sum(static mesh => mesh.Geometry.MorphTargets.Count),
            meshes.Select(static mesh => mesh.Skin).OfType<Skin>().Distinct()
                .Sum(static skin => skin.Joints.Count),
            meshes.Aggregate(0ul, static (total, mesh) => checked(total + EstimateGeometryBytes(mesh))),
            colorTextures.Length + compressedColorTextures.Length,
            dataTextures.Length + compressedDataTextures.Length,
            colorTextures.Concat<object>(dataTextures).Aggregate(0ul, static (total, image) => image switch
            {
                LinearRgbaImage color => checked(total + ((ulong)color.Width * color.Height * 16u)),
                NormalizedRgbaDataImage data => checked(total + ((ulong)data.Width * data.Height * 16u)),
                _ => total,
            }) + compressedBytes,
            colorTextures.Aggregate(0ul, static (total, image) =>
                checked(total + EstimateTextureMipBytes(image.Width, image.Height, 8u))) +
            dataTextures.Aggregate(0ul, static (total, image) =>
                checked(total + EstimateTextureMipBytes(image.Width, image.Height, 4u))) +
            compressedBytes,
            compressedColorTextures.Concat(compressedDataTextures)
                .Select(static texture => texture.Format.ToString())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .DefaultIfEmpty("none")
                .Aggregate(static (left, right) => left + ", " + right));
    }

    internal static void AddAmbientOcclusionProbe(GltfAsset asset, Vector3 target)
    {
        Mesh probe = new(
            MeshPrimitives.CreateUvSphere(0.38f, 32, 16),
            new PbrMaterial(
                new LinearRgba(0.55f, 0.55f, 0.55f, 1f, StandardColorSpaces.LinearSrgb),
                metallic: 0f,
                roughness: 0.85f,
                name: "AO probe material"),
            "AO proximity probe");
        probe.Transform.Position = target + new Vector3(3.15f, -0.2f, 0.55f);
        asset.Scene.Add(probe);
    }

    internal static void AddDiagnosticGround(GltfAsset asset)
    {
        const float halfWidth = 10f;
        const float halfDepth = 5f;
        const float y = -1.05f;
        MeshGeometry geometry = new(
            [
                new Vector3(-halfWidth, y, -halfDepth),
                new Vector3(-halfWidth, y, halfDepth),
                new Vector3(halfWidth, y, -halfDepth),
                new Vector3(halfWidth, y, halfDepth),
            ],
            [0u, 1u, 2u, 2u, 1u, 3u],
            [Vector3.UnitY, Vector3.UnitY, Vector3.UnitY, Vector3.UnitY],
            [Vector2.Zero, Vector2.UnitY, Vector2.UnitX, Vector2.One]);
        Mesh ground = new(
            geometry,
            new PbrMaterial(
                new LinearRgba(0.22f, 0.22f, 0.22f, 1f, StandardColorSpaces.LinearSrgb),
                metallic: 0f,
                roughness: 0.9f,
                name: "Model Lab diagnostic ground material"),
            "Model Lab diagnostic shadow and AO receiver");
        asset.Scene.Add(ground);
    }

    internal static ulong EstimateGeometryBytes(Mesh mesh)
    {
        MeshGeometry geometry = mesh.Geometry;
        ulong bytes = checked(
            ((ulong)geometry.Positions.Count * 12u) +
            ((ulong)geometry.Normals.Count * 12u) +
            ((ulong)geometry.TextureCoordinates.Count * 8u) +
            ((ulong)geometry.TextureCoordinates1.Count * 8u) +
            ((ulong)geometry.BentNormals.Count * 12u) +
            ((ulong)geometry.Tangents.Count * 16u) +
            ((ulong)geometry.Indices.Count * 4u) +
            ((ulong)geometry.JointIndices.Count * 16u) +
            ((ulong)geometry.JointWeights.Count * 16u));
        foreach (MorphTarget target in geometry.MorphTargets)
        {
            bytes = checked(bytes + ((ulong)target.PositionDeltas.Count * 12u) +
                ((ulong)target.NormalDeltas.Count * 12u) +
                ((ulong)target.TangentDeltas.Count * 12u));
        }
        return bytes;
    }

    internal static ulong EstimateTextureMipBytes(uint width, uint height, uint bytesPerPixel)
    {
        ulong bytes = 0;
        while (true)
        {
            bytes = checked(bytes + ((ulong)width * height * bytesPerPixel));
            if (width == 1 && height == 1)
            {
                return bytes;
            }
            width = Math.Max(1u, width / 2);
            height = Math.Max(1u, height / 2);
        }
    }
}

internal readonly record struct ModelResourceCounts(
        int Meshes,
        int Vertices,
        int Triangles,
        int MorphTargets,
        int Joints,
        ulong GeometryBytes,
        int ColorTextures,
        int DataTextures,
        ulong TextureCpuBytes,
        ulong TextureGpuBytes,
        string CompressedFormats);

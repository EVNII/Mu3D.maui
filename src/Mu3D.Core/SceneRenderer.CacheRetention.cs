using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering;

public sealed partial class SceneRenderer
{
    private readonly CacheRetentionScratch cacheRetention = new();
    private bool cacheRetentionInUse;

    private void ThrowIfCacheRetentionInUse()
    {
        if (cacheRetentionInUse)
        {
            throw new InvalidOperationException("Cache trimming cannot be reentered during resource disposal.");
        }
    }

    private void TrimRetainedCaches()
    {
        // Retention ignores camera visibility: hidden and warm scenes must keep their resources.
        // Walk each hierarchy once instead of rebuilding per-resource LINQ pipelines.
        bool retainTextures = materialTextureCache.Count != 0 ||
            compressedMaterialTextureCache.Count != 0 ||
            dataTextureCache.Count != 0 || compressedDataTextureCache.Count != 0;
        foreach (Scene scene in cacheRetention.Scenes)
        {
            cacheRetention.Nodes.Add(scene.Root);
            while (cacheRetention.Nodes.Count != 0)
            {
                int last = cacheRetention.Nodes.Count - 1;
                SceneNode node = cacheRetention.Nodes[last];
                cacheRetention.Nodes.RemoveAt(last);
                if (node is Mesh mesh && cacheRetention.Meshes.Used.Add(mesh))
                {
                    cacheRetention.Geometry.Used.Add(mesh.Geometry);
                    if (retainTextures && mesh.Material is PbrMaterial material &&
                        cacheRetention.Materials.Add(material))
                    {
                        RetainMaterialTextures(PreparePbrMaterial(material));
                    }
                }
                if (node is ImageBasedLight light)
                {
                    cacheRetention.Environments.Used.Add(light.Environment);
                }
                for (int index = node.Children.Count - 1; index >= 0; index--)
                {
                    cacheRetention.Nodes.Add(node.Children[index]);
                }
            }
        }

        cacheRetention.Meshes.Trim(meshCache);
        cacheRetention.Geometry.Trim(geometryCache);
        cacheRetention.Environments.Trim(environmentCache);
        cacheRetention.ColorTextures.Trim(materialTextureCache);
        cacheRetention.CompressedColorTextures.Trim(compressedMaterialTextureCache);
        cacheRetention.DataTextures.Trim(dataTextureCache);
        cacheRetention.CompressedDataTextures.Trim(compressedDataTextureCache);
    }

    private void RetainMaterialTextures(PreparedMaterial material)
    {
        RetainColorTexture(material.BaseColorTexture, material.CompressedBaseColorTexture);
        RetainColorTexture(material.EmissiveTexture, material.CompressedEmissiveTexture);
        RetainColorTexture(material.SheenColorTexture, material.CompressedSheenColorTexture);
        RetainColorTexture(material.SpecularColorTexture, material.CompressedSpecularColorTexture);
        RetainColorTexture(
            material.DiffuseTransmissionColorTexture, material.CompressedDiffuseTransmissionColorTexture);

        RetainDataTexture(material.NormalTexture, material.CompressedNormalTexture, DataTextureSemantic.Normal);
        RetainDataTexture(material.OrmTexture, material.CompressedOrmTexture);
        RetainDataTexture(material.ClearcoatTexture, material.CompressedClearcoatTexture);
        RetainDataTexture(material.ClearcoatRoughnessTexture, material.CompressedClearcoatRoughnessTexture);
        RetainDataTexture(
            material.ClearcoatNormalTexture, material.CompressedClearcoatNormalTexture, DataTextureSemantic.Normal);
        RetainDataTexture(material.AnisotropyTexture, material.CompressedAnisotropyTexture);
        RetainDataTexture(material.TransmissionTexture, material.CompressedTransmissionTexture);
        RetainDataTexture(material.VolumeThicknessTexture, material.CompressedVolumeThicknessTexture);
        RetainDataTexture(material.SheenRoughnessTexture, material.CompressedSheenRoughnessTexture);
        RetainDataTexture(material.IridescenceTexture, material.CompressedIridescenceTexture);
        RetainDataTexture(material.IridescenceThicknessTexture, material.CompressedIridescenceThicknessTexture);
        RetainDataTexture(material.SpecularTexture, material.CompressedSpecularTexture);
        RetainDataTexture(material.DiffuseTransmissionTexture, material.CompressedDiffuseTransmissionTexture);
    }

    private void RetainColorTexture(LinearRgbaImage? image, CompressedMaterialTexture? compressed)
    {
        if (image is not null)
        {
            cacheRetention.ColorTextures.Used.Add(image);
        }
        if (compressed is not null)
        {
            cacheRetention.CompressedColorTextures.Used.Add(compressed);
        }
    }

    private void RetainDataTexture(
        NormalizedRgbaDataImage? image,
        CompressedMaterialTexture? compressed,
        DataTextureSemantic semantic = DataTextureSemantic.LinearChannels)
    {
        if (image is not null)
        {
            cacheRetention.DataTextures.Used.Add(new DataTextureCacheKey(image, semantic));
        }
        if (compressed is not null)
        {
            cacheRetention.CompressedDataTextures.Used.Add(new CompressedDataTextureCacheKey(compressed, semantic));
        }
    }

    private sealed class CacheRetentionScratch
    {
        internal List<Scene> Scenes { get; } = [];
        internal List<SceneNode> Nodes { get; } = [];
        internal HashSet<PbrMaterial> Materials { get; } = new(ReferenceEqualityComparer.Instance);
        internal CacheRetentionSet<Mesh> Meshes { get; } = new();
        internal CacheRetentionSet<MeshGeometry> Geometry { get; } = new();
        internal CacheRetentionSet<EquirectangularHdrEnvironment> Environments { get; } = new();
        internal CacheRetentionSet<LinearRgbaImage> ColorTextures { get; } = new();
        internal CacheRetentionSet<CompressedMaterialTexture> CompressedColorTextures { get; } = new();
        internal CacheRetentionSet<DataTextureCacheKey> DataTextures { get; } = new();
        internal CacheRetentionSet<CompressedDataTextureCacheKey> CompressedDataTextures { get; } = new();

        internal void Clear()
        {
            // Keep capacity, but never keep borrowed scenes, materials or image references.
            Scenes.Clear();
            Nodes.Clear();
            Materials.Clear();
            Meshes.Clear();
            Geometry.Clear();
            Environments.Clear();
            ColorTextures.Clear();
            CompressedColorTextures.Clear();
            DataTextures.Clear();
            CompressedDataTextures.Clear();
        }

        internal void Release()
        {
            Clear();
            Scenes.TrimExcess();
            Nodes.TrimExcess();
            Materials.TrimExcess();
            Meshes.Release();
            Geometry.Release();
            Environments.Release();
            ColorTextures.Release();
            CompressedColorTextures.Release();
            DataTextures.Release();
            CompressedDataTextures.Release();
        }
    }

    private sealed class CacheRetentionSet<TKey> where TKey : notnull
    {
        internal HashSet<TKey> Used { get; } = [];
        private readonly List<TKey> removed = [];

        internal void Trim<TResource>(Dictionary<TKey, TResource> cache) where TResource : IDisposable
        {
            foreach (TKey key in cache.Keys)
            {
                if (!Used.Contains(key))
                {
                    removed.Add(key);
                }
            }
            foreach (TKey key in removed)
            {
                if (cache.Remove(key, out TResource? resource))
                {
                    resource.Dispose();
                }
            }
        }

        internal void Clear()
        {
            Used.Clear();
            removed.Clear();
        }

        internal void Release()
        {
            Clear();
            Used.TrimExcess();
            removed.TrimExcess();
        }
    }
}

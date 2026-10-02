using System.Numerics;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.GalleryApp.Pages;
using Mu3D.Native.UltraHdr;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Web.Pages;

public partial class AssetLabs
{
    private async Task LoadGltfAsync(int version, CancellationTokenSource operation)
    {
        CancellationToken token = operation.Token;
        GltfAsset imported;
        string loadDetails;
        if (feature == "gltf-loading")
        {
            int resolvedCount = 0;
            long resolvedBytes = 0;
            async ValueTask<Stream> Resolve(string uri, CancellationToken resourceToken)
            {
                string path = uri switch
                {
                    "TextureTransformTest.bin" or "Arrow.png" or "Correct.png" or "Error.png" or "NotSupported.png" or "UV.png"
                        => "GltfSamples/TextureTransformTest/" + uri,
                    _ => throw new InvalidDataException($"The Gallery does not allow external glTF URI '{uri}'."),
                };
                byte[] bytes = await GalleryAssets.ReadBytesAsync(path, resourceToken);
                resolvedCount++; resolvedBytes += bytes.Length;
                return new MemoryStream(bytes, writable: false);
            }
            byte[] source = await GalleryAssets.ReadBytesAsync("GltfSamples/TextureTransformTest/TextureTransformTest.gltf", token);
            using MemoryStream stream = new(source, writable: false);
            imported = await GltfAssetLoader.LoadAsync(stream, new GltfAssetLoadOptions
            {
                MaximumSourceByteCount = 64 * 1024,
                MaximumExternalResourceByteCount = 32 * 1024,
                MaximumTotalExternalResourceByteCount = 64 * 1024,
                MaximumRetainedSourceByteCount = 128 * 1024,
                ExternalResourceResolver = Resolve,
                ExternalResourceCache = externalCache,
                SourceRetention = retainSources ? GltfSourceRetentionMode.MainSourceAndExternalResources : GltfSourceRetentionMode.None,
                ImportOptions = new GltfImportOptions { Name = "TextureTransformTest" },
            }, token);
            loadDetails = $"Source: {source.Length:N0} bytes / 64 KiB limit\n" +
                $"Resolver this load: {resolvedCount} resources, {resolvedBytes:N0} bytes\n" +
                $"External cache: {externalCache.Count} entries, {externalCache.TotalByteCount:N0} bytes / 64 KiB capacity\n" +
                (imported.SourceArchive is { } archive
                    ? $"Source archive: {archive.TotalByteCount:N0} bytes; {archive.ExternalResources.Count} external resources"
                    : "Source archive: disabled (temporary encoded buffers released)");
        }
        else
        {
            string path = feature == "gltf-animation" ? "GltfSamples/Fox.glb" : "GltfSamples/MaterialsVariantsShoe.glb";
            byte[] source = await GalleryAssets.ReadBytesAsync(path, token);
            token.ThrowIfCancellationRequested();
            imported = GltfImporter.ImportAsset(source,
                name: feature == "gltf-animation" ? "Fox" : "Materials Variants Shoe",
                imageDecoder: feature == "gltf-animation" ? null : new JpegImageDecoder());
            loadDetails = $"Loaded {source.Length:N0} bytes once\nAuthored variants: {imported.MaterialVariants.Count}";
        }
        if (!Current(version, operation)) return;
        var environment = await GalleryEnvironment.LoadStudioAsync(token);
        if (!Current(version, operation) || surface is null) return;
        DisposePresenters(); asset = imported; animationTime = 0; clipIndex = 0; variantIndex = 0;
        if (feature == "gltf-instances")
        {
            var (_, left, right) = GltfInstancesExample.Create(imported);
            instanceScene = new Scene("Reusable glTF instances");
            GltfInstancesExample.AddLighting(instanceScene, environment);
            left.AttachTo(instanceScene); right.AttachTo(instanceScene);
            leftInstance = left; rightInstance = right;
            instanceCamera = GltfInstancesExample.CreateCamera();
            leftVariant = 1; rightVariant = 2; ApplyVariants();
            Mesh leftMesh = left.Root.EnumerateDepthFirst().OfType<Mesh>().First();
            Mesh rightMesh = right.Root.EnumerateDepthFirst().OfType<Mesh>().First();
            loadDetails += $"\nGeometry shared: {ReferenceEquals(leftMesh.Geometry, rightMesh.Geometry)}; materials isolated: {!ReferenceEquals(leftMesh.Material, rightMesh.Material)}";
        }
        else
        {
            Vector3 position = feature switch
            { "gltf-animation" => new(220, 120, 220), "gltf-loading" => new(0, 0, 4), _ => new(0, 0, 3) };
            Vector3 target = feature == "gltf-animation" ? new(0, 38, -8) : Vector3.Zero;
            gltfPresenter = new GltfScenePresenter(surface.Device, imported, environment, position, target);
        }
        status = $"{Title} ready";
        details = $"Meshes: {imported.Scene.Root.EnumerateDepthFirst().OfType<Mesh>().Count()}\n" +
            $"Animations: {imported.Animations.Count}\nShader variants: {imported.MaterialShaderVariants.Variants.Count}\n" + loadDetails;
    }
}

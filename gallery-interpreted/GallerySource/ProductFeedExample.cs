using System.Numerics;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Native.UltraHdr;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Assets;

namespace Mu3D.GalleryApp.Pages;

internal sealed class ProductAssetProvider(
    Action definitionLoaded,
    Action warmContentLoaded) : IDisposable
{
    private const int MaximumSourceByteCount = 512 * 1024;
    private readonly AsyncAssetCache<string, GltfSceneAsset> definitionCache =
        new(6, StringComparer.Ordinal);
    private readonly AsyncAssetCache<int, ProductSceneContent> contentCache = new(16);

    internal int DefinitionCacheCount => definitionCache.Count;

    internal int WarmContentCount => contentCache.Count;

    public void Dispose() { contentCache.Dispose(); definitionCache.Dispose(); }

    internal ValueTask<ProductSceneContent> GetContentAsync(
        int index,
        string name,
        ProductModel model,
        CancellationToken cancellationToken) => contentCache.GetOrLoadAsync(
            index,
            async (_, loadCancellationToken) =>
            {
                GltfSceneAsset definition = await GetDefinitionAsync(
                    model,
                    loadCancellationToken).ConfigureAwait(false);
                ProductSceneContent content = await Task.Run(
                    () => CreateContent(index, name, model, definition, loadCancellationToken),
                    loadCancellationToken).ConfigureAwait(false);
                warmContentLoaded();
                return content;
            },
            cancellationToken);

    private ValueTask<GltfSceneAsset> GetDefinitionAsync(
        ProductModel model,
        CancellationToken cancellationToken) => definitionCache.GetOrLoadAsync(
            model.Path,
            async (_, loadCancellationToken) =>
            {
                loadCancellationToken.ThrowIfCancellationRequested();
                using Stream stream = new MemoryStream(await GalleryAssets.ReadBytesAsync(
                    model.Path, loadCancellationToken).ConfigureAwait(false), writable: false);
                GltfAsset imported = await GltfAssetLoader.LoadAsync(
                    stream,
                    new GltfAssetLoadOptions
                    {
                        MaximumSourceByteCount = MaximumSourceByteCount,
                        SourceRetention = GltfSourceRetentionMode.None,
                        ImportOptions = new GltfImportOptions
                        {
                            Name = model.DisplayName,
                            ImageDecoder = new JpegImageDecoder(),
                        },
                    },
                    loadCancellationToken).ConfigureAwait(false);
                GltfSceneAsset definition = await Task.Run(
                    () => imported.CreateSceneAsset(model.DisplayName),
                    loadCancellationToken).ConfigureAwait(false);
                definitionLoaded();
                return definition;
            },
            cancellationToken);

    private static ProductSceneContent CreateContent(
        int index,
        string name,
        ProductModel model,
        GltfSceneAsset definition,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GltfSceneInstance instance = definition.CreateInstance(name);
        float initialYaw = ((index % 5) - 2) * 0.08f;
        instance.Root.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, initialYaw);
        if (definition.MaterialVariants.Count > 0)
        {
            instance.ApplyMaterialVariant(index % definition.MaterialVariants.Count);
        }

        Scene scene = new(name);
        instance.AttachTo(scene);
        DirectionalLight key = new(
            new LinearRgba(1f, 0.95f, 0.9f, 1f, StandardColorSpaces.LinearSrgb),
            2.8f,
            "Product key");
        key.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-0.55f, -0.6f, 0f);
        scene.Add(key);
        DirectionalLight fill = new(
            new LinearRgba(0.35f, 0.5f, 1f, 1f, StandardColorSpaces.LinearSrgb),
            1.1f,
            "Product fill");
        fill.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(2.2f, 0.25f, 0f);
        scene.Add(fill);
        cancellationToken.ThrowIfCancellationRequested();
        return new ProductSceneContent(
            scene,
            CreateCamera(model),
            model.AnimatesRotation ? instance.Root : null,
            initialYaw);
    }

    private static PerspectiveCamera CreateCamera(ProductModel model)
    {
        PerspectiveCamera camera = new(fieldOfViewRadians: MathF.PI / 3f, name: "Product camera");
        Matrix4x4 view = Matrix4x4.CreateLookAt(model.CameraPosition, model.CameraTarget, Vector3.UnitY);
        if (!Matrix4x4.Invert(view, out Matrix4x4 world) ||
            !Matrix4x4.Decompose(world, out Vector3 scale, out Quaternion rotation, out Vector3 position))
        {
            throw new InvalidOperationException("A product preview camera is not invertible.");
        }
        camera.Transform.Position = position;
        camera.Transform.Rotation = rotation;
        camera.Transform.Scale = scale;
        return camera;
    }
}

internal sealed record ProductSceneContent(
    Scene Scene,
    Camera Camera,
    SceneNode? AnimatedRoot,
    float InitialYaw)
{
    private const float RotationRadiansPerSecond = 0.7f;

    internal bool AdvanceAnimation(TimeSpan elapsed)
    {
        if (AnimatedRoot is null)
        {
            return false;
        }

        float yaw = InitialYaw + (float)elapsed.TotalSeconds * RotationRadiansPerSecond;
        AnimatedRoot.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
        return true;
    }
}

internal sealed record ProductModel(
    string DisplayName,
    string Path,
    Vector3 CameraPosition,
    Vector3 CameraTarget,
    bool AlternatesHdrTransparencyProbe = false,
    bool AnimatesRotation = false);

internal static class ProductModels
{
    internal static readonly ProductModel[] All =
    [
        new("Interleaved cube", "GltfSamples/BoxInterleaved.glb", new(2.4f, 1.8f, 2.8f), Vector3.Zero),
        new("Morph cube", "GltfSamples/AnimatedMorphCube.glb", new(2.4f, 1.8f, 2.8f), Vector3.Zero),
        new("Fox", "GltfSamples/Fox.glb", new(220f, 120f, 220f), new(0f, 38f, -8f)),
        new("Texture settings", "GltfSamples/TextureSettingsTest.glb", new(0f, 0f, 10f), Vector3.Zero),
        new("Unlit objects", "GltfSamples/UnlitTest.glb", new(0f, 0f, 3.2f), Vector3.Zero),
        new(
            "Emissive row",
            "GltfSamples/EmissiveStrengthTest.glb",
            new(0f, 0f, 18f),
            Vector3.Zero,
            AlternatesHdrTransparencyProbe: true,
            AnimatesRotation: true),
    ];
}

internal static class ProductFeedPolicy
{
    internal const int MaximumDemoProductCount = 60;
    internal const int MaximumConcurrentLoads = 2;
    internal const int GridSpan = 2;
    internal const long ScrollIdleDelayMilliseconds = 200L;
    internal const double PreloadDistanceCentimeters = 3d;
    internal const double SuspendDistanceCentimeters = 6d;
    internal const double ReleaseDistanceCentimeters = 12d;
    internal static readonly TimeSpan SuspendDelay = TimeSpan.FromSeconds(1.5d);
    internal static readonly TimeSpan ReleaseDelay = TimeSpan.FromSeconds(5d);
    internal const double ItemHeight = 260d;
    internal const double VerticalItemSpacing = 8d;
}

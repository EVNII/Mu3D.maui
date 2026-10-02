using System.Collections.ObjectModel;
using Mu3D.Assets;
using Mu3D.SceneGraph;

namespace Mu3D.Formats.Gltf;

/// <summary>
/// Captures one imported glTF definition from which independent mutable scene instances can be
/// created without re-reading or re-decoding the source asset.
/// </summary>
/// <remarks>
/// Immutable geometry and decoded or compressed texture sources are shared. Nodes, transforms,
/// materials, skins, morph weights, animation targets and active material-variant selection are
/// independent for every instance. This managed definition owns no renderer, surface or native GPU
/// resource and can therefore supply instances to different application scenes or scene views.
/// </remarks>
public sealed class GltfSceneAsset
{
    private readonly ReadOnlyCollection<GltfMaterialVariant> materialVariants;
    private readonly ReadOnlyCollection<string> ignoredOptionalExtensions;
    private readonly GltfSceneMaterialBinding[] materialBindings;

    internal GltfSceneAsset(GltfAsset source, string? name)
    {
        ArgumentNullException.ThrowIfNull(source);
        SceneAssetCapture capture = SceneAsset.CaptureWithMappings(
            source.Scene,
            source.Animations,
            cloneOptions: null,
            name,
            source.AllMaterials);
        SceneAsset = capture.Asset;
        materialVariants = Array.AsReadOnly(source.MaterialVariants.ToArray());
        ignoredOptionalExtensions = Array.AsReadOnly(source.IgnoredOptionalExtensions.ToArray());
        MaterialShaderVariants = source.MaterialShaderVariants;
        SourceArchive = source.SourceArchive;
        materialBindings = source.MaterialVariantBindings
            .Select(binding => MapBinding(binding, capture.Nodes, capture.Materials))
            .ToArray();

        // A reusable definition always begins at the authored glTF default, even when the legacy
        // imported scene had a material variant selected before this snapshot was requested.
        foreach (GltfSceneMaterialBinding binding in materialBindings)
        {
            binding.Mesh.Material = binding.DefaultMaterial;
        }
    }

    /// <summary>Gets the reusable backend-independent scene definition.</summary>
    public SceneAsset SceneAsset { get; }

    /// <summary>Gets the optional application-facing definition name.</summary>
    public string? Name => SceneAsset.Name;

    /// <summary>Gets authored KHR_materials_variants entries in source order.</summary>
    public IReadOnlyList<GltfMaterialVariant> MaterialVariants => materialVariants;

    /// <summary>
    /// Gets the de-duplicated shader variants required by the default and authored material
    /// variants. A host may prewarm them before presenting newly created instances.
    /// </summary>
    public MaterialVariantManifest MaterialShaderVariants { get; }

    /// <summary>
    /// Gets optional exact encoded source bytes retained by the asynchronous loader. The archive
    /// belongs to this reusable definition and is not duplicated for each instance.
    /// </summary>
    public GltfSourceArchive? SourceArchive { get; }

    /// <summary>Gets ignored optional glTF extensions whose core fallback remains imported.</summary>
    public IReadOnlyList<string> IgnoredOptionalExtensions => ignoredOptionalExtensions;

    /// <summary>
    /// Creates an independent mutable glTF instance while sharing immutable geometry and texture
    /// sources with this definition and its other instances.
    /// </summary>
    /// <param name="name">Optional name for the new instance root.</param>
    public GltfSceneInstance CreateInstance(string? name = null)
    {
        SceneAssetInstanceClone clone = SceneAsset.CreateInstanceWithMappings(name);
        GltfSceneMaterialBinding[] bindings = materialBindings
            .Select(binding => MapBinding(binding, clone.Nodes, clone.Materials))
            .ToArray();
        return new GltfSceneInstance(this, clone.Instance, bindings);
    }

    private static GltfSceneMaterialBinding MapBinding(
        GltfMaterialVariantBinding binding,
        IReadOnlyDictionary<SceneNode, SceneNode> nodes,
        IReadOnlyDictionary<Material, Material> materials) => new(
            (Mesh)nodes[binding.Mesh],
            materials[binding.DefaultMaterial],
            new ReadOnlyDictionary<int, Material>(binding.Materials.ToDictionary(
                static pair => pair.Key,
                pair => materials[pair.Value])));

    private static GltfSceneMaterialBinding MapBinding(
        GltfSceneMaterialBinding binding,
        IReadOnlyDictionary<SceneNode, SceneNode> nodes,
        IReadOnlyDictionary<Material, Material> materials) => new(
            (Mesh)nodes[binding.Mesh],
            materials[binding.DefaultMaterial],
            new ReadOnlyDictionary<int, Material>(binding.Materials.ToDictionary(
                static pair => pair.Key,
                pair => materials[pair.Value])));
}

/// <summary>
/// Owns one mutable realization of a <see cref="GltfSceneAsset"/>, including independent authored
/// material-variant and animation state.
/// </summary>
public sealed class GltfSceneInstance
{
    private readonly GltfSceneMaterialBinding[] materialBindings;

    internal GltfSceneInstance(
        GltfSceneAsset asset,
        SceneInstance sceneInstance,
        GltfSceneMaterialBinding[] materialBindings)
    {
        Asset = asset;
        SceneInstance = sceneInstance;
        this.materialBindings = materialBindings;
    }

    /// <summary>Gets the reusable glTF definition from which this instance was created.</summary>
    public GltfSceneAsset Asset { get; }

    /// <summary>Gets the underlying general-purpose Mu3D scene instance.</summary>
    public SceneInstance SceneInstance { get; }

    /// <summary>Gets the mutable container root whose transform moves the complete instance.</summary>
    public SceneNode Root => SceneInstance.Root;

    /// <summary>Gets animation clips rebound to this instance's nodes.</summary>
    public IReadOnlyList<AnimationClip> Animations => SceneInstance.Animations;

    /// <summary>Gets the active authored material-variant index, or null for default materials.</summary>
    public int? ActiveMaterialVariantIndex { get; private set; }

    /// <summary>Applies one authored material variant, or restores defaults when index is null.</summary>
    public void ApplyMaterialVariant(int? index)
    {
        if (index.HasValue && (uint)index.Value >= (uint)Asset.MaterialVariants.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        foreach (GltfSceneMaterialBinding binding in materialBindings)
        {
            binding.Mesh.Material = index.HasValue &&
                binding.Materials.TryGetValue(index.Value, out Material? material)
                    ? material
                    : binding.DefaultMaterial;
        }
        ActiveMaterialVariantIndex = index;
    }

    /// <summary>Applies the uniquely named authored material variant.</summary>
    public void ApplyMaterialVariant(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        int index = -1;
        for (int candidate = 0; candidate < Asset.MaterialVariants.Count; candidate++)
        {
            if (!string.Equals(
                Asset.MaterialVariants[candidate].Name,
                name,
                StringComparison.Ordinal))
            {
                continue;
            }
            if (index >= 0)
            {
                throw new InvalidOperationException(
                    $"Material variant name '{name}' is not unique.");
            }
            index = candidate;
        }
        if (index < 0)
        {
            throw new KeyNotFoundException($"Material variant '{name}' was not found.");
        }
        ApplyMaterialVariant(index);
    }

    /// <summary>Attaches or reparents this instance beneath a scene's permanent root.</summary>
    public void AttachTo(Scene scene) => SceneInstance.AttachTo(scene);

    /// <summary>Attaches or reparents this instance beneath an application-owned node.</summary>
    public void AttachTo(SceneNode parent) => SceneInstance.AttachTo(parent);

    /// <summary>Detaches this instance from its current scene hierarchy.</summary>
    public void Detach() => SceneInstance.Detach();
}

internal sealed record GltfSceneMaterialBinding(
    Mesh Mesh,
    Material DefaultMaterial,
    IReadOnlyDictionary<int, Material> Materials);

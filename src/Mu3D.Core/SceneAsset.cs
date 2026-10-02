using System.Collections.ObjectModel;
using Mu3D.SceneGraph;

namespace Mu3D.Assets;

/// <summary>
/// Configures application-defined node and material cloning when a <see cref="SceneAsset"/>
/// contains types outside Mu3D's built-in scene model.
/// </summary>
/// <remarks>
/// Factories must return a new detached object. Mu3D copies common node transform, visibility and
/// hierarchy state after the node factory returns. Factories are invoked when the source snapshot
/// is captured and again for every independent instance.
/// </remarks>
public sealed class SceneAssetCloneOptions
{
    /// <summary>
    /// Gets or initializes a factory for an application-defined node type. The returned node must
    /// be a new detached node with no children.
    /// </summary>
    public Func<SceneNode, SceneNode>? CustomNodeFactory { get; init; }

    /// <summary>
    /// Gets or initializes a factory for an application-defined material type. The returned
    /// material must be a distinct mutable material instance.
    /// </summary>
    public Func<Material, Material>? CustomMaterialFactory { get; init; }
}

/// <summary>
/// Captures a reusable backend-independent scene resource from which independent mutable scene
/// instances can be created.
/// </summary>
/// <remarks>
/// Immutable geometry and decoded or compressed texture sources are shared. Nodes, transforms,
/// materials, skins, morph weights and animation targets are cloned for every instance. The asset
/// never owns a renderer, presentation surface or native GPU resource, so an instance can be
/// reparented between application scenes and scene views.
/// </remarks>
public sealed class SceneAsset
{
    private readonly Scene templateScene;
    private readonly AnimationClip[] templateAnimations;
    private readonly Material[] templateAdditionalMaterials;
    private readonly SceneAssetCloneOptions cloneOptions;

    /// <summary>Captures a private snapshot of a scene and its optional animation clips.</summary>
    /// <param name="scene">The scene to snapshot. Later mutations do not change this asset.</param>
    /// <param name="animations">
    /// Optional clips whose targets must belong to <paramref name="scene"/>.
    /// </param>
    /// <param name="cloneOptions">Optional factories for application-defined scene types.</param>
    /// <param name="name">Optional asset name; the source scene name is used when omitted.</param>
    public SceneAsset(
        Scene scene,
        IEnumerable<AnimationClip>? animations = null,
        SceneAssetCloneOptions? cloneOptions = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        this.cloneOptions = cloneOptions ?? new SceneAssetCloneOptions();
        SceneGraphClone snapshot = SceneGraphCloner.Clone(
            scene,
            animations ?? [],
            this.cloneOptions);
        templateScene = snapshot.Scene;
        templateAnimations = snapshot.Animations;
        templateAdditionalMaterials = [];
        Name = name ?? scene.Name;
    }

    private SceneAsset(
        SceneGraphClone snapshot,
        Material[] additionalMaterials,
        SceneAssetCloneOptions cloneOptions,
        string? name)
    {
        templateScene = snapshot.Scene;
        templateAnimations = snapshot.Animations;
        templateAdditionalMaterials = additionalMaterials;
        this.cloneOptions = cloneOptions;
        Name = name;
    }

    /// <summary>Gets the optional application-facing asset name.</summary>
    public string? Name { get; }

    /// <summary>
    /// Creates independent mutable node, material, skin, morph and animation state while retaining
    /// shared immutable geometry and texture sources.
    /// </summary>
    /// <param name="name">Optional name for the new instance root.</param>
    public SceneInstance CreateInstance(string? name = null)
        => CreateInstanceWithMappings(name).Instance;

    internal static SceneAssetCapture CaptureWithMappings(
        Scene scene,
        IEnumerable<AnimationClip> animations,
        SceneAssetCloneOptions? cloneOptions,
        string? name,
        IEnumerable<Material> additionalMaterials)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(animations);
        ArgumentNullException.ThrowIfNull(additionalMaterials);
        SceneAssetCloneOptions resolvedOptions = cloneOptions ?? new SceneAssetCloneOptions();
        Material[] sourceAdditionalMaterials = additionalMaterials
            .Distinct((IEqualityComparer<Material>)ReferenceEqualityComparer.Instance)
            .ToArray();
        SceneGraphClone snapshot = SceneGraphCloner.Clone(
            scene,
            animations,
            resolvedOptions,
            sourceAdditionalMaterials);
        Material[] templateAdditionalMaterials = sourceAdditionalMaterials
            .Select(material => snapshot.Materials[material])
            .ToArray();
        SceneAsset asset = new(
            snapshot,
            templateAdditionalMaterials,
            resolvedOptions,
            name ?? scene.Name);
        return new SceneAssetCapture(asset, snapshot.Nodes, snapshot.Materials);
    }

    internal SceneAssetInstanceClone CreateInstanceWithMappings(string? name = null)
    {
        SceneGraphClone clone = SceneGraphCloner.Clone(
            templateScene,
            templateAnimations,
            cloneOptions,
            templateAdditionalMaterials);
        SceneNode root = new(name ?? Name ?? "Scene instance");
        foreach (SceneNode child in clone.Scene.Root.Children.ToArray())
        {
            root.AddChild(child);
        }
        SceneInstance instance = new(this, root, clone.Animations);
        return new SceneAssetInstanceClone(instance, clone.Nodes, clone.Materials);
    }
}

/// <summary>
/// Owns one mutable realization of a <see cref="SceneAsset"/> that can be attached to an
/// application scene hierarchy.
/// </summary>
public sealed class SceneInstance
{
    private readonly ReadOnlyCollection<AnimationClip> animations;

    internal SceneInstance(SceneAsset asset, SceneNode root, AnimationClip[] animations)
    {
        Asset = asset;
        Root = root;
        this.animations = Array.AsReadOnly(animations);
    }

    /// <summary>Gets the reusable asset from which this instance was created.</summary>
    public SceneAsset Asset { get; }

    /// <summary>
    /// Gets the mutable container root for this instance. Its transform moves the complete asset.
    /// </summary>
    public SceneNode Root { get; }

    /// <summary>Gets animation clips rebound to this instance's nodes.</summary>
    public IReadOnlyList<AnimationClip> Animations => animations;

    /// <summary>Attaches or reparents the instance beneath a scene's permanent root.</summary>
    public void AttachTo(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        scene.Add(Root);
    }

    /// <summary>Attaches or reparents the instance beneath an application-owned node.</summary>
    public void AttachTo(SceneNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        parent.AddChild(Root);
    }

    /// <summary>Detaches the instance from its current scene hierarchy.</summary>
    public void Detach() => Root.Parent?.RemoveChild(Root);
}

internal sealed record SceneAssetCapture(
    SceneAsset Asset,
    IReadOnlyDictionary<SceneNode, SceneNode> Nodes,
    IReadOnlyDictionary<Material, Material> Materials);

internal sealed record SceneAssetInstanceClone(
    SceneInstance Instance,
    IReadOnlyDictionary<SceneNode, SceneNode> Nodes,
    IReadOnlyDictionary<Material, Material> Materials);

internal sealed record SceneGraphClone(
    Scene Scene,
    AnimationClip[] Animations,
    IReadOnlyDictionary<SceneNode, SceneNode> Nodes,
    IReadOnlyDictionary<Material, Material> Materials);

internal static class SceneGraphCloner
{
    internal static SceneGraphClone Clone(
        Scene source,
        IEnumerable<AnimationClip> animations,
        SceneAssetCloneOptions options,
        IEnumerable<Material>? additionalMaterials = null)
    {
        SceneNode[] sourceNodes = source.Root.Children
            .SelectMany(static node => node.EnumerateDepthFirst())
            .ToArray();
        Dictionary<SceneNode, SceneNode> nodes = new(ReferenceEqualityComparer.Instance);
        Dictionary<Material, Material> materials = new(ReferenceEqualityComparer.Instance);

        foreach (SceneNode sourceNode in sourceNodes)
        {
            SceneNode clone = CreateNode(sourceNode, options, materials);
            CopyCommonNodeState(sourceNode, clone);
            nodes.Add(sourceNode, clone);
        }

        if (additionalMaterials is not null)
        {
            foreach (Material material in additionalMaterials)
            {
                ArgumentNullException.ThrowIfNull(material);
                _ = CloneMaterial(material, options, materials);
            }
        }

        Scene scene = new(source.Name);
        foreach (SceneNode sourceNode in sourceNodes)
        {
            SceneNode clone = nodes[sourceNode];
            if (sourceNode.Parent is null || ReferenceEquals(sourceNode.Parent, source.Root))
            {
                scene.Add(clone);
            }
            else if (nodes.TryGetValue(sourceNode.Parent, out SceneNode? parent))
            {
                parent.AddChild(clone);
            }
            else
            {
                throw new InvalidOperationException(
                    "The scene hierarchy contains a node whose parent is outside the captured scene.");
            }
        }

        foreach (SceneNode sourceNode in sourceNodes)
        {
            if (sourceNode is not Mesh { Skin: not null } sourceMesh)
            {
                continue;
            }
            Mesh cloneMesh = (Mesh)nodes[sourceNode];
            cloneMesh.Skin = new Skin(
                sourceMesh.Skin.Joints.Select(joint => nodes.TryGetValue(joint, out SceneNode? cloneJoint)
                    ? cloneJoint
                    : throw new InvalidOperationException(
                        "A skin joint is outside the captured scene.")),
                sourceMesh.Skin.InverseBindMatrices,
                sourceMesh.Skin.Name);
        }

        AnimationClip[] clonedAnimations = animations
            .Select(clip => CloneAnimation(clip, nodes))
            .ToArray();
        return new SceneGraphClone(scene, clonedAnimations, nodes, materials);
    }

    private static SceneNode CreateNode(
        SceneNode source,
        SceneAssetCloneOptions options,
        Dictionary<Material, Material> materials) => source switch
        {
            Mesh mesh => new Mesh(
                mesh.Geometry,
                CloneMaterial(mesh.Material, options, materials),
                mesh.Name)
            {
                MorphWeights = mesh.MorphWeights,
                ShadowCastingMode = mesh.ShadowCastingMode,
            },
            DirectionalLight light => new DirectionalLight(light.Color, light.Intensity, light.Name)
            {
                CastsShadows = light.CastsShadows,
                AngularDiameterRadians = light.AngularDiameterRadians,
                ShadowOpacity = light.ShadowOpacity,
            },
            PointLight light => new PointLight(light.Color, light.Intensity, light.Name)
            {
                Range = light.Range,
            },
            SpotLight light => new SpotLight(light.Color, light.Intensity, light.Name)
            {
                Range = light.Range,
                OuterConeAngle = light.OuterConeAngle,
                InnerConeAngle = light.InnerConeAngle,
            },
            ImageBasedLight light => new ImageBasedLight(light.Environment, light.Intensity, light.Name),
            PerspectiveCamera camera => new PerspectiveCamera(
                camera.FieldOfViewRadians,
                camera.AspectRatio,
                camera.NearClip,
                camera.FarClip,
                camera.Name),
            _ when source.GetType() == typeof(SceneNode) => new SceneNode(source.Name),
            _ => CreateCustomNode(source, options),
        };

    private static SceneNode CreateCustomNode(SceneNode source, SceneAssetCloneOptions options)
    {
        SceneNode clone = options.CustomNodeFactory?.Invoke(source) ??
            throw new NotSupportedException(
                $"SceneAsset cannot clone node type '{source.GetType().FullName}'. " +
                "Supply SceneAssetCloneOptions.CustomNodeFactory.");
        if (ReferenceEquals(source, clone) || clone.Parent is not null || clone.Children.Count != 0)
        {
            throw new InvalidOperationException(
                "A custom scene-node factory must return a distinct detached node with no children.");
        }
        return clone;
    }

    private static Material CloneMaterial(
        Material source,
        SceneAssetCloneOptions options,
        Dictionary<Material, Material> materials)
    {
        if (materials.TryGetValue(source, out Material? existing))
        {
            return existing;
        }

        Material clone = source switch
        {
            UnlitMaterial unlit when source.GetType() == typeof(UnlitMaterial) =>
                unlit.CloneForSceneInstance(),
            PbrMaterial pbr when source.GetType() == typeof(PbrMaterial) ||
                source.GetType() == typeof(MetallicRoughnessMaterial) =>
                pbr.CloneForSceneInstance(),
            OpenPbrMaterial openPbr => openPbr.CloneForSceneInstance(),
            _ => options.CustomMaterialFactory?.Invoke(source) ??
                throw new NotSupportedException(
                    $"SceneAsset cannot clone material type '{source.GetType().FullName}'. " +
                    "Supply SceneAssetCloneOptions.CustomMaterialFactory."),
        };
        if (ReferenceEquals(source, clone))
        {
            throw new InvalidOperationException(
                "A custom material factory must return a distinct mutable material instance.");
        }
        materials.Add(source, clone);
        return clone;
    }

    private static void CopyCommonNodeState(SceneNode source, SceneNode clone)
    {
        clone.Name = source.Name;
        clone.IsVisible = source.IsVisible;
        clone.VisibilityMask = source.VisibilityMask;
        clone.Transform.Position = source.Transform.Position;
        clone.Transform.Rotation = source.Transform.Rotation;
        clone.Transform.Scale = source.Transform.Scale;
    }

    private static AnimationClip CloneAnimation(
        AnimationClip source,
        IReadOnlyDictionary<SceneNode, SceneNode> nodes) => new(
            source.Tracks.Select(track => CloneTrack(track, nodes)),
            source.Name);

    private static AnimationTrack CloneTrack(
        AnimationTrack source,
        IReadOnlyDictionary<SceneNode, SceneNode> nodes)
    {
        if (!nodes.TryGetValue(source.Target, out SceneNode? target))
        {
            throw new InvalidOperationException(
                "An animation track target is outside the captured scene.");
        }
        return source switch
        {
            Vector3AnimationTrack vector when vector.Interpolation == AnimationInterpolation.CubicSpline =>
                new Vector3AnimationTrack(
                    target,
                    vector.Property,
                    vector.Times,
                    vector.Values,
                    vector.InTangents!,
                    vector.OutTangents!),
            Vector3AnimationTrack vector => new Vector3AnimationTrack(
                target,
                vector.Property,
                vector.Times,
                vector.Values,
                vector.Interpolation),
            QuaternionAnimationTrack rotation when
                rotation.Interpolation == AnimationInterpolation.CubicSpline =>
                new QuaternionAnimationTrack(
                    target,
                    rotation.Times,
                    rotation.Values,
                    rotation.InTangents!,
                    rotation.OutTangents!),
            QuaternionAnimationTrack rotation => new QuaternionAnimationTrack(
                target,
                rotation.Times,
                rotation.Values,
                rotation.Interpolation),
            MorphWeightAnimationTrack morph when
                morph.Interpolation == AnimationInterpolation.CubicSpline =>
                new MorphWeightAnimationTrack(
                    (Mesh)target,
                    morph.Times,
                    morph.Values,
                    morph.InTangents!,
                    morph.OutTangents!),
            MorphWeightAnimationTrack morph => new MorphWeightAnimationTrack(
                (Mesh)target,
                morph.Times,
                morph.Values,
                morph.Interpolation),
            _ => throw new NotSupportedException(
                $"SceneAsset cannot clone animation track type '{source.GetType().FullName}'."),
        };
    }
}

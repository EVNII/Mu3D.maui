using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Identifies the exact material texture bindings used by one shader variant.</summary>
[Flags]
public enum PbrMaterialTextureBindings
{
    /// <summary>Uses no sampled material texture.</summary>
    None = 0,
    /// <summary>Uses the base-color texture.</summary>
    BaseColor = 1 << 0,
    /// <summary>Uses the base tangent-space normal texture.</summary>
    Normal = 1 << 1,
    /// <summary>Uses the packed occlusion/roughness/metallic texture.</summary>
    OcclusionRoughnessMetallic = 1 << 2,
    /// <summary>Uses the emissive color texture.</summary>
    Emissive = 1 << 3,
    /// <summary>Uses the clearcoat-factor texture.</summary>
    ClearcoatFactor = 1 << 4,
    /// <summary>Uses the clearcoat-roughness texture.</summary>
    ClearcoatRoughness = 1 << 5,
    /// <summary>Uses the independent clearcoat normal texture.</summary>
    ClearcoatNormal = 1 << 6,
    /// <summary>Uses the anisotropy direction and strength texture.</summary>
    Anisotropy = 1 << 7,
    /// <summary>Uses the transmission-factor texture.</summary>
    Transmission = 1 << 8,
    /// <summary>Uses the volume-thickness texture.</summary>
    VolumeThickness = 1 << 9,
    /// <summary>Uses the sheen-color texture.</summary>
    SheenColor = 1 << 10,
    /// <summary>Uses the sheen-roughness texture.</summary>
    SheenRoughness = 1 << 11,
    /// <summary>Uses the iridescence-factor texture.</summary>
    IridescenceFactor = 1 << 12,
    /// <summary>Uses the iridescence thin-film-thickness texture.</summary>
    IridescenceThickness = 1 << 13,
    /// <summary>Uses the dielectric specular-strength texture alpha channel.</summary>
    SpecularFactor = 1 << 14,
    /// <summary>Uses the sRGB dielectric specular-color texture.</summary>
    SpecularColor = 1 << 15,
    /// <summary>Uses the diffuse-transmission factor texture alpha channel.</summary>
    DiffuseTransmissionFactor = 1 << 16,
    /// <summary>Uses the sRGB diffuse-transmission color texture.</summary>
    DiffuseTransmissionColor = 1 << 17,
    /// <summary>Uses every currently defined material texture binding.</summary>
    All = BaseColor | Normal | OcclusionRoughnessMetallic | Emissive |
        ClearcoatFactor | ClearcoatRoughness | ClearcoatNormal | Anisotropy |
        Transmission | VolumeThickness | SheenColor | SheenRoughness |
        IridescenceFactor | IridescenceThickness | SpecularFactor | SpecularColor |
        DiffuseTransmissionFactor | DiffuseTransmissionColor,
}

/// <summary>
/// Describes one exact backend-independent material shader and pipeline specialization. Scalar
/// values, texture-coordinate transforms and sampler state are deliberately not variant axes.
/// </summary>
public readonly record struct MaterialShaderVariant
{
    /// <summary>Initializes an exact material shader variant.</summary>
    public MaterialShaderVariant(
        MaterialBaseModel baseModel,
        PbrMaterialExtensions extensions,
        PbrMaterialTextureBindings textureBindings,
        MaterialAlphaMode alphaMode,
        bool isDoubleSided)
    {
        if (!Enum.IsDefined(baseModel))
        {
            throw new ArgumentOutOfRangeException(nameof(baseModel));
        }
        if ((extensions & ~PbrMaterialExtensions.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(extensions));
        }
        if ((textureBindings & ~PbrMaterialTextureBindings.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureBindings));
        }
        if (!Enum.IsDefined(alphaMode))
        {
            throw new ArgumentOutOfRangeException(nameof(alphaMode));
        }
        if (baseModel == MaterialBaseModel.OpenPbr &&
            (extensions != PbrMaterialExtensions.None || textureBindings != PbrMaterialTextureBindings.None))
        {
            throw new ArgumentException(
                "OpenPBR variants retain their own root and cannot use glTF PBR extension or texture flags.",
                nameof(baseModel));
        }
        if ((extensions & PbrMaterialExtensions.Volume) != 0 &&
            (extensions & PbrMaterialExtensions.Transmission) == 0)
        {
            throw new ArgumentException("Volume requires transmission.", nameof(extensions));
        }
        if ((extensions & PbrMaterialExtensions.Dispersion) != 0 &&
            (extensions & PbrMaterialExtensions.Volume) == 0)
        {
            throw new ArgumentException("Dispersion requires finite-volume transmission.", nameof(extensions));
        }
        BaseModel = baseModel;
        Extensions = extensions;
        TextureBindings = textureBindings;
        AlphaMode = alphaMode;
        IsDoubleSided = isDoubleSided;
    }

    /// <summary>Gets the root material closure.</summary>
    public MaterialBaseModel BaseModel { get; }
    /// <summary>Gets the exact optional closure blocks used by this variant.</summary>
    public PbrMaterialExtensions Extensions { get; }
    /// <summary>Gets the exact sampled material textures used by this variant.</summary>
    public PbrMaterialTextureBindings TextureBindings { get; }
    /// <summary>Gets the alpha pipeline mode.</summary>
    public MaterialAlphaMode AlphaMode { get; }
    /// <summary>Gets whether this variant renders both face orientations.</summary>
    public bool IsDoubleSided { get; }
    /// <summary>Gets the number of sampled material textures, excluding shared scene textures.</summary>
    public int SampledMaterialTextureCount => BitOperations.PopCount((uint)TextureBindings);

    /// <summary>
    /// Resolves a material's current values and shader template to its exact specialization key.
    /// Rebuild the manifest after changing factors, texture sources, alpha mode or sidedness.
    /// OpenPBR retains its distinct root without glTF extension flags; its render pass owns closure
    /// specialization. A manifest containing that root cannot be prewarmed by the default renderer.
    /// </summary>
    public static MaterialShaderVariant FromMaterial(Material material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material is not PbrMaterial pbr)
        {
            return new(material.BaseModel, PbrMaterialExtensions.None,
                PbrMaterialTextureBindings.None, material.AlphaMode, material.IsDoubleSided);
        }

        MaterialShaderTemplate template = pbr.ShaderTemplate;
        PbrMaterialExtensions extensions = PbrMaterialExtensions.None;
        PbrMaterialTextureBindings textures = PbrMaterialTextureBindings.None;
        bool emissive = template.Includes(PbrMaterialExtensions.Emissive) &&
            pbr.EmissiveStrength > 0f && MaxRgb(pbr.EmissiveColor) > 0f;
        bool clearcoat = template.Includes(PbrMaterialExtensions.Clearcoat) && pbr.ClearcoatFactor > 0f;
        bool anisotropy = template.Includes(PbrMaterialExtensions.Anisotropy) && pbr.AnisotropyStrength > 0f;
        bool transmission = template.Includes(PbrMaterialExtensions.Transmission) && pbr.TransmissionFactor > 0f;
        bool volume = transmission && template.Includes(PbrMaterialExtensions.Volume) &&
            pbr.VolumeThicknessFactor > 0f;
        bool dispersion = volume && template.Includes(PbrMaterialExtensions.Dispersion) &&
            pbr.Dispersion > 0f;
        bool sheen = template.Includes(PbrMaterialExtensions.Sheen) && MaxRgb(pbr.SheenColor) > 0f;
        bool iridescence = template.Includes(PbrMaterialExtensions.Iridescence) &&
            pbr.IridescenceFactor > 0f;
        bool specular = template.Includes(PbrMaterialExtensions.Specular) &&
            (pbr.SpecularFactor != 1f || !IsWhite(pbr.SpecularColor) ||
             pbr.SpecularTexture is not null || pbr.CompressedSpecularTexture is not null ||
             pbr.SpecularColorTexture is not null || pbr.CompressedSpecularColorTexture is not null);
        bool diffuseTransmission = template.Includes(PbrMaterialExtensions.DiffuseTransmission) &&
            pbr.DiffuseTransmissionFactor > 0f;

        AddTexture(ref textures, PbrMaterialTextureBindings.BaseColor,
            template.Includes(PbrMaterialTextureSlots.BaseColor),
            pbr.BaseColorTexture, pbr.CompressedBaseColorTexture);
        AddTexture(ref textures, PbrMaterialTextureBindings.Normal,
            template.Includes(PbrMaterialTextureSlots.Normal),
            pbr.NormalTexture, pbr.CompressedNormalTexture);
        AddTexture(ref textures, PbrMaterialTextureBindings.OcclusionRoughnessMetallic,
            template.Includes(PbrMaterialTextureSlots.OcclusionRoughnessMetallic),
            pbr.OcclusionRoughnessMetallicTexture, pbr.CompressedOcclusionRoughnessMetallicTexture);
        if (emissive)
        {
            extensions |= PbrMaterialExtensions.Emissive;
            AddTexture(ref textures, PbrMaterialTextureBindings.Emissive,
                template.Includes(PbrMaterialTextureSlots.Emissive),
                pbr.EmissiveTexture, pbr.CompressedEmissiveTexture);
        }
        if (clearcoat)
        {
            extensions |= PbrMaterialExtensions.Clearcoat;
            bool slots = template.Includes(PbrMaterialTextureSlots.Clearcoat);
            AddTexture(ref textures, PbrMaterialTextureBindings.ClearcoatFactor, slots,
                pbr.ClearcoatTexture, pbr.CompressedClearcoatTexture);
            AddTexture(ref textures, PbrMaterialTextureBindings.ClearcoatRoughness, slots,
                pbr.ClearcoatRoughnessTexture, pbr.CompressedClearcoatRoughnessTexture);
            AddTexture(ref textures, PbrMaterialTextureBindings.ClearcoatNormal, slots,
                pbr.ClearcoatNormalTexture, pbr.CompressedClearcoatNormalTexture);
        }
        if (anisotropy)
        {
            extensions |= PbrMaterialExtensions.Anisotropy;
            AddTexture(ref textures, PbrMaterialTextureBindings.Anisotropy,
                template.Includes(PbrMaterialTextureSlots.Anisotropy),
                pbr.AnisotropyTexture, pbr.CompressedAnisotropyTexture);
        }
        if (transmission)
        {
            extensions |= PbrMaterialExtensions.Transmission;
            bool slots = template.Includes(PbrMaterialTextureSlots.TransmissionVolume);
            AddTexture(ref textures, PbrMaterialTextureBindings.Transmission, slots,
                pbr.TransmissionTexture, pbr.CompressedTransmissionTexture);
            if (volume)
            {
                extensions |= PbrMaterialExtensions.Volume;
                if (dispersion)
                {
                    extensions |= PbrMaterialExtensions.Dispersion;
                }
                AddTexture(ref textures, PbrMaterialTextureBindings.VolumeThickness, slots,
                    pbr.VolumeThicknessTexture, pbr.CompressedVolumeThicknessTexture);
            }
        }
        if (sheen)
        {
            extensions |= PbrMaterialExtensions.Sheen;
            bool slots = template.Includes(PbrMaterialTextureSlots.Sheen);
            AddTexture(ref textures, PbrMaterialTextureBindings.SheenColor, slots,
                pbr.SheenColorTexture, pbr.CompressedSheenColorTexture);
            AddTexture(ref textures, PbrMaterialTextureBindings.SheenRoughness, slots,
                pbr.SheenRoughnessTexture, pbr.CompressedSheenRoughnessTexture);
        }
        if (iridescence)
        {
            extensions |= PbrMaterialExtensions.Iridescence;
            bool slots = template.Includes(PbrMaterialTextureSlots.Iridescence);
            AddTexture(ref textures, PbrMaterialTextureBindings.IridescenceFactor, slots,
                pbr.IridescenceTexture, pbr.CompressedIridescenceTexture);
            AddTexture(ref textures, PbrMaterialTextureBindings.IridescenceThickness, slots,
                pbr.IridescenceThicknessTexture, pbr.CompressedIridescenceThicknessTexture);
        }
        if (specular)
        {
            extensions |= PbrMaterialExtensions.Specular;
            bool slots = template.Includes(PbrMaterialTextureSlots.Specular);
            AddTexture(ref textures, PbrMaterialTextureBindings.SpecularFactor, slots,
                pbr.SpecularTexture, pbr.CompressedSpecularTexture);
            AddTexture(ref textures, PbrMaterialTextureBindings.SpecularColor, slots,
                pbr.SpecularColorTexture, pbr.CompressedSpecularColorTexture);
        }
        if (diffuseTransmission)
        {
            extensions |= PbrMaterialExtensions.DiffuseTransmission;
            bool slots = template.Includes(PbrMaterialTextureSlots.DiffuseTransmission);
            AddTexture(ref textures, PbrMaterialTextureBindings.DiffuseTransmissionFactor, slots,
                pbr.DiffuseTransmissionTexture, pbr.CompressedDiffuseTransmissionTexture);
            AddTexture(ref textures, PbrMaterialTextureBindings.DiffuseTransmissionColor, slots,
                pbr.DiffuseTransmissionColorTexture, pbr.CompressedDiffuseTransmissionColorTexture);
        }
        return new(pbr.BaseModel, extensions, textures, pbr.AlphaMode, pbr.IsDoubleSided);
    }

    private static float MaxRgb(Mu3D.Color.LinearRgba value) =>
        MathF.Max(value.Red, MathF.Max(value.Green, value.Blue));

    private static bool IsWhite(Mu3D.Color.LinearRgba value) =>
        value.Red == 1f && value.Green == 1f && value.Blue == 1f;

    private static void AddTexture(
        ref PbrMaterialTextureBindings bindings,
        PbrMaterialTextureBindings binding,
        bool allowed,
        object? decoded,
        object? compressed)
    {
        if (allowed && (decoded is not null || compressed is not null))
        {
            bindings |= binding;
        }
    }
}

/// <summary>
/// Contains the de-duplicated material variants actually referenced by an asset or scene. It is a
/// compile manifest, not the Cartesian product of every feature permitted by a template.
/// </summary>
public sealed class MaterialVariantManifest
{
    private MaterialVariantManifest(IReadOnlyList<MaterialShaderVariant> variants) => Variants = variants;

    /// <summary>Gets exact variants in stable first-use order.</summary>
    public IReadOnlyList<MaterialShaderVariant> Variants { get; }

    /// <summary>Gets the largest per-material sampled-texture count in the manifest.</summary>
    public int MaximumSampledMaterialTextureCount => Variants.Count == 0
        ? 0
        : Variants.Max(static variant => variant.SampledMaterialTextureCount);

    /// <summary>Builds a de-duplicated manifest from every mesh in a scene, including hidden nodes.</summary>
    public static MaterialVariantManifest FromScene(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return FromMaterials(scene.Root.EnumerateDepthFirst().OfType<Mesh>().Select(static mesh => mesh.Material));
    }

    /// <summary>Builds a de-duplicated manifest from the supplied material instances.</summary>
    public static MaterialVariantManifest FromMaterials(IEnumerable<Material> materials)
    {
        ArgumentNullException.ThrowIfNull(materials);
        HashSet<MaterialShaderVariant> seen = [];
        List<MaterialShaderVariant> variants = [];
        foreach (Material material in materials)
        {
            MaterialShaderVariant variant = MaterialShaderVariant.FromMaterial(material);
            if (seen.Add(variant))
            {
                variants.Add(variant);
            }
        }
        return new(new ReadOnlyCollection<MaterialShaderVariant>(variants));
    }
}

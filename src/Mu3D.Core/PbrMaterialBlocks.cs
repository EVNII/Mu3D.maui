using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>Provides the metallic/roughness base closure of a <see cref="PbrMaterial"/>.</summary>
public sealed class PbrBaseBlock
{
    private readonly PbrMaterial material;

    internal PbrBaseBlock(PbrMaterial material) => this.material = material;

    /// <summary>Gets or sets the base color factor.</summary>
    public LinearRgba Color { get => material.BaseColor; set => material.BaseColor = value; }

    /// <summary>Gets or sets the metallic factor.</summary>
    public float Metallic { get => material.Metallic; set => material.Metallic = value; }

    /// <summary>Gets or sets perceptual roughness.</summary>
    public float Roughness { get => material.Roughness; set => material.Roughness = value; }

    /// <summary>Gets or sets the decoded base-color texture.</summary>
    public LinearRgbaImage? ColorTexture
    {
        get => material.BaseColorTexture;
        set => material.BaseColorTexture = value;
    }

    /// <summary>Gets or sets the compressed base-color texture.</summary>
    public CompressedMaterialTexture? CompressedColorTexture
    {
        get => material.CompressedBaseColorTexture;
        set => material.CompressedBaseColorTexture = value;
    }

    /// <summary>Gets or sets the decoded tangent-space normal texture.</summary>
    public NormalizedRgbaDataImage? NormalTexture
    {
        get => material.NormalTexture;
        set => material.NormalTexture = value;
    }

    /// <summary>Gets or sets the compressed tangent-space normal texture.</summary>
    public CompressedMaterialTexture? CompressedNormalTexture
    {
        get => material.CompressedNormalTexture;
        set => material.CompressedNormalTexture = value;
    }

    /// <summary>Gets or sets the decoded packed occlusion/roughness/metallic texture.</summary>
    public NormalizedRgbaDataImage? OcclusionRoughnessMetallicTexture
    {
        get => material.OcclusionRoughnessMetallicTexture;
        set => material.OcclusionRoughnessMetallicTexture = value;
    }

    /// <summary>Gets or sets the compressed packed occlusion/roughness/metallic texture.</summary>
    public CompressedMaterialTexture? CompressedOcclusionRoughnessMetallicTexture
    {
        get => material.CompressedOcclusionRoughnessMetallicTexture;
        set => material.CompressedOcclusionRoughnessMetallicTexture = value;
    }

    /// <summary>Gets or sets base-color sampling and UV mapping.</summary>
    public MaterialTextureMapping ColorTextureMapping
    {
        get => material.BaseColorTextureMapping;
        set => material.BaseColorTextureMapping = value;
    }

    /// <summary>Gets or sets normal-map sampling and UV mapping.</summary>
    public MaterialTextureMapping NormalTextureMapping
    {
        get => material.NormalTextureMapping;
        set => material.NormalTextureMapping = value;
    }

    /// <summary>Gets or sets ORM sampling and UV mapping.</summary>
    public MaterialTextureMapping OcclusionRoughnessMetallicTextureMapping
    {
        get => material.OcclusionRoughnessMetallicTextureMapping;
        set => material.OcclusionRoughnessMetallicTextureMapping = value;
    }
}

/// <summary>Provides the emissive extension block of a <see cref="PbrMaterial"/>.</summary>
public sealed class PbrEmissiveBlock
{
    private readonly PbrMaterial material;

    internal PbrEmissiveBlock(PbrMaterial material) => this.material = material;

    /// <summary>Gets or sets explicitly tagged scene-linear emission.</summary>
    public LinearRgba Color { get => material.EmissiveColor; set => material.EmissiveColor = value; }

    /// <summary>Gets or sets the unclamped non-negative strength.</summary>
    public float Strength { get => material.EmissiveStrength; set => material.EmissiveStrength = value; }

    /// <summary>Gets or sets the decoded emissive color texture.</summary>
    public LinearRgbaImage? Texture { get => material.EmissiveTexture; set => material.EmissiveTexture = value; }

    /// <summary>Gets or sets the compressed emissive color texture.</summary>
    public CompressedMaterialTexture? CompressedTexture
    {
        get => material.CompressedEmissiveTexture;
        set => material.CompressedEmissiveTexture = value;
    }

    /// <summary>Gets or sets emissive sampling and UV mapping.</summary>
    public MaterialTextureMapping TextureMapping
    {
        get => material.EmissiveTextureMapping;
        set => material.EmissiveTextureMapping = value;
    }
}

/// <summary>Provides the outer clearcoat extension block of a <see cref="PbrMaterial"/>.</summary>
public sealed class PbrClearcoatBlock
{
    private readonly PbrMaterial material;

    internal PbrClearcoatBlock(PbrMaterial material) => this.material = material;

    /// <summary>Gets or sets coat coverage.</summary>
    public float Factor { get => material.ClearcoatFactor; set => material.ClearcoatFactor = value; }

    /// <summary>Gets or sets coat perceptual roughness.</summary>
    public float Roughness { get => material.ClearcoatRoughness; set => material.ClearcoatRoughness = value; }

    /// <summary>Gets or sets the independent coat normal scale.</summary>
    public float NormalScale { get => material.ClearcoatNormalScale; set => material.ClearcoatNormalScale = value; }

    /// <summary>Gets or sets the decoded coat-factor texture.</summary>
    public NormalizedRgbaDataImage? FactorTexture
    {
        get => material.ClearcoatTexture;
        set => material.ClearcoatTexture = value;
    }

    /// <summary>Gets or sets the compressed coat-factor texture.</summary>
    public CompressedMaterialTexture? CompressedFactorTexture
    {
        get => material.CompressedClearcoatTexture;
        set => material.CompressedClearcoatTexture = value;
    }

    /// <summary>Gets or sets the decoded coat-roughness texture.</summary>
    public NormalizedRgbaDataImage? RoughnessTexture
    {
        get => material.ClearcoatRoughnessTexture;
        set => material.ClearcoatRoughnessTexture = value;
    }

    /// <summary>Gets or sets the compressed coat-roughness texture.</summary>
    public CompressedMaterialTexture? CompressedRoughnessTexture
    {
        get => material.CompressedClearcoatRoughnessTexture;
        set => material.CompressedClearcoatRoughnessTexture = value;
    }

    /// <summary>Gets or sets the decoded independent coat-normal texture.</summary>
    public NormalizedRgbaDataImage? NormalTexture
    {
        get => material.ClearcoatNormalTexture;
        set => material.ClearcoatNormalTexture = value;
    }

    /// <summary>Gets or sets the compressed independent coat-normal texture.</summary>
    public CompressedMaterialTexture? CompressedNormalTexture
    {
        get => material.CompressedClearcoatNormalTexture;
        set => material.CompressedClearcoatNormalTexture = value;
    }

    /// <summary>Gets or sets coat-factor sampling and UV mapping.</summary>
    public MaterialTextureMapping FactorTextureMapping
    {
        get => material.ClearcoatTextureMapping;
        set => material.ClearcoatTextureMapping = value;
    }

    /// <summary>Gets or sets coat-roughness sampling and UV mapping.</summary>
    public MaterialTextureMapping RoughnessTextureMapping
    {
        get => material.ClearcoatRoughnessTextureMapping;
        set => material.ClearcoatRoughnessTextureMapping = value;
    }

    /// <summary>Gets or sets coat-normal sampling and UV mapping.</summary>
    public MaterialTextureMapping NormalTextureMapping
    {
        get => material.ClearcoatNormalTextureMapping;
        set => material.ClearcoatNormalTextureMapping = value;
    }
}

/// <summary>Provides the anisotropic base-specular extension block of a <see cref="PbrMaterial"/>.</summary>
public sealed class PbrAnisotropyBlock
{
    private readonly PbrMaterial material;

    internal PbrAnisotropyBlock(PbrMaterial material) => this.material = material;

    /// <summary>Gets or sets anisotropy strength.</summary>
    public float Strength { get => material.AnisotropyStrength; set => material.AnisotropyStrength = value; }

    /// <summary>Gets or sets tangent-space rotation in radians.</summary>
    public float Rotation { get => material.AnisotropyRotation; set => material.AnisotropyRotation = value; }

    /// <summary>Gets or sets the decoded direction/strength texture.</summary>
    public NormalizedRgbaDataImage? Texture
    {
        get => material.AnisotropyTexture;
        set => material.AnisotropyTexture = value;
    }

    /// <summary>Gets or sets the compressed direction/strength texture.</summary>
    public CompressedMaterialTexture? CompressedTexture
    {
        get => material.CompressedAnisotropyTexture;
        set => material.CompressedAnisotropyTexture = value;
    }

    /// <summary>Gets or sets anisotropy sampling and UV mapping.</summary>
    public MaterialTextureMapping TextureMapping
    {
        get => material.AnisotropyTextureMapping;
        set => material.AnisotropyTextureMapping = value;
    }
}

/// <summary>Provides transmission and its optional finite-volume block.</summary>
public sealed class PbrTransmissionBlock
{
    private readonly PbrMaterial material;

    internal PbrTransmissionBlock(PbrMaterial material) => this.material = material;

    /// <summary>Gets or sets transmitted coverage.</summary>
    public float Factor { get => material.TransmissionFactor; set => material.TransmissionFactor = value; }

    /// <summary>Gets or sets interface index of refraction.</summary>
    public float IndexOfRefraction
    {
        get => material.IndexOfRefraction;
        set => material.IndexOfRefraction = value;
    }

    /// <summary>Gets or sets non-negative wavelength dispersion for finite-volume transmission.</summary>
    public float Dispersion
    {
        get => material.Dispersion;
        set => material.Dispersion = value;
    }

    /// <summary>Gets or sets finite-volume thickness.</summary>
    public float Thickness { get => material.VolumeThicknessFactor; set => material.VolumeThicknessFactor = value; }

    /// <summary>Gets or sets the Beer-Lambert attenuation distance.</summary>
    public float AttenuationDistance
    {
        get => material.VolumeAttenuationDistance;
        set => material.VolumeAttenuationDistance = value;
    }

    /// <summary>Gets or sets explicitly tagged linear attenuation color.</summary>
    public LinearRgba AttenuationColor
    {
        get => material.VolumeAttenuationColor;
        set => material.VolumeAttenuationColor = value;
    }

    /// <summary>Gets or sets the decoded transmission-factor texture.</summary>
    public NormalizedRgbaDataImage? FactorTexture
    {
        get => material.TransmissionTexture;
        set => material.TransmissionTexture = value;
    }

    /// <summary>Gets or sets the compressed transmission-factor texture.</summary>
    public CompressedMaterialTexture? CompressedFactorTexture
    {
        get => material.CompressedTransmissionTexture;
        set => material.CompressedTransmissionTexture = value;
    }

    /// <summary>Gets or sets the decoded volume-thickness texture.</summary>
    public NormalizedRgbaDataImage? ThicknessTexture
    {
        get => material.VolumeThicknessTexture;
        set => material.VolumeThicknessTexture = value;
    }

    /// <summary>Gets or sets the compressed volume-thickness texture.</summary>
    public CompressedMaterialTexture? CompressedThicknessTexture
    {
        get => material.CompressedVolumeThicknessTexture;
        set => material.CompressedVolumeThicknessTexture = value;
    }

    /// <summary>Gets or sets transmission-factor sampling and UV mapping.</summary>
    public MaterialTextureMapping FactorTextureMapping
    {
        get => material.TransmissionTextureMapping;
        set => material.TransmissionTextureMapping = value;
    }

    /// <summary>Gets or sets volume-thickness sampling and UV mapping.</summary>
    public MaterialTextureMapping ThicknessTextureMapping
    {
        get => material.VolumeThicknessTextureMapping;
        set => material.VolumeThicknessTextureMapping = value;
    }
}

/// <summary>Provides the cloth/fiber sheen extension block of a <see cref="PbrMaterial"/>.</summary>
public sealed class PbrSheenBlock
{
    private readonly PbrMaterial material;

    internal PbrSheenBlock(PbrMaterial material) => this.material = material;

    /// <summary>Gets or sets explicitly tagged linear sheen color.</summary>
    public LinearRgba Color { get => material.SheenColor; set => material.SheenColor = value; }

    /// <summary>Gets or sets sheen perceptual roughness.</summary>
    public float Roughness { get => material.SheenRoughness; set => material.SheenRoughness = value; }

    /// <summary>Gets or sets the decoded sheen color texture.</summary>
    public LinearRgbaImage? ColorTexture
    {
        get => material.SheenColorTexture;
        set => material.SheenColorTexture = value;
    }

    /// <summary>Gets or sets the decoded sheen roughness texture.</summary>
    public NormalizedRgbaDataImage? RoughnessTexture
    {
        get => material.SheenRoughnessTexture;
        set => material.SheenRoughnessTexture = value;
    }

    /// <summary>Gets or sets the compressed sheen-color texture.</summary>
    public CompressedMaterialTexture? CompressedColorTexture
    {
        get => material.CompressedSheenColorTexture;
        set => material.CompressedSheenColorTexture = value;
    }

    /// <summary>Gets or sets the compressed sheen-roughness texture.</summary>
    public CompressedMaterialTexture? CompressedRoughnessTexture
    {
        get => material.CompressedSheenRoughnessTexture;
        set => material.CompressedSheenRoughnessTexture = value;
    }

    /// <summary>Gets or sets sheen-color sampling and UV mapping.</summary>
    public MaterialTextureMapping ColorTextureMapping
    {
        get => material.SheenColorTextureMapping;
        set => material.SheenColorTextureMapping = value;
    }

    /// <summary>Gets or sets sheen-roughness sampling and UV mapping.</summary>
    public MaterialTextureMapping RoughnessTextureMapping
    {
        get => material.SheenRoughnessTextureMapping;
        set => material.SheenRoughnessTextureMapping = value;
    }
}

/// <summary>Provides the thin-film iridescence extension block of a <see cref="PbrMaterial"/>.</summary>
public sealed class PbrIridescenceBlock
{
    private readonly PbrMaterial material;

    internal PbrIridescenceBlock(PbrMaterial material) => this.material = material;

    /// <summary>Gets or sets thin-film coverage.</summary>
    public float Factor { get => material.IridescenceFactor; set => material.IridescenceFactor = value; }

    /// <summary>Gets or sets the thin-film index of refraction.</summary>
    public float IndexOfRefraction
    {
        get => material.IridescenceIndexOfRefraction;
        set => material.IridescenceIndexOfRefraction = value;
    }

    /// <summary>Gets or sets the minimum film thickness in nanometres.</summary>
    public float ThicknessMinimum
    {
        get => material.IridescenceThicknessMinimum;
        set => material.IridescenceThicknessMinimum = value;
    }

    /// <summary>Gets or sets the maximum film thickness in nanometres.</summary>
    public float ThicknessMaximum
    {
        get => material.IridescenceThicknessMaximum;
        set => material.IridescenceThicknessMaximum = value;
    }

    /// <summary>Gets or sets the decoded factor texture.</summary>
    public NormalizedRgbaDataImage? FactorTexture
    {
        get => material.IridescenceTexture;
        set => material.IridescenceTexture = value;
    }

    /// <summary>Gets or sets the compressed factor texture.</summary>
    public CompressedMaterialTexture? CompressedFactorTexture
    {
        get => material.CompressedIridescenceTexture;
        set => material.CompressedIridescenceTexture = value;
    }

    /// <summary>Gets or sets the decoded thickness texture.</summary>
    public NormalizedRgbaDataImage? ThicknessTexture
    {
        get => material.IridescenceThicknessTexture;
        set => material.IridescenceThicknessTexture = value;
    }

    /// <summary>Gets or sets the compressed thickness texture.</summary>
    public CompressedMaterialTexture? CompressedThicknessTexture
    {
        get => material.CompressedIridescenceThicknessTexture;
        set => material.CompressedIridescenceThicknessTexture = value;
    }

    /// <summary>Gets or sets factor-texture sampling and UV mapping.</summary>
    public MaterialTextureMapping FactorTextureMapping
    {
        get => material.IridescenceTextureMapping;
        set => material.IridescenceTextureMapping = value;
    }

    /// <summary>Gets or sets thickness-texture sampling and UV mapping.</summary>
    public MaterialTextureMapping ThicknessTextureMapping
    {
        get => material.IridescenceThicknessTextureMapping;
        set => material.IridescenceThicknessTextureMapping = value;
    }
}

/// <summary>Provides dielectric specular strength and F0 tint controls.</summary>
public sealed class PbrSpecularBlock
{
    private readonly PbrMaterial material;

    internal PbrSpecularBlock(PbrMaterial material) => this.material = material;

    /// <summary>Gets or sets dielectric specular strength.</summary>
    public float Factor { get => material.SpecularFactor; set => material.SpecularFactor = value; }

    /// <summary>Gets or sets the explicitly tagged linear dielectric F0 multiplier.</summary>
    public LinearRgba Color { get => material.SpecularColor; set => material.SpecularColor = value; }

    /// <summary>Gets or sets the decoded alpha-channel strength texture.</summary>
    public NormalizedRgbaDataImage? FactorTexture
    {
        get => material.SpecularTexture;
        set => material.SpecularTexture = value;
    }

    /// <summary>Gets or sets the compressed alpha-channel strength texture.</summary>
    public CompressedMaterialTexture? CompressedFactorTexture
    {
        get => material.CompressedSpecularTexture;
        set => material.CompressedSpecularTexture = value;
    }

    /// <summary>Gets or sets the decoded sRGB specular-color texture.</summary>
    public LinearRgbaImage? ColorTexture
    {
        get => material.SpecularColorTexture;
        set => material.SpecularColorTexture = value;
    }

    /// <summary>Gets or sets the compressed sRGB specular-color texture.</summary>
    public CompressedMaterialTexture? CompressedColorTexture
    {
        get => material.CompressedSpecularColorTexture;
        set => material.CompressedSpecularColorTexture = value;
    }

    /// <summary>Gets or sets strength-texture sampling and UV mapping.</summary>
    public MaterialTextureMapping FactorTextureMapping
    {
        get => material.SpecularTextureMapping;
        set => material.SpecularTextureMapping = value;
    }

    /// <summary>Gets or sets color-texture sampling and UV mapping.</summary>
    public MaterialTextureMapping ColorTextureMapping
    {
        get => material.SpecularColorTextureMapping;
        set => material.SpecularColorTextureMapping = value;
    }
}

/// <summary>Provides thin-surface diffuse-transmission controls.</summary>
public sealed class PbrDiffuseTransmissionBlock
{
    private readonly PbrMaterial material;

    internal PbrDiffuseTransmissionBlock(PbrMaterial material) => this.material = material;

    /// <summary>Gets or sets the fraction of non-specular light that is diffusely transmitted.</summary>
    public float Factor { get => material.DiffuseTransmissionFactor; set => material.DiffuseTransmissionFactor = value; }

    /// <summary>Gets or sets the explicitly tagged linear transmission color.</summary>
    public LinearRgba Color { get => material.DiffuseTransmissionColor; set => material.DiffuseTransmissionColor = value; }

    /// <summary>Gets or sets the decoded alpha-channel factor texture.</summary>
    public NormalizedRgbaDataImage? FactorTexture { get => material.DiffuseTransmissionTexture; set => material.DiffuseTransmissionTexture = value; }

    /// <summary>Gets or sets the compressed alpha-channel factor texture.</summary>
    public CompressedMaterialTexture? CompressedFactorTexture { get => material.CompressedDiffuseTransmissionTexture; set => material.CompressedDiffuseTransmissionTexture = value; }

    /// <summary>Gets or sets the decoded sRGB transmission-color texture.</summary>
    public LinearRgbaImage? ColorTexture { get => material.DiffuseTransmissionColorTexture; set => material.DiffuseTransmissionColorTexture = value; }

    /// <summary>Gets or sets the compressed sRGB transmission-color texture.</summary>
    public CompressedMaterialTexture? CompressedColorTexture { get => material.CompressedDiffuseTransmissionColorTexture; set => material.CompressedDiffuseTransmissionColorTexture = value; }

    /// <summary>Gets or sets factor-texture sampling and UV mapping.</summary>
    public MaterialTextureMapping FactorTextureMapping { get => material.DiffuseTransmissionTextureMapping; set => material.DiffuseTransmissionTextureMapping = value; }

    /// <summary>Gets or sets color-texture sampling and UV mapping.</summary>
    public MaterialTextureMapping ColorTextureMapping { get => material.DiffuseTransmissionColorTextureMapping; set => material.DiffuseTransmissionColorTextureMapping = value; }
}

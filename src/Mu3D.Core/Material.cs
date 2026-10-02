using System.Numerics;
using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>Controls how a material's unpremultiplied alpha participates in rasterization.</summary>
public enum MaterialAlphaMode
{
    /// <summary>Renders as fully opaque and writes depth.</summary>
    Opaque,

    /// <summary>Discards fragments below <see cref="Material.AlphaCutoff"/> and otherwise writes depth.</summary>
    Mask,

    /// <summary>Uses premultiplied source-over blending without writing depth.</summary>
    Blend,
}

/// <summary>Identifies the root lighting closure from which a material is composed.</summary>
public enum MaterialBaseModel
{
    /// <summary>Uses an application-defined closure not understood by the built-in renderer.</summary>
    Custom,

    /// <summary>Uses authored color without physically based lighting.</summary>
    Unlit,

    /// <summary>Uses the glTF-compatible metallic/roughness PBR base closure.</summary>
    PbrMetallicRoughness,

    /// <summary>Uses the versioned OpenPBR Surface closure through an OpenPBR render pass.</summary>
    OpenPbr,
}

/// <summary>Controls how material texture coordinates outside zero-to-one are addressed.</summary>
public enum MaterialTextureAddressMode
{
    /// <summary>Uses the nearest edge texel.</summary>
    ClampToEdge,

    /// <summary>Repeats the texture at every integer coordinate.</summary>
    Repeat,

    /// <summary>Repeats the texture while mirroring every other integer interval.</summary>
    MirrorRepeat,
}

/// <summary>Controls material texture minification, magnification and mip filtering.</summary>
public enum MaterialTextureFilter
{
    /// <summary>Selects the nearest texel or mip level.</summary>
    Nearest,

    /// <summary>Linearly interpolates neighboring texels and mip levels.</summary>
    Linear,
}

/// <summary>Defines backend-independent sampling shared by a material's two-dimensional maps.</summary>
public readonly record struct MaterialTextureSampling
{
    /// <summary>Initializes material texture sampling.</summary>
    public MaterialTextureSampling(
        MaterialTextureAddressMode addressModeU,
        MaterialTextureAddressMode addressModeV,
        MaterialTextureFilter filter)
        : this(addressModeU, addressModeV, filter, filter, filter, true)
    {
    }

    /// <summary>Initializes independently filtered material texture sampling.</summary>
    public MaterialTextureSampling(
        MaterialTextureAddressMode addressModeU,
        MaterialTextureAddressMode addressModeV,
        MaterialTextureFilter magnificationFilter,
        MaterialTextureFilter minificationFilter,
        MaterialTextureFilter mipFilter,
        bool useMipmaps)
    {
        if (!Enum.IsDefined(addressModeU))
        {
            throw new ArgumentOutOfRangeException(nameof(addressModeU));
        }
        if (!Enum.IsDefined(addressModeV))
        {
            throw new ArgumentOutOfRangeException(nameof(addressModeV));
        }
        if (!Enum.IsDefined(magnificationFilter))
        {
            throw new ArgumentOutOfRangeException(nameof(magnificationFilter));
        }
        if (!Enum.IsDefined(minificationFilter))
        {
            throw new ArgumentOutOfRangeException(nameof(minificationFilter));
        }
        if (!Enum.IsDefined(mipFilter))
        {
            throw new ArgumentOutOfRangeException(nameof(mipFilter));
        }
        AddressModeU = addressModeU;
        AddressModeV = addressModeV;
        MagnificationFilter = magnificationFilter;
        MinificationFilter = minificationFilter;
        MipFilter = mipFilter;
        UseMipmaps = useMipmaps;
    }

    /// <summary>Gets repeating, trilinear sampling used by default for PBR material maps.</summary>
    public static MaterialTextureSampling RepeatingLinear { get; } = new(
        MaterialTextureAddressMode.Repeat,
        MaterialTextureAddressMode.Repeat,
        MaterialTextureFilter.Linear);

    /// <summary>Gets horizontal texture addressing.</summary>
    public MaterialTextureAddressMode AddressModeU { get; }

    /// <summary>Gets vertical texture addressing.</summary>
    public MaterialTextureAddressMode AddressModeV { get; }

    /// <summary>
    /// Gets the legacy common filter view. For independently filtered sampling this returns the
    /// minification filter.
    /// </summary>
    public MaterialTextureFilter Filter => MinificationFilter;

    /// <summary>Gets the magnification filter.</summary>
    public MaterialTextureFilter MagnificationFilter { get; }

    /// <summary>Gets the minification filter within one mip level.</summary>
    public MaterialTextureFilter MinificationFilter { get; }

    /// <summary>Gets the filter between mip levels.</summary>
    public MaterialTextureFilter MipFilter { get; }

    /// <summary>Gets whether sampling may select mip levels above zero.</summary>
    public bool UseMipmaps { get; }
}

/// <summary>
/// Defines the sampling and affine texture-coordinate mapping for one material texture slot.
/// </summary>
public readonly record struct MaterialTextureMapping
{
    /// <summary>Initializes a texture-slot mapping.</summary>
    /// <param name="sampling">Backend-independent sampler state.</param>
    /// <param name="textureCoordinateSet">Zero-based texture-coordinate set; currently zero or one.</param>
    /// <param name="scale">Finite scale applied before rotation.</param>
    /// <param name="offset">Finite offset applied after rotation.</param>
    /// <param name="rotation">Finite counter-clockwise rotation in radians.</param>
    public MaterialTextureMapping(
        MaterialTextureSampling sampling,
        int textureCoordinateSet = 0,
        Vector2? scale = null,
        Vector2? offset = null,
        float rotation = 0f)
    {
        _ = new MaterialTextureSampling(
            sampling.AddressModeU,
            sampling.AddressModeV,
            sampling.MagnificationFilter,
            sampling.MinificationFilter,
            sampling.MipFilter,
            sampling.UseMipmaps);
        if (textureCoordinateSet is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(textureCoordinateSet));
        }
        Vector2 resolvedScale = scale ?? Vector2.One;
        Vector2 resolvedOffset = offset ?? Vector2.Zero;
        ValidateFinite(resolvedScale, nameof(scale));
        ValidateFinite(resolvedOffset, nameof(offset));
        if (!float.IsFinite(rotation))
        {
            throw new ArgumentOutOfRangeException(nameof(rotation));
        }
        Sampling = sampling;
        TextureCoordinateSet = textureCoordinateSet;
        Scale = resolvedScale;
        Offset = resolvedOffset;
        Rotation = rotation;
    }

    /// <summary>Gets the default repeating, trilinear mapping of texture-coordinate set zero.</summary>
    public static MaterialTextureMapping Default { get; } = new(
        MaterialTextureSampling.RepeatingLinear);

    /// <summary>Gets this slot's sampler state.</summary>
    public MaterialTextureSampling Sampling { get; }

    /// <summary>Gets the zero-based mesh texture-coordinate set.</summary>
    public int TextureCoordinateSet { get; }

    /// <summary>Gets the UV scale applied before rotation.</summary>
    public Vector2 Scale { get; }

    /// <summary>Gets the UV offset applied after rotation.</summary>
    public Vector2 Offset { get; }

    /// <summary>Gets the counter-clockwise UV rotation in radians.</summary>
    public float Rotation { get; }

    private static void ValidateFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}

/// <summary>
/// Base class for renderer materials. Concrete materials must give every color property an explicit
/// working-space identity rather than treating untagged RGB values as display colors.
/// </summary>
public abstract class Material
{
    private MaterialAlphaMode alphaMode;
    private float alphaCutoff = 0.5f;
    private ulong textureBindingRevision;

    /// <summary>Initializes a material.</summary>
    protected Material(string? name = null) => Name = name;

    /// <summary>Gets the root lighting closure used by this material.</summary>
    public virtual MaterialBaseModel BaseModel => MaterialBaseModel.Custom;

    /// <summary>Gets or sets the optional material name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets or sets whether both geometric face orientations are rendered.</summary>
    public bool IsDoubleSided { get; set; }

    /// <summary>Gets or sets how this material's unpremultiplied alpha is rasterized.</summary>
    public MaterialAlphaMode AlphaMode
    {
        get => alphaMode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            alphaMode = value;
        }
    }

    /// <summary>
    /// Gets or sets the alpha threshold used by <see cref="MaterialAlphaMode.Mask"/> in the inclusive
    /// zero-to-one range. The default is 0.5.
    /// </summary>
    public float AlphaCutoff
    {
        get => alphaCutoff;
        set
        {
            if (!float.IsFinite(value) || value is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            alphaCutoff = value;
        }
    }

    /// <summary>
    /// Gets the renderer-internal revision of properties that can change sampled texture bindings.
    /// Scalar-only edits deliberately do not invalidate immutable GPU bind groups.
    /// </summary>
    internal ulong TextureBindingRevision => textureBindingRevision;

    /// <summary>Marks the sampled-texture topology or sampler state as changed.</summary>
    protected void InvalidateTextureBindings() => textureBindingRevision++;

    internal void CopyInstanceStateTo(Material destination)
    {
        destination.Name = Name;
        destination.IsDoubleSided = IsDoubleSided;
        destination.AlphaMode = AlphaMode;
        destination.AlphaCutoff = AlphaCutoff;
    }
}

/// <summary>Represents an unlit material with an explicitly tagged linear-light color.</summary>
public sealed class UnlitMaterial : Material
{
    private LinearRgba color;
    private LinearRgbaImage? baseColorTexture;
    private CompressedMaterialTexture? compressedBaseColorTexture;
    private MaterialTextureMapping baseColorTextureMapping = MaterialTextureMapping.Default;

    /// <summary>Initializes an unlit material.</summary>
    public UnlitMaterial(LinearRgba color, string? name = null)
        : base(name)
    {
        if (color.ColorSpace is null)
        {
            throw new ArgumentException("The material color must carry a color-space identity.", nameof(color));
        }
        this.color = color;
    }

    /// <inheritdoc />
    public override MaterialBaseModel BaseModel => MaterialBaseModel.Unlit;

    /// <summary>Gets or sets the explicitly tagged unpremultiplied linear-light color.</summary>
    public LinearRgba Color
    {
        get => color;
        set
        {
            if (value.ColorSpace is null)
            {
                throw new ArgumentException("The material color must carry a color-space identity.", nameof(value));
            }
            color = value;
        }
    }

    /// <summary>
    /// Gets or sets an optional unpremultiplied linear-light base-color image. Its RGB is converted
    /// to the renderer working space and multiplied by <see cref="Color"/>. Image alpha remains
    /// unpremultiplied and participates in <see cref="Material.AlphaMode"/>.
    /// </summary>
    public LinearRgbaImage? BaseColorTexture
    {
        get => baseColorTexture;
        set
        {
            if (value?.ColorSpace is not null and not StandardRgbColorSpaceReference)
            {
                throw new NotSupportedException(
                    "The initial unlit texture path supports built-in standard RGB spaces.");
            }
            if (value is not null && compressedBaseColorTexture is not null)
            {
                throw new InvalidOperationException(
                    "An unlit material cannot use decoded and compressed base-color textures together.");
            }
            baseColorTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>
    /// Gets or sets an optional GPU-ready compressed base-color mip chain. Its content must be
    /// color data and it is mutually exclusive with <see cref="BaseColorTexture"/>.
    /// </summary>
    public CompressedMaterialTexture? CompressedBaseColorTexture
    {
        get => compressedBaseColorTexture;
        set
        {
            if (value is not null && value.Content != CompressedMaterialTextureContent.Color)
            {
                throw new ArgumentException("The compressed texture must contain color content.", nameof(value));
            }
            if (value is not null && baseColorTexture is not null)
            {
                throw new InvalidOperationException(
                    "An unlit material cannot use decoded and compressed base-color textures together.");
            }
            compressedBaseColorTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets sampler and UV mapping for the base-color texture slot.</summary>
    public MaterialTextureMapping BaseColorTextureMapping
    {
        get => baseColorTextureMapping;
        set
        {
            baseColorTextureMapping = new MaterialTextureMapping(
                value.Sampling,
                value.TextureCoordinateSet,
                value.Scale,
                value.Offset,
                value.Rotation);
            InvalidateTextureBindings();
        }
    }

    internal UnlitMaterial CloneForSceneInstance()
    {
        UnlitMaterial clone = new(color, Name)
        {
            baseColorTexture = baseColorTexture,
            compressedBaseColorTexture = compressedBaseColorTexture,
            baseColorTextureMapping = baseColorTextureMapping,
        };
        CopyInstanceStateTo(clone);
        return clone;
    }
}

/// <summary>
/// Represents a metallic/roughness material. Base color is explicitly tagged, unpremultiplied
/// linear-light RGBA. Optional dielectric transmission and volume absorption remain scene-linear.
/// </summary>
public class PbrMaterial : Material
{
    private MaterialShaderTemplate shaderTemplate = MaterialShaderTemplate.FullPbr;
    private LinearRgba baseColor;
    private LinearRgba emissiveColor = new(
        0f,
        0f,
        0f,
        1f,
        StandardColorSpaces.LinearSrgb);
    private float emissiveStrength = 1f;
    private float metallic;
    private float roughness;
    private float indirectOcclusion = 1f;
    private LinearRgbaImage? baseColorTexture;
    private CompressedMaterialTexture? compressedBaseColorTexture;
    private LinearRgbaImage? emissiveTexture;
    private CompressedMaterialTexture? compressedEmissiveTexture;
    private NormalizedRgbaDataImage? normalTexture;
    private CompressedMaterialTexture? compressedNormalTexture;
    private NormalizedRgbaDataImage? occlusionRoughnessMetallicTexture;
    private CompressedMaterialTexture? compressedOcclusionRoughnessMetallicTexture;
    private float normalScale = 1f;
    private float clearcoatFactor;
    private float clearcoatRoughness;
    private float clearcoatNormalScale = 1f;
    private float anisotropyStrength;
    private float anisotropyRotation;
    private NormalizedRgbaDataImage? anisotropyTexture;
    private CompressedMaterialTexture? compressedAnisotropyTexture;
    private float transmissionFactor;
    private float indexOfRefraction = 1.5f;
    private float dispersion;
    private float volumeThicknessFactor;
    private float volumeAttenuationDistance = float.PositiveInfinity;
    private LinearRgba volumeAttenuationColor = new(
        1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb);
    private NormalizedRgbaDataImage? transmissionTexture;
    private CompressedMaterialTexture? compressedTransmissionTexture;
    private NormalizedRgbaDataImage? volumeThicknessTexture;
    private CompressedMaterialTexture? compressedVolumeThicknessTexture;
    private LinearRgba sheenColor = new(0f, 0f, 0f, 1f, StandardColorSpaces.LinearSrgb);
    private float sheenRoughness;
    private LinearRgbaImage? sheenColorTexture;
    private CompressedMaterialTexture? compressedSheenColorTexture;
    private NormalizedRgbaDataImage? sheenRoughnessTexture;
    private CompressedMaterialTexture? compressedSheenRoughnessTexture;
    private float iridescenceFactor;
    private float iridescenceIndexOfRefraction = 1.3f;
    private float iridescenceThicknessMinimum = 100f;
    private float iridescenceThicknessMaximum = 400f;
    private NormalizedRgbaDataImage? iridescenceTexture;
    private CompressedMaterialTexture? compressedIridescenceTexture;
    private NormalizedRgbaDataImage? iridescenceThicknessTexture;
    private CompressedMaterialTexture? compressedIridescenceThicknessTexture;
    private float specularFactor = 1f;
    private LinearRgba specularColor = new(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb);
    private NormalizedRgbaDataImage? specularTexture;
    private CompressedMaterialTexture? compressedSpecularTexture;
    private LinearRgbaImage? specularColorTexture;
    private CompressedMaterialTexture? compressedSpecularColorTexture;
    private float diffuseTransmissionFactor;
    private LinearRgba diffuseTransmissionColor = new(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb);
    private NormalizedRgbaDataImage? diffuseTransmissionTexture;
    private CompressedMaterialTexture? compressedDiffuseTransmissionTexture;
    private LinearRgbaImage? diffuseTransmissionColorTexture;
    private CompressedMaterialTexture? compressedDiffuseTransmissionColorTexture;
    private NormalizedRgbaDataImage? clearcoatTexture;
    private CompressedMaterialTexture? compressedClearcoatTexture;
    private NormalizedRgbaDataImage? clearcoatRoughnessTexture;
    private CompressedMaterialTexture? compressedClearcoatRoughnessTexture;
    private NormalizedRgbaDataImage? clearcoatNormalTexture;
    private CompressedMaterialTexture? compressedClearcoatNormalTexture;
    private MaterialTextureMapping baseColorTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping emissiveTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping normalTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping ormTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping clearcoatTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping clearcoatRoughnessTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping clearcoatNormalTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping anisotropyTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping transmissionTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping volumeThicknessTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping sheenColorTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping sheenRoughnessTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping iridescenceTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping iridescenceThicknessTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping specularTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping specularColorTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping diffuseTransmissionTextureMapping = MaterialTextureMapping.Default;
    private MaterialTextureMapping diffuseTransmissionColorTextureMapping = MaterialTextureMapping.Default;

    /// <summary>Initializes a metallic/roughness material.</summary>
    public PbrMaterial(
        LinearRgba baseColor,
        float metallic = 0f,
        float roughness = 0.5f,
        string? name = null)
        : base(name)
    {
        ValidateBaseColor(baseColor);
        ValidateUnitInterval(metallic, nameof(metallic));
        ValidateUnitInterval(roughness, nameof(roughness));
        this.baseColor = baseColor;
        this.metallic = metallic;
        this.roughness = roughness;
        Base = new PbrBaseBlock(this);
        Emissive = new PbrEmissiveBlock(this);
        Clearcoat = new PbrClearcoatBlock(this);
        Anisotropy = new PbrAnisotropyBlock(this);
        Transmission = new PbrTransmissionBlock(this);
        Sheen = new PbrSheenBlock(this);
        Iridescence = new PbrIridescenceBlock(this);
        Specular = new PbrSpecularBlock(this);
        DiffuseTransmission = new PbrDiffuseTransmissionBlock(this);
    }

    /// <summary>Gets the metallic/roughness base closure block.</summary>
    public PbrBaseBlock Base { get; }

    /// <summary>Gets the optional scene-linear emissive block.</summary>
    public PbrEmissiveBlock Emissive { get; }

    /// <summary>Gets the optional outer clearcoat block.</summary>
    public PbrClearcoatBlock Clearcoat { get; }

    /// <summary>Gets the optional anisotropic base-specular block.</summary>
    public PbrAnisotropyBlock Anisotropy { get; }

    /// <summary>Gets the optional transmission and finite-volume block.</summary>
    public PbrTransmissionBlock Transmission { get; }

    /// <summary>Gets the optional cloth/fiber sheen block.</summary>
    public PbrSheenBlock Sheen { get; }

    /// <summary>Gets the optional wavelength-dependent thin-film iridescence block.</summary>
    public PbrIridescenceBlock Iridescence { get; }

    /// <summary>Gets the optional dielectric specular-strength and F0-color block.</summary>
    public PbrSpecularBlock Specular { get; }

    /// <summary>Gets the optional thin-surface diffuse-transmission block.</summary>
    public PbrDiffuseTransmissionBlock DiffuseTransmission { get; }

    /// <inheritdoc />
    public override MaterialBaseModel BaseModel => MaterialBaseModel.PbrMetallicRoughness;

    /// <summary>
    /// Gets or sets the immutable shader-generation policy. The default permits all supported PBR
    /// extensions and texture slots; the renderer specializes it from actual nonzero factors and
    /// present texture sources.
    /// </summary>
    public MaterialShaderTemplate ShaderTemplate
    {
        get => shaderTemplate;
        set
        {
            shaderTemplate = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets the unpremultiplied, explicitly tagged linear base color.</summary>
    public LinearRgba BaseColor
    {
        get => baseColor;
        set
        {
            ValidateBaseColor(value);
            baseColor = value;
        }
    }

    /// <summary>
    /// Gets or sets explicitly tagged linear-light RGB emission added by the Beauty pass. Alpha is
    /// retained for color-value consistency but does not control material coverage. The default is
    /// zero emission in Linear sRGB.
    /// </summary>
    public LinearRgba EmissiveColor
    {
        get => emissiveColor;
        set
        {
            ValidateEmissiveColor(value);
            emissiveColor = value;
        }
    }

    /// <summary>
    /// Gets or sets the finite non-negative unitless multiplier applied to
    /// <see cref="EmissiveColor"/> and the optional emissive texture. Values above one preserve HDR
    /// scene-referred emission. The default is one.
    /// </summary>
    public float EmissiveStrength
    {
        get => emissiveStrength;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            emissiveStrength = value;
        }
    }

    /// <summary>
    /// Gets or sets an optional unpremultiplied linear-light base-color image. Its RGB is converted
    /// to the renderer working space before upload and multiplied by <see cref="BaseColor"/>.
    /// Image alpha remains unpremultiplied and participates in <see cref="Material.AlphaMode"/>.
    /// Sampling follows <see cref="TextureSampling"/>.
    /// </summary>
    public LinearRgbaImage? BaseColorTexture
    {
        get => baseColorTexture;
        set
        {
            if (value?.ColorSpace is not null and not StandardRgbColorSpaceReference)
            {
                throw new NotSupportedException(
                    "The initial material texture path supports built-in standard RGB spaces.");
            }
            if (value is not null && compressedBaseColorTexture is not null)
            {
                throw new InvalidOperationException(
                    "A material cannot use decoded and compressed base-color textures together.");
            }
            baseColorTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>
    /// Gets or sets an optional GPU-ready compressed base-color mip chain. Color identity remains
    /// explicit and this source is mutually exclusive with <see cref="BaseColorTexture"/>.
    /// </summary>
    public CompressedMaterialTexture? CompressedBaseColorTexture
    {
        get => compressedBaseColorTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Color, nameof(value));
            if (value is not null && baseColorTexture is not null)
            {
                throw new InvalidOperationException(
                    "A material cannot use decoded and compressed base-color textures together.");
            }
            compressedBaseColorTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>
    /// Gets or sets an optional unpremultiplied linear-light emissive image. Its RGB is converted
    /// to the renderer working space and multiplied by <see cref="EmissiveColor"/>. Alpha is
    /// retained in the image contract but does not affect material coverage.
    /// </summary>
    public LinearRgbaImage? EmissiveTexture
    {
        get => emissiveTexture;
        set
        {
            if (value?.ColorSpace is not null and not StandardRgbColorSpaceReference)
            {
                throw new NotSupportedException(
                    "The initial emissive texture path supports built-in standard RGB spaces.");
            }
            if (value is not null && compressedEmissiveTexture is not null)
            {
                throw new InvalidOperationException(
                    "A material cannot use decoded and compressed emissive textures together.");
            }
            emissiveTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>
    /// Gets or sets an optional GPU-ready compressed emissive mip chain. It must be tagged as color
    /// data and is mutually exclusive with <see cref="EmissiveTexture"/>.
    /// </summary>
    public CompressedMaterialTexture? CompressedEmissiveTexture
    {
        get => compressedEmissiveTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Color, nameof(value));
            if (value is not null && emissiveTexture is not null)
            {
                throw new InvalidOperationException(
                    "A material cannot use decoded and compressed emissive textures together.");
            }
            compressedEmissiveTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>
    /// Gets or sets an optional tangent-space normal map encoded from minus-one/one into zero/one.
    /// It is non-color data and is never color transformed.
    /// </summary>
    public NormalizedRgbaDataImage? NormalTexture
    {
        get => normalTexture;
        set
        {
            if (value is not null && compressedNormalTexture is not null)
            {
                throw new InvalidOperationException(
                    "A material cannot use decoded and compressed normal textures together.");
            }
            normalTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>
    /// Gets or sets an optional GPU-ready compressed tangent-space normal-map mip chain. It must be
    /// tagged as non-color data and is mutually exclusive with <see cref="NormalTexture"/>.
    /// </summary>
    public CompressedMaterialTexture? CompressedNormalTexture
    {
        get => compressedNormalTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            if (value is not null && normalTexture is not null)
            {
                throw new InvalidOperationException(
                    "A material cannot use decoded and compressed normal textures together.");
            }
            compressedNormalTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>
    /// Gets or sets an optional packed non-color map using red for occlusion, green for perceptual
    /// roughness and blue for metallic, matching glTF channel convention.
    /// </summary>
    public NormalizedRgbaDataImage? OcclusionRoughnessMetallicTexture
    {
        get => occlusionRoughnessMetallicTexture;
        set
        {
            if (value is not null && compressedOcclusionRoughnessMetallicTexture is not null)
            {
                throw new InvalidOperationException(
                    "A material cannot use decoded and compressed ORM textures together.");
            }
            occlusionRoughnessMetallicTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>
    /// Gets or sets an optional GPU-ready compressed occlusion/roughness/metallic mip chain. It must
    /// be non-color data and is mutually exclusive with
    /// <see cref="OcclusionRoughnessMetallicTexture"/>.
    /// </summary>
    public CompressedMaterialTexture? CompressedOcclusionRoughnessMetallicTexture
    {
        get => compressedOcclusionRoughnessMetallicTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            if (value is not null && occlusionRoughnessMetallicTexture is not null)
            {
                throw new InvalidOperationException(
                    "A material cannot use decoded and compressed ORM textures together.");
            }
            compressedOcclusionRoughnessMetallicTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets sampler and UV mapping for the base-color texture slot.</summary>
    public MaterialTextureMapping BaseColorTextureMapping
    {
        get => baseColorTextureMapping;
        set { baseColorTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets sampler and UV mapping for the emissive texture slot.</summary>
    public MaterialTextureMapping EmissiveTextureMapping
    {
        get => emissiveTextureMapping;
        set { emissiveTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets sampler and UV mapping for the tangent-space normal texture slot.</summary>
    public MaterialTextureMapping NormalTextureMapping
    {
        get => normalTextureMapping;
        set { normalTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets sampler and UV mapping for the packed ORM texture slot.</summary>
    public MaterialTextureMapping OcclusionRoughnessMetallicTextureMapping
    {
        get => ormTextureMapping;
        set { ormTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>
    /// Gets or sets addressing and filtering for all current texture slots. The getter returns the
    /// base-color slot value. Prefer the per-slot mapping properties when maps differ.
    /// </summary>
    public MaterialTextureSampling TextureSampling
    {
        get => baseColorTextureMapping.Sampling;
        set
        {
            baseColorTextureMapping = ReplaceSampling(baseColorTextureMapping, value);
            emissiveTextureMapping = ReplaceSampling(emissiveTextureMapping, value);
            normalTextureMapping = ReplaceSampling(normalTextureMapping, value);
            ormTextureMapping = ReplaceSampling(ormTextureMapping, value);
            clearcoatTextureMapping = ReplaceSampling(clearcoatTextureMapping, value);
            clearcoatRoughnessTextureMapping = ReplaceSampling(clearcoatRoughnessTextureMapping, value);
            clearcoatNormalTextureMapping = ReplaceSampling(clearcoatNormalTextureMapping, value);
            anisotropyTextureMapping = ReplaceSampling(anisotropyTextureMapping, value);
            transmissionTextureMapping = ReplaceSampling(transmissionTextureMapping, value);
            volumeThicknessTextureMapping = ReplaceSampling(volumeThicknessTextureMapping, value);
            sheenColorTextureMapping = ReplaceSampling(sheenColorTextureMapping, value);
            sheenRoughnessTextureMapping = ReplaceSampling(sheenRoughnessTextureMapping, value);
            iridescenceTextureMapping = ReplaceSampling(iridescenceTextureMapping, value);
            iridescenceThicknessTextureMapping = ReplaceSampling(iridescenceThicknessTextureMapping, value);
            specularTextureMapping = ReplaceSampling(specularTextureMapping, value);
            specularColorTextureMapping = ReplaceSampling(specularColorTextureMapping, value);
            diffuseTransmissionTextureMapping = ReplaceSampling(diffuseTransmissionTextureMapping, value);
            diffuseTransmissionColorTextureMapping = ReplaceSampling(diffuseTransmissionColorTextureMapping, value);
            InvalidateTextureBindings();
        }
    }

    /// <summary>
    /// Gets or sets finite UV scale for all current texture slots. The getter returns the base-color
    /// slot value. Prefer the per-slot mapping properties when maps differ.
    /// </summary>
    public Vector2 TextureCoordinateScale
    {
        get => baseColorTextureMapping.Scale;
        set
        {
            ValidateFinite(value, nameof(value));
            baseColorTextureMapping = ReplaceScale(baseColorTextureMapping, value);
            emissiveTextureMapping = ReplaceScale(emissiveTextureMapping, value);
            normalTextureMapping = ReplaceScale(normalTextureMapping, value);
            ormTextureMapping = ReplaceScale(ormTextureMapping, value);
            clearcoatTextureMapping = ReplaceScale(clearcoatTextureMapping, value);
            clearcoatRoughnessTextureMapping = ReplaceScale(clearcoatRoughnessTextureMapping, value);
            clearcoatNormalTextureMapping = ReplaceScale(clearcoatNormalTextureMapping, value);
            anisotropyTextureMapping = ReplaceScale(anisotropyTextureMapping, value);
            transmissionTextureMapping = ReplaceScale(transmissionTextureMapping, value);
            volumeThicknessTextureMapping = ReplaceScale(volumeThicknessTextureMapping, value);
            sheenColorTextureMapping = ReplaceScale(sheenColorTextureMapping, value);
            sheenRoughnessTextureMapping = ReplaceScale(sheenRoughnessTextureMapping, value);
            iridescenceTextureMapping = ReplaceScale(iridescenceTextureMapping, value);
            iridescenceThicknessTextureMapping = ReplaceScale(iridescenceThicknessTextureMapping, value);
            specularTextureMapping = ReplaceScale(specularTextureMapping, value);
            specularColorTextureMapping = ReplaceScale(specularColorTextureMapping, value);
            diffuseTransmissionTextureMapping = ReplaceScale(diffuseTransmissionTextureMapping, value);
            diffuseTransmissionColorTextureMapping = ReplaceScale(diffuseTransmissionColorTextureMapping, value);
        }
    }

    /// <summary>
    /// Gets or sets finite UV offset for all current texture slots. The getter returns the
    /// base-color slot value. Prefer the per-slot mapping properties when maps differ.
    /// </summary>
    public Vector2 TextureCoordinateOffset
    {
        get => baseColorTextureMapping.Offset;
        set
        {
            ValidateFinite(value, nameof(value));
            baseColorTextureMapping = ReplaceOffset(baseColorTextureMapping, value);
            emissiveTextureMapping = ReplaceOffset(emissiveTextureMapping, value);
            normalTextureMapping = ReplaceOffset(normalTextureMapping, value);
            ormTextureMapping = ReplaceOffset(ormTextureMapping, value);
            clearcoatTextureMapping = ReplaceOffset(clearcoatTextureMapping, value);
            clearcoatRoughnessTextureMapping = ReplaceOffset(clearcoatRoughnessTextureMapping, value);
            clearcoatNormalTextureMapping = ReplaceOffset(clearcoatNormalTextureMapping, value);
            anisotropyTextureMapping = ReplaceOffset(anisotropyTextureMapping, value);
            transmissionTextureMapping = ReplaceOffset(transmissionTextureMapping, value);
            volumeThicknessTextureMapping = ReplaceOffset(volumeThicknessTextureMapping, value);
            sheenColorTextureMapping = ReplaceOffset(sheenColorTextureMapping, value);
            sheenRoughnessTextureMapping = ReplaceOffset(sheenRoughnessTextureMapping, value);
            iridescenceTextureMapping = ReplaceOffset(iridescenceTextureMapping, value);
            iridescenceThicknessTextureMapping = ReplaceOffset(iridescenceThicknessTextureMapping, value);
            specularTextureMapping = ReplaceOffset(specularTextureMapping, value);
            specularColorTextureMapping = ReplaceOffset(specularColorTextureMapping, value);
            diffuseTransmissionTextureMapping = ReplaceOffset(diffuseTransmissionTextureMapping, value);
            diffuseTransmissionColorTextureMapping = ReplaceOffset(diffuseTransmissionColorTextureMapping, value);
        }
    }

    /// <summary>Gets or sets the finite non-negative tangent-space normal-map X/Y scale.</summary>
    public float NormalScale
    {
        get => normalScale;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            normalScale = value;
        }
    }

    /// <summary>Gets or sets the metallic factor from zero for dielectric to one for metal.</summary>
    public float Metallic
    {
        get => metallic;
        set
        {
            ValidateUnitInterval(value, nameof(value));
            metallic = value;
        }
    }

    /// <summary>
    /// Gets or sets perceptual roughness in the inclusive zero-to-one range. The renderer applies a
    /// small numerical floor only while evaluating the specular distribution.
    /// </summary>
    public float Roughness
    {
        get => roughness;
        set
        {
            ValidateUnitInterval(value, nameof(value));
            roughness = value;
        }
    }

    /// <summary>
    /// Gets or sets the authored visibility of image-based illumination in the inclusive zero-to-one
    /// range. One is fully visible. The renderer combines this value with dynamic screen-space
    /// visibility and applies a roughness-aware policy to specular IBL.
    /// </summary>
    public float IndirectOcclusion
    {
        get => indirectOcclusion;
        set
        {
            ValidateUnitInterval(value, nameof(value));
            indirectOcclusion = value;
        }
    }

    /// <summary>
    /// Gets or sets the dielectric clearcoat layer weight in the inclusive zero-to-one range. The
    /// default is zero, preserving the ordinary metallic/roughness material.
    /// </summary>
    public float ClearcoatFactor
    {
        get => clearcoatFactor;
        set
        {
            ValidateUnitInterval(value, nameof(value));
            clearcoatFactor = value;
        }
    }

    /// <summary>Gets or sets clearcoat perceptual roughness in the inclusive zero-to-one range.</summary>
    public float ClearcoatRoughness
    {
        get => clearcoatRoughness;
        set
        {
            ValidateUnitInterval(value, nameof(value));
            clearcoatRoughness = value;
        }
    }

    /// <summary>Gets or sets the nonnegative tangent-space clearcoat normal-map X/Y scale.</summary>
    public float ClearcoatNormalScale
    {
        get => clearcoatNormalScale;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            clearcoatNormalScale = value;
        }
    }

    /// <summary>Gets or sets the non-color clearcoat weight texture sampled from its red channel.</summary>
    public NormalizedRgbaDataImage? ClearcoatTexture
    {
        get => clearcoatTexture;
        set
        {
            EnsureExclusive(value, compressedClearcoatTexture, "clearcoat");
            clearcoatTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets the compressed non-color clearcoat weight texture.</summary>
    public CompressedMaterialTexture? CompressedClearcoatTexture
    {
        get => compressedClearcoatTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, clearcoatTexture, "clearcoat");
            compressedClearcoatTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets clearcoat roughness data sampled from the texture's green channel.</summary>
    public NormalizedRgbaDataImage? ClearcoatRoughnessTexture
    {
        get => clearcoatRoughnessTexture;
        set
        {
            EnsureExclusive(value, compressedClearcoatRoughnessTexture, "clearcoat roughness");
            clearcoatRoughnessTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets the compressed non-color clearcoat roughness texture.</summary>
    public CompressedMaterialTexture? CompressedClearcoatRoughnessTexture
    {
        get => compressedClearcoatRoughnessTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, clearcoatRoughnessTexture, "clearcoat roughness");
            compressedClearcoatRoughnessTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets an optional non-color tangent-space clearcoat normal map.</summary>
    public NormalizedRgbaDataImage? ClearcoatNormalTexture
    {
        get => clearcoatNormalTexture;
        set
        {
            EnsureExclusive(value, compressedClearcoatNormalTexture, "clearcoat normal");
            clearcoatNormalTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets the compressed non-color clearcoat normal map.</summary>
    public CompressedMaterialTexture? CompressedClearcoatNormalTexture
    {
        get => compressedClearcoatNormalTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, clearcoatNormalTexture, "clearcoat normal");
            compressedClearcoatNormalTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets sampler and UV mapping for clearcoat weight.</summary>
    public MaterialTextureMapping ClearcoatTextureMapping
    {
        get => clearcoatTextureMapping;
        set { clearcoatTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets sampler and UV mapping for clearcoat roughness.</summary>
    public MaterialTextureMapping ClearcoatRoughnessTextureMapping
    {
        get => clearcoatRoughnessTextureMapping;
        set { clearcoatRoughnessTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets sampler and UV mapping for the clearcoat normal map.</summary>
    public MaterialTextureMapping ClearcoatNormalTextureMapping
    {
        get => clearcoatNormalTextureMapping;
        set { clearcoatNormalTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets anisotropic specular strength in the inclusive zero-to-one range.</summary>
    public float AnisotropyStrength
    {
        get => anisotropyStrength;
        set { ValidateUnitInterval(value, nameof(value)); anisotropyStrength = value; }
    }

    /// <summary>Gets or sets finite counter-clockwise anisotropy rotation in tangent space.</summary>
    public float AnisotropyRotation
    {
        get => anisotropyRotation;
        set
        {
            if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            anisotropyRotation = value;
        }
    }

    /// <summary>Gets or sets non-color anisotropy direction (RG) and strength (B) data.</summary>
    public NormalizedRgbaDataImage? AnisotropyTexture
    {
        get => anisotropyTexture;
        set { EnsureExclusive(value, compressedAnisotropyTexture, "anisotropy"); anisotropyTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets compressed non-color anisotropy direction and strength data.</summary>
    public CompressedMaterialTexture? CompressedAnisotropyTexture
    {
        get => compressedAnisotropyTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, anisotropyTexture, "anisotropy");
            compressedAnisotropyTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets sampler and UV mapping for anisotropy data.</summary>
    public MaterialTextureMapping AnisotropyTextureMapping
    {
        get => anisotropyTextureMapping;
        set { anisotropyTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>
    /// Gets or sets dielectric transmission in the inclusive zero-to-one range. Zero preserves the
    /// ordinary opaque metallic/roughness surface; one transmits all non-reflected dielectric light.
    /// </summary>
    public float TransmissionFactor
    {
        get => transmissionFactor;
        set { ValidateUnitInterval(value, nameof(value)); transmissionFactor = value; }
    }

    /// <summary>Gets or sets the dielectric index of refraction. The glTF default is 1.5.</summary>
    public float IndexOfRefraction
    {
        get => indexOfRefraction;
        set
        {
            if (!float.IsFinite(value) || (value != 0f && value < 1f))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Index of refraction must be zero for glTF's infinite-IOR compatibility mode, or at least one.");
            }
            indexOfRefraction = value;
        }
    }

    /// <summary>
    /// Gets or sets the non-negative KHR_materials_dispersion strength applied to finite-volume
    /// transmission. Zero disables chromatic separation.
    /// </summary>
    public float Dispersion
    {
        get => dispersion;
        set
        {
            if (!float.IsFinite(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            dispersion = value;
        }
    }

    /// <summary>Gets or sets non-color transmission weight data sampled from the red channel.</summary>
    public NormalizedRgbaDataImage? TransmissionTexture
    {
        get => transmissionTexture;
        set { EnsureExclusive(value, compressedTransmissionTexture, "transmission"); transmissionTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets compressed non-color transmission weight data.</summary>
    public CompressedMaterialTexture? CompressedTransmissionTexture
    {
        get => compressedTransmissionTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, transmissionTexture, "transmission");
            compressedTransmissionTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets sampler and UV mapping for transmission weight.</summary>
    public MaterialTextureMapping TransmissionTextureMapping
    {
        get => transmissionTextureMapping;
        set { transmissionTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets the non-negative maximum volume thickness in scene units.</summary>
    public float VolumeThicknessFactor
    {
        get => volumeThicknessFactor;
        set
        {
            if (!float.IsFinite(value) || value < 0f) throw new ArgumentOutOfRangeException(nameof(value));
            volumeThicknessFactor = value;
        }
    }

    /// <summary>Gets or sets non-color volume thickness data sampled from the green channel.</summary>
    public NormalizedRgbaDataImage? VolumeThicknessTexture
    {
        get => volumeThicknessTexture;
        set { EnsureExclusive(value, compressedVolumeThicknessTexture, "volume thickness"); volumeThicknessTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets compressed non-color volume thickness data.</summary>
    public CompressedMaterialTexture? CompressedVolumeThicknessTexture
    {
        get => compressedVolumeThicknessTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, volumeThicknessTexture, "volume thickness");
            compressedVolumeThicknessTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets sampler and UV mapping for volume thickness.</summary>
    public MaterialTextureMapping VolumeThicknessTextureMapping
    {
        get => volumeThicknessTextureMapping;
        set { volumeThicknessTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>
    /// Gets or sets the distance at which transmitted white light reaches
    /// <see cref="VolumeAttenuationColor"/>. Positive infinity disables absorption.
    /// </summary>
    public float VolumeAttenuationDistance
    {
        get => volumeAttenuationDistance;
        set
        {
            if ((float.IsNaN(value) || value <= 0f) || (float.IsInfinity(value) && value < 0f))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            volumeAttenuationDistance = value;
        }
    }

    /// <summary>Gets or sets explicitly tagged linear-sRGB volume attenuation RGB.</summary>
    public LinearRgba VolumeAttenuationColor
    {
        get => volumeAttenuationColor;
        set
        {
            ValidateBaseColor(value);
            if (value.Red is < 0f or > 1f || value.Green is < 0f or > 1f || value.Blue is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Volume attenuation RGB must be between zero and one.");
            }
            volumeAttenuationColor = value;
        }
    }

    /// <summary>Gets or sets explicitly tagged linear-light sheen RGB; alpha is ignored.</summary>
    public LinearRgba SheenColor
    {
        get => sheenColor;
        set
        {
            ValidateBaseColor(value);
            if (value.Red is < 0f or > 1f || value.Green is < 0f or > 1f || value.Blue is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Sheen RGB must be between zero and one.");
            }
            sheenColor = value;
        }
    }

    /// <summary>Gets or sets perceptual sheen roughness in the inclusive zero-to-one range.</summary>
    public float SheenRoughness
    {
        get => sheenRoughness;
        set { ValidateUnitInterval(value, nameof(value)); sheenRoughness = value; }
    }

    /// <summary>Gets or sets an optional linear-light sheen color image decoded from sRGB input.</summary>
    public LinearRgbaImage? SheenColorTexture
    {
        get => sheenColorTexture;
        set
        {
            if (value?.ColorSpace is not null and not StandardRgbColorSpaceReference)
            {
                throw new ArgumentException(
                    "Sheen color textures must use a built-in RGB color-space reference.",
                    nameof(value));
            }
            if (value is not null && compressedSheenColorTexture is not null)
            {
                throw new InvalidOperationException("A material cannot use decoded and compressed sheen color textures together.");
            }
            sheenColorTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets an optional GPU-ready compressed sheen color image.</summary>
    public CompressedMaterialTexture? CompressedSheenColorTexture
    {
        get => compressedSheenColorTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Color, nameof(value));
            EnsureExclusive(value, sheenColorTexture, "sheen color");
            compressedSheenColorTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets non-color sheen roughness sampled from the alpha channel.</summary>
    public NormalizedRgbaDataImage? SheenRoughnessTexture
    {
        get => sheenRoughnessTexture;
        set { EnsureExclusive(value, compressedSheenRoughnessTexture, "sheen roughness"); sheenRoughnessTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets compressed non-color sheen roughness data.</summary>
    public CompressedMaterialTexture? CompressedSheenRoughnessTexture
    {
        get => compressedSheenRoughnessTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, sheenRoughnessTexture, "sheen roughness");
            compressedSheenRoughnessTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets sampler and UV mapping for sheen color.</summary>
    public MaterialTextureMapping SheenColorTextureMapping
    {
        get => sheenColorTextureMapping;
        set { sheenColorTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets sampler and UV mapping for sheen roughness.</summary>
    public MaterialTextureMapping SheenRoughnessTextureMapping
    {
        get => sheenRoughnessTextureMapping;
        set { sheenRoughnessTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets thin-film coverage in the inclusive zero-to-one range.</summary>
    public float IridescenceFactor
    {
        get => iridescenceFactor;
        set { ValidateUnitInterval(value, nameof(value)); iridescenceFactor = value; }
    }

    /// <summary>Gets or sets the thin-film index of refraction. The glTF default is 1.3.</summary>
    public float IridescenceIndexOfRefraction
    {
        get => iridescenceIndexOfRefraction;
        set
        {
            if (!float.IsFinite(value) || value < 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    "Iridescence index of refraction must be finite and at least one.");
            }
            iridescenceIndexOfRefraction = value;
        }
    }

    /// <summary>Gets or sets the non-negative minimum thin-film thickness in nanometres.</summary>
    public float IridescenceThicknessMinimum
    {
        get => iridescenceThicknessMinimum;
        set
        {
            if (!float.IsFinite(value) || value < 0f) throw new ArgumentOutOfRangeException(nameof(value));
            iridescenceThicknessMinimum = value;
        }
    }

    /// <summary>Gets or sets the non-negative maximum thin-film thickness in nanometres.</summary>
    public float IridescenceThicknessMaximum
    {
        get => iridescenceThicknessMaximum;
        set
        {
            if (!float.IsFinite(value) || value < 0f) throw new ArgumentOutOfRangeException(nameof(value));
            iridescenceThicknessMaximum = value;
        }
    }

    /// <summary>Gets or sets non-color iridescence coverage sampled from the red channel.</summary>
    public NormalizedRgbaDataImage? IridescenceTexture
    {
        get => iridescenceTexture;
        set { EnsureExclusive(value, compressedIridescenceTexture, "iridescence factor"); iridescenceTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets compressed non-color iridescence coverage data.</summary>
    public CompressedMaterialTexture? CompressedIridescenceTexture
    {
        get => compressedIridescenceTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, iridescenceTexture, "iridescence factor");
            compressedIridescenceTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets normalized film thickness sampled from the green channel.</summary>
    public NormalizedRgbaDataImage? IridescenceThicknessTexture
    {
        get => iridescenceThicknessTexture;
        set { EnsureExclusive(value, compressedIridescenceThicknessTexture, "iridescence thickness"); iridescenceThicknessTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets compressed non-color iridescence thickness data.</summary>
    public CompressedMaterialTexture? CompressedIridescenceThicknessTexture
    {
        get => compressedIridescenceThicknessTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, iridescenceThicknessTexture, "iridescence thickness");
            compressedIridescenceThicknessTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets sampler and UV mapping for iridescence coverage.</summary>
    public MaterialTextureMapping IridescenceTextureMapping
    {
        get => iridescenceTextureMapping;
        set { iridescenceTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets sampler and UV mapping for thin-film thickness.</summary>
    public MaterialTextureMapping IridescenceThicknessTextureMapping
    {
        get => iridescenceThicknessTextureMapping;
        set { iridescenceThicknessTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets dielectric specular strength in the inclusive zero-to-one range.</summary>
    public float SpecularFactor
    {
        get => specularFactor;
        set { ValidateUnitInterval(value, nameof(value)); specularFactor = value; }
    }

    /// <summary>
    /// Gets or sets the non-negative, explicitly tagged linear RGB multiplier applied to dielectric
    /// F0. Components may exceed one; the final F0 is clamped to one as required by glTF.
    /// </summary>
    public LinearRgba SpecularColor
    {
        get => specularColor;
        set
        {
            ValidateBaseColor(value);
            if (!float.IsFinite(value.Red) || !float.IsFinite(value.Green) || !float.IsFinite(value.Blue) ||
                value.Red < 0f || value.Green < 0f || value.Blue < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            specularColor = value;
        }
    }

    /// <summary>Gets or sets non-color specular strength sampled from the alpha channel.</summary>
    public NormalizedRgbaDataImage? SpecularTexture
    {
        get => specularTexture;
        set { EnsureExclusive(value, compressedSpecularTexture, "specular factor"); specularTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets the compressed non-color specular-strength texture.</summary>
    public CompressedMaterialTexture? CompressedSpecularTexture
    {
        get => compressedSpecularTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, specularTexture, "specular factor");
            compressedSpecularTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets a linear image decoded from the sRGB specular-color texture.</summary>
    public LinearRgbaImage? SpecularColorTexture
    {
        get => specularColorTexture;
        set { EnsureExclusive(value, compressedSpecularColorTexture, "specular color"); specularColorTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets the compressed sRGB specular-color texture.</summary>
    public CompressedMaterialTexture? CompressedSpecularColorTexture
    {
        get => compressedSpecularColorTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Color, nameof(value));
            EnsureExclusive(value, specularColorTexture, "specular color");
            compressedSpecularColorTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets sampler and UV mapping for specular strength.</summary>
    public MaterialTextureMapping SpecularTextureMapping
    {
        get => specularTextureMapping;
        set { specularTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets sampler and UV mapping for specular color.</summary>
    public MaterialTextureMapping SpecularColorTextureMapping
    {
        get => specularColorTextureMapping;
        set { specularColorTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets the fraction of non-specular light diffusely transmitted through the surface.</summary>
    public float DiffuseTransmissionFactor
    {
        get => diffuseTransmissionFactor;
        set { ValidateUnitInterval(value, nameof(value)); diffuseTransmissionFactor = value; }
    }

    /// <summary>Gets or sets the explicitly tagged linear color modulating diffuse transmission.</summary>
    public LinearRgba DiffuseTransmissionColor
    {
        get => diffuseTransmissionColor;
        set
        {
            ValidateBaseColor(value);
            if (!float.IsFinite(value.Red) || !float.IsFinite(value.Green) ||
                !float.IsFinite(value.Blue) || value.Red < 0f || value.Red > 1f ||
                value.Green < 0f || value.Green > 1f || value.Blue < 0f || value.Blue > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            diffuseTransmissionColor = value;
        }
    }

    /// <summary>Gets or sets non-color diffuse-transmission strength sampled from alpha.</summary>
    public NormalizedRgbaDataImage? DiffuseTransmissionTexture
    {
        get => diffuseTransmissionTexture;
        set { EnsureExclusive(value, compressedDiffuseTransmissionTexture, "diffuse transmission factor"); diffuseTransmissionTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets compressed non-color diffuse-transmission strength.</summary>
    public CompressedMaterialTexture? CompressedDiffuseTransmissionTexture
    {
        get => compressedDiffuseTransmissionTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Data, nameof(value));
            EnsureExclusive(value, diffuseTransmissionTexture, "diffuse transmission factor");
            compressedDiffuseTransmissionTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets a linear image decoded from the sRGB diffuse-transmission color texture.</summary>
    public LinearRgbaImage? DiffuseTransmissionColorTexture
    {
        get => diffuseTransmissionColorTexture;
        set { EnsureExclusive(value, compressedDiffuseTransmissionColorTexture, "diffuse transmission color"); diffuseTransmissionColorTexture = value; InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets the compressed sRGB diffuse-transmission color texture.</summary>
    public CompressedMaterialTexture? CompressedDiffuseTransmissionColorTexture
    {
        get => compressedDiffuseTransmissionColorTexture;
        set
        {
            ValidateCompressedContent(value, CompressedMaterialTextureContent.Color, nameof(value));
            EnsureExclusive(value, diffuseTransmissionColorTexture, "diffuse transmission color");
            compressedDiffuseTransmissionColorTexture = value;
            InvalidateTextureBindings();
        }
    }

    /// <summary>Gets or sets strength-texture sampling and UV mapping for diffuse transmission.</summary>
    public MaterialTextureMapping DiffuseTransmissionTextureMapping
    {
        get => diffuseTransmissionTextureMapping;
        set { diffuseTransmissionTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    /// <summary>Gets or sets color-texture sampling and UV mapping for diffuse transmission.</summary>
    public MaterialTextureMapping DiffuseTransmissionColorTextureMapping
    {
        get => diffuseTransmissionColorTextureMapping;
        set { diffuseTransmissionColorTextureMapping = ValidateMapping(value, nameof(value)); InvalidateTextureBindings(); }
    }

    private static void ValidateBaseColor(LinearRgba color)
    {
        if (color.ColorSpace is null)
        {
            throw new ArgumentException("The base color must carry a color-space identity.", nameof(color));
        }
    }

    private static MaterialTextureMapping ValidateMapping(
        MaterialTextureMapping value,
        string parameterName)
    {
        try
        {
            return new MaterialTextureMapping(
                value.Sampling,
                value.TextureCoordinateSet,
                value.Scale,
                value.Offset,
                value.Rotation);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("The texture mapping is invalid.", parameterName, exception);
        }
    }

    private static MaterialTextureMapping ReplaceSampling(
        MaterialTextureMapping mapping,
        MaterialTextureSampling sampling) => new(
            sampling,
            mapping.TextureCoordinateSet,
            mapping.Scale,
            mapping.Offset,
            mapping.Rotation);

    private static MaterialTextureMapping ReplaceScale(
        MaterialTextureMapping mapping,
        Vector2 scale) => new(
            mapping.Sampling,
            mapping.TextureCoordinateSet,
            scale,
            mapping.Offset,
            mapping.Rotation);

    private static MaterialTextureMapping ReplaceOffset(
        MaterialTextureMapping mapping,
        Vector2 offset) => new(
            mapping.Sampling,
            mapping.TextureCoordinateSet,
            mapping.Scale,
            offset,
            mapping.Rotation);

    private static void ValidateEmissiveColor(LinearRgba color)
    {
        if (color.ColorSpace is null)
        {
            throw new ArgumentException(
                "The emissive color must carry a color-space identity.",
                nameof(color));
        }
        if (color.Red < 0f || color.Green < 0f || color.Blue < 0f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(color),
                "Emissive RGB components must be non-negative.");
        }
    }

    private static void ValidateUnitInterval(float value, string parameterName)
    {
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The material factor must be between zero and one.");
        }
    }

    private static void ValidateCompressedContent(
        CompressedMaterialTexture? texture,
        CompressedMaterialTextureContent expected,
        string parameterName)
    {
        if (texture is not null && texture.Content != expected)
        {
            throw new ArgumentException(
                $"The compressed texture must contain {expected} content.",
                parameterName);
        }
    }

    private static void EnsureExclusive(object? value, object? alternative, string semantic)
    {
        if (value is not null && alternative is not null)
        {
            throw new InvalidOperationException(
                $"A material cannot use decoded and compressed {semantic} textures together.");
        }
    }

    private static void ValidateFinite(Vector2 value, string parameterName)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Texture coordinates must be finite.");
        }
    }

    internal PbrMaterial CloneForSceneInstance()
    {
        PbrMaterial clone = new(baseColor, metallic, roughness, Name)
        {
            shaderTemplate = shaderTemplate,
            emissiveColor = emissiveColor,
            emissiveStrength = emissiveStrength,
            indirectOcclusion = indirectOcclusion,
            baseColorTexture = baseColorTexture,
            compressedBaseColorTexture = compressedBaseColorTexture,
            emissiveTexture = emissiveTexture,
            compressedEmissiveTexture = compressedEmissiveTexture,
            normalTexture = normalTexture,
            compressedNormalTexture = compressedNormalTexture,
            occlusionRoughnessMetallicTexture = occlusionRoughnessMetallicTexture,
            compressedOcclusionRoughnessMetallicTexture = compressedOcclusionRoughnessMetallicTexture,
            normalScale = normalScale,
            clearcoatFactor = clearcoatFactor,
            clearcoatRoughness = clearcoatRoughness,
            clearcoatNormalScale = clearcoatNormalScale,
            anisotropyStrength = anisotropyStrength,
            anisotropyRotation = anisotropyRotation,
            anisotropyTexture = anisotropyTexture,
            compressedAnisotropyTexture = compressedAnisotropyTexture,
            transmissionFactor = transmissionFactor,
            indexOfRefraction = indexOfRefraction,
            dispersion = dispersion,
            volumeThicknessFactor = volumeThicknessFactor,
            volumeAttenuationDistance = volumeAttenuationDistance,
            volumeAttenuationColor = volumeAttenuationColor,
            transmissionTexture = transmissionTexture,
            compressedTransmissionTexture = compressedTransmissionTexture,
            volumeThicknessTexture = volumeThicknessTexture,
            compressedVolumeThicknessTexture = compressedVolumeThicknessTexture,
            sheenColor = sheenColor,
            sheenRoughness = sheenRoughness,
            sheenColorTexture = sheenColorTexture,
            compressedSheenColorTexture = compressedSheenColorTexture,
            sheenRoughnessTexture = sheenRoughnessTexture,
            compressedSheenRoughnessTexture = compressedSheenRoughnessTexture,
            iridescenceFactor = iridescenceFactor,
            iridescenceIndexOfRefraction = iridescenceIndexOfRefraction,
            iridescenceThicknessMinimum = iridescenceThicknessMinimum,
            iridescenceThicknessMaximum = iridescenceThicknessMaximum,
            iridescenceTexture = iridescenceTexture,
            compressedIridescenceTexture = compressedIridescenceTexture,
            iridescenceThicknessTexture = iridescenceThicknessTexture,
            compressedIridescenceThicknessTexture = compressedIridescenceThicknessTexture,
            specularFactor = specularFactor,
            specularColor = specularColor,
            specularTexture = specularTexture,
            compressedSpecularTexture = compressedSpecularTexture,
            specularColorTexture = specularColorTexture,
            compressedSpecularColorTexture = compressedSpecularColorTexture,
            diffuseTransmissionFactor = diffuseTransmissionFactor,
            diffuseTransmissionColor = diffuseTransmissionColor,
            diffuseTransmissionTexture = diffuseTransmissionTexture,
            compressedDiffuseTransmissionTexture = compressedDiffuseTransmissionTexture,
            diffuseTransmissionColorTexture = diffuseTransmissionColorTexture,
            compressedDiffuseTransmissionColorTexture = compressedDiffuseTransmissionColorTexture,
            clearcoatTexture = clearcoatTexture,
            compressedClearcoatTexture = compressedClearcoatTexture,
            clearcoatRoughnessTexture = clearcoatRoughnessTexture,
            compressedClearcoatRoughnessTexture = compressedClearcoatRoughnessTexture,
            clearcoatNormalTexture = clearcoatNormalTexture,
            compressedClearcoatNormalTexture = compressedClearcoatNormalTexture,
            baseColorTextureMapping = baseColorTextureMapping,
            emissiveTextureMapping = emissiveTextureMapping,
            normalTextureMapping = normalTextureMapping,
            ormTextureMapping = ormTextureMapping,
            clearcoatTextureMapping = clearcoatTextureMapping,
            clearcoatRoughnessTextureMapping = clearcoatRoughnessTextureMapping,
            clearcoatNormalTextureMapping = clearcoatNormalTextureMapping,
            anisotropyTextureMapping = anisotropyTextureMapping,
            transmissionTextureMapping = transmissionTextureMapping,
            volumeThicknessTextureMapping = volumeThicknessTextureMapping,
            sheenColorTextureMapping = sheenColorTextureMapping,
            sheenRoughnessTextureMapping = sheenRoughnessTextureMapping,
            iridescenceTextureMapping = iridescenceTextureMapping,
            iridescenceThicknessTextureMapping = iridescenceThicknessTextureMapping,
            specularTextureMapping = specularTextureMapping,
            specularColorTextureMapping = specularColorTextureMapping,
            diffuseTransmissionTextureMapping = diffuseTransmissionTextureMapping,
            diffuseTransmissionColorTextureMapping = diffuseTransmissionColorTextureMapping,
        };
        CopyInstanceStateTo(clone);
        return clone;
    }
}

/// <summary>
/// Compatibility name for <see cref="PbrMaterial"/>. New code should use <see cref="PbrMaterial"/>
/// to reflect that metallic/roughness is the base closure and clearcoat, sheen, transmission,
/// volume, anisotropy and emission are composable extension blocks.
/// </summary>
public sealed class MetallicRoughnessMaterial : PbrMaterial
{
    /// <summary>Initializes the compatibility metallic/roughness material name.</summary>
    public MetallicRoughnessMaterial(
        LinearRgba baseColor,
        float metallic = 0f,
        float roughness = 0.5f,
        string? name = null)
        : base(baseColor, metallic, roughness, name)
    {
    }
}

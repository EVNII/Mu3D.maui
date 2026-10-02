namespace Mu3D.SceneGraph;

/// <summary>Identifies optional physically based lighting blocks compiled for a material.</summary>
[Flags]
public enum PbrMaterialExtensions
{
    /// <summary>Uses only the metallic/roughness base closure.</summary>
    None = 0,

    /// <summary>Adds scene-linear emission.</summary>
    Emissive = 1 << 0,

    /// <summary>Adds the outer dielectric clearcoat layer.</summary>
    Clearcoat = 1 << 1,

    /// <summary>Adds anisotropic base-specular response.</summary>
    Anisotropy = 1 << 2,

    /// <summary>Adds dielectric surface transmission.</summary>
    Transmission = 1 << 3,

    /// <summary>Adds finite-volume thickness and absorption to transmission.</summary>
    Volume = 1 << 4,

    /// <summary>Adds the cloth/fiber sheen layer.</summary>
    Sheen = 1 << 5,

    /// <summary>Adds wavelength-dependent thin-film iridescence to the base specular interface.</summary>
    Iridescence = 1 << 6,

    /// <summary>Modulates dielectric specular strength and F0 color.</summary>
    Specular = 1 << 7,

    /// <summary>Adds diffuse transmission through a thin dielectric surface.</summary>
    DiffuseTransmission = 1 << 8,

    /// <summary>Adds wavelength-dependent dispersion to finite-volume transmission.</summary>
    Dispersion = 1 << 9,

    /// <summary>Enables every currently supported optional PBR block.</summary>
    All = Emissive | Clearcoat | Anisotropy | Transmission | Volume | Sheen | Iridescence |
        Specular | DiffuseTransmission | Dispersion,
}

/// <summary>Identifies optional sampled texture slots compiled for a PBR material.</summary>
[Flags]
public enum PbrMaterialTextureSlots
{
    /// <summary>Compiles no sampled material textures; authored scalar factors remain available.</summary>
    None = 0,

    /// <summary>Compiles the base-color texture slot.</summary>
    BaseColor = 1 << 0,

    /// <summary>Compiles the tangent-space base-normal texture slot.</summary>
    Normal = 1 << 1,

    /// <summary>Compiles the packed occlusion/roughness/metallic texture slot.</summary>
    OcclusionRoughnessMetallic = 1 << 2,

    /// <summary>Compiles the emissive color texture slot.</summary>
    Emissive = 1 << 3,

    /// <summary>Compiles clearcoat factor, roughness and independent-normal texture slots.</summary>
    Clearcoat = 1 << 4,

    /// <summary>Compiles the anisotropy direction/strength texture slot.</summary>
    Anisotropy = 1 << 5,

    /// <summary>Compiles transmission-factor and volume-thickness texture slots.</summary>
    TransmissionVolume = 1 << 6,

    /// <summary>Compiles sheen color and roughness texture slots.</summary>
    Sheen = 1 << 7,

    /// <summary>Compiles iridescence-factor and thin-film-thickness texture slots.</summary>
    Iridescence = 1 << 8,

    /// <summary>Compiles dielectric specular-strength and specular-color texture slots.</summary>
    Specular = 1 << 9,

    /// <summary>Compiles diffuse-transmission factor and color texture slots.</summary>
    DiffuseTransmission = 1 << 10,

    /// <summary>Enables every currently supported sampled material slot.</summary>
    All = BaseColor | Normal | OcclusionRoughnessMetallic | Emissive | Clearcoat |
        Anisotropy | TransmissionVolume | Sheen | Iridescence | Specular | DiffuseTransmission,
}

/// <summary>
/// Defines an immutable PBR shader-generation policy. Material values remain instance state; this
/// template only controls which optional closure blocks and sampled slots may enter a shader key.
/// </summary>
public readonly record struct MaterialShaderTemplate
{
    /// <summary>Initializes a PBR shader template.</summary>
    public MaterialShaderTemplate(
        PbrMaterialExtensions extensions,
        PbrMaterialTextureSlots textureSlots)
    {
        if ((extensions & ~PbrMaterialExtensions.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(extensions));
        }
        if ((textureSlots & ~PbrMaterialTextureSlots.All) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(textureSlots));
        }
        if ((extensions & PbrMaterialExtensions.Volume) != 0 &&
            (extensions & PbrMaterialExtensions.Transmission) == 0)
        {
            throw new ArgumentException("Volume requires the transmission extension.", nameof(extensions));
        }
        if ((extensions & PbrMaterialExtensions.Dispersion) != 0 &&
            (extensions & PbrMaterialExtensions.Volume) == 0)
        {
            throw new ArgumentException("Dispersion requires finite-volume transmission.", nameof(extensions));
        }
        Extensions = extensions;
        TextureSlots = textureSlots;
    }

    /// <summary>Gets the portable metallic/roughness base template.</summary>
    public static MaterialShaderTemplate BasePbr { get; } = new(
        PbrMaterialExtensions.None,
        PbrMaterialTextureSlots.BaseColor | PbrMaterialTextureSlots.Normal |
            PbrMaterialTextureSlots.OcclusionRoughnessMetallic);

    /// <summary>
    /// Gets the automatic full PBR authoring template. The renderer still removes blocks and slots
    /// whose factors are zero or whose texture source is absent.
    /// </summary>
    public static MaterialShaderTemplate FullPbr { get; } = new(
        PbrMaterialExtensions.All,
        PbrMaterialTextureSlots.All);

    /// <summary>Gets the optional lighting blocks permitted by this template.</summary>
    public PbrMaterialExtensions Extensions { get; }

    /// <summary>Gets the sampled material slots permitted by this template.</summary>
    public PbrMaterialTextureSlots TextureSlots { get; }

    /// <summary>Returns a copy with the requested extension blocks enabled.</summary>
    public MaterialShaderTemplate WithExtensions(PbrMaterialExtensions extensions)
    {
        PbrMaterialExtensions resolved = Extensions | extensions;
        if ((resolved & PbrMaterialExtensions.Volume) != 0)
        {
            resolved |= PbrMaterialExtensions.Transmission;
        }
        if ((resolved & PbrMaterialExtensions.Dispersion) != 0)
        {
            resolved |= PbrMaterialExtensions.Transmission | PbrMaterialExtensions.Volume;
        }
        return new(resolved, TextureSlots);
    }

    /// <summary>Returns a copy with the requested extension blocks disabled.</summary>
    public MaterialShaderTemplate WithoutExtensions(PbrMaterialExtensions extensions)
    {
        PbrMaterialExtensions resolved = Extensions & ~extensions;
        if ((extensions & PbrMaterialExtensions.Transmission) != 0)
        {
            resolved &= ~(PbrMaterialExtensions.Volume | PbrMaterialExtensions.Dispersion);
        }
        if ((extensions & PbrMaterialExtensions.Volume) != 0)
        {
            resolved &= ~PbrMaterialExtensions.Dispersion;
        }
        return new(resolved, TextureSlots);
    }

    /// <summary>Returns a copy with the requested sampled slots enabled.</summary>
    public MaterialShaderTemplate WithTextureSlots(PbrMaterialTextureSlots slots) =>
        new(Extensions, TextureSlots | slots);

    /// <summary>Returns a copy with the requested sampled slots disabled.</summary>
    public MaterialShaderTemplate WithoutTextureSlots(PbrMaterialTextureSlots slots) =>
        new(Extensions, TextureSlots & ~slots);

    /// <summary>Gets whether an optional lighting block is permitted.</summary>
    public bool Includes(PbrMaterialExtensions extension) => (Extensions & extension) == extension;

    /// <summary>Gets whether a sampled texture slot is permitted.</summary>
    public bool Includes(PbrMaterialTextureSlots slot) => (TextureSlots & slot) == slot;
}

namespace Mu3D.Rendering;

/// <summary>Identifies one stable render output independently of scene visibility and pass order.</summary>
/// <remarks>
/// Built-in identifiers are exposed by <see cref="RenderOutputIds"/>. Extensions should use a
/// stable namespaced identifier such as <c>com.example.outline</c> and register an explicit pass
/// factory with <see cref="RenderOutputRegistry"/>. No reflection or assembly scanning occurs.
/// </remarks>
public readonly record struct RenderOutputId
{
    private readonly string? value;

    /// <summary>Initializes a non-empty stable render-output identifier.</summary>
    /// <param name="value">The ordinal, case-sensitive identifier.</param>
    public RenderOutputId(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        this.value = value;
    }

    /// <summary>Gets the identifier text, or an empty string for an uninitialized value.</summary>
    public string Value => value ?? string.Empty;

    /// <summary>Gets whether this value was initialized with a non-empty identifier.</summary>
    public bool IsValid => value is not null;

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Provides stable identifiers for every output implemented by <see cref="SceneRenderer"/>.</summary>
public static class RenderOutputIds
{
    /// <summary>The final composed scene-linear image.</summary>
    public static RenderOutputId Beauty { get; } = new("mu3d.beauty");

    /// <summary>Screen-space ambient visibility.</summary>
    public static RenderOutputId AmbientOcclusion { get; } = new("mu3d.ambient-occlusion");

    /// <summary>Directional and punctual direct lighting.</summary>
    public static RenderOutputId DirectLighting { get; } = new("mu3d.direct-lighting");

    /// <summary>Diffuse and specular image-based lighting.</summary>
    public static RenderOutputId ImageBasedLighting { get; } = new("mu3d.image-based-lighting");

    /// <summary>Normalized camera-space distance.</summary>
    public static RenderOutputId ViewDepth { get; } = new("mu3d.view-depth");

    /// <summary>Directional-shadow visibility.</summary>
    public static RenderOutputId DirectionalShadow { get; } = new("mu3d.directional-shadow");

    /// <summary>The effective world-space bent normal.</summary>
    public static RenderOutputId BentNormal { get; } = new("mu3d.bent-normal");

    /// <summary>The working-space material base color.</summary>
    public static RenderOutputId BaseColor { get; } = new("mu3d.base-color");

    /// <summary>Repeated material texture coordinates.</summary>
    public static RenderOutputId TextureCoordinates { get; } = new("mu3d.texture-coordinates");

    /// <summary>The final world-space shading normal.</summary>
    public static RenderOutputId SurfaceNormal { get; } = new("mu3d.surface-normal");

    /// <summary>Packed occlusion, roughness, and metallic channels.</summary>
    public static RenderOutputId OcclusionRoughnessMetallic { get; } =
        new("mu3d.occlusion-roughness-metallic");

    /// <summary>The material emissive contribution.</summary>
    public static RenderOutputId Emissive { get; } = new("mu3d.emissive");

    /// <summary>The direct and approximate image-based sheen lobe.</summary>
    public static RenderOutputId Sheen { get; } = new("mu3d.sheen");

    /// <summary>The screen-space refracted background.</summary>
    public static RenderOutputId Transmission { get; } = new("mu3d.transmission");

    /// <summary>The finite-volume attenuation multiplier.</summary>
    public static RenderOutputId VolumeAttenuation { get; } = new("mu3d.volume-attenuation");

    /// <summary>The direct and image-based PBR specular lobe.</summary>
    public static RenderOutputId Specular { get; } = new("mu3d.specular");

    /// <summary>The direct and image-based diffuse-transmission lobe.</summary>
    public static RenderOutputId DiffuseTransmission { get; } =
        new("mu3d.diffuse-transmission");

    /// <summary>Converts a legacy built-in render layer to its stable output identifier.</summary>
    /// <param name="layer">The built-in scene render layer.</param>
    /// <returns>The corresponding stable output identifier.</returns>
    public static RenderOutputId FromSceneRenderLayer(SceneRenderLayer layer) => layer switch
    {
        SceneRenderLayer.Beauty => Beauty,
        SceneRenderLayer.AmbientOcclusion => AmbientOcclusion,
        SceneRenderLayer.DirectLighting => DirectLighting,
        SceneRenderLayer.ImageBasedLighting => ImageBasedLighting,
        SceneRenderLayer.ViewDepth => ViewDepth,
        SceneRenderLayer.DirectionalShadow => DirectionalShadow,
        SceneRenderLayer.BentNormal => BentNormal,
        SceneRenderLayer.BaseColor => BaseColor,
        SceneRenderLayer.TextureCoordinates => TextureCoordinates,
        SceneRenderLayer.SurfaceNormal => SurfaceNormal,
        SceneRenderLayer.OcclusionRoughnessMetallic => OcclusionRoughnessMetallic,
        SceneRenderLayer.Emissive => Emissive,
        SceneRenderLayer.Sheen => Sheen,
        SceneRenderLayer.Transmission => Transmission,
        SceneRenderLayer.VolumeAttenuation => VolumeAttenuation,
        SceneRenderLayer.Specular => Specular,
        SceneRenderLayer.DiffuseTransmission => DiffuseTransmission,
        _ => throw new ArgumentOutOfRangeException(nameof(layer)),
    };

    /// <summary>Tries to resolve a stable identifier implemented by the built-in scene renderer.</summary>
    /// <param name="output">The stable render-output identifier.</param>
    /// <param name="layer">The corresponding built-in layer when the method returns true.</param>
    /// <returns>True when the built-in scene renderer implements the identifier.</returns>
    public static bool TryGetSceneRenderLayer(RenderOutputId output, out SceneRenderLayer layer)
    {
        if (output == Beauty) layer = SceneRenderLayer.Beauty;
        else if (output == AmbientOcclusion) layer = SceneRenderLayer.AmbientOcclusion;
        else if (output == DirectLighting) layer = SceneRenderLayer.DirectLighting;
        else if (output == ImageBasedLighting) layer = SceneRenderLayer.ImageBasedLighting;
        else if (output == ViewDepth) layer = SceneRenderLayer.ViewDepth;
        else if (output == DirectionalShadow) layer = SceneRenderLayer.DirectionalShadow;
        else if (output == BentNormal) layer = SceneRenderLayer.BentNormal;
        else if (output == BaseColor) layer = SceneRenderLayer.BaseColor;
        else if (output == TextureCoordinates) layer = SceneRenderLayer.TextureCoordinates;
        else if (output == SurfaceNormal) layer = SceneRenderLayer.SurfaceNormal;
        else if (output == OcclusionRoughnessMetallic)
            layer = SceneRenderLayer.OcclusionRoughnessMetallic;
        else if (output == Emissive) layer = SceneRenderLayer.Emissive;
        else if (output == Sheen) layer = SceneRenderLayer.Sheen;
        else if (output == Transmission) layer = SceneRenderLayer.Transmission;
        else if (output == VolumeAttenuation) layer = SceneRenderLayer.VolumeAttenuation;
        else if (output == Specular) layer = SceneRenderLayer.Specular;
        else if (output == DiffuseTransmission) layer = SceneRenderLayer.DiffuseTransmission;
        else
        {
            layer = default;
            return false;
        }

        return true;
    }
}

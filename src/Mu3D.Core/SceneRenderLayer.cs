namespace Mu3D.Rendering;

/// <summary>Selects the lighting contribution visualized by <see cref="SceneRenderer"/>.</summary>
public enum SceneRenderLayer
{
    /// <summary>Composes direct lighting, indirect diffuse, indirect specular, and emissive output.</summary>
    Beauty,

    /// <summary>Displays screen-space ambient visibility as grayscale.</summary>
    AmbientOcclusion,

    /// <summary>Displays directional direct lighting only.</summary>
    DirectLighting,

    /// <summary>Displays diffuse and specular image-based lighting without direct lighting.</summary>
    ImageBasedLighting,

    /// <summary>Displays normalized camera distance as grayscale.</summary>
    ViewDepth,

    /// <summary>Displays directional-shadow visibility as grayscale.</summary>
    DirectionalShadow,

    /// <summary>Displays the effective world-space bent normal after normal-map detail.</summary>
    BentNormal,

    /// <summary>Displays the working-space base color after material-image multiplication.</summary>
    BaseColor,

    /// <summary>Displays repeated U and V coordinates as red and green.</summary>
    TextureCoordinates,

    /// <summary>Displays the final world-space shading normal after normal mapping.</summary>
    SurfaceNormal,

    /// <summary>Displays packed occlusion, perceptual roughness and metallic data as RGB.</summary>
    OcclusionRoughnessMetallic,

    /// <summary>Displays the working-space emissive texture multiplied by the emissive factor.</summary>
    Emissive,

    /// <summary>Displays the direct and approximate image-based sheen lobe without base or clearcoat lighting.</summary>
    Sheen,

    /// <summary>Displays the screen-space refracted background after finite-volume attenuation.</summary>
    Transmission,

    /// <summary>Displays the finite-volume Beer-Lambert transmittance multiplier as linear RGB.</summary>
    VolumeAttenuation,

    /// <summary>Displays the base PBR direct and image-based specular lobe without diffuse, sheen, or clearcoat.</summary>
    Specular,

    /// <summary>Displays only direct and image-based diffuse transmission.</summary>
    DiffuseTransmission,
}

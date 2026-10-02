namespace Mu3D.SceneGraph;

/// <summary>Identifies each connectable input of the pinned OpenPBR Surface definition.</summary>
public enum OpenPbrInput
{
    /// <summary>The <c>base_weight</c> input.</summary>
    BaseWeight,
    /// <summary>The <c>base_color</c> input.</summary>
    BaseColor,
    /// <summary>The <c>base_diffuse_roughness</c> input.</summary>
    BaseDiffuseRoughness,
    /// <summary>The <c>base_metalness</c> input.</summary>
    BaseMetalness,
    /// <summary>The <c>specular_weight</c> input.</summary>
    SpecularWeight,
    /// <summary>The <c>specular_color</c> input.</summary>
    SpecularColor,
    /// <summary>The <c>specular_roughness</c> input.</summary>
    SpecularRoughness,
    /// <summary>The <c>specular_ior</c> input.</summary>
    SpecularIor,
    /// <summary>The <c>specular_roughness_anisotropy</c> input.</summary>
    SpecularRoughnessAnisotropy,
    /// <summary>The <c>transmission_weight</c> input.</summary>
    TransmissionWeight,
    /// <summary>The <c>transmission_color</c> input.</summary>
    TransmissionColor,
    /// <summary>The <c>transmission_depth</c> input.</summary>
    TransmissionDepth,
    /// <summary>The <c>transmission_scatter</c> input.</summary>
    TransmissionScatter,
    /// <summary>The <c>transmission_scatter_anisotropy</c> input.</summary>
    TransmissionScatterAnisotropy,
    /// <summary>The <c>transmission_dispersion_scale</c> input.</summary>
    TransmissionDispersionScale,
    /// <summary>The <c>transmission_dispersion_abbe_number</c> input.</summary>
    TransmissionDispersionAbbeNumber,
    /// <summary>The <c>subsurface_weight</c> input.</summary>
    SubsurfaceWeight,
    /// <summary>The <c>subsurface_color</c> input.</summary>
    SubsurfaceColor,
    /// <summary>The <c>subsurface_radius</c> input.</summary>
    SubsurfaceRadius,
    /// <summary>The <c>subsurface_radius_scale</c> input.</summary>
    SubsurfaceRadiusScale,
    /// <summary>The <c>subsurface_scatter_anisotropy</c> input.</summary>
    SubsurfaceScatterAnisotropy,
    /// <summary>The <c>fuzz_weight</c> input.</summary>
    FuzzWeight,
    /// <summary>The <c>fuzz_color</c> input.</summary>
    FuzzColor,
    /// <summary>The <c>fuzz_roughness</c> input.</summary>
    FuzzRoughness,
    /// <summary>The <c>coat_weight</c> input.</summary>
    CoatWeight,
    /// <summary>The <c>coat_color</c> input.</summary>
    CoatColor,
    /// <summary>The <c>coat_roughness</c> input.</summary>
    CoatRoughness,
    /// <summary>The <c>coat_roughness_anisotropy</c> input.</summary>
    CoatRoughnessAnisotropy,
    /// <summary>The <c>coat_ior</c> input.</summary>
    CoatIor,
    /// <summary>The <c>coat_darkening</c> input.</summary>
    CoatDarkening,
    /// <summary>The <c>thin_film_weight</c> input.</summary>
    ThinFilmWeight,
    /// <summary>The <c>thin_film_thickness</c> input.</summary>
    ThinFilmThickness,
    /// <summary>The <c>thin_film_ior</c> input.</summary>
    ThinFilmIor,
    /// <summary>The <c>emission_luminance</c> input.</summary>
    EmissionLuminance,
    /// <summary>The <c>emission_color</c> input.</summary>
    EmissionColor,
    /// <summary>The <c>geometry_opacity</c> input.</summary>
    GeometryOpacity,
    /// <summary>The <c>geometry_thin_walled</c> input.</summary>
    GeometryThinWalled,
    /// <summary>The <c>geometry_normal</c> input.</summary>
    GeometryNormal,
    /// <summary>The <c>geometry_coat_normal</c> input.</summary>
    GeometryCoatNormal,
    /// <summary>The <c>geometry_tangent</c> input.</summary>
    GeometryTangent,
    /// <summary>The <c>geometry_coat_tangent</c> input.</summary>
    GeometryCoatTangent,
}

/// <summary>Provides stable parameter names, types and physical rendering bounds.</summary>
public static class OpenPbrInputInfo
{
    /// <summary>Returns the MaterialX name, graph type and inclusive component bounds.</summary>
    /// <remarks>Colors use nonnegative ACEScg reflectance; emission permits HDR values.
    /// Subsurface radius scale is channel data, not a color transform.</remarks>
    public static (string Name, OpenPbrNodeType Type, float Minimum, float Maximum) Get(OpenPbrInput input) => input switch
    {
        OpenPbrInput.BaseWeight => ("base_weight", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.BaseColor => ("base_color", OpenPbrNodeType.Color3, 0, 1),
        OpenPbrInput.BaseDiffuseRoughness => ("base_diffuse_roughness", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.BaseMetalness => ("base_metalness", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.SpecularWeight => ("specular_weight", OpenPbrNodeType.Float, 0, float.MaxValue),
        OpenPbrInput.SpecularColor => ("specular_color", OpenPbrNodeType.Color3, 0, 1),
        OpenPbrInput.SpecularRoughness => ("specular_roughness", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.SpecularIor => ("specular_ior", OpenPbrNodeType.Float, 0, float.MaxValue),
        OpenPbrInput.SpecularRoughnessAnisotropy => ("specular_roughness_anisotropy", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.TransmissionWeight => ("transmission_weight", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.TransmissionColor => ("transmission_color", OpenPbrNodeType.Color3, 0, 1),
        OpenPbrInput.TransmissionDepth => ("transmission_depth", OpenPbrNodeType.Float, 0, float.MaxValue),
        OpenPbrInput.TransmissionScatter => ("transmission_scatter", OpenPbrNodeType.Color3, 0, 1),
        OpenPbrInput.TransmissionScatterAnisotropy => ("transmission_scatter_anisotropy", OpenPbrNodeType.Float, -1, 1),
        OpenPbrInput.TransmissionDispersionScale => ("transmission_dispersion_scale", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.TransmissionDispersionAbbeNumber => ("transmission_dispersion_abbe_number", OpenPbrNodeType.Float, 0, float.MaxValue),
        OpenPbrInput.SubsurfaceWeight => ("subsurface_weight", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.SubsurfaceColor => ("subsurface_color", OpenPbrNodeType.Color3, 0, 1),
        OpenPbrInput.SubsurfaceRadius => ("subsurface_radius", OpenPbrNodeType.Float, 0, float.MaxValue),
        OpenPbrInput.SubsurfaceRadiusScale => ("subsurface_radius_scale", OpenPbrNodeType.Vector3, 0, float.MaxValue),
        OpenPbrInput.SubsurfaceScatterAnisotropy => ("subsurface_scatter_anisotropy", OpenPbrNodeType.Float, -1, 1),
        OpenPbrInput.FuzzWeight => ("fuzz_weight", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.FuzzColor => ("fuzz_color", OpenPbrNodeType.Color3, 0, 1),
        OpenPbrInput.FuzzRoughness => ("fuzz_roughness", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.CoatWeight => ("coat_weight", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.CoatColor => ("coat_color", OpenPbrNodeType.Color3, 0, 1),
        OpenPbrInput.CoatRoughness => ("coat_roughness", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.CoatRoughnessAnisotropy => ("coat_roughness_anisotropy", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.CoatIor => ("coat_ior", OpenPbrNodeType.Float, 0, float.MaxValue),
        OpenPbrInput.CoatDarkening => ("coat_darkening", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.ThinFilmWeight => ("thin_film_weight", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.ThinFilmThickness => ("thin_film_thickness", OpenPbrNodeType.Float, 0, float.MaxValue),
        OpenPbrInput.ThinFilmIor => ("thin_film_ior", OpenPbrNodeType.Float, 0, float.MaxValue),
        OpenPbrInput.EmissionLuminance => ("emission_luminance", OpenPbrNodeType.Float, 0, float.MaxValue),
        OpenPbrInput.EmissionColor => ("emission_color", OpenPbrNodeType.Color3, 0, float.MaxValue),
        OpenPbrInput.GeometryOpacity => ("geometry_opacity", OpenPbrNodeType.Float, 0, 1),
        OpenPbrInput.GeometryThinWalled => ("geometry_thin_walled", OpenPbrNodeType.Boolean, 0, 1),
        OpenPbrInput.GeometryNormal => ("geometry_normal", OpenPbrNodeType.Vector3, -float.MaxValue, float.MaxValue),
        OpenPbrInput.GeometryCoatNormal => ("geometry_coat_normal", OpenPbrNodeType.Vector3, -float.MaxValue, float.MaxValue),
        OpenPbrInput.GeometryTangent => ("geometry_tangent", OpenPbrNodeType.Vector3, -float.MaxValue, float.MaxValue),
        OpenPbrInput.GeometryCoatTangent => ("geometry_coat_tangent", OpenPbrNodeType.Vector3, -float.MaxValue, float.MaxValue),
        _ => throw new ArgumentOutOfRangeException(nameof(input)),
    };
}

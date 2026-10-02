using System.Numerics;
using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>
/// Stores constant and connected authoring inputs for OpenPBR Surface 1.1.1 independently of the renderer.
/// This is not a <see cref="Material"/> and does not assert OpenPBR scattering conformance.
/// </summary>
/// <remarks>
/// Defaults and parameter identities follow the pinned OpenPBR v1.1.1 reference nodedef.
/// Color3 values carry linear-light metadata and alpha one; signed and extended-range authoring
/// values are retained without implicit gamut mapping. Numeric bounds follow the nodedef's hard
/// scalar bounds, not its soft UI bounds. Geometry vectors default to inherited world-space
/// normal/tangent semantics. Graph connections are immutable and override the associated constants.
/// </remarks>
public sealed class OpenPbrSurface
{
    /// <summary>Gets or sets immutable texture and expression connections overriding constant inputs.</summary>
    public OpenPbrGraph? Graph { get; set; }

    /// <summary>Identifies the pinned normative OpenPBR specification and reference nodedef.</summary>
    public const string SpecificationVersion = "1.1.1";

    /// <summary>Gets or sets the optional application-facing material name.</summary>
    public string? Name { get; set; }

    private float metersPerUnit = 1f;

    /// <summary>
    /// Gets or sets the positive number of metres represented by one scene length unit.
    /// The explicit default convention is metres. Thin-film thickness always uses micrometres.
    /// </summary>
    public float MetersPerUnit
    {
        get => metersPerUnit;
        set => metersPerUnit = value > 0f ? Nonnegative(value, nameof(MetersPerUnit))
            : throw new ArgumentOutOfRangeException(nameof(value));
    }

    private float baseWeight = 1f;

    /// <summary>Gets or sets <c>base_weight</c>. Base reflection multiplier, in [0, 1]. Default: 1.</summary>
    public float BaseWeight
    {
        get => baseWeight;
        set => baseWeight = Unit(value, nameof(BaseWeight));
    }

    private float baseDiffuseRoughness = 0f;

    /// <summary>Gets or sets <c>base_diffuse_roughness</c>. Diffuse lobe roughness, in [0, 1]. Default: 0.</summary>
    public float BaseDiffuseRoughness
    {
        get => baseDiffuseRoughness;
        set => baseDiffuseRoughness = Unit(value, nameof(BaseDiffuseRoughness));
    }

    private float baseMetalness = 0f;

    /// <summary>Gets or sets <c>base_metalness</c>. Dielectric-to-metal mixture, in [0, 1]. Default: 0.</summary>
    public float BaseMetalness
    {
        get => baseMetalness;
        set => baseMetalness = Unit(value, nameof(BaseMetalness));
    }

    private float specularWeight = 1f;

    /// <summary>Gets or sets <c>specular_weight</c>. Nonnegative specular multiplier; values above one are authoring overrides. Default: 1.</summary>
    public float SpecularWeight
    {
        get => specularWeight;
        set => specularWeight = Nonnegative(value, nameof(SpecularWeight));
    }

    private float specularRoughness = 0.3f;

    /// <summary>Gets or sets <c>specular_roughness</c>. Specular lobe roughness, in [0, 1]. Default: 0.3.</summary>
    public float SpecularRoughness
    {
        get => specularRoughness;
        set => specularRoughness = Unit(value, nameof(SpecularRoughness));
    }

    private float specularIor = 1.5f;

    /// <summary>Gets or sets <c>specular_ior</c>. Nonnegative dielectric index of refraction; preview may require a narrower domain. Default: 1.5.</summary>
    public float SpecularIor
    {
        get => specularIor;
        set => specularIor = Nonnegative(value, nameof(SpecularIor));
    }

    private float specularRoughnessAnisotropy = 0f;

    /// <summary>Gets or sets <c>specular_roughness_anisotropy</c>. Base roughness anisotropy, in [0, 1]. Default: 0.</summary>
    public float SpecularRoughnessAnisotropy
    {
        get => specularRoughnessAnisotropy;
        set => specularRoughnessAnisotropy = Unit(value, nameof(SpecularRoughnessAnisotropy));
    }

    private float transmissionWeight = 0f;

    /// <summary>Gets or sets <c>transmission_weight</c>. Transparent dielectric mixture weight, in [0, 1]. Default: 0.</summary>
    public float TransmissionWeight
    {
        get => transmissionWeight;
        set => transmissionWeight = Unit(value, nameof(TransmissionWeight));
    }

    private float transmissionDepth = 0f;

    /// <summary>Gets or sets <c>transmission_depth</c>. Absorption reference distance in scene units; zero selects surface tint semantics. Default: 0.</summary>
    public float TransmissionDepth
    {
        get => transmissionDepth;
        set => transmissionDepth = Nonnegative(value, nameof(TransmissionDepth));
    }

    private float transmissionScatterAnisotropy = 0f;

    /// <summary>Gets or sets <c>transmission_scatter_anisotropy</c>. Scattering directional bias, in [-1, 1]. Default: 0.</summary>
    public float TransmissionScatterAnisotropy
    {
        get => transmissionScatterAnisotropy;
        set => transmissionScatterAnisotropy = SignedUnit(value, nameof(TransmissionScatterAnisotropy));
    }

    private float transmissionDispersionScale = 0f;

    /// <summary>Gets or sets <c>transmission_dispersion_scale</c>. Dispersion multiplier, in [0, 1]. Default: 0.</summary>
    public float TransmissionDispersionScale
    {
        get => transmissionDispersionScale;
        set => transmissionDispersionScale = Unit(value, nameof(TransmissionDispersionScale));
    }

    private float transmissionDispersionAbbeNumber = 20f;

    /// <summary>Gets or sets <c>transmission_dispersion_abbe_number</c>. Nonnegative Abbe number of the dielectric medium. Default: 20.</summary>
    public float TransmissionDispersionAbbeNumber
    {
        get => transmissionDispersionAbbeNumber;
        set => transmissionDispersionAbbeNumber = Nonnegative(value, nameof(TransmissionDispersionAbbeNumber));
    }

    private float subsurfaceWeight = 0f;

    /// <summary>Gets or sets <c>subsurface_weight</c>. Subsurface mixture weight, in [0, 1]. Default: 0.</summary>
    public float SubsurfaceWeight
    {
        get => subsurfaceWeight;
        set => subsurfaceWeight = Unit(value, nameof(SubsurfaceWeight));
    }

    private float subsurfaceRadius = 1f;

    /// <summary>Gets or sets <c>subsurface_radius</c>. Subsurface mean-free-path scale in scene units. Default: 1.</summary>
    public float SubsurfaceRadius
    {
        get => subsurfaceRadius;
        set => subsurfaceRadius = Nonnegative(value, nameof(SubsurfaceRadius));
    }

    private float subsurfaceScatterAnisotropy = 0f;

    /// <summary>Gets or sets <c>subsurface_scatter_anisotropy</c>. Subsurface phase-function bias, in [-1, 1]. Default: 0.</summary>
    public float SubsurfaceScatterAnisotropy
    {
        get => subsurfaceScatterAnisotropy;
        set => subsurfaceScatterAnisotropy = SignedUnit(value, nameof(SubsurfaceScatterAnisotropy));
    }

    private float fuzzWeight = 0f;

    /// <summary>Gets or sets <c>fuzz_weight</c>. Fuzz layer coverage, in [0, 1]. Default: 0.</summary>
    public float FuzzWeight
    {
        get => fuzzWeight;
        set => fuzzWeight = Unit(value, nameof(FuzzWeight));
    }

    private float fuzzRoughness = 0.5f;

    /// <summary>Gets or sets <c>fuzz_roughness</c>. Fuzz lobe roughness, in [0, 1]. Default: 0.5.</summary>
    public float FuzzRoughness
    {
        get => fuzzRoughness;
        set => fuzzRoughness = Unit(value, nameof(FuzzRoughness));
    }

    private float coatWeight = 0f;

    /// <summary>Gets or sets <c>coat_weight</c>. Coat layer coverage, in [0, 1]. Default: 0.</summary>
    public float CoatWeight
    {
        get => coatWeight;
        set => coatWeight = Unit(value, nameof(CoatWeight));
    }

    private float coatRoughness = 0f;

    /// <summary>Gets or sets <c>coat_roughness</c>. Coat lobe roughness, in [0, 1]. Default: 0.</summary>
    public float CoatRoughness
    {
        get => coatRoughness;
        set => coatRoughness = Unit(value, nameof(CoatRoughness));
    }

    private float coatRoughnessAnisotropy = 0f;

    /// <summary>Gets or sets <c>coat_roughness_anisotropy</c>. Coat roughness anisotropy, in [0, 1]. Default: 0.</summary>
    public float CoatRoughnessAnisotropy
    {
        get => coatRoughnessAnisotropy;
        set => coatRoughnessAnisotropy = Unit(value, nameof(CoatRoughnessAnisotropy));
    }

    private float coatIor = 1.6f;

    /// <summary>Gets or sets <c>coat_ior</c>. Nonnegative coat index of refraction. Default: 1.6.</summary>
    public float CoatIor
    {
        get => coatIor;
        set => coatIor = Nonnegative(value, nameof(CoatIor));
    }

    private float coatDarkening = 1f;

    /// <summary>Gets or sets <c>coat_darkening</c>. Coat-induced substrate darkening strength, in [0, 1]. Default: 1.</summary>
    public float CoatDarkening
    {
        get => coatDarkening;
        set => coatDarkening = Unit(value, nameof(CoatDarkening));
    }

    private float thinFilmWeight = 0f;

    /// <summary>Gets or sets <c>thin_film_weight</c>. Thin-film coverage, in [0, 1]. Default: 0.</summary>
    public float ThinFilmWeight
    {
        get => thinFilmWeight;
        set => thinFilmWeight = Unit(value, nameof(ThinFilmWeight));
    }

    private float thinFilmThickness = 0.5f;

    /// <summary>Gets or sets <c>thin_film_thickness</c>. Thin-film thickness in micrometres, independent of scene units. Default: 0.5.</summary>
    public float ThinFilmThickness
    {
        get => thinFilmThickness;
        set => thinFilmThickness = Nonnegative(value, nameof(ThinFilmThickness));
    }

    private float thinFilmIor = 1.4f;

    /// <summary>Gets or sets <c>thin_film_ior</c>. Nonnegative thin-film index of refraction. Default: 1.4.</summary>
    public float ThinFilmIor
    {
        get => thinFilmIor;
        set => thinFilmIor = Nonnegative(value, nameof(ThinFilmIor));
    }

    private float emissionLuminance = 0f;

    /// <summary>Gets or sets <c>emission_luminance</c>. White emission luminance in cd/m² (nits); no SDR upper bound is imposed. Default: 0.</summary>
    public float EmissionLuminance
    {
        get => emissionLuminance;
        set => emissionLuminance = Nonnegative(value, nameof(EmissionLuminance));
    }

    private float geometryOpacity = 1f;

    /// <summary>Gets or sets <c>geometry_opacity</c>. Surface presence weight, in [0, 1]. Default: 1.</summary>
    public float GeometryOpacity
    {
        get => geometryOpacity;
        set => geometryOpacity = Unit(value, nameof(GeometryOpacity));
    }

    private LinearRgba baseColor = new(0.8f, 0.8f, 0.8f, 1f, StandardColorSpaces.AcesCg);

    /// <summary>
    /// Gets or sets <c>base_color</c> with explicit linear RGB identity and alpha one.
    /// The default is (0.8, 0.8, 0.8) in ACEScg; signed and HDR components are preserved.
    /// </summary>
    public LinearRgba BaseColor
    {
        get => baseColor;
        set => baseColor = Color(value, nameof(BaseColor));
    }

    private LinearRgba specularColor = new(1f, 1f, 1f, 1f, StandardColorSpaces.AcesCg);

    /// <summary>
    /// Gets or sets <c>specular_color</c> with explicit linear RGB identity and alpha one.
    /// The default is (1, 1, 1) in ACEScg; signed and HDR components are preserved.
    /// </summary>
    public LinearRgba SpecularColor
    {
        get => specularColor;
        set => specularColor = Color(value, nameof(SpecularColor));
    }

    private LinearRgba transmissionColor = new(1f, 1f, 1f, 1f, StandardColorSpaces.AcesCg);

    /// <summary>
    /// Gets or sets <c>transmission_color</c> with explicit linear RGB identity and alpha one.
    /// The default is (1, 1, 1) in ACEScg; signed and HDR components are preserved.
    /// </summary>
    public LinearRgba TransmissionColor
    {
        get => transmissionColor;
        set => transmissionColor = Color(value, nameof(TransmissionColor));
    }

    private LinearRgba transmissionScatter = new(0f, 0f, 0f, 1f, StandardColorSpaces.AcesCg);

    /// <summary>
    /// Gets or sets <c>transmission_scatter</c> with explicit linear RGB identity and alpha one.
    /// The default is (0, 0, 0) in ACEScg; signed and HDR components are preserved.
    /// </summary>
    public LinearRgba TransmissionScatter
    {
        get => transmissionScatter;
        set => transmissionScatter = Color(value, nameof(TransmissionScatter));
    }

    private LinearRgba subsurfaceColor = new(0.8f, 0.8f, 0.8f, 1f, StandardColorSpaces.AcesCg);

    /// <summary>
    /// Gets or sets <c>subsurface_color</c> with explicit linear RGB identity and alpha one.
    /// The default is (0.8, 0.8, 0.8) in ACEScg; signed and HDR components are preserved.
    /// </summary>
    public LinearRgba SubsurfaceColor
    {
        get => subsurfaceColor;
        set => subsurfaceColor = Color(value, nameof(SubsurfaceColor));
    }

    private LinearRgba fuzzColor = new(1f, 1f, 1f, 1f, StandardColorSpaces.AcesCg);

    /// <summary>
    /// Gets or sets <c>fuzz_color</c> with explicit linear RGB identity and alpha one.
    /// The default is (1, 1, 1) in ACEScg; signed and HDR components are preserved.
    /// </summary>
    public LinearRgba FuzzColor
    {
        get => fuzzColor;
        set => fuzzColor = Color(value, nameof(FuzzColor));
    }

    private LinearRgba coatColor = new(1f, 1f, 1f, 1f, StandardColorSpaces.AcesCg);

    /// <summary>
    /// Gets or sets <c>coat_color</c> with explicit linear RGB identity and alpha one.
    /// The default is (1, 1, 1) in ACEScg; signed and HDR components are preserved.
    /// </summary>
    public LinearRgba CoatColor
    {
        get => coatColor;
        set => coatColor = Color(value, nameof(CoatColor));
    }

    private LinearRgba emissionColor = new(1f, 1f, 1f, 1f, StandardColorSpaces.AcesCg);

    /// <summary>
    /// Gets or sets <c>emission_color</c> with explicit linear RGB identity and alpha one.
    /// The default is (1, 1, 1) in ACEScg; signed and HDR components are preserved.
    /// </summary>
    public LinearRgba EmissionColor
    {
        get => emissionColor;
        set => emissionColor = Color(value, nameof(EmissionColor));
    }

    private Vector3 subsurfaceRadiusScale = new(1f, 0.5f, 0.25f);

    /// <summary>
    /// Gets or sets <c>subsurface_radius_scale</c>, nonnegative per-channel length multipliers.
    /// These are numeric RGB-channel data, not a color to transform. Default: (1, 0.5, 0.25).
    /// </summary>
    public Vector3 SubsurfaceRadiusScale
    {
        get => subsurfaceRadiusScale;
        set
        {
            Nonnegative(value.X, nameof(SubsurfaceRadiusScale));
            Nonnegative(value.Y, nameof(SubsurfaceRadiusScale));
            Nonnegative(value.Z, nameof(SubsurfaceRadiusScale));
            subsurfaceRadiusScale = value;
        }
    }

    /// <summary>Gets or sets <c>geometry_thin_walled</c>. The default is false.</summary>
    public bool GeometryThinWalled { get; set; }

    private Vector3? geometryNormal;

    /// <summary>
    /// Gets or sets the finite nonzero world-space <c>geometry_normal</c> override.
    /// Null inherits the geometry's <c>Nworld</c>; explicit vectors are preserved without normalization.
    /// </summary>
    public Vector3? GeometryNormal
    {
        get => geometryNormal;
        set => geometryNormal = Direction(value, nameof(GeometryNormal));
    }

    private Vector3? geometryCoatNormal;

    /// <summary>
    /// Gets or sets the finite nonzero world-space <c>geometry_coat_normal</c> override.
    /// Null inherits the geometry's <c>Nworld</c>; explicit vectors are preserved without normalization.
    /// </summary>
    public Vector3? GeometryCoatNormal
    {
        get => geometryCoatNormal;
        set => geometryCoatNormal = Direction(value, nameof(GeometryCoatNormal));
    }

    private Vector3? geometryTangent;

    /// <summary>
    /// Gets or sets the finite nonzero world-space <c>geometry_tangent</c> override.
    /// Null inherits the geometry's <c>Tworld</c>; explicit vectors are preserved without normalization.
    /// </summary>
    public Vector3? GeometryTangent
    {
        get => geometryTangent;
        set => geometryTangent = Direction(value, nameof(GeometryTangent));
    }

    private Vector3? geometryCoatTangent;

    /// <summary>
    /// Gets or sets the finite nonzero world-space <c>geometry_coat_tangent</c> override.
    /// Null inherits the geometry's <c>Tworld</c>; explicit vectors are preserved without normalization.
    /// </summary>
    public Vector3? GeometryCoatTangent
    {
        get => geometryCoatTangent;
        set => geometryCoatTangent = Direction(value, nameof(GeometryCoatTangent));
    }

    /// <summary>
    /// Creates an independent mutable copy of every constant input, inherited-vector state, name
    /// and scene-unit scale. Immutable graph and color-space identities are retained without conversion.
    /// </summary>
    /// <returns>A new authoring surface unaffected by subsequent edits to this instance.</returns>
    public OpenPbrSurface Clone() => (OpenPbrSurface)MemberwiseClone();

    /// <summary>
    /// Creates an explicitly approximate metallic/roughness preview and conversion diagnostics.
    /// Unsupported active parameters fail unless loss is explicitly permitted by the options.
    /// </summary>
    public OpenPbrPreviewResult ToPbrPreview(OpenPbrPreviewOptions? options = null) =>
        OpenPbrPreviewConverter.Convert(this, options ?? new OpenPbrPreviewOptions());

    /// <summary>
    /// Updates a dedicated existing preview only after conversion fully succeeds, returning that
    /// material and diagnostics. Copies preview numeric values, name, alpha mode and sidedness;
    /// leaves the target's texture bindings, shader template and other renderer policies intact.
    /// </summary>
    public OpenPbrPreviewResult ApplyToPbrPreview(PbrMaterial target, OpenPbrPreviewOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        OpenPbrPreviewResult result = ToPbrPreview(options);
        PbrMaterial source = result.Material;
        target.Name = source.Name;
        target.BaseColor = source.BaseColor;
        target.Metallic = source.Metallic;
        target.Roughness = source.Roughness;
        target.IndexOfRefraction = source.IndexOfRefraction;
        target.SpecularFactor = source.SpecularFactor;
        target.SpecularColor = source.SpecularColor;
        target.AnisotropyStrength = source.AnisotropyStrength;
        target.AnisotropyRotation = source.AnisotropyRotation;
        target.TransmissionFactor = source.TransmissionFactor;
        target.DiffuseTransmissionFactor = source.DiffuseTransmissionFactor;
        target.DiffuseTransmissionColor = source.DiffuseTransmissionColor;
        target.VolumeThicknessFactor = source.VolumeThicknessFactor;
        target.VolumeAttenuationColor = source.VolumeAttenuationColor;
        target.VolumeAttenuationDistance = source.VolumeAttenuationDistance;
        target.Dispersion = source.Dispersion;
        target.SheenColor = source.SheenColor;
        target.SheenRoughness = source.SheenRoughness;
        target.ClearcoatFactor = source.ClearcoatFactor;
        target.ClearcoatRoughness = source.ClearcoatRoughness;
        target.IridescenceFactor = source.IridescenceFactor;
        target.IridescenceIndexOfRefraction = source.IridescenceIndexOfRefraction;
        target.IridescenceThicknessMinimum = source.IridescenceThicknessMinimum;
        target.IridescenceThicknessMaximum = source.IridescenceThicknessMaximum;
        target.EmissiveColor = source.EmissiveColor;
        target.EmissiveStrength = source.EmissiveStrength;
        target.IsDoubleSided = source.IsDoubleSided;
        target.AlphaMode = source.AlphaMode;
        return new OpenPbrPreviewResult(target, result.Diagnostics);
    }

    private static float Unit(float value, string name) =>
        float.IsFinite(value) && value is >= 0f and <= 1f ? value
            : throw new ArgumentOutOfRangeException(name, "Expected a finite value in [0, 1].");

    private static float SignedUnit(float value, string name) =>
        float.IsFinite(value) && value is >= -1f and <= 1f ? value
            : throw new ArgumentOutOfRangeException(name, "Expected a finite value in [-1, 1].");

    private static float Nonnegative(float value, string name) =>
        float.IsFinite(value) && value >= 0f ? value
            : throw new ArgumentOutOfRangeException(name, "Expected a finite nonnegative value.");

    private static LinearRgba Color(LinearRgba value, string name)
    {
        if (value.ColorSpace is null || !value.ColorSpace.IsLinear || value.Alpha != 1f)
        {
            throw new ArgumentException("OpenPBR color3 requires an explicitly tagged linear RGB color with alpha one.", name);
        }
        return value;
    }

    private static Vector3? Direction(Vector3? value, string name)
    {
        if (value is Vector3 vector &&
            (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z) ||
             vector == Vector3.Zero))
        {
            throw new ArgumentOutOfRangeException(name, "Geometry vectors must be finite and nonzero.");
        }
        return value;
    }
}

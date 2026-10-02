using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>Controls treatment of OpenPBR parameters the metallic/roughness preview cannot reproduce.</summary>
public enum OpenPbrPreviewPolicy
{
    /// <summary>Rejects active parameters without a supported preview mapping. The base shader remains approximate.</summary>
    RejectUnsupported,

    /// <summary>Permits the documented approximations or omissions returned with the preview.</summary>
    AllowLossyApproximation,
}

/// <summary>Configures an explicitly approximate OpenPBR-to-metallic/roughness preview conversion.</summary>
public sealed class OpenPbrPreviewOptions
{
    /// <summary>Gets or initializes the loss policy. The default rejects unsupported active parameters.</summary>
    public OpenPbrPreviewPolicy Policy { get; init; } = OpenPbrPreviewPolicy.RejectUnsupported;

    /// <summary>
    /// Gets or initializes how many cd/m² one unit of white scene-linear emission represents.
    /// An explicit finite positive value is required when emission luminance is nonzero.
    /// This scale does not configure the presentation surface or tone mapping.
    /// </summary>
    public float? NitsPerSceneUnit { get; init; }
}

/// <summary>Describes one renderer approximation or active parameter that loses authoring semantics.</summary>
public sealed class OpenPbrPreviewDiagnostic
{
    internal OpenPbrPreviewDiagnostic(string parameter, string message, bool isLossy)
    {
        Parameter = parameter;
        Message = message;
        IsLossy = isLossy;
    }

    /// <summary>Gets the official OpenPBR input name, or <c>surface</c> for the overall shader.</summary>
    public string Parameter { get; }

    /// <summary>Gets the precise limitation or mapping applied by the preview.</summary>
    public string Message { get; }

    /// <summary>Gets whether this item requires explicit permission for a lossy mapping.</summary>
    public bool IsLossy { get; }
}

/// <summary>Contains the renderable preview and an immutable account of its approximations.</summary>
public sealed class OpenPbrPreviewResult
{
    internal OpenPbrPreviewResult(PbrMaterial material, IReadOnlyList<OpenPbrPreviewDiagnostic> diagnostics)
    {
        Material = material;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the existing metallic/roughness material; this does not become an OpenPBR shader.</summary>
    public PbrMaterial Material { get; }

    /// <summary>Gets all limitations, including the baseline shader-model approximation.</summary>
    public IReadOnlyList<OpenPbrPreviewDiagnostic> Diagnostics { get; }

    /// <summary>Gets whether any active parameter required explicit permission for loss.</summary>
    public bool HasLossyMappings => Diagnostics.Any(static item => item.IsLossy);
}

/// <summary>Reports all unsupported active inputs when a preview conversion rejects parameter loss.</summary>
public sealed class OpenPbrPreviewException : NotSupportedException
{
    internal OpenPbrPreviewException(IReadOnlyList<OpenPbrPreviewDiagnostic> diagnostics)
        : base("The PBR preview cannot preserve these active OpenPBR inputs: " +
               string.Join(", ", diagnostics.Where(static item => item.IsLossy).Select(static item => item.Parameter)) +
               ". Inspect Diagnostics or explicitly select AllowLossyApproximation.") => Diagnostics = diagnostics;

    /// <summary>Gets the immutable conversion diagnostics, including every detected loss.</summary>
    public IReadOnlyList<OpenPbrPreviewDiagnostic> Diagnostics { get; }
}

internal static class OpenPbrPreviewConverter
{
    internal static OpenPbrPreviewResult Convert(OpenPbrSurface source, OpenPbrPreviewOptions options)
    {
        if (source.Graph is { Bindings.Count: > 0 })
            throw new NotSupportedException("OpenPBR graph inputs require OpenPbrRenderPass; the metallic/roughness conversion cannot preserve them.");
        if (!Enum.IsDefined(options.Policy)) throw new ArgumentOutOfRangeException(nameof(options));
        if (options.NitsPerSceneUnit is float nits && (!float.IsFinite(nits) || nits <= 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "NitsPerSceneUnit must be finite and positive.");
        }
        if (source.EmissionLuminance > 0f && options.NitsPerSceneUnit is null)
        {
            throw new InvalidOperationException("Active OpenPBR emission requires an explicit NitsPerSceneUnit conversion scale.");
        }

        List<OpenPbrPreviewDiagnostic> diagnostics =
        [
            new("surface", "Uses Mu3D's existing glTF metallic/roughness shader; OpenPBR's layered scattering, energy compensation and reference-image conformance are not implemented by this conversion.", false),
        ];
        void Loss(string parameter, string message) => diagnostics.Add(new(parameter, message, true));

        if (source.BaseDiffuseRoughness > 0f && source.BaseMetalness < 1f && source.TransmissionWeight < 1f)
            Loss("base_diffuse_roughness", "The preview retains the existing diffuse model and omits OpenPBR's rough diffuse response.");
        if (source.SpecularWeight > 1f)
            Loss("specular_weight", "The existing specular factor is limited to one; the preview clamps this multiplier to one.");
        if (source.BaseMetalness < 1f && source.SpecularWeight != 1f)
            Loss("specular_weight", "The preview scales the glTF Fresnel response instead of modulating the dielectric IOR as OpenPBR requires; grazing reflection and refraction can differ.");
        if (source.BaseMetalness < 1f && source.SpecularWeight > 0f && !IsWhite(source.SpecularColor))
            Loss("specular_color", "The preview tints dielectric reflectance at normal incidence; OpenPBR tints the initial reflection across incidence angles, so grazing response and substrate coupling differ.");
        if (source.SpecularRoughness < 0.045f)
            Loss("specular_roughness", "The existing shader raises specular roughness to 0.045 and cannot reproduce sharper or ideal-mirror inputs.");
        if (source.BaseMetalness > 0f && source.SpecularWeight != 1f)
            Loss("specular_weight", "The preview cannot apply OpenPBR specular_weight to the metallic lobe.");
        if (source.BaseMetalness > 0f && !IsWhite(source.SpecularColor))
            Loss("specular_color", "The preview cannot reproduce OpenPBR metallic F82 edge tint.");
        if (source.SpecularIor < 1f)
            Loss("specular_ior", "The preview uses IOR one because this renderer cannot represent OpenPBR dielectric IOR values below one.");
        if (source.SpecularRoughnessAnisotropy > 0f)
            Loss("specular_roughness_anisotropy", "Maps the value to KHR anisotropy strength; its roughness parameterization differs from OpenPBR.");
        if (source.SubsurfaceWeight > 0f && source.BaseMetalness < 1f && source.TransmissionWeight < 1f)
            Loss("subsurface_weight", "Subsurface transport is omitted; subsurface_color, radius, radius_scale and scatter_anisotropy remain solely in the authoring model.");
        if (source.FuzzWeight > 0f)
            Loss("fuzz_weight", "Approximates fuzz with the existing Charlie sheen lobe and fuzz_color times fuzz_weight; OpenPBR fuzz layering differs.");
        if (source.CoatWeight > 0f)
        {
            Loss("coat_weight", "Uses the existing glTF clearcoat lobe with fixed IOR 1.5; OpenPBR coat absorption, substrate roughening, darkening and layer coupling are omitted.");
            if (source.CoatIor != 1.5f) Loss("coat_ior", "The preview's fixed coat IOR is 1.5.");
            if (!IsWhite(source.CoatColor)) Loss("coat_color", "Coat absorption color is omitted.");
            if (source.CoatRoughnessAnisotropy > 0f) Loss("coat_roughness_anisotropy", "The preview's coat is isotropic.");
            if (source.CoatDarkening != 1f) Loss("coat_darkening", "The preview cannot vary OpenPBR coat-induced substrate darkening.");
        }
        if (source.TransmissionWeight > 0f && source.BaseMetalness < 1f)
        {
            Loss("transmission_weight", "Uses screen-space glTF transmission; OpenPBR bulk-medium transport, nested interfaces and internal path lengths are not reproduced.");
            if (!IsWhite(source.TransmissionColor)) Loss("transmission_color", "Transmission tint is omitted because no matching bulk path-length or zero-depth tint model exists.");
            if (source.TransmissionDepth > 0f) Loss("transmission_depth", "Absorption reference distance is preserved only in authoring data; it is not incorrectly treated as object thickness.");
            if (!IsBlack(source.TransmissionScatter)) Loss("transmission_scatter", "Volumetric scattering and its phase-function anisotropy are omitted.");
            if (source.TransmissionDispersionScale > 0f) Loss("transmission_dispersion_scale", "OpenPBR Abbe-number dispersion is omitted; it is not equivalent to glTF dispersion strength.");
        }
        if (source.ThinFilmWeight > 0f)
        {
            Loss("thin_film_weight", "Approximates the film using glTF iridescence, converting micrometres to nanometres; layer placement and spectral response differ.");
            if (source.ThinFilmIor < 1f) Loss("thin_film_ior", "The preview clamps the thin-film IOR to one.");
        }
        if (source.GeometryThinWalled)
            Loss("geometry_thin_walled", "Enables double-sided preview rendering; this does not implement OpenPBR thin-wall scattering or paired interfaces.");
        if (source.GeometryNormal.HasValue) Loss("geometry_normal", "The preview uses mesh normals and cannot apply this constant world-space override.");
        if (source.GeometryTangent.HasValue) Loss("geometry_tangent", "The preview uses mesh tangents and cannot apply this constant world-space override.");
        if (source.CoatWeight > 0f && source.GeometryCoatNormal.HasValue) Loss("geometry_coat_normal", "The preview uses mesh coat normals and cannot apply this world-space override.");
        if (source.CoatWeight > 0f && source.GeometryCoatTangent.HasValue) Loss("geometry_coat_tangent", "The preview cannot apply a separate coat tangent override.");
        if (source.EmissionLuminance > 0f && (source.CoatWeight > 0f || source.FuzzWeight > 0f))
            Loss("emission_luminance", "The preview emission is additive and omits attenuation through overlying coat and fuzz.");

        LinearRgba baseColor = Reflectance(source.BaseColor, "base_color", Loss);
        LinearRgba specularColor = Reflectance(source.SpecularColor, "specular_color", Loss);
        LinearRgba fuzzColor = source.FuzzWeight > 0f
            ? Reflectance(source.FuzzColor, "fuzz_color", Loss)
            : new(0f, 0f, 0f, 1f, StandardColorSpaces.AcesCg);
        LinearRgba emissionColor = source.EmissionLuminance > 0f
            ? Emission(source.EmissionColor, Loss)
            : new(0f, 0f, 0f, 1f, StandardColorSpaces.AcesCg);

        IReadOnlyList<OpenPbrPreviewDiagnostic> snapshot = diagnostics.AsReadOnly();
        if (options.Policy == OpenPbrPreviewPolicy.RejectUnsupported && diagnostics.Any(static item => item.IsLossy))
            throw new OpenPbrPreviewException(snapshot);

        float emissionStrength = source.EmissionLuminance > 0f
            ? source.EmissionLuminance / options.NitsPerSceneUnit!.Value : 0f;
        float thicknessNanometres = source.ThinFilmWeight > 0f ? source.ThinFilmThickness * 1000f : 0f;
        if (!float.IsFinite(emissionStrength) || !float.IsFinite(thicknessNanometres))
            throw new ArgumentOutOfRangeException(nameof(source), "Preview unit conversion exceeds finite FP32 storage.");
        // Validate the actual emitted product as well as its independently finite factors.
        if (!float.IsFinite(emissionColor.Red * emissionStrength) ||
            !float.IsFinite(emissionColor.Green * emissionStrength) ||
            !float.IsFinite(emissionColor.Blue * emissionStrength))
            throw new ArgumentOutOfRangeException(nameof(source), "Preview emission exceeds finite FP32 storage.");

        // OpenPBR v1.1.1 defines base_weight on diffuse reflectance and metal F0, not dielectric
        // gloss. Multiplying this shared base-color input preserves that control boundary.
        PbrMaterial material = new(Scale(baseColor, source.BaseWeight, source.GeometryOpacity),
            source.BaseMetalness, source.SpecularRoughness, source.Name)
        {
            IndexOfRefraction = MathF.Max(1f, source.SpecularIor),
            // An index-matched dielectric has no Fresnel reflection at any angle. The existing
            // Schlick approximation otherwise retains an artificial grazing term at IOR one.
            SpecularFactor = source.SpecularIor == 1f ? 0f : MathF.Min(1f, source.SpecularWeight),
            SpecularColor = specularColor,
            AnisotropyStrength = source.SpecularRoughnessAnisotropy,
            // OpenPBR selects the entire metal branch at metalness one. Keeping an inactive
            // transmission factor would unnecessarily select transparent rendering and disable shadows.
            TransmissionFactor = source.BaseMetalness < 1f ? source.TransmissionWeight : 0f,
            SheenColor = Scale(fuzzColor, source.FuzzWeight),
            SheenRoughness = source.FuzzRoughness,
            ClearcoatFactor = source.CoatWeight,
            ClearcoatRoughness = source.CoatRoughness,
            IridescenceFactor = source.ThinFilmWeight,
            IridescenceIndexOfRefraction = source.ThinFilmWeight > 0f ? MathF.Max(1f, source.ThinFilmIor) : 1.3f,
            IridescenceThicknessMinimum = thicknessNanometres,
            IridescenceThicknessMaximum = thicknessNanometres,
            EmissiveColor = emissionColor,
            EmissiveStrength = emissionStrength,
            AlphaMode = source.GeometryOpacity < 1f ? MaterialAlphaMode.Blend : MaterialAlphaMode.Opaque,
            IsDoubleSided = source.GeometryThinWalled,
        };
        return new OpenPbrPreviewResult(material, snapshot);
    }

    private static bool IsWhite(LinearRgba color) => color.Red == 1f && color.Green == 1f && color.Blue == 1f;
    private static bool IsBlack(LinearRgba color) => color.Red == 0f && color.Green == 0f && color.Blue == 0f;

    private static LinearRgba Reflectance(LinearRgba color, string name, Action<string, string> loss)
    {
        RequireStandardSpace(color);
        if (color.Red is < 0f or > 1f || color.Green is < 0f or > 1f || color.Blue is < 0f or > 1f)
            loss(name, "The preview clamps reflectance components to [0, 1]; original signed and above-one authoring values remain unchanged.");
        return new(Math.Clamp(color.Red, 0f, 1f), Math.Clamp(color.Green, 0f, 1f),
            Math.Clamp(color.Blue, 0f, 1f), 1f, color.ColorSpace);
    }

    private static LinearRgba Emission(LinearRgba color, Action<string, string> loss)
    {
        RequireStandardSpace(color);
        if (color.Red < 0f || color.Green < 0f || color.Blue < 0f)
            loss("emission_color", "The existing emitter rejects negative components; the preview clamps those to zero and preserves positive HDR values.");
        return new(MathF.Max(color.Red, 0f), MathF.Max(color.Green, 0f), MathF.Max(color.Blue, 0f), 1f, color.ColorSpace);
    }

    private static void RequireStandardSpace(LinearRgba color)
    {
        if (color.ColorSpace is not StandardRgbColorSpaceReference)
            throw new NotSupportedException("The PBR preview requires a supported standard linear RGB space; authoring retains custom color-space identity.");
    }

    private static LinearRgba Scale(LinearRgba color, float multiplier, float alpha = 1f) =>
        new(color.Red * multiplier, color.Green * multiplier, color.Blue * multiplier, alpha, color.ColorSpace);
}

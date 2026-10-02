using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

int checks = 0;
void Expect(bool condition, string name)
{
    checks++;
    if (!condition) throw new InvalidOperationException(name);
}
T Throws<T>(Action action, string name) where T : Exception
{
    checks++;
    try { action(); }
    catch (T exception) { return exception; }
    throw new InvalidOperationException(name);
}

OpenPbrSurface defaults = new();
Expect(OpenPbrSurface.SpecificationVersion == "1.1.1", "Pinned specification version");
Expect(defaults.BaseWeight == 1f && defaults.BaseColor.Red == 0.8f && defaults.BaseColor.Green == 0.8f &&
    defaults.BaseColor.Blue == 0.8f && defaults.BaseMetalness == 0f && defaults.BaseDiffuseRoughness == 0f,
    "Official base defaults");
Expect(defaults.SpecularWeight == 1f && defaults.SpecularRoughness == 0.3f && defaults.SpecularIor == 1.5f &&
    defaults.SpecularRoughnessAnisotropy == 0f && defaults.SpecularColor.Red == 1f, "Official specular defaults");
Expect(defaults.TransmissionWeight == 0f && defaults.TransmissionDepth == 0f &&
    defaults.TransmissionColor.Red == 1f && defaults.TransmissionScatter.Red == 0f &&
    defaults.TransmissionScatterAnisotropy == 0f && defaults.TransmissionDispersionScale == 0f &&
    defaults.TransmissionDispersionAbbeNumber == 20f, "Official transmission defaults");
Expect(defaults.SubsurfaceWeight == 0f && defaults.SubsurfaceColor.Red == 0.8f && defaults.SubsurfaceRadius == 1f &&
    defaults.SubsurfaceRadiusScale == new Vector3(1f, 0.5f, 0.25f) && defaults.SubsurfaceScatterAnisotropy == 0f,
    "Official subsurface defaults");
Expect(defaults.FuzzWeight == 0f && defaults.FuzzColor.Red == 1f && defaults.FuzzRoughness == 0.5f &&
    defaults.CoatWeight == 0f && defaults.CoatColor.Red == 1f && defaults.CoatRoughness == 0f &&
    defaults.CoatRoughnessAnisotropy == 0f && defaults.CoatIor == 1.6f && defaults.CoatDarkening == 1f,
    "Official outer layer defaults");
Expect(defaults.ThinFilmWeight == 0f && defaults.ThinFilmThickness == 0.5f && defaults.ThinFilmIor == 1.4f &&
    defaults.EmissionLuminance == 0f && defaults.EmissionColor.Red == 1f && defaults.GeometryOpacity == 1f &&
    !defaults.GeometryThinWalled && defaults.GeometryNormal is null && defaults.GeometryCoatNormal is null &&
    defaults.GeometryTangent is null && defaults.GeometryCoatTangent is null, "Official film, emission and inherited geometry defaults");
Expect(defaults.BaseColor.ColorSpace == StandardColorSpaces.AcesCg && defaults.MetersPerUnit == 1f,
    "Explicit default ACEScg and metre convention");

Throws<ArgumentOutOfRangeException>(() => defaults.BaseWeight = float.NaN, "Nonfinite scalar rejection");
Throws<ArgumentOutOfRangeException>(() => defaults.GeometryOpacity = 2f, "Unit interval validation");
Throws<ArgumentOutOfRangeException>(() => defaults.TransmissionScatterAnisotropy = -1.1f, "Signed interval validation");
Throws<ArgumentOutOfRangeException>(() => defaults.MetersPerUnit = 0f, "Positive length metadata");
Throws<ArgumentOutOfRangeException>(() => defaults.GeometryNormal = Vector3.Zero, "Zero normal rejection");
Throws<ArgumentOutOfRangeException>(() => defaults.GeometryTangent = new(float.PositiveInfinity, 0, 0), "Finite tangent rejection");
Throws<ArgumentOutOfRangeException>(() => defaults.SubsurfaceRadiusScale = new(-1f, 0f, 0f), "Numeric channel lengths validation");
Throws<ArgumentException>(() => defaults.BaseColor = default, "Untagged color rejection");
Throws<ArgumentException>(() => defaults.BaseColor = new(1f, 1f, 1f, 0.5f, StandardColorSpaces.AcesCg),
    "Color3 cannot conceal alpha");
Expect(defaults.BaseWeight == 1f && defaults.BaseColor.Red == 0.8f, "Failed setters preserve author values");

OpenPbrPreviewResult baseline = defaults.ToPbrPreview();
Expect(!baseline.HasLossyMappings && baseline.Diagnostics.Count == 1 &&
    baseline.Diagnostics[0].Parameter == "surface", "Baseline discloses shader approximation");
Expect(baseline.Material.Roughness == 0.3f && baseline.Material.BaseColor.ColorSpace == StandardColorSpaces.AcesCg,
    "Baseline mapping preserves parameter and space identity");
OpenPbrSurface emission = new()
{
    EmissionLuminance = 800f,
    EmissionColor = new(2f, 0.5f, 0.25f, 1f, StandardColorSpaces.LinearRec2020),
};
Throws<InvalidOperationException>(() => emission.ToPbrPreview(), "Emission cannot silently assume nits scaling");
OpenPbrPreviewResult hdr = emission.ToPbrPreview(new() { NitsPerSceneUnit = 100f });
Expect(hdr.Material.EmissiveStrength == 8f && hdr.Material.EmissiveColor.Red == 2f &&
    hdr.Material.EmissiveColor.Red * hdr.Material.EmissiveStrength == 16f &&
    hdr.Material.EmissiveColor.ColorSpace == StandardColorSpaces.LinearRec2020, "Emission retains HDR radiance and explicit color identity");
Throws<ArgumentOutOfRangeException>(() => emission.ToPbrPreview(new() { NitsPerSceneUnit = float.NaN }),
    "Invalid emission scaling rejection");

OpenPbrSurface layers = new()
{
    Name = "layers", CoatWeight = 1f, CoatColor = new(0.8f, 0.5f, 0.25f, 1f, StandardColorSpaces.AcesCg),
    TransmissionWeight = 0.5f, TransmissionDepth = 3f, TransmissionDispersionScale = 0.25f,
    SubsurfaceWeight = 0.25f, FuzzWeight = 0.2f, ThinFilmWeight = 1f, ThinFilmThickness = 0.6f,
};
OpenPbrPreviewException rejected = Throws<OpenPbrPreviewException>(() => layers.ToPbrPreview(), "Layers require explicit loss permission");
Expect(rejected.Diagnostics.Any(x => x.Parameter == "subsurface_weight") &&
    rejected.Diagnostics.Any(x => x.Parameter == "coat_color") &&
    rejected.Diagnostics.Any(x => x.Parameter == "transmission_depth") &&
    rejected.Diagnostics.Any(x => x.Parameter == "transmission_dispersion_scale"), "Rejected preview enumerates losses");
OpenPbrPreviewOptions lossy = new() { Policy = OpenPbrPreviewPolicy.AllowLossyApproximation };
OpenPbrPreviewResult layered = layers.ToPbrPreview(lossy);
Expect(layered.HasLossyMappings && layered.Material.ClearcoatFactor == 1f && layered.Material.TransmissionFactor == 0.5f &&
    layered.Material.IridescenceThicknessMaximum == 600f, "Explicit preview maps renderable layers and physical film units");
Expect(layered.Material.VolumeThicknessFactor == 0f && layered.Material.Dispersion == 0f &&
    layers.TransmissionDepth == 3f && layers.TransmissionDispersionScale == 0.25f,
    "Preview never misuses absorption distance as geometric thickness or destroys original dispersion");

OpenPbrSurface signed = new() { BaseColor = new(-0.2f, 2f, 0.5f, 1f, StandardColorSpaces.AcesCg) };
Throws<OpenPbrPreviewException>(() => signed.ToPbrPreview(), "Reflectance clamp requires explicit permission");
OpenPbrPreviewResult clamped = signed.ToPbrPreview(lossy);
Expect(clamped.Material.BaseColor.Red == 0f && clamped.Material.BaseColor.Green == 1f &&
    signed.BaseColor.Red == -0.2f && signed.BaseColor.Green == 2f, "Lossy reflectance preview preserves original extended authoring range");
OpenPbrPreviewException metalLoss = Throws<OpenPbrPreviewException>(
    () => new OpenPbrSurface { BaseMetalness = 1f, SpecularWeight = 0f }.ToPbrPreview(),
    "Metallic specular weight cannot silently lose its meaning");
Expect(metalLoss.Diagnostics.Any(item => item.Parameter == "specular_weight" && item.IsLossy),
    "Metallic weight loss identifies the actual author input");
OpenPbrPreviewException dielectricWeightLoss = Throws<OpenPbrPreviewException>(
    () => new OpenPbrSurface { SpecularWeight = 0.5f }.ToPbrPreview(),
    "Dielectric weight cannot silently replace IOR modulation with overall Fresnel scaling");
Expect(dielectricWeightLoss.Diagnostics.Any(item => item.Parameter == "specular_weight" && item.IsLossy),
    "Dielectric weight loss identifies the actual author input");
OpenPbrPreviewException dielectricTintLoss = Throws<OpenPbrPreviewException>(
    () => new OpenPbrSurface { SpecularColor = new(1f, 0.2f, 0.4f, 1f, StandardColorSpaces.AcesCg) }.ToPbrPreview(),
    "Dielectric tint cannot silently lose its grazing-angle meaning");
Expect(dielectricTintLoss.Diagnostics.Any(item => item.Parameter == "specular_color" && item.IsLossy),
    "Dielectric tint loss identifies the actual author input");
OpenPbrPreviewException mirrorLoss = Throws<OpenPbrPreviewException>(
    () => new OpenPbrSurface { SpecularRoughness = 0f }.ToPbrPreview(),
    "The shader's minimum roughness cannot silently replace an ideal mirror");
Expect(mirrorLoss.Diagnostics.Any(item => item.Parameter == "specular_roughness" && item.IsLossy),
    "Minimum roughness is an explicit preview loss");
OpenPbrPreviewResult weightedBase = new OpenPbrSurface { BaseWeight = 0.25f }.ToPbrPreview();
Expect(weightedBase.Material.BaseColor.Red == 0.2f && weightedBase.Material.SpecularFactor == 1f &&
    weightedBase.Material.IndexOfRefraction == 1.5f && !weightedBase.HasLossyMappings,
    "Base weight scales the diffuse base and leaves dielectric gloss controls intact");
OpenPbrPreviewResult indexMatched = new OpenPbrSurface { SpecularIor = 1f }.ToPbrPreview();
Expect(indexMatched.Material.SpecularFactor == 0f && !indexMatched.HasLossyMappings,
    "Index-matched dielectric disables the existing shader's artificial grazing reflection");
OpenPbrSurface opaqueMetal = new() { BaseMetalness = 1f, TransmissionWeight = 1f, TransmissionDepth = 5f };
OpenPbrPreviewResult metalPreview = opaqueMetal.ToPbrPreview();
Expect(!metalPreview.HasLossyMappings && metalPreview.Material.TransmissionFactor == 0f &&
    metalPreview.Material.AlphaMode == MaterialAlphaMode.Opaque &&
    !MaterialShaderVariant.FromMaterial(metalPreview.Material).Extensions.HasFlag(PbrMaterialExtensions.Transmission) &&
    opaqueMetal.TransmissionWeight == 1f,
    "Inactive metallic transmission preserves opaque rendering and author values");
OpenPbrSurface inactive = new() { SubsurfaceRadius = 100f, CoatColor = new(0.2f, 0.3f, 0.4f, 1f, StandardColorSpaces.AcesCg) };
Expect(!inactive.ToPbrPreview().HasLossyMappings, "Inactive authoring controls are retained without a false active-lobe rejection");

PbrMaterial stable = baseline.Material;
LinearRgba stableBase = stable.BaseColor;
Throws<OpenPbrPreviewException>(() => layers.ApplyToPbrPreview(stable), "Strict apply rejects before mutating stable target");
Expect(stable.BaseColor == stableBase && stable.ClearcoatFactor == 0f && stable.Name is null,
    "Failed apply is atomic for target scalar state");
OpenPbrPreviewResult applied = layers.ApplyToPbrPreview(stable, lossy);
Expect(ReferenceEquals(applied.Material, stable) && stable.Name == "layers" && stable.ClearcoatFactor == 1f,
    "Successful apply updates existing material identity");
defaults.ApplyToPbrPreview(stable);
Expect(stable.ClearcoatFactor == 0f && stable.IridescenceFactor == 0f && stable.EmissiveStrength == 0f,
    "Resetting author inputs removes previous preview layers");

using RecordingGraphicsDevice device = new();
using SceneRenderer renderer = new(device, GraphicsTextureFormat.Rgba16Float);
using RecordingGraphicsTexture target = (RecordingGraphicsTexture)device.CreateTexture(new(
    new GraphicsExtent3D(16, 16), GraphicsTextureFormat.Rgba16Float, GraphicsTextureUsage.RenderAttachment));
using GraphicsTexture depth = device.CreateTexture(new(
    new GraphicsExtent3D(16, 16), GraphicsTextureFormat.Depth32Float, GraphicsTextureUsage.RenderAttachment));
Scene scene = new("OpenPBR explicit preview");
scene.Add(new Mesh(MeshPrimitives.CreateCone(), layered.Material));
scene.Add(new DirectionalLight(new(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb)));
PerspectiveCamera camera = new(aspectRatio: 1f);
camera.Transform.Position = new(0f, 0f, 4f);
renderer.Render(scene, camera, target, depth);
Expect(target.IndexedDrawCount > 0 && renderer.ObservedMaterialShaderVariantCount > 0,
    "Preview reaches existing renderer's HDR command path with a recording backend");
Expect(MaterialShaderVariant.FromMaterial(layered.Material).BaseModel == MaterialBaseModel.PbrMetallicRoughness,
    "Preview never claims a new OpenPBR scattering identity");
checks += OpenPbrMaterialChecks.Run();
Console.WriteLine($"OpenPBR authoring, material identity and preview checks passed: {checks}. Recording-backend rendering only; no GPU conformance claim.");

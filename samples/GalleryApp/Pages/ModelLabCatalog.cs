using System.Numerics;

namespace Mu3D.GalleryApp.Pages;

// Exact native Gallery conformance catalog, shared with its Web host.
internal static class ModelLabCatalog
{
    internal static readonly ModelSpec[] Models =
    [
        new(
            "BoxInterleaved",
            "GltfSamples/BoxInterleaved.glb",
            "Khronos BoxInterleaved",
            "GLB + hierarchy + matrix + indexed interleaved POSITION/NORMAL + scalar PBR.",
            "Vertices: 24; indices: 36\nAccessor stride: 24 bytes\nAnimation: none",
            new Vector3(2.4f, 1.8f, 2.8f),
            Vector3.Zero),
        new(
            "AnimatedMorphCube",
            "GltfSamples/AnimatedMorphCube.glb",
            "Khronos AnimatedMorphCube",
            "Two POSITION/NORMAL/TANGENT morph targets + mesh weights + looping LINEAR weight animation.",
            "Morph targets: 2\nBase and morph tangents: retained\nWeight keys: 127\nAnimation: looping LINEAR interpolation",
            new Vector3(2.4f, 1.8f, 2.8f),
            Vector3.Zero),
        new(
            "Fox",
            "GltfSamples/Fox.glb",
            "Khronos Fox",
            "Embedded sRGB PNG + UV/sampler + 24-joint skin + Survey/Walk/Run transform clips.",
            "Vertices: 1728; triangles: 576\nTexture: 1024 x 1024 PNG\nSkin joints: 24\nClips: Survey / Walk / Run",
            new Vector3(220f, 120f, 220f),
            new Vector3(0f, 38f, -8f)),
        new(
            "InterpolationTest",
            "GltfSamples/InterpolationTest.glb",
            "Khronos InterpolationTest",
            "Nine synchronized Step, Linear and CubicSpline scale/rotation/translation clips.",
            "Meshes: 10 official + 1 diagnostic ground\nClips: 9 synchronized\nInterpolation: 3 Step / 3 Linear / 3 CubicSpline\nLabel: 4-bit indexed-color PNG\nDiagnostic ground: shadow/AO receiver (not part of Khronos asset)",
            new Vector3(12f, 7f, 16f),
            new Vector3(0f, 2.5f, 0f),
            ApplyAllAnimations: true,
            AddDiagnosticGround: true),
        new(
            "NormalTangentMirrorTest",
            "GltfSamples/NormalTangentMirrorTest.glb",
            "Khronos Normal-Tangent Mirror Test",
            "Author-supplied tangent vectors + handedness across mirrored UV normal mapping.",
            "Official authored-tangent gate\nExpected: mirrored sections retain coherent bumps\nLicense: CC BY 4.0",
            new Vector3(0f, 0f, 4f),
            Vector3.Zero),
        new(
            "NormalTangentTest",
            "GltfSamples/NormalTangentTest.glb",
            "Khronos Normal-Tangent Test",
            "No tangent attribute; normal mapping exercises derivative tangent-frame reconstruction.",
            "Official derivative-fallback gate\nExpected: coherent normal-map lighting without TANGENT\nLicense: embedded CC BY 4.0 notice retained",
            new Vector3(0f, 0f, 4f),
            Vector3.Zero),
        new(
            "AlphaBlendModeTest",
            "GltfSamples/AlphaBlendModeTest.glb",
            "Khronos AlphaBlendModeTest",
            "JPEG/PNG color and non-color maps + texture alpha + OPAQUE/MASK/BLEND.",
            "Meshes: 9 + AO probe\nProbe surface gap: 0.17 units from Cutoff 0.75\nAlpha modes: 4 opaque / 3 mask / 2 blend\nMaps: base color / normal / packed ORM",
            new Vector3(7f, 4.7f, 8.5f),
            new Vector3(0f, 1f, 0f),
            true),
        new(
            "TextureTransformTest",
            "GltfSamples/TextureTransformTest/TextureTransformTest.gltf",
            "Khronos KHR_texture_transform Test",
            "External GLTF/BIN/PNG + per-slot sampler + offset/rotation/scale.",
            "Official Khronos CC0 asset\n" +
                "Expected: six panels show correct offset, rotation, scale and combined transform",
            new Vector3(0f, 0f, 4f),
            Vector3.Zero,
            ExternalResources:
            [
                "TextureTransformTest.bin",
                "Arrow.png",
                "Correct.png",
                "Error.png",
                "NotSupported.png",
                "UV.png",
            ]),
        new(
            "TextureSettingsTest",
            "GltfSamples/TextureSettingsTest.glb",
            "Khronos Texture Settings Test",
            "Independent glTF wrap modes and double-sided materials.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Expected: test column matches pass column; clamp panels are solid green",
            new Vector3(0f, 0f, 10f),
            Vector3.Zero),
        new(
            "BoomBox",
            "GltfSamples/BoomBox.glb",
            "Khronos Boom Box",
            "Core glTF emissiveTexture + emissiveFactor alongside BaseColor/Normal/ORM.",
            "Official Khronos CC0 asset\n" +
                "Expected: the front display is self-lit with texture-defined detail\n" +
                "Core glTF emissive adds radiance; bloom and light spill are separate effects\n" +
                "Gate: emissive sRGB decode, independent binding and Beauty composition",
            new Vector3(0.026f, 0.016f, 0.034f),
            Vector3.Zero),
        new(
            "EmissiveStrengthTest",
            "GltfSamples/EmissiveStrengthTest.glb",
            "Khronos Emissive Strength Test",
            "Official isolated KHR_materials_emissive_strength comparison without transmission.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Expected left-to-right strengths: 1, 2, 4, 8, 16\n" +
                "Beauty and Emissive must preserve progressively brighter linear HDR values\n" +
                "Bloom is optional and intentionally disabled",
            new Vector3(0f, 0f, 18f),
            Vector3.Zero),
        new(
            "SheenTestGrid",
            "GltfSamples/SheenTestGrid.glb",
            "Khronos Sheen Test Grid",
            "Required KHR_materials_sheen with Charlie direct lighting and layered base-energy compensation.",
            "Official Khronos CC0 asset\n" +
                "Expected: blue spheres progress from no sheen to cyan cloth sheen while roughness varies\n" +
                "Direct sheen follows the Khronos Charlie distribution and visibility fit\n" +
                "IBL uses a dedicated Charlie-prefiltered environment and directional-energy LUT",
            new Vector3(0f, 0.16f, 0.62f),
            new Vector3(0f, 0.045f, 0.055f)),
        new(
            "IridescenceSuzanne",
            "GltfSamples/IridescenceSuzanne.glb",
            "Khronos Iridescence Suzanne",
            "Required KHR_materials_iridescence using the ratified thin-film Fresnel model.",
            "Official Khronos CC0 asset\n" +
                "Expected: view-dependent spectral hue shifts across the Suzanne surfaces\n" +
                "Controls isolate factor, film IOR and authored nanometre thickness\n" +
                "Gate: dielectric/metallic energy-conserving direct and image-based reflection",
            new Vector3(0f, 0f, 5.5f),
            Vector3.Zero),
        new(
            "SpecularTest",
            "GltfSamples/SpecularTest.glb",
            "Khronos Specular Test",
            "Required KHR_materials_specular factor, color and texture conformance gate.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Rows compare factor and texture forms of dielectric F0/F90 control\n" +
                "Expected: the first-row left sphere is black; color rows retain white grazing edges\n" +
                "The final row permits specularColorFactor above 1 while conserving energy",
            new Vector3(0f, 0f, 2.5f),
            Vector3.Zero,
            EnvironmentPath: "Hdri/artist_workshop_1k.hdr",
            DirectionalLightEnabled: false),
        new(
            "UnlitTest",
            "GltfSamples/UnlitTest.glb",
            "Khronos Unlit Test",
            "Required KHR_materials_unlit with lighting-independent base color.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Expected: orange and blue objects are perfectly uniform across every face\n" +
                "Any visible face shading, highlight or environment reflection is a failure\n" +
                "Applied: baseColorFactor/texture, alpha, double-sided and glTF sampler semantics",
            new Vector3(0f, 0f, 3.2f),
            Vector3.Zero,
            DirectionalLightEnabled: false),
        new(
            "ClearCoatTest",
            "GltfSamples/ClearCoatTest.glb",
            "Khronos Clear Coat Test",
            "Required KHR_materials_clearcoat isolated factor, roughness and normal-map gate.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Expected: top row compares base material with clearcoat factor/texture\n" +
                "Roughness rows broaden only the outer lobe; normal rows perturb only that layer",
            new Vector3(0f, 0f, 3.4f),
            Vector3.Zero,
            EnvironmentPath: "Hdri/artist_workshop_1k.hdr",
            DirectionalLightEnabled: false),
        new(
            "IORTestGrid",
            "GltfSamples/IORTestGrid.glb",
            "Khronos IOR Test Grid",
            "Required KHR_materials_ior isolated dielectric reflection/refraction gate.",
            "Official Khronos CC0 asset\n" +
                "Expected: increasing IOR strengthens Fresnel reflection and bends transmission\n" +
                "The IOR 1.0 reference has no dielectric interface reflection",
            new Vector3(0f, 0f, 3.3f),
            Vector3.Zero,
            EnvironmentPath: "Hdri/artist_workshop_1k.hdr",
            DirectionalLightEnabled: false),
        new(
            "TransmissionTest",
            "GltfSamples/TransmissionTest.glb",
            "Khronos Transmission Test",
            "Required KHR_materials_transmission factor and texture gate.",
            "Official Khronos CC0 asset\n" +
                "Expected: authored factor and texture columns reveal the background while retaining Fresnel\n" +
                "Beauty uses the HDR screen-space background pyramid; Transmission isolates its contribution",
            new Vector3(0f, 0f, 3.2f),
            Vector3.Zero,
            EnvironmentPath: "Hdri/artist_workshop_1k.hdr",
            DirectionalLightEnabled: false),
        new(
            "GlassHurricaneCandleHolder",
            "GltfSamples/GlassHurricaneCandleHolder.glb",
            "Khronos Glass Hurricane Candle Holder",
            "KHR_materials_volume thickness and colored attenuation holistic gate.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Expected: glass remains transparent with thickness-dependent colored absorption\n" +
                "Thickness zero removes volume distance while preserving the dielectric interface",
            new Vector3(0f, 0.75f, 2.8f),
            new Vector3(0f, 0.65f, 0f),
            EnvironmentPath: "Hdri/artist_workshop_1k.hdr",
            DirectionalLightEnabled: false),
        new(
            "TransmissionRoughnessTest",
            "GltfSamples/TransmissionRoughnessTest.glb",
            "Khronos Transmission Roughness Test",
            "KHR_materials_transmission + IOR + volume rough-refraction conformance gate.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Rows progress from IOR 1.0 to 2.42; columns increase authored roughness\n" +
                "Expected: IOR 1 remains sharp while higher IOR progressively blurs transmission\n" +
                "Expected: dielectric Fresnel reflection also strengthens as IOR rises\n" +
                "Raster gate: HDR screen-space refraction with a GPU-generated background mip pyramid",
            new Vector3(0.2f, 0f, 1.45f),
            new Vector3(0.2f, 0f, 0f),
            EnvironmentPath: "Hdri/artist_workshop_1k.hdr",
            DirectionalLightEnabled: false),
        new(
            "DiffuseTransmissionPlant",
            "GltfSamples/DiffuseTransmissionPlant.glb",
            "Khronos Diffuse Transmission Plant",
            "KHR_materials_diffuse_transmission thin-leaf backlighting with an authored color texture.",
            "Official Khronos CC BY 4.0 / source model CC0\n" +
                "Expected: front-lit leaves remain green while backlighting reveals red transmission and veins\n" +
                "Gate: A-channel factor, sRGB transmission color, opposite-hemisphere direct and IBL\n" +
                "Applied: animated KHR_lights_punctual firefly point lights\n" +
                "Specification status: Khronos Release Candidate",
            new Vector3(0f, 0.3f, 1.35f),
            new Vector3(0f, 0.22f, 0f),
            EnvironmentPath: "Hdri/artist_workshop_1k.hdr",
            DirectionalLightEnabled: false),
        new(
            "DispersionTest",
            "GltfSamples/DispersionTest.glb",
            "Khronos Dispersion Test",
            "Ratified KHR_materials_dispersion across IOR and dispersion grids.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Rows vary IOR from 1.0 through diamond-like 2.42; columns vary dispersion 0 through 5\n" +
                "Expected: chromatic separation increases with both authored dispersion and IOR\n" +
                "IOR zero compatibility mode intentionally suppresses dispersion",
            new Vector3(0.03f, 0.12f, 0.52f),
            new Vector3(0.03f, 0.12f, 0f),
            EnvironmentPath: "Hdri/artist_workshop_1k.hdr",
            DirectionalLightEnabled: false),
        new(
            "MaterialsVariantsShoe",
            "GltfSamples/MaterialsVariantsShoe.glb",
            "Khronos Materials Variants Shoe",
            "Ratified KHR_materials_variants holistic runtime material switching.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Use the authored material-variant picker to switch midnight, beach and street\n" +
                "Geometry stays shared; reset restores the primitive's default glTF material\n" +
                "All candidate shader variants are prewarmed before first presentation",
            new Vector3(0f, 0f, 3f),
            Vector3.Zero,
            EnvironmentPath: "Hdri/artist_workshop_1k.hdr",
            DirectionalLightEnabled: false),
        new(
            "AnisotropyBarnLamp KTX2",
            "GltfSamples/AnisotropyBarnLampKtx/AnisotropyBarnLamp.gltf",
            "Khronos AnisotropyBarnLamp KTX2 advanced dielectric gate",
            "External GLTF/BIN/KTX2 + compressed BaseColor/Normal/ORM, clearcoat, anisotropy and " +
                "screen-space HDR transmission/volume.",
            "Official Khronos CC BY 4.0 asset\n" +
                "Required: KHR_texture_basisu\n" +
                "Applied: metallic/roughness + clearcoat + anisotropy direction/strength\n" +
                "Applied: KHR_materials_emissive_strength = 25\n" +
                "Applied: KHR_materials_transmission + volume + default IOR 1.5\n" +
                "Expected: the HDR filament remains visible through refractive absorbing glass",
            new Vector3(0.28f, -0.23f, 0.38f),
            new Vector3(-0.01f, -0.146f, 0.133f),
            ExternalResources:
            [
                "AnisotropyBarnLamp.bin",
                "AnisotropyBarnLamp_normalbump.ktx2",
                "AnisotropyBarnLamp_occlusionroughnessmetal.ktx2",
                "AnisotropyBarnLamp_basecolor.ktx2",
                "AnisotropyBarnLamp_anisotropy.ktx2",
            ],
            UseKtx: true),
    ];
}

internal sealed record ModelSpec(
        string DisplayName,
        string PackagePath,
        string SceneName,
        string GateDescription,
        string Details,
        Vector3 CameraPosition,
        Vector3 CameraTarget,
        bool AddAmbientOcclusionProbe = false,
        bool ApplyAllAnimations = false,
        bool AddDiagnosticGround = false,
        IReadOnlyList<string>? ExternalResources = null,
        bool UseKtx = false,
        string? EnvironmentPath = null,
        bool DirectionalLightEnabled = true);

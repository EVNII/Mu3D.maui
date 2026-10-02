// Mu3D Fast mode (ADR 0030): raster visibility, pinned-closure direct lighting and split-sum IBL.
// No BVH, media stack or path tracing; connected inputs arrive as baked mipmapped textures.
// Maintainer source. Consumer builds load the committed WGSL.
#define OPENPBR_LANGUAGE_TARGET_SLANG 1
#define OPENPBR_ENERGY_TABLES_USE_UINT16 0
#include "lut-buffer.slang"
#include "openpbr.h"

[[vk::binding(0, 0)]] cbuffer Frame { float4 frame[20]; }
[[vk::binding(1, 0)]] StructuredBuffer<float4> triangles;
[[vk::binding(2, 0)]] StructuredBuffer<float4> materials;
[[vk::binding(3, 0)]] StructuredBuffer<float4> lights;
// Per-material bake headers, then two words per baked entry.
[[vk::binding(4, 0)]] StructuredBuffer<float4> bakeInfo;

// Rasterized perspective-correct barycentrics and triangle index + 1; zero means background.
[[vk::binding(0, 2)]] Texture2D<float4> primaryVisibility;
[[vk::binding(1, 2)]] TextureCube<float4> environmentDiffuse;
[[vk::binding(2, 2)]] TextureCubeArray<float4> environmentSpecular;
[[vk::binding(3, 2)]] Texture2D<float4> environmentBrdf;
[[vk::binding(4, 2)]] SamplerState environmentSampler;
[[vk::binding(5, 2)]] Texture2D<float4> baked0;
[[vk::binding(6, 2)]] Texture2D<float4> baked1;
[[vk::binding(7, 2)]] Texture2D<float4> baked2;
[[vk::binding(8, 2)]] Texture2D<float4> baked3;
[[vk::binding(9, 2)]] Texture2D<float4> baked4;
[[vk::binding(10, 2)]] Texture2D<float4> baked5;
[[vk::binding(11, 2)]] Texture2D<float4> baked6;
[[vk::binding(12, 2)]] Texture2D<float4> baked7;
[[vk::binding(13, 2)]] SamplerState bakeSampler;
#include "shadow.slang"

OpenPBR_Basis make_basis(float3 n, float3 t, float handedness)
{
    t -= n * dot(n, t);
    return dot(t, t) > 1e-15 ? openpbr_make_basis(n, t, handedness < 0 ? -1.0 : 1.0) : openpbr_make_basis(n);
}

OpenPBR_ResolvedInputs material_inputs(float4 p[19])
{
    OpenPBR_ResolvedInputs s = openpbr_make_default_resolved_inputs();
    s.base_color = p[0].xyz; s.base_weight = p[0].w;
    s.specular_color = p[1].xyz; s.base_metalness = p[1].w;
    s.base_diffuse_roughness = p[2].x; s.specular_weight = p[2].y; s.specular_roughness = p[2].z; s.specular_ior = p[2].w;
    s.specular_roughness_anisotropy = p[3].x; s.transmission_weight = p[3].y; s.transmission_depth = p[3].z; s.transmission_scatter_anisotropy = p[3].w;
    s.transmission_color = p[4].xyz; s.transmission_dispersion_scale = p[4].w;
    s.transmission_scatter = p[5].xyz; s.transmission_dispersion_abbe_number = p[5].w;
    s.subsurface_color = p[6].xyz; s.subsurface_weight = p[6].w;
    s.subsurface_radius_scale = p[7].xyz; s.subsurface_radius = p[7].w;
    s.subsurface_scatter_anisotropy = p[8].x; s.fuzz_weight = p[8].y; s.fuzz_roughness = p[8].z; s.coat_weight = p[8].w;
    s.fuzz_color = p[9].xyz; s.coat_roughness = p[9].w;
    s.coat_color = p[10].xyz; s.coat_ior = p[10].w;
    s.coat_roughness_anisotropy = p[11].x; s.coat_darkening = p[11].y; s.thin_film_weight = p[11].z; s.thin_film_thickness = p[11].w;
    s.thin_film_ior = p[12].x; s.emission_luminance = p[12].y; s.geometry_opacity = p[12].z; s.geometry_thin_walled = p[12].w > 0;
    s.emission_color = p[13].xyz;
    return s;
}

// Deterministic central-direction lights, matching the raster mode's direct-lighting semantics.
float3 sample_light(float3 position, int light, out float3 direction)
{
    float4 p = lights[light * 4], color = lights[light * 4 + 1], extra = lights[light * 4 + 2];
    if (p.w == 0) { direction = p.xyz; return color.xyz; }
    direction = p.xyz - position; float distance = length(direction);
    if (distance <= 0 || distance > color.w) { direction = float3(0, 0, 1); return float3(0); }
    direction /= distance;
    float attenuation = 1 / (distance * distance);
    if (p.w == 2)
    {
        float cosine = dot(-direction, extra.xyz), outer = lights[light * 4 + 3].x;
        float spot = extra.w > outer ? saturate((cosine - outer) / (extra.w - outer)) : (cosine >= outer ? 1.0 : 0.0);
        attenuation *= spot * spot;
    }
    return color.xyz * attenuation;
}

float3 environment_direction(float3 d)
{
    return normalize(float3(dot(d, frame[10].xyz), dot(d, frame[11].xyz), dot(d, frame[12].xyz)));
}
// Prefiltered radiance plus the constant EnvironmentRadiance term: the prefilter of a uniform
// environment is the environment itself, so the constant adds exactly once per lobe query.
float3 ggx_environment(float3 direction, float roughness)
{
    float3 result = frame[9].xyz;
    if (frame[13].w != 0)
        result += environmentSpecular.SampleLevel(environmentSampler,
            float4(environment_direction(direction), 0.0), roughness * frame[14].z).rgb * frame[13].z;
    return result;
}
float3 charlie_environment(float3 direction, float roughness)
{
    float3 result = frame[9].xyz;
    if (frame[13].w != 0)
        result += environmentSpecular.SampleLevel(environmentSampler,
            float4(environment_direction(direction), 1.0), roughness * frame[14].z).rgb * frame[13].z;
    return result;
}
float3 diffuse_environment(float3 normal)
{
    float3 result = frame[9].xyz;
    if (frame[13].w != 0)
        result += environmentDiffuse.SampleLevel(environmentSampler, environment_direction(normal), 0).rgb * frame[13].z;
    return result;
}

float4 fast_shade(int triangle, float3 bary, float3 direction)
{
    // All fragment lanes compute UV gradients before the visibility-dependent return.
    // Explicit gradients keep mip filtering valid when background lanes leave the shader.
    int uvBase = max(triangle, 0) * 12;
    float4 uv = float4(0);
    if (frame[7].x > 0)
        uv = triangles[uvBase + 9] * bary.x + triangles[uvBase + 10] * bary.y + triangles[uvBase + 11] * bary.z;
    float4 uvDx = ddx(uv), uvDy = ddy(uv);
    if (triangle < 0) return float4(ggx_environment(direction, 0) * frame[9].w, frame[9].w);
    int base = triangle * 12;
    int material = int(triangles[base].w);
    float3 geometricNormal = normalize(cross(triangles[base + 1].xyz - triangles[base].xyz,
                                             triangles[base + 2].xyz - triangles[base].xyz));
    float3 position = triangles[base].xyz * bary.x + triangles[base + 1].xyz * bary.y + triangles[base + 2].xyz * bary.z;
    float3 interpolated = triangles[base + 3].xyz * bary.x + triangles[base + 4].xyz * bary.y + triangles[base + 5].xyz * bary.z;
    float3 normal = dot(interpolated, interpolated) > 1e-20 ? normalize(interpolated) : geometricNormal;
    float4 tangent = triangles[base + 6] * bary.x + triangles[base + 7] * bary.y + triangles[base + 8] * bary.z;

    float4 p[19];
    for (int i = 0; i < 19; ++i) p[i] = materials[material * 19 + i];
    float nits = p[18].x;

    // Baked inputs: keep the coordinates and gradients on the same authored UV set.
    float4 header = bakeInfo[material];
    int entryStart = int(header.x), entryCount = int(header.y);
    float2 texUv[8], texDx[8], texDy[8];
    for (int t = 0; t < 8; ++t) { texUv[t] = uv.xy; texDx[t] = uvDx.xy; texDy[t] = uvDy.xy; }
    for (int e = 0; e < entryCount; ++e)
    {
        float4 w0 = bakeInfo[entryStart + e * 2];
        if ((int(w0.w) & 1) != 0)
        {
            int texture = int(w0.z);
            texUv[texture] = uv.zw; texDx[texture] = uvDx.zw; texDy[texture] = uvDy.zw;
        }
    }
    float4 baked[8];
    baked[0] = baked0.SampleGrad(bakeSampler, texUv[0], texDx[0], texDy[0]);
    baked[1] = baked1.SampleGrad(bakeSampler, texUv[1], texDx[1], texDy[1]);
    baked[2] = baked2.SampleGrad(bakeSampler, texUv[2], texDx[2], texDy[2]);
    baked[3] = baked3.SampleGrad(bakeSampler, texUv[3], texDx[3], texDy[3]);
    baked[4] = baked4.SampleGrad(bakeSampler, texUv[4], texDx[4], texDy[4]);
    baked[5] = baked5.SampleGrad(bakeSampler, texUv[5], texDx[5], texDy[5]);
    baked[6] = baked6.SampleGrad(bakeSampler, texUv[6], texDx[6], texDy[6]);
    baked[7] = baked7.SampleGrad(bakeSampler, texUv[7], texDx[7], texDy[7]);
    for (int e = 0; e < entryCount; ++e)
    {
        float4 w0 = bakeInfo[entryStart + e * 2], w1 = bakeInfo[entryStart + e * 2 + 1];
        int slot = int(w0.x), channel = int(w0.y);
        float4 value = baked[int(w0.z)];
        if ((int(w0.w) & 2) != 0)
        {
            // Tangent normal map: the baked texture holds the [0,1] data; the hit frame decodes it here.
            float3 n = value.xyz * 2 - 1; n.xy *= w1.x;
            OpenPBR_Basis frameBasis = make_basis(normal, tangent.xyz, tangent.w);
            float3 world = frameBasis.t * n.x + frameBasis.b * n.y + frameBasis.n * n.z;
            p[slot] = float4(dot(world, world) > 1e-20 ? normalize(world) : normal, 1);
        }
        else if (channel < 0)
        {
            p[slot] = float4(value.xyz, slot >= 14 ? (dot(value.xyz, value.xyz) > 1e-20 ? 1 : 0) : p[slot].w);
        }
        else p[slot][channel] = value.x;
    }
    float3 view = -direction;
    if (p[14].w > 0) normal = normalize(p[14].xyz);
    if (dot(normal, geometricNormal) < 0) normal = -normal;
    if (dot(view, normal) * dot(view, geometricNormal) <= 0) normal = geometricNormal;
    if (p[15].w > 0) tangent.xyz = p[15].xyz;
    float3 coatNormal = p[16].w > 0 ? normalize(p[16].xyz) : normal;
    float3 coatTangent = p[17].w > 0 ? p[17].xyz : tangent.xyz;
    if (dot(coatNormal, geometricNormal) < 0) coatNormal = -coatNormal;
    if (dot(view, coatNormal) * dot(view, geometricNormal) <= 0) coatNormal = geometricNormal;

    OpenPBR_ResolvedInputs inputs = material_inputs(p);
    inputs.geometry_basis = make_basis(normal, tangent.xyz, tangent.w);
    inputs.geometry_coat_basis = make_basis(coatNormal, coatTangent, tangent.w);

    // Dropped lobes (reported per surface): subsurface, thin film, dispersion.
    OpenPBR_ResolvedInputs direct = inputs;
    direct.subsurface_weight = 0;
    direct.thin_film_weight = 0;
    direct.transmission_dispersion_scale = 0;

    OpenPBR_PreparedBsdf prepared = openpbr_prepare(direct, float3(1), OpenPBR_BaseRgbWavelengths_nm, 1.0, view);
    float3 result = prepared.emission / nits;
    int lightCount = int(frame[7].z);
    for (int light = 0; light < lightCount; ++light)
    {
        float3 ld; float3 li = sample_light(position, light, ld);
        if ((dot(ld, geometricNormal) * dot(view, geometricNormal) > 0) !=
            (dot(ld, inputs.geometry_basis.n) * dot(view, inputs.geometry_basis.n) > 0)) continue;
        result += li * directional_visibility(light, position, geometricNormal, ld) * openpbr_get_sum_of_diffuse_specular(openpbr_eval(prepared, ld));
    }

    // Split-sum environment (ADR 0030 lobe mapping).
    float3 n = inputs.geometry_basis.n;
    float ndotv = saturate(dot(n, view));
    float metalness = inputs.base_metalness;
    float3 baseColor = inputs.base_color * inputs.base_weight;
    float ior = max(inputs.specular_ior, 1.0001);
    float f0Dielectric = pow((ior - 1) / (ior + 1), 2);
    float3 specularF0 = lerp(inputs.specular_color * inputs.specular_weight * f0Dielectric, baseColor, metalness);
    float3 specularF90 = lerp(float3(inputs.specular_weight), float3(1), metalness);
    float roughness = saturate(inputs.specular_roughness);
    // Isotropic chain: bend the reflection toward the tangent frame's anisotropy, as the Core renderer does.
    float anisotropy = abs(inputs.specular_roughness_anisotropy);
    float3 bent = normalize(cross(cross(inputs.geometry_basis.b, view), inputs.geometry_basis.b));
    float bend = pow(1 - anisotropy * (1 - roughness), 4);
    bent = normalize(lerp(bent, n, bend));
    float3 reflection = normalize(lerp(reflect(-view, bent), bent, roughness * roughness));
    float3 prefiltered = ggx_environment(reflection, roughness);
    // The BRDF lookup has one mip; it does not use screen-space derivatives.
    float2 brdf = environmentBrdf.SampleLevel(environmentSampler, float2(ndotv, roughness), 0).rg;
    float3 specularEnv = prefiltered * (specularF0 * brdf.x + specularF90 * brdf.y);
    float3 diffuseAttenuation = (1 - (specularF0 * brdf.x + specularF90 * brdf.y)) * (1 - metalness);
    float3 diffuseEnv = diffuse_environment(n) * diffuseAttenuation * baseColor;
    // Fuzz environment through the Charlie sheen chain (pinned fuzz closure still applies to direct light).
    float fuzzRoughness = max(inputs.fuzz_roughness, 0.07);
    float3 fuzzEnv = inputs.fuzz_weight * inputs.fuzz_color *
        charlie_environment(reflect(-view, n), fuzzRoughness) *
        environmentBrdf.SampleLevel(environmentSampler, float2(ndotv, fuzzRoughness), 0).b;
    // Coat environment through the base GGX chain at coat roughness.
    float3 coatN = inputs.geometry_coat_basis.n;
    float coatNdotV = saturate(dot(coatN, view));
    float coatRoughness = saturate(inputs.coat_roughness);
    float coatIor = max(inputs.coat_ior, 1.0001);
    float coatF0 = pow((coatIor - 1) / (coatIor + 1), 2);
    float2 coatBrdf = environmentBrdf.SampleLevel(environmentSampler, float2(coatNdotV, coatRoughness), 0).rg;
    float3 coatEnergy = coatF0 * coatBrdf.x + coatBrdf.y;
    float3 coatEnv = inputs.coat_weight * inputs.coat_color *
        ggx_environment(reflect(-view, coatN), coatRoughness) * coatEnergy;
    float3 coatAttenuation = 1 - inputs.coat_weight * coatEnergy;

    result += coatEnv + coatAttenuation * (specularEnv + diffuseEnv + fuzzEnv);
    return all(isfinite(result)) ? float4(result, 1) : float4(1, 0, 1, 1);
}

[shader("fragment")]
float4 main(float4 pixel : SV_Position) : SV_Target0
{
    uint2 p = uint2(pixel.xy);
    float2 ndc = (float2(p) + float2(.5)) / frame[6].xy * 2 - 1; ndc.y = -ndc.y;
    float4 nearPoint = frame[1] * ndc.x + frame[2] * ndc.y + frame[4];
    float3 nearWorld = nearPoint.xyz / nearPoint.w;
    float3 origin = frame[0].xyz;
    float3 direction = normalize(nearWorld - origin);
    float4 visibility = primaryVisibility.Load(int3(p, 0));
    return fast_shade(int(visibility.w) - 1, visibility.xyz, direction);
}

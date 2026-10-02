// Mu3D scene transport around the unmodified, pinned Adobe OpenPBR closure.
// Maintainer source. Consumer builds load the committed WGSL and FP32 lookup buffer.
#define OPENPBR_LANGUAGE_TARGET_SLANG 1
#define OPENPBR_ENERGY_TABLES_USE_UINT16 0
#include "lut-buffer.slang"
#include "openpbr.h"

[[vk::binding(0, 0)]] cbuffer Frame { float4 frame[20]; }
[[vk::binding(1, 0)]] StructuredBuffer<float4> triangles;
[[vk::binding(2, 0)]] StructuredBuffer<float4> nodes;
[[vk::binding(3, 0)]] StructuredBuffer<float4> materials;
[[vk::binding(4, 0)]] StructuredBuffer<float4> lights;
// CPU-validated nested camera media, outermost first: material index, object ID.
[[vk::binding(5, 0)]] StructuredBuffer<float4> initialMedia;
[[vk::binding(0, 2)]] Texture2D<float4> history;
[[vk::binding(1, 2)]] Texture2D<float4> environmentImage;
// Rasterized perspective-correct barycentrics and triangle index + 1; zero means background.
[[vk::binding(2, 2)]] Texture2D<float4> primaryVisibility;
#include "shadow.slang"

struct Hit { float distance; float3 bary; int triangle; };
struct Medium { OpenPBR_HomogeneousVolume volume; float ior; int object; };

uint hash(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; return x ^ (x >> 16); }
float random(inout uint state) { state = hash(state + 0x9e3779b9u); return float(state >> 8) * (1.0 / 16777216.0); }
float3 random3(inout uint state) { float x = random(state); float y = random(state); return float3(x, y, random(state)); }
float max_component(float3 v) { return max(v.x, max(v.y, v.z)); }
float3 hit_position(Hit hit)
{
    int base = hit.triangle * 12;
    return triangles[base].xyz * hit.bary.x + triangles[base + 1].xyz * hit.bary.y + triangles[base + 2].xyz * hit.bary.z;
}
float3 offset_hit(Hit hit, float3 n, float3 d)
{
    float sign = dot(n, d) < 0 ? -1 : 1;
    int base = hit.triangle * 12;
    float3 p = hit_position(hit);
    // Reconstruct from the triangle, avoiding cancellation in origin + t * direction.
    // gamma(7) bounds FP32 interpolation error projected onto the geometric normal.
    float3 error = 8.34466e-7 * (abs(triangles[base].xyz * hit.bary.x) +
        abs(triangles[base + 1].xyz * hit.bary.y) + abs(triangles[base + 2].xyz * hit.bary.z));
    float distance = dot(abs(n), error) + max(1e-3, max_component(abs(p))) * 2e-6;
    return p + n * (sign * distance);
}
bool bounds_hit(float3 origin, float3 direction, float3 minimum, float3 maximum, float nearT, float farT)
{
    for (int axis = 0; axis < 3; ++axis)
    {
        if (direction[axis] == 0) { if (origin[axis] < minimum[axis] || origin[axis] > maximum[axis]) return false; }
        else
        {
            float a = (minimum[axis] - origin[axis]) / direction[axis];
            float b = (maximum[axis] - origin[axis]) / direction[axis];
            nearT = max(nearT, min(a, b)); farT = min(farT, max(a, b));
            if (farT < nearT) return false;
        }
    }
    return true;
}
Hit intersect(float3 origin, float3 direction, float nearT, float farT)
{
    Hit hit; hit.distance = farT; hit.triangle = -1; hit.bary = float3(0);
    if (frame[7].y == 0) return hit;
    // Project onto the dominant ray axis. Shared edges then use the same two products
    // in opposite order, unlike independently rounded Moller-Trumbore u + v tests.
    // This matters for deterministic pixel-center and specular rays exactly on a diagonal.
    int kz = 0;
    if (abs(direction.y) > abs(direction[kz])) kz = 1;
    if (abs(direction.z) > abs(direction[kz])) kz = 2;
    int kx = (kz + 1) % 3, ky = (kx + 1) % 3;
    if (direction[kz] < 0) { int swap = kx; kx = ky; ky = swap; }
    float3 shear = float3(direction[kx], direction[ky], 1.0) / direction[kz];
    int stack[64]; int top = 1; stack[0] = 0;
    while (top > 0)
    {
        int index = stack[--top] * 2;
        float4 lo = nodes[index], hi = nodes[index + 1];
        if (!bounds_hit(origin, direction, lo.xyz, hi.xyz, nearT, hit.distance)) continue;
        if (hi.w >= 0) { stack[top++] = int(hi.w); stack[top++] = int(lo.w); continue; }
        int last = int(lo.w - hi.w);
        for (int t = int(lo.w); t < last; ++t)
        {
            float3 a = triangles[t * 12].xyz - origin;
            float3 b = triangles[t * 12 + 1].xyz - origin;
            float3 c = triangles[t * 12 + 2].xyz - origin;
            float2 ap = float2(a[kx], a[ky]) - shear.xy * a[kz];
            float2 bp = float2(b[kx], b[ky]) - shear.xy * b[kz];
            float2 cp = float2(c[kx], c[ky]) - shear.xy * c[kz];
            float u = cp.x * bp.y - cp.y * bp.x;
            float v = ap.x * cp.y - ap.y * cp.x;
            float w = bp.x * ap.y - bp.y * ap.x;
            if ((min(u, min(v, w)) < 0) && (max(u, max(v, w)) > 0)) continue;
            float determinant = u + v + w;
            if (determinant == 0) continue;
            float distance = dot(float3(u, v, w), float3(a[kz], b[kz], c[kz]) * shear.z) / determinant;
            if (distance >= nearT && distance < hit.distance)
            { hit.distance = distance; hit.triangle = t; hit.bary = float3(u, v, w) / determinant; }
        }
    }
    return hit;
}

OpenPBR_Basis make_basis(float3 n, float3 t, float handedness)
{
    t -= n * dot(n, t);
    return dot(t, t) > 1e-15 ? openpbr_make_basis(n, t, handedness < 0 ? -1.0 : 1.0) : openpbr_make_basis(n);
}
#include "material-graph.slang"
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

OpenPBR_ResolvedInputs resolve_material(Hit hit, float3 view, out float3 geometricNormal, out int object, out float nits)
{
    int base = hit.triangle * 12;
    int material = int(triangles[base].w), offset = material * 19;
    object = int(triangles[base + 1].w); nits = materials[offset + 18].x;
    geometricNormal = normalize(cross(triangles[base + 1].xyz - triangles[base].xyz,
                                     triangles[base + 2].xyz - triangles[base].xyz));
    float3 interpolated = triangles[base + 3].xyz * hit.bary.x + triangles[base + 4].xyz * hit.bary.y + triangles[base + 5].xyz * hit.bary.z;
    float3 normal = dot(interpolated, interpolated) > 1e-20 ? normalize(interpolated) : geometricNormal;
    float4 tangent = triangles[base + 6] * hit.bary.x + triangles[base + 7] * hit.bary.y + triangles[base + 8] * hit.bary.z;
    if (dot(tangent.xyz, tangent.xyz) < 1e-15)
    {
        float2 du = triangles[base + 10].xy - triangles[base + 9].xy;
        float2 dv = triangles[base + 11].xy - triangles[base + 9].xy;
        float determinant = du.x * dv.y - du.y * dv.x;
        if (abs(determinant) > 1e-15)
        {
            float3 edge1 = triangles[base + 1].xyz - triangles[base].xyz;
            float3 edge2 = triangles[base + 2].xyz - triangles[base].xyz;
            tangent.xyz = (edge1 * dv.y - edge2 * du.y) / determinant;
            float3 bitangent = (edge2 * du.x - edge1 * dv.x) / determinant;
            tangent.w = dot(cross(normal, tangent.xyz), bitangent) < 0 ? -1 : 1;
        }
    }
    float4 uv = triangles[base + 9] * hit.bary.x + triangles[base + 10] * hit.bary.y + triangles[base + 11] * hit.bary.z;
    float4 p[19]; load_material(material, uv, normal, tangent, p);
    if (p[14].w > 0) normal = normalize(p[14].xyz);
    if (dot(normal, geometricNormal) < 0) normal = -normal;
    if (dot(view, normal) * dot(view, geometricNormal) <= 0) normal = geometricNormal;
    if (p[15].w > 0) tangent.xyz = p[15].xyz;
    float3 coatNormal = p[16].w > 0 ? normalize(p[16].xyz) : normal;
    float3 coatTangent = p[17].w > 0 ? p[17].xyz : tangent.xyz;
    if (dot(coatNormal, geometricNormal) < 0) coatNormal = -coatNormal;
    if (dot(view, coatNormal) * dot(view, geometricNormal) <= 0) coatNormal = geometricNormal;
    OpenPBR_ResolvedInputs s = material_inputs(p);
    s.geometry_basis = make_basis(normal, tangent.xyz, tangent.w);
    s.geometry_coat_basis = make_basis(coatNormal, coatTangent, tangent.w);
    return s;
}

// This reproduces the upstream's entering-side effective absolute IOR. The upstream
// still approximates coat behavior on exit and applies dispersion to relative IORs.
float entered_ior(OpenPBR_ResolvedInputs inputs, float exteriorIor)
{
    float coat = lerp(exteriorIor, inputs.coat_ior, inputs.coat_weight);
    return coat * openpbr_apply_specular_weight_to_ior(inputs.specular_ior / coat, inputs.specular_weight);
}

float3 environment(float3 direction)
{
    if (frame[13].w == 0) return frame[9].xyz;
    direction = normalize(float3(dot(direction, frame[10].xyz), dot(direction, frame[11].xyz), dot(direction, frame[12].xyz)));
    float2 uv = float2(atan2(direction.z, direction.x) / OpenPBR_TwoPi + .5, acos(clamp(direction.y, -1.0, 1.0)) / OpenPBR_Pi);
    int2 size = int2(frame[13].xy); float2 p = uv * float2(size) - .5;
    int2 a = int2(floor(p)); float2 f = p - float2(a);
    int x0 = (a.x % size.x + size.x) % size.x, x1 = (x0 + 1) % size.x;
    int y0 = clamp(a.y, 0, size.y - 1), y1 = clamp(a.y + 1, 0, size.y - 1);
    return lerp(lerp(environmentImage.Load(int3(x0, y0, 0)).xyz, environmentImage.Load(int3(x1, y0, 0)).xyz, f.x),
                lerp(environmentImage.Load(int3(x0, y1, 0)).xyz, environmentImage.Load(int3(x1, y1, 0)).xyz, f.x), f.y) * frame[13].z + frame[9].xyz;
}
float power_weight(float a, float b) { return a * a / max(a * a + b * b, 1e-30); }

// Stochastic opacity is independent from BSDF transmission. Interior refraction is sampled by the
// transport walk; a direct connection does not pretend refractive boundaries are empty space.
float visibility(float3 origin, float3 direction, float distance, inout uint rng)
{
    for (int skip = 0; skip < 64; ++skip)
    {
        Hit h = intersect(origin, direction, 0, distance);
        if (h.triangle < 0) return 1;
        float3 gn; int object; float nits;
        OpenPBR_ResolvedInputs blocker = resolve_material(h, -direction, gn, object, nits);
        if (random(rng) < blocker.geometry_opacity) return 0;
        float3 n = normalize(cross(triangles[h.triangle * 12 + 1].xyz - triangles[h.triangle * 12].xyz,
                                  triangles[h.triangle * 12 + 2].xyz - triangles[h.triangle * 12].xyz));
        origin = offset_hit(h, n, direction);
        distance -= h.distance;
    }
    return 0;
}
float3 sample_light(float3 position, int light, inout uint rng, out float3 direction, out float distance, out float pdf)
{
    float4 p = lights[light * 4], color = lights[light * 4 + 1], extra = lights[light * 4 + 2];
    pdf = 1; distance = 1e30;
    if (p.w == 0)
    {
        direction = p.xyz;
        // Raster's deterministic approximation treats a finite sun as its central delta light.
        if (frame[8].z == 2) return color.xyz;
        if (extra.w < 1)
        {
            float z = lerp(extra.w, 1.0, random(rng)), phi = OpenPBR_TwoPi * random(rng);
            OpenPBR_Basis basis = openpbr_make_basis(direction);
            direction = normalize(basis.n * z + sqrt(max(0.0, 1 - z * z)) * (basis.t * cos(phi) + basis.b * sin(phi)));
            pdf = 1.0 / (OpenPBR_TwoPi * (1 - extra.w));
            return color.xyz / (OpenPBR_Pi * (1 - extra.w * extra.w));
        }
        return color.xyz;
    }
    direction = p.xyz - position; distance = length(direction);
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
float3 escape_radiance(float3 direction, float previousPdf, bool previousDelta)
{
    float3 result = environment(direction);
    int count = int(frame[7].z);
    for (int i = 0; i < count; ++i)
    {
        float4 p = lights[i * 4], extra = lights[i * 4 + 2];
        if (p.w != 0 || extra.w >= 1 || dot(direction, p.xyz) < extra.w) continue;
        float pdf = 1 / (count * OpenPBR_TwoPi * (1 - extra.w));
        float weight = previousDelta ? 1.0 : power_weight(previousPdf, pdf);
        result += weight * lights[i * 4 + 1].xyz / (OpenPBR_Pi * (1 - extra.w * extra.w));
    }
    return result;
}

float4 trace(float3 origin, float3 direction, float nearT, float farT, inout uint rng, Hit primary, bool usePrimary)
{
    float3 radiance = float3(0), throughput = float3(1);
    // Starting inside a validated volume already gives full geometric coverage, including
    // paths absorbed before reaching its first surface. Absorption must not become a hole.
    float alpha = frame[15].y > 0 ? 1.0 : 0.0, previousPdf = 0; bool previousDelta = true;
    Medium media[8]; int mediumCount = int(frame[15].y);
    for (int initial = 0; initial < mediumCount; ++initial)
    {
        float4 p[19]; load_material(int(initialMedia[initial].x), float4(0), float3(0, 0, 1), float4(1, 0, 0, 1), p);
        OpenPBR_ResolvedInputs inputs = material_inputs(p);
        OpenPBR_VolumeDerivedProps volumeProperties; OpenPBR_PreparedBsdf prepared;
        openpbr_prepare_volume(inputs, volumeProperties, prepared, true);
        media[initial].object = int(initialMedia[initial].y);
        media[initial].ior = entered_ior(inputs, initial > 0 ? media[initial - 1].ior : 1.0);
        media[initial].volume = prepared.volume;
    }
    float3 wavelengths = OpenPBR_BaseRgbWavelengths_nm;
    int lightCount = int(frame[7].z), maxBounces = int(frame[8].x);
    int depth = 0, cutouts = 0;
    while (depth < maxBounces)
    {
        Hit hit = usePrimary ? primary : intersect(origin, direction, nearT, farT);
        usePrimary = false; // Opacity cutouts and every secondary segment return to the BVH.
        float3 geometricNormal = float3(0, 0, 1); int object = -1; float nits = 1;
        OpenPBR_ResolvedInputs inputs = openpbr_make_default_resolved_inputs();
        bool front = true; float exteriorIor = 1.0;
        if (hit.triangle >= 0)
        {
            inputs = resolve_material(hit, -direction, geometricNormal, object, nits);
            front = dot(direction, geometricNormal) < 0;
            exteriorIor = mediumCount > 0 ? media[mediumCount - 1].ior : 1.0;
            if (!front && mediumCount > 0 && media[mediumCount - 1].object == object)
                exteriorIor = mediumCount > 1 ? media[mediumCount - 2].ior : 1.0;
        }
        if (mediumCount > 0 && openpbr_is_homogeneous_volume(media[mediumCount - 1].volume))
        {
            OpenPBR_HomogeneousVolume volume = media[mediumCount - 1].volume;
            float eventDistance = 1e30;
            openpbr_sample_event_distance(volume, throughput, random(rng), eventDistance);
            if (eventDistance < hit.distance)
            {
                alpha = 1;
                throughput *= openpbr_calculate_weight_for_event_at_distance(volume, throughput, eventDistance);
                if (!all(isfinite(throughput)) || max_component(throughput) <= 0) break;
                origin += direction * eventDistance; alpha = 1;
                if (lightCount > 0)
                {
                    int light = min(int(random(rng) * lightCount), lightCount - 1);
                    float3 ld; float distance, pdf; float3 li = sample_light(origin, light, rng, ld, distance, pdf);
                    float phase = openpbr_calculate_anisotropic_phase_function_value(volume, -direction, ld);
                    bool delta = lights[light * 4].w != 0 || lights[light * 4 + 2].w >= 1;
                    float weight = delta ? 1.0 : power_weight(pdf / lightCount, phase);
                    radiance += throughput * li * (phase * weight * lightCount / pdf) *
                        openpbr_calculate_transmittance_at_distance(volume, distance) * visibility(origin, ld, distance, rng);
                }
                float3 oldDirection = direction;
                direction = normalize(openpbr_sample_anisotropic_phase_function(volume, -oldDirection, random3(rng).xy));
                previousPdf = openpbr_calculate_anisotropic_phase_function_pdf(volume, -oldDirection, direction);
                previousDelta = false; nearT = 0; farT = 1e30; ++depth;
                if (depth >= int(frame[8].y))
                {
                    float survival = min(.95, max_component(throughput));
                    if (survival <= 0 || random(rng) >= survival) break;
                    throughput /= survival;
                }
                continue;
            }
            throughput *= openpbr_calculate_weight_for_surface_at_distance(volume, throughput, hit.distance);
            if (!all(isfinite(throughput)) || max_component(throughput) <= 0) break;
        }
        if (hit.triangle < 0)
        {
            float backgroundAlpha = alpha > 0 ? 1.0 : frame[9].w;
            radiance += throughput * escape_radiance(direction, previousPdf, previousDelta) * backgroundAlpha;
            alpha = max(alpha, backgroundAlpha); break;
        }
        float3 position = hit_position(hit);
        if (random(rng) >= inputs.geometry_opacity)
        {
            if (++cutouts >= 64) break;
            // A cutout exit still ends an interior segment. Leaving the previous
            // medium on the stack would incorrectly absorb light outside the object.
            if (!inputs.geometry_thin_walled && !front && mediumCount > 0 && media[mediumCount - 1].object == object)
                --mediumCount;
            origin = offset_hit(hit, geometricNormal, direction); nearT = 0;
            farT = max(0.0, farT - hit.distance); continue;
        }
        cutouts = 0; alpha = 1;
        // Prepare after segment attenuation: sampling probabilities use the current
        // path throughput, and a rejected opacity sample never evaluates the BSDF.
        OpenPBR_PreparedBsdf prepared = openpbr_prepare(inputs, throughput, wavelengths, exteriorIor, -direction);
        radiance += throughput * prepared.emission / nits;
        if (lightCount > 0)
        {
            int light = min(int(random(rng) * lightCount), lightCount - 1);
            float3 ld; float distance, pdf; float3 li = sample_light(position, light, rng, ld, distance, pdf);
            float3 value = openpbr_get_sum_of_diffuse_specular(openpbr_eval(prepared, ld));
            // Prevent a perturbed shading frame from sending reflected energy through the
            // geometric boundary (or transmitted energy into the reflection hemisphere).
            if ((dot(ld, geometricNormal) * dot(-direction, geometricNormal) > 0) !=
                (dot(ld, inputs.geometry_basis.n) * dot(-direction, inputs.geometry_basis.n) > 0)) value = float3(0);
            bool delta = lights[light * 4].w != 0 || lights[light * 4 + 2].w >= 1;
            float weight = delta ? 1.0 : power_weight(pdf / lightCount, openpbr_pdf(prepared, ld));
            float3 start = offset_hit(hit, geometricNormal, ld);
            OpenPBR_HomogeneousVolume connectionVolume = openpbr_make_empty_volume();
            if (!inputs.geometry_thin_walled && dot(ld, geometricNormal) < 0) connectionVolume = prepared.volume;
            else
            {
                int exteriorIndex = mediumCount - 1;
                if (!front && exteriorIndex >= 0 && media[exteriorIndex].object == object) --exteriorIndex;
                if (exteriorIndex >= 0) connectionVolume = media[exteriorIndex].volume;
            }
            float3 attenuation = openpbr_calculate_transmittance_at_distance(connectionVolume.extinction_coefficient, distance);
            radiance += throughput * li * value * attenuation * (weight * lightCount / pdf) * visibility(start, ld, distance, rng);
        }
        float3 next; OpenPBR_DiffuseSpecular weight; float pdf; OpenPBR_BsdfLobeType type;
        openpbr_sample(prepared, random3(rng), next, weight, pdf, type);
        if (pdf <= 0) break;
        next = normalize(next);
        bool crossesBoundary = dot(direction, geometricNormal) * dot(next, geometricNormal) > 0;
        bool transmission = (type & OpenPBR_BsdfLobeTypeTransmission) != 0;
        if (transmission != crossesBoundary) break;
        throughput *= openpbr_get_sum_of_diffuse_specular(weight);
        previousPdf = pdf; previousDelta = (type & OpenPBR_BsdfLobeTypeSpecular) != 0;
        if (!inputs.geometry_thin_walled && transmission)
        {
            if (front)
            {
                if (mediumCount >= 8) return float4(1, 0, 1, 1);
                media[mediumCount].object = object; media[mediumCount].volume = prepared.volume;
                media[mediumCount].ior = entered_ior(inputs, exteriorIor); ++mediumCount;
            }
            else if (mediumCount > 0 && media[mediumCount - 1].object == object) --mediumCount;
        }
        else if (!front && mediumCount > 0 && media[mediumCount - 1].object == object)
            media[mediumCount - 1].volume = prepared.volume;
        origin = offset_hit(hit, geometricNormal, next); direction = next; nearT = 0; farT = 1e30; ++depth;
        if (!all(isfinite(throughput))) return float4(1, 0, 1, 1);
        if (depth >= int(frame[8].y))
        {
            float survival = min(.95, max_component(throughput));
            if (survival <= 0 || random(rng) >= survival) break;
            throughput /= survival;
        }
    }
    return float4(radiance, alpha);
}

float4 raster_shade(Hit hit, float3 direction)
{
    if (hit.triangle < 0) return float4(environment(direction) * frame[9].w, frame[9].w);
    float3 normal; int object; float nits;
    OpenPBR_ResolvedInputs inputs = resolve_material(hit, -direction, normal, object, nits);
    OpenPBR_PreparedBsdf prepared = openpbr_prepare(inputs, float3(1), OpenPBR_BaseRgbWavelengths_nm, 1.0, -direction);
    float3 result = prepared.emission / nits;
    float3 position = hit_position(hit);
    uint rng = 1;
    for (int light = 0; light < int(frame[7].z); ++light)
    {
        float3 ld; float distance, pdf;
        float3 li = sample_light(position, light, rng, ld, distance, pdf);
        if ((dot(ld, normal) * dot(-direction, normal) > 0) !=
            (dot(ld, inputs.geometry_basis.n) * dot(-direction, inputs.geometry_basis.n) > 0)) continue;
        result += li * directional_visibility(light, position, normal, ld) * openpbr_get_sum_of_diffuse_specular(openpbr_eval(prepared, ld));
    }
    // Fixed BSDF quadrature: stable across frames/pixels, no secondary visibility or interreflection.
    // Unlike random per-pixel sampling it has deterministic integration error, especially for small HDR emitters.
    int count = int(frame[8].w);
    for (int i = 0; i < count; ++i)
    {
        float3 u = float3((i + .5) / count, frac(.5 + i * .754877666), frac(.5 + i * .569840296));
        float3 ld; OpenPBR_DiffuseSpecular weight; float pdf; OpenPBR_BsdfLobeType type;
        openpbr_sample(prepared, u, ld, weight, pdf, type);
        if (pdf > 0 && dot(ld, normal) * dot(-direction, normal) > 0)
            result += environment(ld) * openpbr_get_sum_of_diffuse_specular(weight) / count;
    }
    return all(isfinite(result)) ? float4(result, 1) : float4(1, 0, 1, 1);
}

[shader("fragment")]
float4 main(float4 pixel : SV_Position) : SV_Target0
{
    uint2 p = uint2(pixel.xy); uint sampleIndex = uint(frame[6].z);
    uint rng = hash(p.x + p.y * uint(frame[6].x) + hash(sampleIndex + uint(frame[15].x)));
    float2 jitter = float2(random(rng), random(rng));
    bool rasterPrimary = frame[8].z >= 2;
    if (rasterPrimary) jitter = float2(.5);
    float2 ndc = (float2(p) + jitter) / frame[6].xy * 2 - 1; ndc.y = -ndc.y;
    float4 nearPoint = frame[1] * ndc.x + frame[2] * ndc.y + frame[4];
    float3 nearWorld = nearPoint.xyz / nearPoint.w;
    // CPU validates a perspective camera and initializes its nested media. Transport
    // starts at the camera so a near-plane boundary cannot be silently skipped.
    float3 origin = frame[0].xyz;
    float3 direction = normalize(nearWorld - origin);
    // Explicit view-space far-plane distance avoids inaccurate unprojection close to depth 1.
    float farT = frame[5].w / dot(direction, frame[5].xyz);
    Hit primary; primary.triangle = -1; primary.bary = float3(0); primary.distance = farT;
    if (rasterPrimary && frame[15].z == 0)
    {
        float4 visibility = primaryVisibility.Load(int3(p, 0));
        primary.triangle = int(visibility.w) - 1; primary.bary = visibility.xyz;
        if (primary.triangle >= 0) primary.distance = length(hit_position(primary) - origin);
    }
    float4 sample;
#if MU3D_RASTER_ONLY
    sample = raster_shade(primary, direction);
#else
    if (frame[8].z == 2) sample = raster_shade(primary, direction);
    else sample = trace(origin, direction, 0, farT, rng, primary, rasterPrimary && frame[15].z == 0);
#endif
    if (frame[7].w > 0 && sampleIndex > 0)
    {
        float4 previous = history.Load(int3(p, 0));
        return previous + (sample - previous) / float(sampleIndex + 1);
    }
    return sample;
}

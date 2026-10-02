// Mu3D numerical conformance inputs. Shared by the C++ oracle and Slang GPU probe.
// This file contains authored test inputs, not a replacement BSDF implementation.
#ifndef MU3D_OPENPBR_REFERENCE_CASES_H
#define MU3D_OPENPBR_REFERENCE_CASES_H

#define MU3D_OPENPBR_REFERENCE_CASE_COUNT 24

OpenPBR_ResolvedInputs mu3d_openpbr_case(int id)
{
    OpenPBR_ResolvedInputs p = openpbr_make_default_resolved_inputs();
    switch (id)
    {
    case 1: // Rough diffuse with disabled interface reflection.
        p.base_weight = 0.7f; p.base_color = vec3(0.7f, 0.2f, 0.08f);
        p.base_diffuse_roughness = 0.8f; p.specular_weight = 0.0f;
        break;
    case 2: // Metal uses F82 edge tint, not glTF specular tint.
        p.base_metalness = 1.0f; p.base_weight = 0.9f;
        p.base_color = vec3(0.9f, 0.55f, 0.25f);
        p.specular_color = vec3(0.3f, 0.7f, 0.95f); p.specular_weight = 0.85f;
        p.specular_roughness = 0.28f;
        break;
    case 3:
        p.specular_roughness = 0.45f; p.specular_roughness_anisotropy = 0.85f;
        p.geometry_basis = openpbr_make_basis(vec3(0.0f, 0.0f, 1.0f), normalize(vec3(1.0f, 1.0f, 0.0f)), 1.0f);
        break;
    case 4:
        p.base_color = vec3(0.4f, 0.15f, 0.05f); p.coat_weight = 0.8f;
        p.coat_color = vec3(0.8f, 0.9f, 1.0f); p.coat_ior = 1.7f;
        p.coat_roughness = 0.3f; p.coat_roughness_anisotropy = 0.6f; p.coat_darkening = 0.7f;
        break;
    case 5:
        p.base_color = vec3(0.12f, 0.08f, 0.04f); p.fuzz_weight = 0.9f;
        p.fuzz_color = vec3(0.95f, 0.3f, 0.1f); p.fuzz_roughness = 0.75f;
        break;
    case 6:
        p.base_color = vec3(0.3f, 0.6f, 0.1f); p.coat_weight = 0.6f;
        p.coat_roughness = 0.2f; p.coat_color = vec3(0.6f, 0.9f, 0.8f);
        p.fuzz_weight = 0.4f; p.fuzz_roughness = 0.65f; p.fuzz_color = vec3(0.8f, 0.7f, 0.5f);
        break;
    case 7:
        p.thin_film_weight = 1.0f; p.thin_film_thickness = 0.43f; p.thin_film_ior = 1.35f;
        p.specular_roughness = 0.15f;
        break;
    case 8:
        p.base_metalness = 1.0f; p.base_color = vec3(0.85f, 0.6f, 0.3f);
        p.thin_film_weight = 0.8f; p.thin_film_thickness = 0.7f; p.thin_film_ior = 1.45f;
        break;
    case 9:
        p.geometry_thin_walled = true; p.transmission_weight = 1.0f;
        p.transmission_color = vec3(0.3f, 0.75f, 0.95f); p.specular_roughness = 0.18f;
        break;
    case 10:
        p.emission_luminance = 350.0f; p.emission_color = vec3(0.4f, 1.0f, 0.2f);
        p.coat_weight = 0.75f; p.coat_color = vec3(0.8f, 0.5f, 0.9f); p.coat_roughness = 0.2f;
        p.fuzz_weight = 0.35f; p.fuzz_color = vec3(0.5f); p.fuzz_roughness = 0.6f;
        break;
    case 11:
        p.subsurface_weight = 1.0f; p.subsurface_color = vec3(0.85f, 0.45f, 0.25f);
        p.subsurface_radius = 0.012f; p.subsurface_radius_scale = vec3(1.0f, 0.4f, 0.15f);
        p.subsurface_scatter_anisotropy = 0.3f;
        break;
    case 12:
        p.transmission_weight = 1.0f; p.transmission_color = vec3(0.9f, 0.6f, 0.2f);
        p.specular_ior = 1.45f; p.specular_roughness = 0.25f;
        break;
    case 13:
        p.transmission_weight = 1.0f; p.transmission_color = vec3(0.7f, 0.5f, 0.3f);
        p.transmission_depth = 0.2f; p.transmission_scatter = vec3(0.12f, 0.3f, 0.2f);
        p.transmission_scatter_anisotropy = -0.35f;
        break;
    case 14:
        p.transmission_weight = 1.0f; p.specular_ior = 1.6f;
        p.transmission_dispersion_scale = 0.8f; p.transmission_dispersion_abbe_number = 24.0f;
        p.specular_roughness = 0.22f;
        break;
    case 15: // Deliberately non-default full-model mixture.
        p.base_weight = 0.82f; p.base_color = vec3(0.6f, 0.3f, 0.2f);
        p.base_diffuse_roughness = 0.4f; p.base_metalness = 0.22f;
        p.specular_weight = 1.1f; p.specular_color = vec3(0.8f, 0.9f, 0.7f);
        p.specular_roughness = 0.37f; p.specular_ior = 1.47f; p.specular_roughness_anisotropy = 0.4f;
        p.transmission_weight = 0.33f; p.transmission_color = vec3(0.7f, 0.85f, 0.95f);
        p.transmission_depth = 0.15f; p.transmission_scatter = vec3(0.05f, 0.08f, 0.12f);
        p.transmission_scatter_anisotropy = 0.2f; p.transmission_dispersion_scale = 0.6f;
        p.transmission_dispersion_abbe_number = 30.0f;
        p.subsurface_weight = 0.45f; p.subsurface_color = vec3(0.65f, 0.4f, 0.25f);
        p.subsurface_radius = 0.018f; p.subsurface_radius_scale = vec3(1.0f, 0.5f, 0.2f);
        p.subsurface_scatter_anisotropy = -0.2f;
        p.fuzz_weight = 0.3f; p.fuzz_color = vec3(0.7f, 0.8f, 0.9f); p.fuzz_roughness = 0.4f;
        p.coat_weight = 0.65f; p.coat_color = vec3(0.9f, 0.7f, 0.8f);
        p.coat_roughness = 0.23f; p.coat_roughness_anisotropy = 0.3f; p.coat_ior = 1.56f; p.coat_darkening = 0.6f;
        p.thin_film_weight = 0.4f; p.thin_film_thickness = 0.37f; p.thin_film_ior = 1.32f;
        p.emission_luminance = 12.0f; p.emission_color = vec3(0.9f, 0.4f, 0.15f); p.geometry_opacity = 0.7f;
        break;
    case 16:
        p.specular_roughness_anisotropy = 0.75f; p.coat_weight = 0.85f;
        p.coat_roughness = 0.28f; p.coat_roughness_anisotropy = 0.65f;
        p.geometry_basis = openpbr_make_basis(normalize(vec3(0.12f, 0.08f, 1.0f)), vec3(0.0f, 1.0f, 0.0f), -1.0f);
        p.geometry_coat_basis = openpbr_make_basis(normalize(vec3(-0.15f, 0.18f, 1.0f)), vec3(1.0f, 0.0f, 0.0f), 1.0f);
        break;
    case 17: // BSDF values equal case 0: opacity is host-owned stochastic coverage.
        p.geometry_opacity = 0.25f;
        break;
    case 18:
        p.geometry_thin_walled = true; p.subsurface_weight = 0.85f;
        p.subsurface_color = vec3(0.7f, 0.4f, 0.15f); p.subsurface_scatter_anisotropy = 0.6f;
        p.base_diffuse_roughness = 0.65f;
        break;
    case 19: // Interior incidence and zero interior emission.
        p.transmission_weight = 1.0f; p.transmission_depth = 0.4f;
        p.transmission_color = vec3(0.8f, 0.9f, 0.95f); p.specular_roughness = 0.3f;
        p.emission_luminance = 50.0f;
        break;
    case 20: // Grazing total internal reflection.
        p.transmission_weight = 1.0f; p.specular_ior = 1.7f; p.specular_roughness = 0.2f;
        break;
    case 21: // White furnace, energy-compensated rough metal.
        p.base_metalness = 1.0f; p.base_color = vec3(1.0f); p.specular_color = vec3(1.0f);
        p.specular_roughness = 0.65f;
        break;
    case 22: // White furnace, energy-compensated rough diffuse.
        p.base_color = vec3(1.0f); p.base_diffuse_roughness = 0.8f; p.specular_weight = 0.0f;
        break;
    case 23: // White furnace, layered opaque dielectric.
        p.base_color = vec3(1.0f); p.specular_roughness = 0.5f;
        p.coat_weight = 0.7f; p.coat_roughness = 0.3f; p.fuzz_weight = 0.4f;
        break;
    }
    return p;
}

vec3 mu3d_openpbr_view(int id)
{
    if (id == 19) return normalize(vec3(0.3f, 0.2f, -1.0f));
    if (id == 20) return normalize(vec3(1.0f, 0.0f, -0.25f));
    return normalize(vec3(0.3f, 0.2f, 1.0f));
}

vec3 mu3d_openpbr_light(int id)
{
    if (id == 9 || id == 12 || id == 14 || id == 18 || id == 19)
        return normalize(vec3(-0.4f, 0.1f, -1.0f));
    if (id == 20) return normalize(vec3(-1.0f, 0.1f, -0.25f));
    return normalize(vec3(-0.4f, 0.1f, 1.0f));
}

vec3 mu3d_openpbr_wavelengths(int id)
{
    if (id == 14) return vec3(650.0f, 530.0f, 430.0f);
    return OpenPBR_BaseRgbWavelengths_nm;
}

vec3 mu3d_openpbr_throughput() { return vec3(1.0f, 0.8f, 0.6f); }
vec3 mu3d_openpbr_random() { return vec3(0.23f, 0.67f, 0.41f); }

#endif

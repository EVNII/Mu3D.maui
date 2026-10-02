// Mu3D maintainer-only numerical oracle. Calls unmodified pinned Adobe OpenPBR.
// No C++ compiler, GLM, or upstream source is required by Mu3D consumers or tests.
#include <glm/glm.hpp>
#include "openpbr.h"
#include "cases.h"
#include <array>
#include <cmath>
#include <iomanip>
#include <iostream>
#include <limits>
#include <stdexcept>

static const char* case_names[MU3D_OPENPBR_REFERENCE_CASE_COUNT] = {
    "default", "rough_diffuse", "metal_f82", "anisotropy", "coat", "fuzz", "coat_and_fuzz",
    "thin_film_dielectric", "thin_film_metal", "thin_walled_transmission", "layered_emission",
    "subsurface_volume", "transmission_tint", "transmission_volume", "dispersion", "full_mixture",
    "independent_geometry_bases", "host_opacity", "thin_walled_subsurface", "interior_incidence",
    "interior_grazing", "furnace_metal", "furnace_diffuse", "furnace_layers"
};

static void write_vector(const vec3 v)
{
    std::cout << '[' << v.x << ',' << v.y << ',' << v.z << ']';
}

static std::array<float, 32> evaluate(int id)
{
    const OpenPBR_ResolvedInputs p = mu3d_openpbr_case(id);
    const OpenPBR_PreparedBsdf prepared = openpbr_prepare(p, mu3d_openpbr_throughput(),
        mu3d_openpbr_wavelengths(id), OpenPBR_VacuumIor, mu3d_openpbr_view(id));
    const OpenPBR_DiffuseSpecular evaluated = openpbr_eval(prepared, mu3d_openpbr_light(id));
    vec3 direction(0.0f);
    OpenPBR_DiffuseSpecular weight = openpbr_make_zero_diffuse_specular();
    float pdf = 0.0f;
    OpenPBR_BsdfLobeType sampled_type = 0;
    openpbr_sample(prepared, mu3d_openpbr_random(), direction, weight, pdf, sampled_type);
    if (!(pdf > 0.0f))
    {
        // Upstream explicitly leaves these outputs undefined on failed sampling.
        direction = vec3(0.0f);
        weight = openpbr_make_zero_diffuse_specular();
        sampled_type = 0;
    }
    std::array<float, 32> r = {};
    for (int channel = 0; channel < 3; ++channel)
    {
        r[channel] = evaluated.diffuse[channel]; r[4 + channel] = evaluated.specular[channel];
        r[8 + channel] = prepared.volume.extinction_coefficient[channel];
        r[12 + channel] = prepared.volume.albedo[channel]; r[16 + channel] = prepared.emission[channel];
        r[20 + channel] = direction[channel]; r[24 + channel] = weight.diffuse[channel];
        r[28 + channel] = weight.specular[channel];
    }
    r[3] = openpbr_pdf(prepared, mu3d_openpbr_light(id)); r[7] = prepared.volume.anisotropy;
    r[11] = pdf; r[15] = static_cast<float>(sampled_type);
    r[19] = openpbr_needs_rgb_wavelengths(p) ? 1.0f : 0.0f;
    r[23] = pdf > 0.0f ? openpbr_pdf(prepared, direction) : 0.0f;
    for (float value : r)
        if (!std::isfinite(value)) throw std::runtime_error("Non-finite reference result");
    return r;
}

static vec3 integrate_hemisphere(int id)
{
    // Fixed midpoint quadrature of the cosine-weighted BSDF over solid angle.
    // The oracle BSDF already includes the cosine. Accumulate in double to avoid
    // quadrature summation noise hiding the upstream's actual energy behavior.
    const OpenPBR_ResolvedInputs p = mu3d_openpbr_case(id);
    const OpenPBR_PreparedBsdf prepared = openpbr_prepare(p, vec3(1.0f),
        mu3d_openpbr_wavelengths(id), OpenPBR_VacuumIor, mu3d_openpbr_view(id));
    constexpr int Size = 256;
    std::array<double, 3> sum = {};
    for (int y = 0; y < Size; ++y)
    {
        const float z = (float(y) + 0.5f) / float(Size);
        const float radial = std::sqrt(1.0f - z * z);
        for (int x = 0; x < Size; ++x)
        {
            const float phi = OpenPBR_TwoPi * (float(x) + 0.5f) / float(Size);
            const vec3 direction(radial * std::cos(phi), radial * std::sin(phi), z);
            const vec3 f = openpbr_get_sum_of_diffuse_specular(openpbr_eval(prepared, direction));
            for (int c = 0; c < 3; ++c) sum[c] += f[c];
        }
    }
    const double factor = double(OpenPBR_TwoPi) / double(Size * Size);
    return vec3(float(sum[0] * factor), float(sum[1] * factor), float(sum[2] * factor));
}

int main()
{
    std::cout << std::setprecision(std::numeric_limits<float>::max_digits10);
    std::cout << "{\n  \"schemaVersion\":1,\n  \"openPbrVersion\":\"1.1.1\",\n"
        << "  \"adobeCommit\":\"c91aad1d1ce1693e803f039d7c92c2965c4eb013\",\n"
        << "  \"absoluteTolerance\":0.00002,\n  \"relativeTolerance\":0.0002,\n  \"cases\":[\n";
    for (int id = 0; id < MU3D_OPENPBR_REFERENCE_CASE_COUNT; ++id)
    {
        const auto r = evaluate(id);
        std::cout << "    {\"id\":" << id << ",\"name\":\"" << case_names[id] << "\",\"view\":";
        write_vector(mu3d_openpbr_view(id)); std::cout << ",\"light\":"; write_vector(mu3d_openpbr_light(id));
        std::cout << ",\"wavelengthsNm\":"; write_vector(mu3d_openpbr_wavelengths(id));
        std::cout << ",\"values\":[";
        for (int j = 0; j < 32; ++j) std::cout << (j ? "," : "") << r[j];
        std::cout << "]}" << (id + 1 < MU3D_OPENPBR_REFERENCE_CASE_COUNT ? "," : "") << '\n';
    }
    std::cout << "  ],\n  \"hemisphereQuadrature\":{\"size\":256,\"cases\":[";
    for (int id = 21; id <= 23; ++id)
    {
        const vec3 energy = integrate_hemisphere(id);
        for (int c = 0; c < 3; ++c)
            if (!std::isfinite(energy[c]) || std::abs(energy[c] - 1.0f) > 0.02f)
                throw std::runtime_error("White furnace energy outside explicit 2 percent tolerance");
        std::cout << (id > 21 ? "," : "") << "{\"id\":" << id << ",\"integral\":";
        write_vector(energy); std::cout << '}';
    }
    std::cout << "]},\n  \"volumeTransmittance\":[";
    for (int id = 11; id <= 13; ++id)
    {
        const auto p = openpbr_prepare(mu3d_openpbr_case(id), mu3d_openpbr_throughput(),
            mu3d_openpbr_wavelengths(id), OpenPBR_VacuumIor, mu3d_openpbr_view(id));
        std::cout << (id > 11 ? "," : "") << "{\"id\":" << id << ",\"distances\":[0,0.1,1],\"values\":[";
        for (int d = 0; d < 3; ++d)
        {
            if (d) std::cout << ',';
            write_vector(openpbr_calculate_transmittance_at_distance(p.volume.extinction_coefficient,
                d == 0 ? 0.0f : d == 1 ? 0.1f : 1.0f));
        }
        std::cout << "]}";
    }
    std::cout << "]\n}\n";
}

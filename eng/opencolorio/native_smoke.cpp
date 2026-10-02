#include <cmath>
#include <cstdio>
#include <cstring>
#include <stdexcept>

extern "C" {
unsigned mu3d_ocio_abi_version();
const char* mu3d_ocio_version();
const char* mu3d_ocio_last_error();
int mu3d_ocio_config_create(int, const char*, const char*, void**);
void mu3d_ocio_config_delete(void*);
int mu3d_ocio_processor_colorspace(void*, const char*, const char*, void**);
void mu3d_ocio_processor_delete(void*);
int mu3d_ocio_apply(void*, float*, int, int);
}

int main() {
    void* config = nullptr;
    void* processor = nullptr;
    try {
        if (mu3d_ocio_abi_version() != 1 || std::strcmp(mu3d_ocio_version(), "2.5.2"))
            throw std::runtime_error("Unexpected native ABI/release.");
        if (mu3d_ocio_config_create(2, "cg-config-v4.0.0_aces-v2.0_ocio-v2.5", nullptr, &config))
            throw std::runtime_error(mu3d_ocio_last_error());
        if (mu3d_ocio_processor_colorspace(config, "ACEScg", "ACES2065-1", &processor))
            throw std::runtime_error(mu3d_ocio_last_error());
        // Processor ownership must remain independent of configuration ownership.
        mu3d_ocio_config_delete(config); config = nullptr;
        float values[] = {0.18f, 0.18f, 0.18f, -0.0f, 4.0f, 4.0f, 4.0f, 0.375f};
        if (mu3d_ocio_apply(processor, values, 2, 4))
            throw std::runtime_error(mu3d_ocio_last_error());
        for (int c = 0; c < 3; ++c)
            if (std::abs(values[c] - 0.18f) > 2e-6f || std::abs(values[c + 4] - 4.0f) > 2e-5f)
                throw std::runtime_error("Native ACES neutral/HDR mismatch.");
        if (!std::signbit(values[3]) || values[7] != 0.375f)
            throw std::runtime_error("Native straight alpha changed.");
        mu3d_ocio_processor_delete(processor); processor = nullptr;
        std::puts("OpenColorIO native C ABI 1 / 2.5.2 smoke passed (built-in config, ACES, HDR, alpha, lifetime).");
        return 0;
    } catch (const std::exception& error) {
        if (processor) mu3d_ocio_processor_delete(processor);
        if (config) mu3d_ocio_config_delete(config);
        std::fprintf(stderr, "%s\n", error.what());
        return 1;
    }
}

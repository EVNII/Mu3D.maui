#include <webgpu/webgpu.h>

// Link feasibility only. Creating an instance does not request a GPU adapter.
int main(void) {
    WGPUInstance instance = wgpuCreateInstance(NULL);
    if (!instance) return 1;
    wgpuInstanceRelease(instance);
    return 0;
}

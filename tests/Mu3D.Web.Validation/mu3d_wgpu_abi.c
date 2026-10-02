#include <emscripten.h>
#include <stddef.h>
#include <stdlib.h>
#include <webgpu/webgpu.h>

// Header/ABI tests only: no adapter, GPU, or WebGPU implementation is created.
int mu3d_wgpu_abi_value(int index) {
    const int values[] = {
        sizeof(WGPUStringView), sizeof(WGPUFuture), sizeof(WGPURequestAdapterCallbackInfo),
        sizeof(WGPUInstanceDescriptor), sizeof(WGPURequestAdapterOptions),
        sizeof(WGPUDeviceDescriptor), sizeof(WGPULimits), sizeof(WGPUTextureDescriptor),
        sizeof(WGPUBufferDescriptor), sizeof(WGPURenderPassColorAttachment),
        sizeof(WGPURenderPassDescriptor), sizeof(WGPUSurfaceConfiguration),
        offsetof(WGPURequestAdapterCallbackInfo, callback),
        offsetof(WGPUDeviceDescriptor, deviceLostCallbackInfo),
        WGPUCallbackMode_AllowSpontaneous, WGPUTextureFormat_RGBA16Float,
        WGPUTextureUsage_RenderAttachment, WGPUBufferUsage_MapRead,
        WGPUSType_ShaderSourceWGSL, WGPURequestAdapterStatus_Success
    };
    return index >= 0 && index < (int)(sizeof(values)/sizeof(values[0])) ? values[index] : -1;
}

static void complete_adapter_shape(void* argument) {
    WGPURequestAdapterCallbackInfo* info = (WGPURequestAdapterCallbackInfo*)argument;
    WGPUStringView message = { "Mu3D ABI", 8 };
    info->callback(WGPURequestAdapterStatus_Success, NULL, message, info->userdata1, info->userdata2);
    free(info);
}

WGPUFuture mu3d_wgpu_callback_shape(WGPURequestAdapterCallbackInfo info) {
    WGPURequestAdapterCallbackInfo* pending = malloc(sizeof(*pending));
    if (!pending) return (WGPUFuture){ 0 };
    *pending = info;
    emscripten_async_call(complete_adapter_shape, pending, 1);
    return (WGPUFuture){ 0x123456789abcdef0ULL };
}

typedef void (*mu3d_flat_callback)(WGPURequestAdapterStatus, WGPUAdapter, const char*, size_t, void*, void*);
typedef struct { mu3d_flat_callback callback; void* state; void* sentinel; } mu3d_flat_pending;

static void flatten_callback(WGPURequestAdapterStatus status, WGPUAdapter adapter,
                             WGPUStringView message, void* state, void* ignored) {
    mu3d_flat_pending* pending = state;
    pending->callback(status, adapter, message.data, message.length, pending->state, pending->sentinel);
    free(pending);
}

WGPUFuture mu3d_wgpu_callback_flat(mu3d_flat_callback callback, void* state, void* sentinel) {
    mu3d_flat_pending* pending = malloc(sizeof(*pending));
    if (!pending) return (WGPUFuture){ 0 };
    *pending = (mu3d_flat_pending){ callback, state, sentinel };
    WGPURequestAdapterCallbackInfo info = WGPU_REQUEST_ADAPTER_CALLBACK_INFO_INIT;
    info.mode = WGPUCallbackMode_AllowSpontaneous;
    info.callback = flatten_callback;
    info.userdata1 = pending;
    return mu3d_wgpu_callback_shape(info);
}

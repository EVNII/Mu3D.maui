// Gallery/validation browser lifecycle. Drawing uses Mu3D's shared C-API adapter.
#include <emscripten.h>
#include <emscripten/html5.h>
#include <stdlib.h>
#include <stdio.h>
#include <webgpu/webgpu.h>

typedef void (*mu3d_complete)(int, void*, const char*, size_t, void*);
typedef struct {
    WGPUInstance instance;
    WGPUAdapter adapter;
    WGPUDevice device;
    mu3d_complete callback;
    void* state;
    char lost_reason[4096];
} mu3d_browser;
typedef struct {
    WGPUBuffer buffer;
    size_t offset, size;
    mu3d_complete callback;
    void* state;
} mu3d_map;

// One-device diagnostic host: late device callbacks never access freed managed state.
static char mu3d_error[4096];
const char* mu3d_browser_error(void) { return mu3d_error; }
static void error_received(WGPUDevice const* device, WGPUErrorType type,
                           WGPUStringView message, void* unused1, void* unused2) {
    snprintf(mu3d_error, sizeof(mu3d_error), "WebGPU error %d: %.*s", type,
             (int)message.length, message.data);
    fprintf(stderr, "%s\n", mu3d_error);
}
static void lost(WGPUDevice const* device, WGPUDeviceLostReason reason,
                 WGPUStringView message, void* unused1, void* unused2) {
    if (reason == WGPUDeviceLostReason_Destroyed || reason == WGPUDeviceLostReason_CallbackCancelled) return;
    mu3d_browser* ctx = unused1;
    snprintf(ctx->lost_reason, sizeof(ctx->lost_reason), "WebGPU device lost %d: %.*s", reason,
             (int)message.length, message.data);
    snprintf(mu3d_error, sizeof(mu3d_error), "WebGPU device lost %d: %.*s", reason,
             (int)message.length, message.data);
    fprintf(stderr, "%s\n", mu3d_error);
}
void mu3d_browser_release(mu3d_browser* ctx) {
    if (!ctx) return;
    // Unconfiguring here would clear the last displayed frame. Reload owns the page lifetime.
    if (ctx->device) wgpuDeviceRelease(ctx->device);
    if (ctx->adapter) wgpuAdapterRelease(ctx->adapter);
    if (ctx->instance) wgpuInstanceRelease(ctx->instance);
    free(ctx);
}
static void device_received(WGPURequestDeviceStatus status, WGPUDevice device,
                            WGPUStringView message, void* data, void* unused) {
    mu3d_browser* ctx = data;
    ctx->device = device;
    ctx->callback(status == WGPURequestDeviceStatus_Success ? 0 : (int)status,
                  ctx, message.data, message.length, ctx->state);
}
static void adapter_received(WGPURequestAdapterStatus status, WGPUAdapter adapter,
                             WGPUStringView message, void* data, void* unused) {
    mu3d_browser* ctx = data;
    ctx->adapter = adapter;
    if (status != WGPURequestAdapterStatus_Success) {
        ctx->callback((int)status, ctx, message.data, message.length, ctx->state);
        return;
    }
    WGPUDeviceDescriptor descriptor = WGPU_DEVICE_DESCRIPTOR_INIT;
    descriptor.uncapturedErrorCallbackInfo.callback = error_received;
    descriptor.deviceLostCallbackInfo.mode = WGPUCallbackMode_AllowSpontaneous;
    descriptor.deviceLostCallbackInfo.callback = lost;
    descriptor.deviceLostCallbackInfo.userdata1 = ctx;
    // Request only advertised optional features; the managed adapter reads the device's actual set.
    WGPUFeatureName requested[4];
    const WGPUFeatureName optional[] = {WGPUFeatureName_ShaderF16, WGPUFeatureName_TextureCompressionBC,
        WGPUFeatureName_TextureCompressionETC2, WGPUFeatureName_TextureCompressionASTC};
    size_t count = 0;
    for (size_t i = 0; i < 4; ++i)
        if (wgpuAdapterHasFeature(adapter, optional[i])) requested[count++] = optional[i];
    descriptor.requiredFeatureCount = count;
    descriptor.requiredFeatures = requested;
    WGPURequestDeviceCallbackInfo info = WGPU_REQUEST_DEVICE_CALLBACK_INFO_INIT;
    info.mode = WGPUCallbackMode_AllowSpontaneous;
    info.callback = device_received; info.userdata1 = ctx;
    wgpuAdapterRequestDevice(adapter, &descriptor, info);
}
void mu3d_browser_open(mu3d_complete callback, void* state) {
    mu3d_error[0] = 0;
    mu3d_browser* ctx = calloc(1, sizeof(*ctx));
    if (!ctx) { callback(-1, NULL, "Allocation failed", 17, state); return; }
    ctx->callback = callback; ctx->state = state;
    ctx->instance = wgpuCreateInstance(NULL);
    if (!ctx->instance) { callback(-1, ctx, "Instance failed", 15, state); return; }
    WGPURequestAdapterCallbackInfo info = WGPU_REQUEST_ADAPTER_CALLBACK_INFO_INIT;
    info.mode = WGPUCallbackMode_AllowSpontaneous;
    info.callback = adapter_received; info.userdata1 = ctx;
    wgpuInstanceRequestAdapter(ctx->instance, NULL, info);
}
WGPUInstance mu3d_browser_instance(mu3d_browser* ctx) { return ctx->instance; }
WGPUAdapter mu3d_browser_adapter(mu3d_browser* ctx) { return ctx->adapter; }
WGPUDevice mu3d_browser_device(mu3d_browser* ctx) { return ctx->device; }
const char* mu3d_browser_lost_reason(mu3d_browser* ctx) { return ctx->lost_reason; }
int mu3d_browser_max_dimension(mu3d_browser* ctx) {
    WGPULimits limits = WGPU_LIMITS_INIT;
    if (wgpuDeviceGetLimits(ctx->device, &limits) != WGPUStatus_Success) return 0;
    return (int)limits.maxTextureDimension2D;
}
WGPUSurface mu3d_browser_create_surface(mu3d_browser* ctx, const char* selector) {
    WGPUEmscriptenSurfaceSourceCanvasHTMLSelector source = WGPU_EMSCRIPTEN_SURFACE_SOURCE_CANVAS_HTML_SELECTOR_INIT;
    source.selector = (WGPUStringView){selector, WGPU_STRLEN};
    WGPUSurfaceDescriptor descriptor = WGPU_SURFACE_DESCRIPTOR_INIT;
    descriptor.nextInChain = &source.chain;
    return wgpuInstanceCreateSurface(ctx->instance, &descriptor);
}
int mu3d_browser_configure(mu3d_browser* ctx, WGPUSurface surface, int width, int height, int hdr, int transparent) {
    if (!surface) return 0;
    WGPUSurfaceConfiguration configuration = WGPU_SURFACE_CONFIGURATION_INIT;
    configuration.device = ctx->device;
    configuration.format = hdr ? WGPUTextureFormat_RGBA16Float : WGPUTextureFormat_BGRA8Unorm;
    configuration.usage = WGPUTextureUsage_RenderAttachment | WGPUTextureUsage_CopySrc;
    configuration.width = width; configuration.height = height;
    configuration.alphaMode = transparent ? WGPUCompositeAlphaMode_Premultiplied : WGPUCompositeAlphaMode_Opaque;
    configuration.presentMode = WGPUPresentMode_Fifo;
    WGPUSurfaceColorManagement color = WGPU_SURFACE_COLOR_MANAGEMENT_INIT;
    color.colorSpace = WGPUPredefinedColorSpace_SRGB;
    color.toneMappingMode = hdr ? WGPUToneMappingMode_Extended : WGPUToneMappingMode_Standard;
    configuration.nextInChain = &color.chain;
    wgpuSurfaceConfigure(surface, &configuration);
    return 1;
}
WGPUTexture mu3d_browser_acquire(WGPUSurface surface) {
    WGPUSurfaceTexture result = WGPU_SURFACE_TEXTURE_INIT;
    wgpuSurfaceGetCurrentTexture(surface, &result);
    return result.texture;
}
static void mapped(WGPUMapAsyncStatus status, WGPUStringView message, void* data, void* unused) {
    mu3d_map* pending = data;
    if (status == WGPUMapAsyncStatus_Success) {
        const void* bytes = wgpuBufferGetConstMappedRange(pending->buffer, pending->offset, pending->size);
        if (bytes) pending->callback(0, (void*)bytes, "", pending->size, pending->state);
        else pending->callback(-1, NULL, "Mapped range unavailable", 24, pending->state);
        wgpuBufferUnmap(pending->buffer);
    } else {
        pending->callback((int)status, NULL, message.data, message.length, pending->state);
    }
    wgpuBufferRelease(pending->buffer);
    free(pending);
}
void mu3d_browser_read(WGPUBuffer buffer, size_t offset, size_t size, mu3d_complete callback, void* state) {
    mu3d_map* pending = malloc(sizeof(*pending));
    if (!pending) { callback(-1, NULL, "Allocation failed", 17, state); return; }
    *pending = (mu3d_map){buffer, offset, size, callback, state};
    wgpuBufferAddRef(buffer);
    WGPUBufferMapCallbackInfo info = WGPU_BUFFER_MAP_CALLBACK_INFO_INIT;
    info.mode = WGPUCallbackMode_AllowSpontaneous;
    info.callback = mapped; info.userdata1 = pending;
    wgpuBufferMapAsync(buffer, WGPUMapMode_Read, offset, size, info);
}
typedef struct { WGPUQueue queue; mu3d_complete callback; void* state; } mu3d_work;
typedef struct { mu3d_complete callback; void* state; } mu3d_validation;
void mu3d_browser_begin_validation(mu3d_browser* ctx) {
    wgpuDevicePushErrorScope(ctx->device, WGPUErrorFilter_Validation);
}
static void validation_done(WGPUPopErrorScopeStatus status, WGPUErrorType type,
                            WGPUStringView message, void* data, void* unused) {
    mu3d_validation* pending = data;
    int result = status == WGPUPopErrorScopeStatus_Success && type == WGPUErrorType_NoError ? 0 : -1;
    pending->callback(result, NULL, message.data, result == 0 ? 0 : message.length, pending->state);
    free(pending);
}
void mu3d_browser_end_validation(mu3d_browser* ctx, mu3d_complete callback, void* state) {
    mu3d_validation* pending = malloc(sizeof(*pending));
    if (!pending) { callback(-1, NULL, "Allocation failed", 17, state); return; }
    *pending = (mu3d_validation){callback, state};
    WGPUPopErrorScopeCallbackInfo info = WGPU_POP_ERROR_SCOPE_CALLBACK_INFO_INIT;
    info.mode = WGPUCallbackMode_AllowSpontaneous;
    info.callback = validation_done; info.userdata1 = pending;
    wgpuDevicePopErrorScope(ctx->device, info);
}
static void work_done(WGPUQueueWorkDoneStatus status, WGPUStringView message, void* data, void* unused) {
    mu3d_work* pending = data;
    if (status == WGPUQueueWorkDoneStatus_Success) pending->callback(0, NULL, NULL, 0, pending->state);
    else pending->callback((int)status, NULL, message.data, message.length, pending->state);
    wgpuQueueRelease(pending->queue);
    free(pending);
}
void mu3d_browser_wait_for_work(mu3d_browser* ctx, mu3d_complete callback, void* state) {
    mu3d_work* pending = malloc(sizeof(*pending));
    if (!pending) { callback(-1, NULL, "Allocation failed", 17, state); return; }
    *pending = (mu3d_work){wgpuDeviceGetQueue(ctx->device), callback, state};
    WGPUQueueWorkDoneCallbackInfo info = WGPU_QUEUE_WORK_DONE_CALLBACK_INFO_INIT;
    info.mode = WGPUCallbackMode_AllowSpontaneous;
    info.callback = work_done; info.userdata1 = pending;
    wgpuQueueOnSubmittedWorkDone(pending->queue, info);
}
// Browser presentation is automatic at an animation boundary. Do not call wgpuSurfacePresent.
typedef struct { mu3d_complete callback; void* state; int frames; } mu3d_frame;
static EM_BOOL frame_boundary(double time, void* data) {
    mu3d_frame* pending = data;
    if (++pending->frames < 2) { emscripten_request_animation_frame(frame_boundary, pending); return EM_FALSE; }
    pending->callback(0, NULL, NULL, 0, pending->state);
    free(pending);
    return EM_FALSE;
}
void mu3d_browser_present(mu3d_complete callback, void* state) {
    mu3d_frame* pending = calloc(1, sizeof(*pending));
    if (!pending) { callback(-1, NULL, "Allocation failed", 17, state); return; }
    pending->callback = callback; pending->state = state;
    emscripten_request_animation_frame(frame_boundary, pending);
}

#include <emscripten.h>
#include <stdlib.h>
#include <webgpu/webgpu.h>

typedef void (*mu3d_result)(int, const char*, size_t, void*);
typedef struct { mu3d_result callback; void* state; } mu3d_request;

WGPUInstance mu3d_emdawn_create(void) { return wgpuCreateInstance(NULL); }
void mu3d_emdawn_release(WGPUInstance instance) { wgpuInstanceRelease(instance); }
EM_JS(int, mu3d_emdawn_has_gpu, (), { return typeof navigator !== 'undefined' && !!navigator.gpu; });

static void completed(WGPURequestAdapterStatus status, WGPUAdapter adapter, WGPUStringView message,
                      void* state, void* ignored) {
    mu3d_request* pending = state;
    // Flatten the by-value WGPUStringView before crossing into a managed callback.
    pending->callback((int)status, message.data, message.length, pending->state);
    if (adapter) wgpuAdapterRelease(adapter);
    free(pending);
}

void mu3d_emdawn_request(WGPUInstance instance, mu3d_result callback, void* state) {
    mu3d_request* pending = malloc(sizeof(*pending));
    if (!pending) { callback(4, "Allocation failed", 17, state); return; }
    *pending = (mu3d_request){ callback, state };
    WGPURequestAdapterCallbackInfo info = WGPU_REQUEST_ADAPTER_CALLBACK_INFO_INIT;
    info.mode = WGPUCallbackMode_AllowSpontaneous;
    info.callback = completed;
    info.userdata1 = pending;
    wgpuInstanceRequestAdapter(instance, NULL, info);
}

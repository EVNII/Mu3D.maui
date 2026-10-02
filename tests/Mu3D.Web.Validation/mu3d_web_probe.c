#include <emscripten.h>
#include <stdlib.h>

typedef void (*mu3d_callback)(int, void*);
typedef struct { mu3d_callback callback; void* state; } mu3d_pending;

int mu3d_web_pointer_size(void) { return sizeof(void*); }

static void complete(void* argument) {
    mu3d_pending* pending = (mu3d_pending*)argument;
    mu3d_callback callback = pending->callback;
    void* state = pending->state;
    free(pending);
    callback(42, state);
}

void mu3d_web_callback(mu3d_callback callback, void* state) {
    mu3d_pending* pending = (mu3d_pending*)malloc(sizeof(mu3d_pending));
    if (!pending) { callback(-1, state); return; }
    pending->callback = callback;
    pending->state = state;
    emscripten_async_call(complete, pending, 1);
}

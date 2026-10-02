# Mu3D.Web

Optional, experimental browser host handlers for Mu3D WASM applications. Reference this project
explicitly; Core, Graphics and the MAUI packages do not depend on it. This is not a browser GPU
backend or a new supported 1.0 platform. The current Emdawn/WASM device bridge remains experimental
in `tests/Mu3D.Web.Validation`.

Use the same Core renderer and Toolkit on both hosts. Browser adapters connect that shared code
to Canvas, properties, input and lifecycle; they do not implement another scene or control model.
The managed assembly depends only on `Mu3D.Graphics`, with no MAUI or concrete GPU dependency.

`CanvasViewHandler` borrows an `IPresentationSurfaceSession` supplied by the backend:

```csharp
using Mu3D.Web;
using Mu3D.Graphics;

using var handler = new CanvasViewHandler(presentationSession);
PresentationSurfaceFrameStatus status = handler.RenderAndPresent(width, height,
    target => DrawSharedScene(target), refreshSurface: surfaceChanged);
```

Sizing and synchronous acquisition/drawing are serialized. A same-size refresh still reconfigures;
the session's status passes through without retries. Dispose stops future frames and clears the
borrowed reference after an active synchronous frame returns; it never disposes the session/device.
The host must settle asynchronous callbacks/readback before releasing its renderer and backend.
The existing `Action<GraphicsTexture>` boundary preserves custom drawing and shared render pipelines.

All Web features must support trimmed Release AOT and interpreted execution with equivalent
behavior; applications choose whether to enable AOT. See the [Web validation guide](../../tests/Mu3D.Web.Validation/README.md)
for build commands and browser checks, and the [Gallery host](../../samples/GalleryApp/Web/README.md)
for a complete application using these handlers.

The normal JS entry point is `createManagedViewportHandler` in `wwwroot/mu3d/viewport-handler.mjs`:

```js
import { createManagedViewportHandler } from './mu3d/viewport-handler.mjs';
const host = createManagedViewportHandler({
  canvas, maxDimension: 8192, properties: {exposure: 0},
  callbacks: {draw: (frame, properties) => drawManaged(frame, properties),
              disconnect: () => disconnectManaged()},
  onFrame: result => updateHostUi(result), onError: reportError,
});
host.setProperties({exposure: 1});
await host.dispose();
```

Explicit callbacks choose their own WASM exports/serialization. Optional `connect`, `resize` and
`setProperties` callbacks run before `draw`; effective property changes coalesce into shallow frozen
snapshots. `onFrame` receives the result, physical/CSS sizing and that frame's properties. Dispose
detaches observers immediately, suppresses late results and waits the full callback chain before
disconnecting once. Callbacks must not await disposal of their own handler. A failed connection
closes the handler; other frame errors stop continuous scheduling and allow explicit recovery.

`createViewportHandler` remains the lower-level scheduler with its existing contract:

```js
import { createViewportHandler } from './mu3d/viewport-handler.mjs';
const host = createViewportHandler({
  canvas, maxDimension: 8192, continuous: false,
  render: async frame => { /* forward physical size and timing to your renderer */ },
  onError: error => { /* report the error; automatic animation has stopped */ },
});
host.invalidate();          // one coalesced frame
host.setContinuous(true);   // explicitly opt into visible continuous rendering
host.dispose();             // detach observers and listeners
```

`render` receives physical width/height, CSS width/height, a bounded `deltaSeconds`, the raw
`frameIntervalSeconds` (zero on resume), and `refreshSurface`. One render may be in flight;
invalidations during it coalesce. Hidden/offscreen canvases suspend. The caller chooses rendering,
display transforms, GPU ownership and error UI, and must serialize disposal with in-flight work.
Rendering continuity and scene animation are separate application choices. A paused scene can
retain continuous rendering for an active Gizmo interaction, then return to on-demand frames when
the contact ends. Switch once at those boundaries; repeatedly calling `setContinuous` resets its
time baseline. Independent on-demand edits intentionally exclude idle time from frame cadence.

Callbacks measure submission pacing, not actual display refresh or GPU completion. No timers,
readback, browser globals changed, automatic SDR fallback, or UI controls are supplied.

Project references copy the module into published `wwwroot/mu3d`; NuGet uses the included
buildTransitive targets for the same path. Hosting/network security and serving published files
remain application-owned. `CanvasViewHandler` is an additive managed API: it exposes no native ABI,
changes no shared renderer/Toolkit behavior and preserves the existing JS scheduler contract.

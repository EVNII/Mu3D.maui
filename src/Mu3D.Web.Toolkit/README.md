# Mu3D.Web.Toolkit

Optional experimental browser input and HTML adapters over shared `Mu3D.Toolkit`; references
`Mu3D.Web`/Toolkit, with no MAUI dependency or browser GPU backend. Camera, Gizmo, raycast,
projection and collector mathematics stay in managed shared code. Applications own selection
policy, gesture arbitration, undo, accessible controls and the explicit WASM bridge.
Modules publish to `wwwroot/mu3d`. All adapters must support trimmed Release AOT and interpreted
execution; applications choose whether to enable AOT. Native and browser hosts share Core/Toolkit
behavior while using their own input and UI controls. See the [Web validation guide](../../tests/Mu3D.Web.Validation/README.md)
and [Gallery host](../../samples/Mu3D.Gallery/Web/README.md) for build and verification instructions.

## Orbit and raw pointer input

```js
import {attachOrbitInput} from './mu3d/orbit-input.mjs';
const orbit = attachOrbitInput(canvas, ({rotateX, rotateY, dolly}) => {
  forwardToManagedOrbit({rotateX, rotateY, dolly}); // radians / log-distance
}, {dragThreshold: 6});
orbit.dispose();
```

The callback forwards/coalesces commands into existing `OrbitController.Rotate`/`Dolly`; the
adapter owns no camera or render clock. Canvas primary-left drag, ordinary wheel and arrow/+/-
keys are consumed; Ctrl-wheel page pinch stays browser-owned. The adapter temporarily owns
`touch-action:none`, capture and listeners, restoring them on disposal. Supply Canvas focusability,
accessible instructions and optional `{isEnabled: () => ...}` gating.

`dragThreshold` defaults to `0` and must be finite/nonnegative CSS pixels. Zero displacement emits
no command. For click selection use its `tapTolerance` (default/viewer `6`). Motion at/below that
distance remains a tap; strictly beyond it starts Orbit with the full pointerdown displacement.
Later commands are incremental. Ordered coalesced excursions cannot become taps by moving back.
Wheel/keyboard commands are immediate; cancellation/disposal discards pending motion.

`attachPointerInput(canvas, {acceptContact, onSamples})` from `pointer-input.mjs` is independent.
Register before selection/Orbit. Synchronous `acceptContact(sample)` claims a primary contact;
misses fall through. Gate Orbit against `pointer.isActive`. Claimed contact excludes other contacts;
complete the shared drag on up, restore it on cancel. Escape, blur, hidden page, lost capture,
pointercancel, `cancel()` and `dispose()` cancel. Cancel before resize/tool/camera changes. The
host supplies Canvas touch-action CSS. Samples include unclaimed hover/movement for diagnostics.

| Sample fields | Meaning |
| --- | --- |
| `pointerId`, `pointerType`, `isPrimary`, `phase`, `captured` | Browser identity/contact lifecycle |
| `timeStamp`, `u/v`, `x/y` | Browser ms; normalized Canvas coordinates; physical backing pixels (captured values unclamped) |
| `pressure`, `tangentialPressure`, `tiltX/Y`, `twist` | [0,1], [-1,1], degrees, degrees |
| `altitudeAngle`, `azimuthAngle`, `buttons`, `button`, `eraser`, `barrel` | Radians and browser button reports; no imposed tool behavior |

Missing numeric fields are null. Actual coalesced samples preserve order without duplicating the
parent; predicted samples are excluded. Field presence/default sensor values do not prove hardware
capability ([Pointer Events](https://www.w3.org/TR/pointerevents/)). No brush engine, palm rejection,
hover distance or Apple Pencil squeeze/haptics is provided. The viewer uses shared
`TransformGizmoHitTester`, `TransformGizmoDragController` and HDR `TransformGizmoRenderPass`, as MAUI does.

## One selection for Gizmo, outline and content

`scene-selection.mjs` exports `createSceneSelectionSource` / `attachSceneSelectionInput`.
The source adapts application-owned selection; shared `SceneRaycaster` supplies geometry.
Native `SceneSelectionTool.SelectedNode` can bind `TransformGizmoTool.Target`, `OutlineHelper.Target`
and `SceneNodeAnchor.Target` inside `Mu3DSceneView.Features`/`ViewportTools`. That property is the
declarative `SceneNode3D` facade; `SceneNodeAnchor.Node` is the separate Core-node path.

```js
import {createSceneSelectionSource, attachSceneSelectionInput} from './mu3d/scene-selection.mjs';
const selection = createSceneSelectionSource(applicationSelectionBridge);
card.target = selection.selectedNode;
const stop = selection.subscribeSelection(({newNode}) => { card.target = newNode; });
const tap = attachSceneSelectionInput(canvas, selection, {
  tapTolerance: 6,
  isInputEnabled: () => !pointer.isActive && !overlays.hasActivePointer,
  onError: reportError,
});
const orbit = attachOrbitInput(canvas, command => {
  tap.cancel(); forwardToManagedOrbit(command);
}, {dragThreshold: 6, isEnabled: () => !pointer.isActive && !overlays.hasActivePointer});
selection.selectedNode = anotherScopedNode; // same commit path as a completed tap
tap.dispose(); orbit.dispose(); stop(); selection.dispose();
```

Register Gizmo/raw input first, selection second, Orbit third. The explicit bridge provides:

- Required synchronous `hitTest({x,y}, selectionMask)` returning ordered `{node,hit}` candidates.
  Map mesh hits to owners, filter selectability/categories and de-duplicate without changing
  distance order. Finite JSON-like hit evidence/candidates are copied and frozen.
- Optional `commitSelection(node)` (default no-op), before notification. Construction commits the
  initial target once without notifying. If commit throws the source keeps its prior selection;
  host side effects must also be transactional.
- Optional `validateNode(node)` (default object handles); null is always valid. Concrete bridges
  enforce source identity. There are no universal renderer export names.

| Writable source property | Default / meaning |
| --- | --- |
| `selectedNode` | `null`; borrowed scoped handle |
| `isEnabled`, `clearSelectionOnMiss` | `true`, `true` |
| `selectionMask` | `0xffffffff`, independently of camera render masks |

Native attached `SceneSelection.IsSelectable`/`Mask` default to true/`1`. Enablement/category,
hide/mask/detach changes do not implicitly clear committed selection. `requestSelection({x,y})`
uses finite top-left physical pixels, proposes the first candidate, and returns current selection.
Misses propose null or retain the old value according to policy. `subscribeRequested` receives frozen
`viewportPositionPixels`/`candidates` plus writable `selectedNode`/`cancel`; replacements use the
same validate/commit path. `subscribeSelection` reports `{oldNode,newNode}`; `subscribeOptions`
reports frozen `options`. Subscriptions return unsubscribe functions without initial replay;
unchanged assignments do not commit/notify. Reentrant changes stop obsolete delivery; mutation
inside commit is rejected. Dispose releases subscriptions/references without clearing host state
or disposing nodes/tools/content.

Tap input consumes no events/capture/touch-action. It maps Canvas-rect CSS coordinates to physical
pixels; writable `source` cancels and detaches before replacement. Other capture, recognized Orbit,
secondary contacts/buttons, Canvas exit or over-tolerance excursions prevent selection. Escape,
capture loss, blur, hidden/unload, source/options/selection changes and disposal cancel tracking.
Use `isActive`/`cancel()`/`dispose()` and gate `isInputEnabled` against interactive HTML.

Shared Outline renders a silhouette before Gizmo/display, loading initialized HDR color and
sampleable `Depth32Float` (`TextureBinding`). Native adapter style is sRGB `#FF38E8FF` decoded to
linear, 3 physical pixels, descendants and scene-depth testing; shared style's separate default
is above-one linear cyan. Gizmo rejects null/root, detached, hidden/ancestor-hidden and masked
targets. Outline filters each mesh while reaching independently layered children; `IncludeInvisible`
bypasses hiding only, and scene root can anchor a subtree. Targets/selection remain borrowed/retained.
Rigid/morph geometry is current each query/frame; skin deformation/alpha masks are unsupported.
Raycasts read current scene state rather than a previous presented-frame snapshot.

## HTML node bindings

`scene-node-overlay.mjs` provides the normal node/local-point facade. It owns registrations, one
shared source-frame subscription, projection delivery, retargeting and DOM restoration. Core
`ViewportFrameSnapshot.TryProject` performs projection; no per-element render loop is needed.
Native `ViewportOverlay` binds ordinary content via `SceneNodeAnchor Node="{Binding SelectedNode}"
and local `X/Y/Z` (or declarative `Target`). Browser content remains real DOM with styling,
events, focus and accessibility, outside the HDR attachment.

```js
import {createSceneNodeAnchorSource, createSceneNodeOverlay} from './mu3d/scene-node-overlay.mjs';
const source = createSceneNodeAnchorSource(applicationAnchorBridge);
const overlays = createSceneNodeOverlay({container: canvasFrame, source,
  onInteraction: active => { if (active) cancelSceneGesture(); }});
const card = overlays.bind(cardElement, {
  target: source.node(selectedNodeToken), localPosition: [0, 0.8, 0],
});
card.target = source.node(anotherNodeToken);
card.localPosition = [0, 0.5, 0];
card.target = null;
card.dispose(); overlays.dispose(); source.dispose();
```

| Native concept | Writable binding property | Default / units |
| --- | --- | --- |
| Core `Node` / declarative `Target` | `target` from `source.node(token)` | `null` hides |
| `X/Y/Z`, `LocalPosition` | `localPosition` | `[0,0,0]`, finite node-local FP32 |
| `Offset` | `offset` | `[8,-28]` CSS px / MAUI DIPs |
| Top-left content origin | `pivot` | `[0,0]`; Web alignment extension |
| `InputMode` | `interactive` | `true`; false is pass-through |
| `HideWhenOutsideViewport` | `hideWhenOutsideViewport` | `true`; frustum, not mesh occlusion |

Binding/overlay `options` are frozen; `subscribeOptions` notifies effective changes once. Overlay
`source`/`isEnabled` are writable. Disabling hides/releases interaction but retains registration.
Target/local changes invalidate placement until a matching successful frame. Source replacement
clears targets to null; explicitly assign new-source handles. Null, removed, hidden/ancestor-hidden,
camera-masked, behind-camera and invalid/unprojected targets hide. Opting out of frustum hiding
permits only finite projected outside points; wrappers remain viewport-clipped.

`source.clear()` invalidates every borrower's frame after failure/suspension; `overlay.clear()`
only clears its placement. Neither unregisters or requests rendering. Detach releases registrations,
subscriptions/interaction and restores DOM while retaining targets/options; attach registers afresh.
Source observation is lazy/shared and stops after the last observer. Disposal releases owned
wrappers/listeners/registrations, borrowing scene/source/content. It restores original element slots
unless application code moved them elsewhere; dispose before rebinding to another overlay.
Frameworks owning parentage should use a dedicated portal/mount element. Decorative content is
inert/pass-through; interactive content preserves native events and suppresses common bubbling.
Gate viewport capture handlers with `!overlays.hasActivePointer`; focus alone must allow the next
Canvas gesture. `isInteracting` also includes focus and is not the contact gate.

Use a `position:relative` container matching the Canvas content rectangle without independent
border/padding/transform. Logical `X/Y` are never multiplied by DPR. WebGPU submission is not a
physical presentation acknowledgment; HTML follows successful submission/browser composition.

### Explicit managed bridge

The host owns shared `SceneNodeAnchorSource(scene)` and maps nonempty scoped tokens to borrowed
Core nodes. `Register(node, localPosition)` returns a disposable `SceneNodeAnchorRegistration`;
`Update` changes its target/point. Call `CaptureFrame(frameSnapshot, camera.VisibilityMask)` once
at the successful submission boundary, before further mutation. It checks membership/visibility/
mask and current world matrices; the frame snapshot retains camera/extents, not future node changes.
Applications serialize capture/registration/scene mutation on their owning thread. Immutable output
contains no scene references. Register/effective update/unregister advance revision; no-op updates do not.

`createSceneNodeAnchorSource` requires:

- `sourceId`: nonempty managed source identity.
- Synchronous `configureAnchor({id,target,localPosition})`: register for null ID or update, resolve
  token/null, return `{id,revision}` (optional `sourceId`).
- Synchronous `removeAnchor(id)`: dispose registration, return current revision.
- `subscribeFrames(listener)`: return unsubscribe; deliver successful batches or null invalidation.

Mutations cannot reenter; asynchronous delivery uses generations to reject detached/replaced streams.
Managed batch fields: `SourceId, Revision, FrameId, Width, Height, Points`; points:
`Id, Projected, InsideViewport, X, Y, Depth`. IDs are nonempty strings, revisions/frame IDs nonnegative
safe integers, extents positive logical units, projected values finite, depth normalized 0–1.
Malformed batches reject atomically; copied frames are frozen. Identity/revision must match;
duplicate/older frame IDs remain rejected across registration changes/clear within one stream.
Read-only `sourceId`, `revision`, `latestFrame` and `subscribe` expose state.

### Manual batch escape hatch

`createSceneAnchorOverlay` from `scene-anchor-overlay.mjs` retains manual host-owned IDs/batches
and defaults: offset `[8,-8]`, pivot `[0,1]`, `interactive:false`, outside hiding true.
Bind with `{anchor:'id'}`, deliver `update({FrameId,Width,Height,Points})`, retarget with
`binding.setAnchor(id)` and clean up with `dispose()`. The host performs membership/visibility/mask
checks and shared projection once per successful frame. Missing/unprojected points hide, off-frustum
points mark `InsideViewport:false`, IDs are unique, flags boolean. Older batches are ignored;
malformed updates reject wholly. `clear()` hides/resets the stream on failure/replacement;
page hiding clears too, so update on resume. `setEnabled(false)` retains the last batch.
DOM borrowing, clipping, inert/input and cleanup contracts match the normal facade.

## Frame statistics source/view/overlay

`frame-statistics.mjs` exports `createFrameStatisticsSource`, `createFrameStatisticsView` and
`createFrameStatisticsOverlay`, corresponding to native behavior/view/overlay. The host retains
shared `FrameStatisticsCollector`/`FrameStatisticsSnapshot`; JS owns observable bindings and DOM.
Native `FrameStatisticsOverlay IsDetailed="True" SnapshotInterval="00:00:00.250"` attaches through
`ViewportTools`. The browser counterpart explicitly disposes its owned UI on host unload:

```js
import {createFrameStatisticsSource, createFrameStatisticsOverlay} from './mu3d/frame-statistics.mjs';
const source = createFrameStatisticsSource();
const overlay = createFrameStatisticsOverlay({container: canvasFrame, source});
overlay.isDetailed = true;
source.snapshotIntervalMilliseconds = 250;
source.publish(managedReport.snapshot); // newly captured/versioned snapshots only
overlay.dispose(); source.dispose();
```

| Native concept | JavaScript property | Default / units |
| --- | --- | --- |
| Source/collector binding | View/overlay `source` | `null` empty state; borrowed |
| `IsEnabled`, `SnapshotInterval` | Source/overlay `isEnabled`, `snapshotIntervalMilliseconds` | true, 500 ms; zero publishes every positive sample |
| `DrawCallCount`, `PrimitiveCount` | Source/overlay `drawCallCount`, `primitiveCount` | null; nonnegative safe integer or unavailable |
| `LatestSnapshot`, `SnapshotUpdated` | `latestSnapshot`, `subscribe` | Immutable camelCase snapshot, subsequent notifications |
| `IsDetailed`, `IsGraphVisible` | View/overlay `isDetailed`, `isGraphVisible` | false, true |
| Text/graph colors | `textColor`, `graphColor` | White, cyan |
| Graph background | `graphBackgroundColor` | Opaque RGB `(0,0.035,0.09)` |
| Overlay background | `backgroundColor` | RGB `(25,36,56)`, alpha 217/255 (`#D9192438`) |
| Placement/margin/width | Overlay `placement`, `margin`, `maximumWidth` | top-right; nine placements; 12 / 520 CSS px |
| Visibility | Overlay `isVisible` | true; independent of collection |
| History/graph | Read-only view `history` | 120 published FPS values; graph height 52 CSS px |

The collector's 120 raw frame samples differ from 120 throttled graph entries. Headline shows
FPS, average ms and displayed-history extrema; compact details show renderer/presentation totals
and draws/primitives; detailed mode adds interval extrema, sample count, CPU stages/resources.
Graph is right-aligned with bars/lines/guides and scale at least 60 FPS. Appearance changes update
existing DOM. Missing fields/resources stay null and display `—`; measured zero stays zero.

Writable source/display options expose frozen `options`/`subscribeOptions`; read once initially,
then receive effective changes. Subscriptions return unsubscribe. Interval must be finite,
nonnegative and within native TimeSpan range. Counts/interval changes preserve collected data.
Hosts apply options to their sampler and publish defensive frozen snapshot copies. The validation
host uses source-generated JSON and transfers full statistics only when version changes.

`subscribePublish` handles `source.publishSnapshot()`/`overlay.publishSnapshot()` capture requests
without adding a frame; synchronous host capture calls `publish` and marks its publication throttle.
Explicit capture works while collection is disabled. `reset()` publishes empty/notifies
`subscribeReset` for one explicit managed reset, retaining options; avoid duplicate empty publication.
Overlay forwards these operations to its source.

View starts observing immediately; append `view.element`, call `detach()` on unmount (DOM removal
alone does not unsubscribe), and `attach()` on reuse. Reattachment preserves history/applies latest
without inventing missed entries. Source replacement clears history/unsubscribes; null shows empty.
Overlay mounts itself in the borrowed positioned Canvas container and passes through input.
Hide detaches its view while source collection/overlay observation may continue; show retains
history/applies latest. Detach removes DOM/subscriptions; attach restores them. Idempotent disposal
removes owned DOM/resize observers/subscriptions without changing source/collector/renderer/container.
Dispose borrowers before an application-owned source.

The validation host samples positive completion-to-completion intervals after complete successful
scene/overlay/Canvas submissions. First submission primes the clock. Resume/surface changes/zero
continuity reset clocks only; paused independent on-demand requests retain history without defining
continuous cadence. Active Gizmo capture continues the existing viewport clock/FPS while animation
is paused. FPS measures CPU-observed successful submissions, not GPU time or display refresh.
`SceneRenderer.LastFrameTimings` supplies CPU stages; only actually supplied counts are reported
(viewer mesh-cache count). The module adds no timer, render loop, GPU query/readback or Canvas policy.

The adapters use explicit factories, validated observable properties, immutable state and
deterministic cleanup. Run the Node adapter tests with
`node --test tests/Mu3D.Web.Viewer.Tests/*.test.mjs` from the repository root. Browser verification
also needs actual DOM/layout/input checks and comparison of the affected routes in interpreted
and trimmed AOT builds.

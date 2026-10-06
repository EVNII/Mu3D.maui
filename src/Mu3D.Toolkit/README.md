# Mu3D.Toolkit

Optional UI- and backend-independent viewport helpers for Mu3D.

The package supplies `OrbitController`, which consumes normalized rotate, pan, dolly and
frame-update commands. Its additive `PanMode` selects camera-view or world-up-plane translation;
`MapController` packages world-plane pan and an upper-hemisphere camera constraint for map and CAD
navigation without acquiring UI input. `FlyController` is the separate first-person mode: it moves
a borrowed camera in world units and changes yaw/pitch, including one atomic combined navigation
call, without inventing an orbit target, raw-key policy or continuous input loop. `TransformGizmo` applies host-normalized cumulative
translate/rotate/scale interactions to an explicitly selected scene node. The gizmo supports
world/local axes, independently enabled translate/rotate/scale handles, per-operation snapping,
immutable begin/change/complete/cancel snapshots and configurable screen-relative sizing.
Like Three.js `TransformControls`, scale handles always follow the target-local axes; the visible
axis, pointer mapping and changed TRS scale component therefore remain aligned without shear.
`TransformGizmoHitTester` projects deterministic axis segments, rotation rings and the uniform-scale
center into physical viewport pixels, returning only the closest gizmo-owned handle inside a
configurable tolerance. It does not rank scene objects, begin interactions, capture pointers or own
handle rendering.

`TransformGizmoDragController` converts cumulative physical-pixel drags into those existing Gizmo
commands using a borrowed perspective camera and a validated handle hit. It shares the projection
math used by MAUI and browser adapters, supports an optional control-arbiter lease, and restores
the initial pose on cancel. The host owns capture and must cancel before changing camera/target/
viewport state; selection, device input, pen semantics and undo remain outside the controller.

`SceneRaycaster` provides a separate UI-independent scene-picking primitive. It unprojects one
physical viewport position, intersects visible indexed current rigid/morph geometry, returns the closest triangle
per mesh and orders mesh hits by camera distance. It does not acquire input or choose a selected
node. High-frequency application input can instead call `TryHitClosest`, cache its optional mesh
filter and receive a value-type result without allocating a hit collection, per-mesh hit objects or
scene traversal iterators. Every request reads current geometry and morph weights; finite negative
and above-one weights are applied in target order without copying the mesh or mutating base data.
Hit positions describe the current morphed mesh-local surface and corresponding world point;
distance remains in world units through nonuniform or reflected transforms. Non-finite morphed
triangles contribute no hit. Queries use current scene state, not a prior presented-frame snapshot.
Skin deformation and material alpha masks remain outside this CPU geometry path.

`TransformGizmoRenderPass` is an explicitly ordered HDR-linear render adapter for those handles. It
draws translate arrows, rotation rings and scale markers at physical-pixel sizes, highlights the
active handle and preserves negative or above-one RGB values without implicit gamut mapping,
clipping or tone mapping. Its immutable style uses explicitly tagged standard linear RGB colors and
defaults to a true overlay that loads prior color without attaching scene depth; an explicit
scene-depth-tested mode loads and tests depth without writing it. The pass owns only its
device/format-bound GPU resources and borrows the gizmo and target. At execution it requires the
target to belong to the context's scene, to have visible ancestors and to match the camera's mask.
Null, detached, hidden, camera-masked and permanent scene-root targets submit no handles; these
checks never clear `Gizmo.Target` or cancel a host-owned interaction. Ancestor layer masks do not
suppress an independently layered target. Input adapters must separately apply their target
eligibility and cancellation policy.

`ViewportControlArbiter` lets cooperating adapters claim control through disposable leases. A
higher-priority gizmo claim can revoke a camera claim while raw input and pointer capture remain
host-owned. The Toolkit does not acquire mouse, touch, pen or keyboard input, own selection, undo or
editing policy, or create a timer. Host adapters may implement `IViewportFrameRequester` to map
one-shot frame requests to their existing on-demand viewport scheduler.

`FrameStatisticsCollector` records host-observed frame intervals in a fixed-capacity rolling window
and produces immutable `FrameStatisticsSnapshot` values. Presentation and renderer CPU timings,
draw calls and primitives are independently optional; `RenderResourceCounts` distinguishes an
unavailable value from a measured zero. Snapshot creation has no UI dependency, so telemetry and
diagnostics overlays can refresh less often than rendering. These wall-clock values are not GPU
timestamp queries.
`FrameStatisticsDisplayMode` supplies the backend-independent Compact, Normal and Detail choices
for optional host indicators; the collector does not change collection when display mode changes.

Toolkit objects are explicitly constructed and application-owned. The package performs no assembly
scanning, reflection-based activation or backend-native interop.

`SceneViewportBudget` provides separate cancellable limits for live 3D viewport and
asset-loading work. Virtualized UI decides when to request and release its disposable leases. The
budget retains no scene, asset, view or native handle, and does not replace an application-owned
bounded asset cache. `SceneViewportActivity` gives UI adapters a common application-requested
`Active`/`Suspended`/`Unloaded` vocabulary; visibility discovery and range sizing remain host policy.

`AsyncAssetCache<TKey,TAsset>` is an explicit application-owned bounded LRU for managed asset
definitions. Exact-key misses are single-flight, a canceled caller stops waiting, the final waiter
cancels unfinished work, and full capacity evicts only completed entries. Removing an entry drops
the cache reference without disposing asset data still retained by scene instances.

`ViewportProgressController` applies an immutable snapshot of explicit
`IViewportProgressMapping` instances and issues one coalesced `IViewportFrameRequester` request.
It clamps finite application progress to zero through one but owns no animation, scroll view,
timer, scene, camera or target. `DelegateViewportProgressMapping` is the direct C# extension path;
MAUI bindable adapters live in `Mu3D.Maui.Toolkit`.

`Animation.IPlayable` is the UI-independent transport boundary for a glTF clip player, camera path,
MAUI animation bridge or application timeline. It reports playback state, position, duration,
seeking and looping, while implementations retain their own clock, evaluation and resource
lifetime. UI controls observe and operate the interface rather than depending on a format package.

`Helpers.AxesHelper` and `AxesHelperHitTester` define fixed-pixel world/target-local orientation
handles, including optional negative axes and twelve two-axis 45-degree camera directions. The
HDR-linear `AxesHelperRenderPass` renders that state without acquiring input or mutating a camera.
The pass reuses bounded numeric layout and vertex staging, reevaluates current camera/target
orientation and physical viewport settings, and preserves authored order for equal-depth handles.
Failed attempts clear staging; disposal releases capacity without disposing the borrowed helper
or orientation target. Hit tests use temporary bounded numeric layout and retain the existing
cardinal/distance/depth ranking. Target-local orientation remains an explicit geometry query for
the assigned node, including detached, hidden or camera-masked nodes.

`Helpers.GridHelper` defines a bounded finite XY, XZ or YZ reference grid in world units. Its
HDR-linear `GridHelperRenderPass` loads existing scene color and depth, blends physical-pixel minor,
major and colored center-axis lines, tests depth without writing it, and never adds selectable or
exportable scene geometry.
The pass reuses bounded numeric vertex staging and command labels, reevaluates current grid and
camera state on every execution, clears partial submissions after failures, and releases staging
capacity on disposal. This removes recurring staging allocations after warmup; render commands
and backend work can still allocate.

`Helpers.BoundsHelper` evaluates a borrowed target's current world transform, visible subtree and
active morph weights on demand. Precise mode transforms real vertices for a tight world AABB;
optional fast mode weakly caches and transforms conservative mesh-local bounds. Its HDR-linear
`BoundsHelperRenderPass` draws a physical-pixel world AABB either against loaded scene depth or as
an overlay. The helper owns no target, input, scene membership or exportable geometry.
The public `TryGetWorldBounds(out ...)` remains an explicit geometry query: it evaluates the target
subtree without a scene/camera context or ancestors outside that subtree. The render pass instead
restricts contributing meshes to its current scene, inherited visibility and camera layers. It
continues through masked parents to independently layered children, and `IncludeInvisible` overrides
hiding only. Detached/foreign targets produce no commands; `Scene.Root` can anchor the subtree,
ignoring its permanent container flags. Suppression retains the borrowed target. The pass reuses
bounded numeric line staging, reevaluates current rigid/morph/camera data and clears partial or
failed submissions before reuse; disposal releases staging capacity.

`Helpers.OutlineHelper` expands one borrowed target's current rigid/morph triangles for a private
GPU mask. `OutlineHelperRenderPass` composites the mask's outer screen-space edge into initialized
HDR scene color. Its scene-depth-tested mode samples the shared depth attachment so foreground
geometry occludes the edge; overlay mode draws the complete silhouette. It does not duplicate scene
membership, encode an image or perform CPU pixel transfer. Each execution restricts the borrowed
target to the context's scene, evaluates target/ancestor visibility and filters each mesh against
the camera mask. A differently layered group remains traversable for independently layered children.
`IncludeInvisible` overrides visibility checks only, preserving scene membership and camera-mask
filtering. `Scene.Root` is permitted as a subtree anchor; its permanent container flags do not hide
eligible descendants. Suppression retains `Helper.Target`.

The pass reuses clip-triangle staging between frames, recomputes current world/camera transforms
and morph weights, and releases staging on disposal. Reuse does not cache a stale silhouette or
remove command/backend allocations. Skin deformation and material alpha masks remain outside this
slice; the triangle limit applies to eligible meshes. Shared render eligibility does not certify
native input or generic MAUI overlay visibility alignment, nor actual browser GPU/HDR behavior.

## Scene-node anchor batches

`Mu3D.Toolkit.Overlays.SceneNodeAnchorSource` borrows a `Scene` and registers borrowed
`SceneNode` targets with finite FP32 local points. Capture its immutable `SceneNodeAnchorFrame`
immediately after a successful host frame using that frame's `ViewportFrameSnapshot` and camera
visibility mask. The source owns no UI, render clock or backend. Unbound/removed/invisible/masked
or behind-camera targets are unprojected; finite outside projections remain available for the UI
adapter's clipping policy. Root itself is excluded by the scene's visible traversal. Depth occlusion
is not inferred.

Registrations expose stable source-scoped IDs and atomic `Update(node, localPosition)`; effective
changes advance `Revision`. Disposal releases borrowed references without changing nodes or scene.
Frames defensively copy points and retain no node references. Capture and scene/registration mutation
must be serialized by the host. The optional Web Toolkit's normal HTML node binding uses this source;
UI acquisition and DOM ownership remain outside this package.

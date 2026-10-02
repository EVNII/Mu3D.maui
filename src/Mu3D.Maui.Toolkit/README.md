# Mu3D.Maui.Toolkit

Optional MAUI adapters for the UI-independent `Mu3D.Toolkit` package.

For the concise declarative path, `ViewportTools` groups nested `SceneSelectionTool`, `OrbitTool`,
`MapTool` and `TransformGizmoTool` elements under one SceneView Feature. `MapTool` is the XAML
map/CAD preset over `OrbitTool`: one-finger/left-button drag pans on the world-up plane, direct
two-finger and native trackpad Pan rotate around the target while Pinch/wheel dollies, and supported
right-button drag uses the same Rotate route. `OrbitTool` exposes the shared `PanMode`,
minimum/maximum polar constraints and one `ViewportInput` object divided into `Mouse`, `Trackpad`,
`Touchscreen` and `Keyboard` device groups; future DCC-style schemes compose those settings and richer modifier
handling without a new camera controller. Its stable
`NavigationCommand` accepts an `OrbitNavigationAction`, so MAUI buttons, menu items, gestures and
desktop keyboard accelerators can share one focus-independent action route. Mouse button actions,
native trackpad gestures, direct-touch gestures and optional native rotate/dolly keys are strongly
typed bindable XAML properties. Each device group has an `IsEnabled` switch, and drag actions or
individual keys can be disabled with `None`.
`FlyTool` is the distinct first-person XAML mode. Its `NavigationCommand` accepts discrete movement
and look actions, while an atomic vector overload accepts normalized held-key/gamepad chords. It
uses the same `ViewportInput` shape as Orbit and Map: Rotate means look, Pan means strafe/lift and
Dolly means forward/back movement without merging free-camera state into MapTool. One-pointer
Rotate and two-pointer Pan are isotropic on Fly; Pinch and supported wheels use Dolly.
`RotationSensitivity`, `PanSensitivity` and `DollySensitivity` use the same action names on every
controller; Fly applies them to look, local X/Y translation and local Z movement respectively.
Apple continuous trackpad translation feeds the same two-pointer route. Optional
movement/look damping is advanced by the behavior's on-demand presentation-frame chain and owns no
idle timer. Raw keyboard state remains host-owned.
`SceneSelectionTool.SelectedNode` is
a two-way bindable property that can drive `TransformGizmoTool.Target`; a miss may clear it and a
null target creates no gizmo. The selection request exposes all distance-ordered candidates before
commit so application code can replace or cancel the default nearest-owner proposal. The container
supports attached `SceneSelection.IsSelectable` for an individual opt-out and attached node masks
plus a tool mask for categories; neither reuses render visibility layers or adds selection state to
the base `SceneNode3D` contract. The container
creates a shared control arbiter and attachment-owned adapter/controller/gizmo state. Children
attach in order and detach in reverse; multi-selection, undo and editor tool state remain
application-owned. Downstream packages can implement `IViewportTool` directly without reflection
discovery.

`ProgressTool` is the declarative MAUI animation/scroll bridge. Its bindable `Progress` property
drives ordered `IViewportProgressMapping` children and requests one coalesced scene frame without
owning the source clock or control. `NodeTransformProgress` maps endpoints onto one explicit
`SceneNode3D` transform component; declarative cameras use the same mapping because they are scene
nodes. Applications may bind progress, assign it from `Scrolled`, or animate it with MAUI's normal
animation manager. Downstream mappings can target clip time, materials or application state without
reflection discovery.

`AxesHelper` adds a fixed-pixel HDR-linear orientation overlay to `ViewportTools`. Interaction is
off by default; when enabled, an explicit sibling `OrbitTool` handles cardinal and two-axis
45-degree view changes, or `ViewRequested` lets the application replace that camera policy.

`GridHelper` adds a finite depth-tested XY, XZ or YZ reference plane to `ViewportTools`. XAML can
bind its world origin, size, bounded divisions, major-line cadence and minor/major/center-axis
visibility. It remains a render-only helper rather than scene geometry.

`BoundsHelper` binds an explicit XAML `SceneNode3D` target and draws its world-axis-aligned bound.
XAML controls descendant and invisible-subtree inclusion, world padding, sRGB color, physical-pixel
line width, precise versus fast-conservative bounds, and depth-tested versus overlay rendering. The
helper follows current transforms without becoming selectable or extending the target's own bounds.

`OutlineHelper` binds the same kind of explicit XAML target but draws its actual screen-space model
silhouette instead of a box. XAML controls subtree inclusion, the source-triangle safety limit,
sRGB color, physical-pixel width and normal scene occlusion versus always-visible overlay rendering.
It remains a render-only viewport tool rather than selectable or exportable scene geometry.

`ViewportOverlay` is the shared visual host for arbitrary MAUI content inside `ViewportTools`.
Without an anchor it supports nine fixed viewport alignments; an optional `SceneNodeAnchor` instead
projects a declarative `SceneNode3D` or low-level Core node from the latest successful frame. One
manager and one frame subscription serve every overlay in a viewport. `Interactive` content holds
the shared overlay-input priority while active; `PassThrough` content remains a HUD. Normal MAUI
binding, styling, accessibility, animation, layout constraints, visibility and Z order remain
available because content stays in the native visual tree. The existing `SceneNodeAnchorLayer`
continues to work as a sibling and can now also attach directly through `ViewportTools`.

`RenderOutputToolbar` presents its built-in outputs as a compact, horizontally scrollable MAUI toolbar
inside the viewport and selects one stable `RenderOutputId` without conflating output identity with
visibility masks or pass order. It is the first Toolkit-provided control built on
`ViewportOverlay`; downstream layouts use the same host. `Placement` supports all nine viewport
alignments, with logical `Margin`
and `MaximumWidth`; `IsVisible` and `IsInteractive` are independent. Nested `RenderOutputOption`
elements expose explicitly registered third-party IDs. The overlay stays in the MAUI visual tree for
layout, input and accessibility while HDR scene color remains GPU-native. Disable or detach restores
the previous output only while the tool still owns its assignment, preserving later application
changes. `RenderOutputTool` remains the behavior-compatible base name. Render Output, generic MAUI
overlays and Axes use the shared UI-independent `ViewportOverlayPlacement` enum.

`PlaybackToolbar` is the reusable transport control on the same overlay host. It consumes one
application-owned `Mu3D.Toolkit.Animation.IPlayable` and presents Start, Play/Pause, continuous
seeking, optional time text and Loop. The playable retains its scheduling, clip/camera-path
evaluation and lifecycle; the bar owns no timer, glTF object or MAUI animation. Placement, margin,
width, visibility and interactivity are ordinary bindable XAML properties, and a disabled or
noninteractive bar becomes pass-through to the viewport below.

`VsyncFrameSource` is the standard frame source for continuously rendered viewports. On iOS and
Mac Catalyst it owns a `CADisplayLink` whose `PreferredFrameRateRange` follows the window screen's
`MaximumFramesPerSecond` by default, so ProMotion displays are not capped at MAUI's 60 Hz ticker;
other platforms use the MAUI VSync animation service, which already follows the display. The
application still owns scheduling policy: start it while an animated view is visible and stop it on
hide or pause. iPhone apps additionally need the `CADisableMinimumFrameDurationOnPhone` Info.plist
opt-in. `MeasuredTicksPerSecond` and `ScreenMaximumFramesPerSecond` expose the live contract for
diagnostics overlays.

`OrbitSceneViewFeature` and `TransformGizmoSceneViewFeature` compose the existing replaceable input
behaviors through `Mu3DSceneView.Features`. They attach with the native Handler, detach in reverse
collection order and preserve application ownership of controllers, arbiters, gizmos and targets.
The Gizmo feature registers its owned HDR-linear pass after the default scene output; renderer
recreation rebuilds the pass and disposes the prior pass resources before renderer release. An
application-assigned complete `RenderPipeline` still takes precedence.

The package supplies `ViewportNavigationBehavior`, which maps shared device-separated input
settings into any application-owned `IViewportNavigationController`,
participates in optional `ViewportControlArbiter` leases,
advances interaction and damping tails through MAUI's platform VSync ticker only while motion is
active, and removes its ticker, recognizers and subscriptions when idle, unloaded, detached or
disposed. On every supported target it also maps pointer-
wheel/trackpad scrolling to Dolly plus optional focused portable Rotate, Pan and Dolly keys.
Windows and Android additionally map numeric Add/Subtract.
Applications may disable this focused-key adapter and route `NavigationCommand` through MAUI
keyboard accelerators instead. Apple uses a
Handler-scoped scroll recognizer and private first-responder proxy rather than page-global commands.
Capability properties report whether the current target supplies each platform adapter.
MAUI Pan plus Pinch cover touch-capable Windows devices and Android. Apple keeps the shared Pan
mapping but uses an attachment-owned native `UIPinchGestureRecognizer` for direct iPad touch and Mac
trackpad transform Pinch, locally permitting the two recognizers to overlap. Consumers that need
indirect Apple trackpad transform events declare `UIApplicationSupportsIndirectInputEvents` in
Info.plist; Gallery declares it for iOS and Mac Catalyst. The Toolkit does not mutate application-
global gesture policy.
A reference-counted Apple attachment lease disables the containing navigation controller's
interactive-pop recognizer while a SceneView is loaded and restores its prior state on
unload/detach. Discrete wheel events retain Dolly/forward-back; continuous trackpad translation
feeds `ViewportInput.Trackpad.TwoFingerDragAction`, and the attached controller supplies the
camera-mode interpretation.

`TransformGizmoPointerBehavior` maps one accepted primary mouse/touch pointer into an application-
owned `TransformGizmo`. It converts native coordinates to physical viewport pixels, hit-tests only
the gizmo's own handles, obtains an optional gizmo-priority lease, captures the accepted pointer and
submits cumulative projected-axis translation, ring-tangent rotation or exponential scale commands.
Windows uses exact WinUI pointer capture, Android owns only the accepted touch stream, and Apple uses
a one-touch pan recognizer. Stylus input is deliberately ignored so pen semantics remain application-
owned. Disable, detach, disposal, native cancellation and lease revocation cancel and restore the
active interaction before releasing adapter-owned state.
The pointer adapter follows the same scene eligibility as visible handles: the target must be an
ordinary node in the attached view's current scene, visible through its ancestors and in the
camera's own target layers. A hidden, detached, foreign-scene or masked target retains its binding
but cannot begin a drag. Losing eligibility during input or a successful frame cancels the edit,
restores its initial pose and releases the control lease and native capture. Replacing the view's
scene or camera cancels the old projection mapping. Applications still serialize mutable scene
changes and request a frame with `InvalidateScene`; the adapter adds no scene-mutation observer
or render clock. Explicit Toolkit hit-test/drag mathematics remain available to custom hosts.

`FrameStatisticsBehavior` records successful `Mu3DSceneView` presentation frames into an
application-replaceable `FrameStatisticsCollector` and publishes immutable snapshots on a separate,
configurable cadence. Applications may supply current draw-call and primitive counts when those
values are known. `FrameStatisticsView` borrows one behavior and renders either a compact summary or
detailed frame, presentation, renderer and resource breakdown plus a 120-point rolling FPS graph.
`FrameStatisticsOverlay` is the XAML-first combination: it creates the behavior/view at
`ViewportTools` attachment, exposes the collector and reset/snapshot operations, and mounts the
pass-through panel through the common nine-position viewport overlay.
The indicator follows the current/minimum/maximum convention used by Three.js performance panels,
but text and graph redraw only when a throttled snapshot is published rather than on every frame. It
unsubscribes when unloaded or disposed and never attaches, resets or disposes the source behavior.
All three input/statistics behaviors borrow their assigned application objects; none owns the scene view, controller,
gizmo, hit tester, arbiter or externally assigned collector.

`SceneNodeAnchorLayer` batches placement of real MAUI child views from the exact camera matrices,
physical extent and logical extent of the latest successfully presented `Mu3DSceneView` frame. Each
child names an application-owned scene node and optional node-local point/ logical offset. The layer
subscribes only while loaded, performs camera-frustum clipping, and deliberately makes no depth-
occlusion claim.
Both anchor APIs require an ordinary visible member of the view's current scene whose own layer
matches its camera. Null, hidden, removed, foreign-scene, behind-camera or non-finite projections
hide the content while retaining the borrowed target. `HideWhenOutsideViewport=false` permits a
finite projection outside the frustum; it does not keep invalid targets visible. Parent layer
mismatch does not hide independently layered children, and permanent-root flags do not hide them.
Scene/camera replacement clears stale placement until another successful frame. A hidden generic
overlay releases its active overlay-input lease; a standalone anchor layer clears placement on
unload or disconnect. Fixed overlays without an anchor continue to use viewport placement.

`SceneViewProxyHost` is the preferred product-feed primitive. One visual-tree-scoped host uses a
hidden one-pixel `Mu3DView` for MAUI-managed graphics-device lifecycle and shares that Device, Queue,
renderer, pipelines and uploaded resources with lightweight `SceneViewProxy` elements. Every proxy
still contains its own manual `Mu3DView`/native Surface, so the presented buffer is a child of the
MAUI card and moves with native scrolling without reconstructing coordinates. Surface creation is
serialized instead of initializing every realized item in one batch. No GPU readback, image
encoding or decode occurs, and HDR-first `Rgba16Float` presentation remains available.

`SceneBackgroundColor` accepts alpha-0 transparent or alpha-1 opaque colors. Opaque proxies use a
direct HDR Surface. Transparent proxies select the platform compositor mode behind the shared XAML
contract: premultiplied on Windows, straight alpha on Apple, and inherited native-window alpha on
Android. The renderer remains premultiplied internally; Apple uses a reusable FP16 texture and
full-screen GPU pass to convert to Metal straight alpha without a second scene render or CPU
readback. Fractional background composition remains deferred. RGB is decoded to linear light while
FP16 object values remain extended-range.
`RenderRevision` or
`InvalidateScene` requests a replacement frame after mutable scene or camera state changes.
Binding `IsRenderingEnabled` to a bounded visible range unconfigures cold Proxy sessions and restores
their XAML placeholders without discarding application-owned scene/camera data. Disabling the Host
unconfigures every Proxy session for page suspension; normal Handler teardown remains the final owner.

`SceneViewportHost` remains available for explicitly coordinated live regions rendered into one
shared Mu3D Surface. It is not the catalog default because a sibling Surface cannot be guaranteed to
present atomically with native collection scrolling.

Do not register a proxy host, proxy, page, Handler or Surface as an application singleton. Those
objects belong to the MAUI visual lifecycle. Application DI may hold bounded immutable asset caches,
and a future graphics-host service may coordinate window-scoped device/pipeline caches, but native
presentation ownership remains with each visual-tree host.

`VirtualizedSceneView` remains available for a small number of independent interactive viewports.
It accepts an explicit `SceneViewportActivity`: active items load content and
request a live lease, suspended items release their internal `Mu3DSceneView`/surface but retain
already loaded managed content, and unloaded items release both. This is intentionally separate
from MAUI `Loaded`, because a virtualized collection may keep off-screen cells realized. New content
is loaded before a live view is created, stale recycled-cell generations are cancelled, and native
surface teardown stays in normal MAUI Handler lifecycle. An `ISceneViewportSource` may create
lightweight instances from a separate bounded application asset cache; the control creates no
hidden global cache. Do not use one instance per item in a long catalog; bind page visibility to
`IsActivationEnabled` when this independent-view path is appropriate.

Transform-gizmo handle rendering is supplied by the UI-independent `Mu3D.Toolkit` package. The
Gallery includes focused Transform Gizmo and Frame Statistics examples that compose these optional
packages without moving application selection, tool state or continuous-render policy into Mu3D.

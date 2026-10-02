---
title: Application-owned pointer and pen input
description: Feed native mouse, touch, and pen samples into Mu3D scene raycasting without moving editor policy into Core.
feature_id: pointer-pen-input
---

# Application-owned pointer and pen input

Mu3D deliberately does not define a second cross-platform raw pointer model. MAUI or the native
platform owns device acquisition, capture, pressure, tilt, eraser state, coalescing, and lifecycle.
The application converts only the position it wants to query into physical viewport pixels and
passes that position to <xref:Mu3D.Toolkit.Selection.SceneRaycaster>.

The Gallery keeps its `PointerInputBridge` inside the sample application. It is not part of
`Mu3D.Maui.Toolkit`. The Behavior subscribes to the native view only while the MAUI view is loaded,
captures one accepted primary contact, and removes every platform event on unload, handler change,
or detach:

```xaml
<mu3d:Mu3DSceneView
    x:Name="SceneView"
    SurfaceError="OnSurfaceError">
  <mu3d:Mu3DSceneView.Behaviors>
    <pages:PointerInputBridge Sampled="OnPointerSampled" />
  </mu3d:Mu3DSceneView.Behaviors>

  <mu3d:Mu3DSceneView.SceneContent>
    <mu3d:Scene3D>
      <mu3d:Scene3D.Camera>
        <mu3d:PerspectiveCamera3D Z="6.5" />
      </mu3d:Scene3D.Camera>
      <mu3d:Cone3D x:Name="Target" />
    </mu3d:Scene3D>
  </mu3d:Mu3DSceneView.SceneContent>
</mu3d:Mu3DSceneView>
```

The page chooses when a contact becomes an application stroke and performs one explicit geometric
query. That separation is important: a raycast hit is evidence, not a selection or paint command.

```csharp
private readonly SceneRaycaster raycaster = new();
private static readonly Predicate<Mesh> RaycastFilter =
    static mesh => mesh.Name != "Application-owned hit marker";

private void OnPointerSampled(
    object? sender,
    ApplicationPointerSample sample)
{
    if (sample.Phase is not ApplicationPointerPhase.Pressed and
        not ApplicationPointerPhase.Moved)
    {
        return;
    }

    bool hasHit = raycaster.TryHitClosest(
        SceneView.Scene!,
        (PerspectiveCamera)SceneView.Camera!,
        SceneView.PixelWidth,
        SceneView.PixelHeight,
        sample.PositionPixels,
        out SceneRaycastIntersection proposed,
        RaycastFilter);

    // Application code decides whether hasHit means paint, select, annotate, or nothing.
}
```

`TryHitClosest` traverses the visible hierarchy directly and returns a value type. With a cached
filter delegate it does not create a result list, per-mesh hit objects, or traversal iterators. The
ordered `HitTest` API remains available when an application really needs every mesh candidate.

## Platform mapping

- Windows reads WinUI `PointerRoutedEventArgs`, including pen pressure, X/Y tilt, eraser and barrel
  button state, and uses exact pointer capture for the accepted contact.
- Android reads the native `MotionEvent`, accepts finger, mouse, stylus and eraser tool types, and
  normalizes pressure plus orientation/tilt while preventing parent interception only for the
  active contact.
- iOS and Mac Catalyst use a zero-delay continuous UIKit contact recognizer plus a stylus-filtered
  `UIHoverGestureRecognizer`. `UITouch` supplies Apple Pencil pressure, altitude and azimuth during
  contact; the hover recognizer supplies the position and available orientation before contact.
  Apple Pencil hover requires supported Pencil/iPad hardware and iPadOS 16.1 or later. MAUI handler
  load/unload remains the lifetime owner on both Apple targets. The bound hover altitude/azimuth
  APIs require iOS/Mac Catalyst 16.4, so 15.0–16.3 retains the moving preview without hover-tilt
  rotation. On iPad, the sample also carries nullable `HoverDistanceNormalized` data from
  `ZOffset` (zero near the screen and one at maximum hover distance) from iPadOS 16.1, plus the
  nullable `IsHoverToolPreviewPreferred` system preference from iPadOS 17.5. Mac Catalyst and
  non-Apple providers leave those iPad-only values unavailable instead of advertising a false
  capability.

The Gallery changes the hit marker's size from normalized pressure and maps pen tilt direction to
its color. A same-tree MAUI vector cursor follows pen hover and rotates with the reported tilt, so
cursor motion remains in the UI layer instead of becoming another render pass. On Windows its
position and tilt are updated through the element's Composition visual, and a scoped window cursor
hook suppresses WinUI's later cursor restoration while the pen remains over this viewport. The hook
is removed and the arrow restored on exit, cancel, handler replacement, unload, or detach. Android
uses the corresponding native pointer-icon lifetime. Apple uses a scoped `UIPointerInteraction`
hidden style while stylus hover/contact owns the drawing cursor. It restores the default pointer on
exit, cancel, handler replacement, unload, or detach. Mouse and trackpad hover never enters the
stylus-filtered preview path. The first real distance-bearing sample enables the page's Apple
Pencil physical-shadow switch and, when available, initializes it from the system tool-preview
preference. The default application-owned `IPenTipShadowPreview` builds a complete 166 mm by
8.9 mm Apple Pencil-style reference, including its nib, taper, full body, rounded rear cap and one
flat side. That caster hierarchy is detached from the Scene and supplied only to the
Gallery-owned display-normal render feature. It therefore never enters camera color/depth,
raycasting, AO, transmission or the scene directional-shadow map. The Pencil model itself is never
visible; this is explicit feature ownership, not a zero-alpha material trick.

A capped closest raycast anchors the hidden caster to the visible world surface. The page converts
its millimetre dimensions to world units from caster camera-space depth, field of view, physical
viewport pixels and calibrated display points per inch. Camera dolly, field-of-view changes,
application canvas zoom and Pencil hover distance therefore update the world-space scale while the
complete Pencil remains the same physical size on the display. The sample defaults to a compatible
iPad logical density and keeps that calibration an explicit constructor input for other devices.

The sole **Display-normal surface shadow** path uses no light. It flattens the complete hidden Pencil
at its anchored screen depth before rasterizing an FP16 mask, avoiding near-plane, model-depth and
scene-occlusion clipping even when the virtual 166 mm body crosses scene geometry. Each mask vertex
stores caster view depth, coverage and endpoint-normalized opacity derived from its physical
distance above the device display. Caster preparation normalizes the hover range so the first
detectable/maximum hover distance is exactly zero shadow and
screen contact is exactly the selected shadow coefficient. An endpoint-preserving exponential
coefficient changes the curve between those limits. Because distance is measured for every
transformed vertex, Pencil tilt naturally makes the raised body and rear cap fainter than the nib
rather than applying one alpha to the whole model.

A private `SurfaceNormal` plus depth output reconstructs the visible receiver beneath each mask
pixel. The absolute caster-to-receiver distance selects a local Gaussian sigma, so the nib remains
sharp near its projection surface while raised or depth-separated parts of the body become
progressively softer. A 7-by-7 Gaussian gather replaces the former fixed sparse blur. Its FP16 mask
uses linear subpixel sampling, and normalized hover height is lightly low-pass filtered without
allocating while snapping its two endpoints exactly. This avoids integer sample/radius and
hover-to-exit jumps that could flash while lifting the Pencil. The
page exposes live controls for overall shadow coefficient, Gaussian blur growth per millimetre and
hover-shadow falloff per millimetre. Opacity exponentials are prepared once per caster vertex rather
than repeated inside every 7-by-7 fragment gather. A Pencil-bounds scissor limits the expensive
composite to the projected shadow area. Surface normals
continue bending the result through sloped geometry. Every visible scene surface beneath the mask
can receive it regardless of front/back ordering; only clear background is excluded. Pressed,
moved and released samples retain a zero tip-to-display distance, so contact keeps the nib shadow
instead of hiding it. Exit, cancellation, disabling the option or page teardown removes the pass.
The platform-neutral circular UI cursor and all authored scene lights remain independent.

UIKit uses separate contact and hover recognizers. A Pencil `Released` sample therefore remains the
active preview state while UIKit hands ownership back to hover instead of emitting an intermediate
`Exited`; only a real far-hover end, cancellation or failure terminates the preview. A hover end next
to the display is classified as the reciprocal transition into contact. The mask also compensates
the elevated caster's screen projection: its nib remains pinned to the current raycast landing point
at every hover height, while height changes opacity/softness and tilt changes the body silhouette.
Landing UV comes directly from normalized pen position and does not require a triangle beneath the
nib. A hit refreshes only projection depth; on clear background the previous valid depth, or an
initial scene-origin/safe camera fallback, keeps the mask defined. Visible geometry beneath another
part of the long Pencil body can therefore receive shadow while the nib is over clear background.

`PointerInputBridge.SuppressDirectTouchPressWhilePenActive` is an opt-in application palm-rejection
example. During pen hover/contact, a finger cannot enter this bridge's primary Press/Move/Release
raycast stream on Apple, Android or Windows. The native touch stream remains unconsumed, so separate
two-finger Pan/Pinch recognizers on the same SceneView stay available. Real pen exit/cancellation
restores ordinary finger primary input.

The normalized sample is a value type, tilt colors are precomputed, the contact raycast is capped at
the viewport frame rate, and detailed status text changes only at input-phase boundaries. Continuous
hover and movement therefore do not manufacture event-argument, color, hit-list, or status-string
garbage on the application hot path.

This remains application UI rather than a Toolkit drawing policy. The example intentionally does
not create strokes, a canvas, smoothing, undo, or tool modes; those creative and editor policies
remain application-owned.

The CPU raycaster intersects current rigid/morph triangle positions without changing immutable base
geometry or morph weights. Local hit positions belong to the current morphed surface, and queries
read current scene state rather than a previously presented frame. Skin deformation and material
alpha masks remain unsupported. An application can replace it with a BVH, physics query, or GPU
object-ID path while retaining the same native-input boundary.

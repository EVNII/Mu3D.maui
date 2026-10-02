---
title: UI anchors
description: Position real MAUI UI from the exact camera and extent of successfully presented Mu3D frames.
feature_id: ui-anchors
---

# UI anchors

<xref:Mu3D.Maui.Controls.Mu3DSceneView.LatestFrameSnapshot> exposes the exact view matrix,
projection matrix, physical extent and logical extent associated with the most recently
successfully presented scene frame. A timeout or failed frame does not publish coordinates for
content the user never saw.

The concise XAML path combines <xref:Mu3D.Maui.Toolkit.Overlays.ViewportOverlay> with a
<xref:Mu3D.Maui.Toolkit.Overlays.SceneNodeAnchor>. The shared per-viewport overlay manager batches
all anchored content from one frame subscription and supplies the owning SceneView automatically:

```xaml
<mu3d:Mu3DSceneView>
  <mu3d:Mu3DSceneView.Features>
    <toolkit:ViewportTools>
      <overlays:ViewportOverlay InputMode="PassThrough">
        <overlays:ViewportOverlay.Anchor>
          <overlays:SceneNodeAnchor
              Target="{x:Reference ProductNode}"
              X="0"
              Y="1"
              Z="0"
              Offset="8,-28" />
        </overlays:ViewportOverlay.Anchor>
        <Label Text="Selected product" />
      </overlays:ViewportOverlay>
    </toolkit:ViewportTools>
  </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

Use `Target` for an explicitly referenced declarative `SceneNode3D`, or bind `Node` to a low-level
Core `SceneNode`. The properties are mutually exclusive. `X`, `Y` and `Z` select a point in
node-local space; `Offset` provides the final logical MAUI displacement after projection.

The earlier <xref:Mu3D.Maui.Toolkit.Overlays.SceneNodeAnchorLayer> remains a compatible batched
layout for application-owned child collections. It may still be placed as a sibling with an
explicit `SceneView`, or declared directly inside `ViewportTools`; the latter path receives the
SceneView and shared successful-frame updates from the common overlay manager.

Both APIs hide null, hidden, detached, foreign-scene and camera-masked targets, retaining the node
binding so content can return when the target becomes eligible. Hidden ordinary ancestors hide
their children; ancestor layer masks do not hide children on independently visible layers.
`HideWhenOutsideViewport="False"` permits finite positions outside the camera frustum. It still
hides behind-camera or otherwise unprojected targets. Replacing the SceneView's scene or camera
waits for another successful frame before using its projection. A hidden interactive overlay
releases its viewport-input lease, and a standalone anchor layer hides its children on unload.

## Fixed viewport content

Without an `Anchor`, `ViewportOverlay` hosts arbitrary real MAUI content at one of nine fixed
alignments. Normal MAUI binding, style, accessibility, animation, layout constraints, visibility
and Z order remain available because content stays in the native visual tree:

```xaml
<overlays:ViewportOverlay
    InputMode="Interactive"
    Margin="16"
    Placement="BottomCenter">
  <Grid ColumnDefinitions="Auto,*,Auto" WidthRequest="520">
    <Button Text="Play" />
    <Slider Grid.Column="1" />
    <Label Grid.Column="2" Text="01:24 / 03:40" />
  </Grid>
</overlays:ViewportOverlay>
```

`TopLeft`, `TopCenter`, `TopRight`, `CenterLeft`, `Center`, `CenterRight`, `BottomLeft`,
`BottomCenter` and `BottomRight` use the shared
<xref:Mu3D.Toolkit.Helpers.ViewportOverlayPlacement> contract. `InputMode="Interactive"`
temporarily takes the shared overlay-input priority so Orbit, selection and Gizmo controllers do
not start from the same pointer interaction. `PassThrough` makes the complete subtree
input-transparent for HUD-only content. Toolkit controls such as
<xref:Mu3D.Maui.Toolkit.Controls.RenderOutputToolbar> use this same host.

## Snapshot projection

<xref:Mu3D.Rendering.ViewportFrameSnapshot.TryProject%2A> is UI-framework independent. It reports
both physical-pixel and logical coordinates plus normalized depth. A successful projection means
the point is finite and in front of the camera; inspect
<xref:Mu3D.Rendering.ViewportProjection.IsInsideViewport> separately for frustum clipping.

The snapshot is viewport-specific. Do not store its screen position on a `SceneNode` or reuse one
view's result in another view with a different camera, size or display scale.

The anchor system provides camera-frustum visibility only. It does not read the depth buffer
and therefore does not claim that an anchor is hidden behind another mesh. Depth-based occlusion is
a separate optional render/readback feature.

For moving scenes, update 3D state from the platform's VSync/suspend-aware animation service and
invalidate the scene. The anchor layer updates only after the resulting frame is successfully
presented. Do not create a generic timer merely to reposition UI.

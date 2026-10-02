---
title: Bounds and outline helpers
feature_id: bounds-helper
---

# Bounds and outline helpers

`BoundsHelper` draws a target node's current world-axis-aligned bounding box without adding helper
geometry to the scene. It is opt-in, disposable, and does not affect selection, export, physics,
or scene bounds.

```xaml
<toolkit:BoundsHelper
    Target="{x:Reference ProductModel}"
    Color="#FFFF9D24"
    LineWidth="3"
    Padding="0.08"
    IncludeDescendants="True"
    Precise="True"
    DepthMode="SceneDepthTested" />

<toolkit:OutlineHelper
    Target="{x:Reference ProductModel}"
    Color="#FF38E8FF"
    LineWidth="4"
    IncludeDescendants="True"
    DepthMode="SceneDepthTested" />
```

`Target` accepts any declarative `SceneNode3D`. `IncludeDescendants="True"` combines every eligible
mesh in the target subtree. With `IncludeInvisible="False"`, an invisible ancestor suppresses its
whole subtree exactly as visible scene traversal does. Set `IncludeInvisible="True"` to inspect
hidden content.

Both render passes require the target to belong to the current scene and filter each contributing
mesh against the camera's layers. A differently layered parent still allows independently layered
children to contribute. `IncludeInvisible="True"` overrides hiding, while scene membership and
camera layers still apply. Hidden or removed targets retain their binding so the helper can resume
when they become eligible. The low-level Toolkit bounds query without a scene/camera context
continues to calculate the supplied subtree's geometry explicitly.

`Precise="True"` (the default) transforms current rigid/morph vertices before finding world extrema,
so rotation still produces a tight world AABB. Set it false only when a cached local box transformed
by eight corners is an acceptable conservative approximation for a very large mesh. Skinned meshes
currently use unskinned positions transformed by the mesh node.

`OutlineHelper` is a separate silhouette helper, not a bounding-box display mode. It rasterizes the
target's current rigid/morph triangles into private GPU mask and depth attachments, then composites
only the outer screen-space edge into the existing HDR color target. It does not duplicate scene
membership, encode an image, or transfer pixels through the CPU. The initial outline path does not
apply skin deformation or material alpha masks. `TriangleLimit` supplies an explicit per-frame CPU
geometry-expansion safety bound.

Both helpers expose `DepthMode`. `SceneDepthTested` is normal scene behavior: foreground geometry
can hide the helper. `Overlay` draws the helper through other scene geometry. Color remains
HDR-linear in either mode; `LineWidth` is measured in physical pixels.

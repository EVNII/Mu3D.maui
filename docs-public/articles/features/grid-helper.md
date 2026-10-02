---
title: Scene grid helper
description: Add a finite HDR-linear, scene-depth-tested reference grid through XAML.
feature_id: grid-helper
---

# Scene grid helper

`GridHelper` draws a finite reference plane in world units. It is a `ViewportTool`, not scene
geometry: it does not affect bounds, selection, physics, serialization, or export. The render pass
loads both existing HDR-linear color and scene depth, blends premultiplied line color, and leaves
depth unchanged. Scene objects therefore occlude the grid without the grid changing later passes.

## Declare a grid in XAML

Place the helper in the same explicit `ViewportTools` collection as camera controllers and other
tools:

```xaml
<toolkit:ViewportTools>
  <toolkit:OrbitTool Camera="{x:Reference MainCamera}" />
  <toolkit:GridHelper
      Plane="XZ"
      Size="12"
      Divisions="24"
      MajorLineEvery="4"
      ShowMinorLines="True"
      ShowMajorLines="True"
      ShowCenterAxes="True" />
</toolkit:ViewportTools>
```

`Plane` accepts `XY`, `XZ`, or `YZ`. `X`, `Y`, and `Z` move the grid origin in world units. `Size`
is the full width along both plane axes, while `Divisions` is the number of equal intervals.
`MajorLineEvery` controls emphasis without changing spacing. Division count is bounded by
`Mu3D.Toolkit.Helpers.GridHelper.MaximumDivisions` so an untrusted binding cannot generate
unbounded frame geometry.

Minor, major, and center-axis colors and physical-pixel widths can be replaced with one immutable
`GridHelperRenderStyle`. Colors remain scene-linear and may contain HDR values above one.

The shared render pass reuses bounded vertex staging while evaluating current grid, camera, and
viewport settings each frame. Empty or failed submissions cannot carry previous lines into the
next frame. Disposing the pass releases its graphics resources and staging capacity while leaving
the borrowed grid definition available to the application.

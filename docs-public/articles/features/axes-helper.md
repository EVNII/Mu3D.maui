---
title: Orientation axes helper
description: Add an HDR-linear orientation overlay with optional cardinal and 45-degree camera navigation.
feature_id: axes-helper
---

# Orientation axes helper

`AxesHelper` is a passive, fixed-physical-pixel orientation overlay by default. It is not scene
geometry, is never selectable, does not affect bounds or exports, and always renders after scene
color without a depth attachment. Red, green, and blue identify X, Y, and Z; every cardinal marker
contains a high-contrast `+` or `-`, and negative endpoints are also dimmed. Optional neutral dots
select two-axis 45-degree views. The signs are vector geometry rather than font glyphs, so they keep
the same physical-pixel shape on every target platform. Sign color is contrast-aware: bright green
markers use a dark sign while red, blue, and other dark markers use a light sign.

## Add passive axes

Place the helper in the same explicit `ViewportTools` collection as other tools:

```xaml
<toolkit:ViewportTools>
  <toolkit:AxesHelper
      Placement="TopRight"
      ScreenSize="112"
      ShowNegativeAxes="True"
      ShowDiagonalViews="True" />
</toolkit:ViewportTools>
```

The overlay uses physical pixels, so DPI changes do not make its hit targets too small. `Margin`,
`Placement`, `ScreenSize`, visibility, negative axes, and diagonal handles are bindable. World axes
are the default. Set `Space="TargetLocal"` and assign `Target` to display a declarative node's world
orientation instead.

## Enable camera navigation

Interaction is opt-in. Explicitly reference the sibling Orbit tool so the controller updates its
own azimuth, polar angle, target, distance, and pending damping state together:

```xaml
<toolkit:ViewportTools>
  <toolkit:OrbitTool
      x:Name="Orbit"
      Camera="{x:Reference Camera}" />
  <toolkit:AxesHelper
      IsInteractive="True"
      Orbit="{x:Reference Orbit}"
      ShowDiagonalViews="True" />
</toolkit:ViewportTools>
```

Click a cardinal endpoint to move the camera onto positive or negative X, Y, or Z while preserving
the current orbit target and distance. Click a neutral dot to choose one of the twelve normalized
two-axis directions, producing an exact 45-degree relationship between those axes. Top and bottom
views respect the Orbit controller's pole constraints.

The adapter installs one MAUI tap recognizer only while attached and interactive. An accepted
helper tap briefly claims the shared `ViewportControlArbiter`, preventing the same tap from becoming
scene selection. It does not interfere with a transform drag that already owns control.

## Replace the camera policy

`ViewRequested` fires before the default Orbit action. An application can replace
`CameraDirection`, cancel the request, or set `Handled` after applying its own camera rig. This keeps
camera transitions, projection changes, undo policy, and animation application-owned. The
UI-independent `Mu3D.Toolkit.Helpers.AxesHelper` and `AxesHelperHitTester` can also be used by a
non-MAUI host.

The shared pass reuses bounded drawing data while evaluating current camera, target orientation,
placement and physical viewport settings each frame. Equal-depth handles retain their authored
drawing order. Failed submissions cannot carry old geometry into the next frame; disposal releases
owned graphics resources and staging while retaining the borrowed helper and target. Target-local
orientation explicitly queries the assigned node even if it is detached, hidden or camera-masked.

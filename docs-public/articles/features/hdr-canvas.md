---
title: HDR tiled canvas
description: Composite tagged HDR colors in bounded Float16 or Float32 tiles and present an explicit snapshot.
feature_id: hdr-canvas
---

# HDR tiled canvas

The optional `Mu3D.Creative` package contains UI-independent HDR document primitives. It depends on
`Mu3D.Color` and acquires no pointer/pen events. Applications own brush spacing, pressure mapping,
gesture policy, edit commands and undo.

```csharp
using Mu3D.Color;
using Mu3D.Creative;

HdrCanvas canvas = new(256, 256, StandardColorSpaces.AcesCg,
    new HdrCanvasOptions { Precision = PixelPrecision.Float16, TileSize = 64 });
canvas.ApplyDab(new BrushDab(128, 128, 40,
    new LinearRgba(4, 0.6f, 0.15f, 0.75f, StandardColorSpaces.AcesCg), hardness: 0.2f));
LinearRgbaImage snapshot = canvas.Snapshot();
```

Float16 tiles are the default; Float32 is an explicit reference/document storage mode. Computation
remains FP32. Working space and compositing space are separate tagged standard linear RGB identities.
Public colors and snapshots use straight alpha; internal compositing uses premultiplied alpha.
Negative and above-one channels survive until an explicit downstream policy changes them. Half-float
overflow fails before mutation rather than storing infinity. Transparent pixels are canonical black.

Normal/source-over, Multiply, Screen and Add blend modes are explicit. Screen/Add use their
algebraically extended linear HDR formulas and do not clamp to the SDR range. `Fill`, `Composite`,
`CompositeImage` and `ApplyDab` stage updates atomically. Invalid input, arithmetic overflow or a
configured allocation failure leaves canvas pixels and dirty state unchanged.

Storage, staging, snapshots and tile metadata have separate limits. Tiles are sparse and edge tiles
allocate their real extent. `GetDirtyRegions()` reports modified tile regions; the application
acknowledges them using `ClearDirtyRegions()`. Snapshots are immutable and may crop a region. This
initial CPU snapshot path is explicit and bounded, not a promise of zero-copy high-frequency painting.

## Present in MAUI XAML

The Gallery **HDR Canvas** page presents a canvas snapshot as an ordinary Mu3D texture. The scene,
camera and material are declared in XAML; application code owns the canvas and its edits.

```xaml
<mu3d:Mu3DSceneView>
  <mu3d:Scene3D>
    <mu3d:Scene3D.Camera>
      <mu3d:PerspectiveCamera3D Z="4" />
    </mu3d:Scene3D.Camera>
    <mu3d:Sphere3D Radius="1.1">
      <mu3d:UnlitMaterial3D x:Name="CanvasMaterial" />
    </mu3d:Sphere3D>
  </mu3d:Scene3D>
</mu3d:Mu3DSceneView>
```

After an edit, assign `CanvasMaterial.Texture = canvas.Snapshot()` or bind `Texture` to an immutable
snapshot property. The material invalidates its owning view. The renderer converts the tagged working
space and uploads floating-point texture data through the existing HDR path. There is no MAUI
`ImageSource`, PNG encoding or automatic SDR tone map. Physical HDR remains subject to the actual
surface's reported capability.

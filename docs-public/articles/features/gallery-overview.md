---
title: Gallery overview
description: Choose the right Mu3D Gallery example and understand the HDR-first contract.
feature_id: gallery-overview
---

# Gallery overview

The Gallery is a collection of focused, copyable examples. Start with
<xref:Mu3D.Maui.Controls.Mu3DSceneView> when an application wants a scene, camera, automatic
presentation session and renderer ownership. Use <xref:Mu3D.Maui.Controls.Mu3DView> when the
application needs the low-level draw callback or intentionally owns swapchain policy.

## XAML starting point

Add the MAUI control namespace and place the renderer directly in the page layout. Do not wrap the
surface in a decorative border, clip or shadow.

```xaml
<ContentPage
    xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:mu3d="clr-namespace:Mu3D.Maui.Controls;assembly=Mu3D.Maui">
    <mu3d:Mu3DSceneView
        ClearColor="{Binding ClearColor}"
        Scene="{Binding Scene}"
        Camera="{Binding Camera}" />
</ContentPage>
```

See the [XAML component catalog](../xaml-components.md) for declarative scenes, tools, overlays,
compiled bindings and the advanced low-level forms.

## HDR-first contract

Mu3D keeps colors, transforms, cameras, skinning and critical accumulation in FP32. Normal HDR
attachments use FP16. Presentation prefers an extended-linear `Rgba16Float` surface and never
silently applies SDR tone mapping. <xref:Mu3D.Graphics.OutputSettings> makes dynamic range and
fallback policy explicit.

The device and surface probes answer different questions. A successful device probe proves backend
creation and GPU command execution. A successful surface probe additionally proves swapchain
negotiation and presentation. Neither alone proves that a physical display entered HDR mode or that
its measured output is color-accurate.

## Example map

- **Custom Draw** is the low-level manual drawing escape hatch.
- **Transform Gizmo** and **Frame Statistics** demonstrate optional Toolkit composition.
- **Pointer + Pen Input** keeps native pressure/tilt acquisition in the application and feeds only
  an explicit physical-pixel query into the UI-independent scene raycaster.
- Lighting, shadows, materials and animation exercise the high-level renderer.
- glTF, KTX2 and HDR JPEG pages demonstrate bounded asset and codec boundaries.
- **Material Conformance** is an advanced validation harness, not beginner UI.

Each feature page owns only the resources described by its article. Application services, selection,
undo, file picking and long-lived caches remain host policy.

## Reading and reusing an example

On Mac Catalyst and Windows, every focused feature page includes a collapsed **Show example code**
drawer. Expand it to switch between the page's packaged XAML and C# and copy either form. Android
and iOS omit this desktop authoring chrome so the example remains focused and does not package its
source files.

Examples that need the same interactive inspection surface can reuse the Gallery's
`GalleryDebugSceneControls`: it exposes controller enablement, scene picking, transform-gizmo,
grid, axes and frame-statistics controls in one collapsed panel. The example still declares each
Mu3D tool explicitly in XAML, so the code remains copyable and the helper never hides ownership or
lifecycle.

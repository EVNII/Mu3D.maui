---
title: OpenPBR white furnace
description: Inspect a uniformly illuminated OpenPBR sphere, scene-linear energy ratios and explicit GPU measurements.
feature_id: openpbr-white-furnace
---

# OpenPBR white furnace

Open **Examples → OpenPBR White Furnace**. A unit sphere sits in a uniform white environment,
without direct lights, emission or a tone-mapping view. The initial white diffuse sphere should
merge into the background; an apparently empty white viewport is the intended baseline.

The [OpenPBR specification's white-furnace test](https://academysoftwarefoundation.github.io/OpenPBR/#whitefurnacetesting)
expects nonabsorbing configurations to preserve the environment radiance after enough transport
bounces. This page also includes a 50% gray Lambertian control, whose outgoing/environment ratio
is 0.5. Its visible darkening is intentional absorption, not an energy leak.

## Minimal scene

Declare the usual `mu3d` namespace (`Mu3D.Maui.Controls` in `Mu3D.Maui`):

```xaml
<mu3d:Mu3DSceneView x:Name="FurnaceView">
  <mu3d:Scene3D>
    <mu3d:Scene3D.Camera>
      <mu3d:PerspectiveCamera3D Z="4" FieldOfView="40" />
    </mu3d:Scene3D.Camera>
    <mu3d:Sphere3D Radius="1">
      <mu3d:OpenPbrMaterial3D>
        <mu3d:OpenPbrSurface3D BaseColor="1,1,1;acescg"
            SpecularWeight="0" SpecularIor="1" />
      </mu3d:OpenPbrMaterial3D>
    </mu3d:Sphere3D>
  </mu3d:Scene3D>
</mu3d:Mu3DSceneView>
```

Supply a uniform environment through the optional pass:

```csharp
using Mu3D.Color;
using Mu3D.Rendering.OpenPbr;

var pass = new OpenPbrRenderPass {
    Mode = OpenPbrRenderMode.Reference,
    EnvironmentRadiance = new LinearRgba(1, 1, 1, 1, StandardColorSpaces.AcesCg),
    BackgroundAlpha = 1,
};
FurnaceView.RenderPipeline = pass.Pipeline;
```

The application schedules additional frames and disposes the pass after detaching it. The complete
Gallery page adds the bounded diagnostic renderer, controls and explicit measurement lifetime.

## Controls

- Select white diffuse, white metal, white coat, white fuzz, clear glass, white subsurface or the
  gray control. Roughness changes diffuse roughness for the white diffuse preset and lobe roughness
  for the others; the gray control remains Lambertian. Anisotropy controls base/coat specular lobes.
- Compare Reference, Hybrid, Interactive and Raster. Glass/subsurface explicitly switch the selector
  to Reference if Raster was selected, with an explanatory message; unsupported transport is never
  silently discarded. Raster uses 64 fixed environment samples and only renders on invalidation.
- Change the path-event limit (4–128) independently of sample count. The initial limit is 32.
  Continue/pause sampling, reset history or request a measurement while paused. Each requested
  measurement renders one additional frame; the report records that frame's mode and sample count.
- Environment EV changes actual illumination: `Lenv = 2^EV` in linear ACEScg. It resets accumulated
  radiance. This tests HDR scale invariance; it is not a display-exposure adjustment.
- Choose raw linear HDR, the `Lout/Lenv` ratio, or an explicitly false-color target difference ×10.
  The difference view uses red for positive error, blue for negative error and gray for zero; it
  saturates for readability. For the gray control its target is 0.5, including when visualizing the
  white background. Changing the diagnostic view preserves radiance history. No AgX/ACES view is used.

## Measurement and interpretation

**Measure current linear result** performs one explicit asynchronous texture-to-buffer readback.
Normal frames keep their data on the GPU. The source and accumulation are FP32, before diagnostic
coloring or display conversion. The bounded source is at most 384 pixels on its longest side and
preserves the viewport's aspect ratio; the presentation surface remains control-owned. This probe
requires a floating-point FP16/FP32 presentation attachment rather than silently clipping HDR values
into an SDR texture.

Statistics include mean RGB ratio, minimum/maximum channel ratio and RMS deviation from the selected
target. Only the analytically projected inner sphere (radius 0.9 inside the unit test sphere) is
measured, excluding background and the silhouette. Results are snapshots rather than live counters;
editing the material, environment or event budget invalidates the report. This is a scene-specific
probe, not an arbitrary mesh meter or a hemispherical BSDF integral.

Finite Monte Carlo samples can overshoot or undershoot. Truncated paths can lose energy; glass and
subsurface often need more events and samples. Raster has deterministic quadrature error; the tessellated sphere also has small differences between
interpolated shading normals and geometric face normals. A single
bright pixel or nonzero RMS is not an automatic conformance failure, and this page never presents
an automatic pass/fail certificate. The pinned BSDF's documented approximations also remain in force.
Compare converged measurements across roughness, modes, HDR levels and event budgets.

## API and ownership

The example wraps @Mu3D.Rendering.OpenPbr.OpenPbrRenderPass in an application-owned
@Mu3D.Rendering.IRenderPass and uses the backend-independent @Mu3D.Graphics.GraphicsBuffer readback
boundary. It does not create a device, presentation session or platform timer. Its XAML, code-behind
and renderer helper are available in Gallery's source drawer on desktop.

See [OpenPBR rendering and MaterialX](openpbr-materialx.md) for authoring and transport limits.

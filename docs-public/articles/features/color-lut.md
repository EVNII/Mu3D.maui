---
title: GPU color lookup tables
description: Apply an explicit FP32 color lookup table to HDR textures without CPU readback.
feature_id: color-lut
---

# GPU color lookup tables

<xref:Mu3D.Rendering.LinearRgbLutGpuTransform> applies an immutable
<xref:Mu3D.Color.LinearRgbLut3D> to caller-owned straight-alpha FP16/FP32 textures. The LUT and
manual trilinear interpolation remain FP32; destination storage is explicitly Float16 or Float32.
The class owns only its internal GPU resources. It does not own the device, caller textures,
presentation surface, display transform or frame loop.

```csharp
using System.Numerics;
using Mu3D.Color;
using Mu3D.Rendering;

LinearRgbLut3D table = LinearRgbLut3D.Bake(17, new Vector3(-0.5f), new Vector3(8),
    StandardColorSpaces.LinearSrgb, StandardColorSpaces.AcesCg,
    color => StandardLinearRgbConverter.Convert(color, StandardColorSpaces.AcesCg),
    ColorLutRangePolicy.Clamp);
using LinearRgbLutGpuTransform transform = new(device, table, PixelPrecision.Float16);
transform.Apply(sourceTexture, StandardColorSpaces.LinearSrgb,
    destinationTexture, StandardColorSpaces.AcesCg);
```

The GPU path requires explicit `Clamp`: unlike the CPU `Reject` path, a GPU pass cannot synchronously
report individual out-of-domain pixels without a readback. Output RGB and alpha are not silently
tone mapped or gamut clipped. Float16 output rejects tables containing values outside finite half
range before allocation. The caller supplies finite straight-alpha input, with alpha in `[0,1]`.
GPU domains require zero-or-normal FP32 endpoints and normal positive widths; subnormal domains
are rejected because GPU execution may flush them to zero. The CPU table still supports them.
Input and output must be distinct, same-sized, single-layer, single-mip, single-sample textures. Input requires
texture binding; output requires render-attachment usage and the selected output format.

## Gallery host

The **Color LUT** page uses the low-level automatic surface path:

```xaml
<mu3d:Mu3DView
    x:Name="SurfaceView"
    Draw="OnSurfaceDraw"
    PresentationSessionChanged="OnPresentationSessionChanged"
    SurfaceError="OnSurfaceError" />
```

The application supplies an explicitly tagged HDR pattern at the authoritative draw extent and
transforms it only when the actual surface reports the required extended-linear Float16 path.
Unsupported presentation is reported, not relabeled as HDR. The control retains session ownership;
the page disposes its own pattern and LUT resources on replacement/navigation.

Numerical tests compare actual headless GPU output to the CPU reference across all four FP16/FP32
input/output combinations, including negative channels, HDR, range boundaries and alpha. Host GPU
execution is separate from physical Android/iOS/Mac Catalyst/Windows display acceptance.
See [linear RGB lookup tables](../color-lookup-tables.md) for CPU evaluation and baking error policy.

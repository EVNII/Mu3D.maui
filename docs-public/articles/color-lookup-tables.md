---
title: Linear RGB lookup tables
description: Bake and evaluate explicitly tagged HDR color transforms with bounded FP32 lookup tables.
---

# Linear RGB lookup tables

<xref:Mu3D.Color.LinearRgbLut1D> is an independent-channel linear shaper;
<xref:Mu3D.Color.LinearRgbLut3D> supports cross-channel RGB transforms with trilinear interpolation.
Both copy their samples, preserve alpha, and carry explicit source/destination color-space identity.
They neither select a display transform nor silently apply a gamma, gamut map or tone map.

```csharp
using System.Numerics;
using Mu3D.Color;

LinearRgbLut3D lut = LinearRgbLut3D.Bake(17, new Vector3(-0.5f), new Vector3(8),
    StandardColorSpaces.LinearSrgb, StandardColorSpaces.AcesCg,
    color => StandardLinearRgbConverter.Convert(color, StandardColorSpaces.AcesCg));
LinearRgba result = lut.Transform(new LinearRgba(3, 0.2f, -0.1f, 0.5f,
    StandardColorSpaces.LinearSrgb));
```

Choose a domain that contains the intended HDR and negative inputs. The default
`ColorLutRangePolicy.Reject` catches out-of-domain input; `Clamp` is an explicit opt-in. Output is
never clipped. A one-dimensional table has 2–65536 samples per channel. A cubic table has 2–65
samples per edge, in R-fastest/G/B order. All storage and interpolation are FP32.

Baking approximates the supplied transform between sample points. Validate off-grid error against
the original transform for the intended colors and domain; a larger table alone does not establish
ICC, perceptual or display accuracy. Existing ICC transform caches continue to own their complete
profile/intent/BPC identity. A LUT does not replace profile preservation or reference validation.

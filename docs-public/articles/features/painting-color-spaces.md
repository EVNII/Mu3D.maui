---
title: Painting color coordinates and batch transforms
description: Use standard color conversions and application-owned editing rules with reusable typed CPU buffers and explicit GPU lookup tables.
feature_id: painting-color-spaces
---

# Painting color coordinates and batch transforms

Mu3D supplies coordinate types, standard conversion math and a typed batch execution contract.
The painting application selects its working space, editing model, slider ranges, hue interpolation,
mixing behavior, gamut mapping and display view. The Gallery menu is an example of those application
choices, not a required Mu3D editor or a fixed list of models.

## Standard coordinates

| Coordinates | Mu3D representation | Required interpretation |
| --- | --- | --- |
| CIE Lab / LCh | `CieLabColor` / `CieLchColor` | D50 relative white; ordinary L* spans 0–100. |
| Oklab / Oklch | `OklabColor` / `OklchColor` | D65 relative white; ordinary lightness spans 0–1. |
| Y′UV / Y′CbCr | `LumaChromaColor` | Explicit RGB encoding plus the selected matrix and zero-centered chroma scaling. |
| HSL / HSV | `HslColor` / `HsvColor` | Explicit nonlinear RGB encoding; bounded SDR saturation and lightness/value. |

<xref:Mu3D.Color.PerceptualColorConverter> converts tagged linear RGB to and from Lab and Oklab,
including white-point adaptation. LCh and Oklch retain the same lightness in cylindrical form.
Hue is stored in degrees and normalized to `[0,360)`; an application still chooses how to interpolate
across the wrap. These are relative coordinates, not an absolute HDR luminance or viewing-condition
model. The conversion conventions follow [CSS Color 4's conversion reference](https://www.w3.org/TR/css-color-4/#color-conversion-code)
and [Oklab's published matrices](https://bottosson.github.io/posts/oklab/).

<xref:Mu3D.Color.LumaChromaConverter> operates on encoded RGB. Its Y′ is nonlinear luma, distinct
from linear-light luminance. Selecting BT.601, BT.709 or BT.2020 matrix coefficients does not change
the source's tagged primaries or transfer encoding. These floating-point coordinates include no
video quantization, limited-range coding or chroma subsampling. Convert RGB encoding explicitly
before applying the selected matrix when those RGB identities must change.

FP32 Lab/Oklab and luma/chroma conversion preserves negative and above-one values and straight
alpha. It does not silently clip, gamut map or tone map. HSL/HSV conversion deliberately requires
encoded RGB in `[0,1]`; the application must explicitly map HDR or out-of-gamut colors before
using that bounded representation. Finite coordinates whose results overflow FP32 fail explicitly.

## Typed CPU batches

<xref:Mu3D.Color.ColorBatch> writes into caller-owned spans. The generic transform is selected
statically; a value-type implementation uses constrained calls without boxing or a per-color
delegate. Implementing `IColorTransform<TSource, TDestination>` supplies a CPU operation, not a GPU
program. Coordinate types and transform parameters carry the color identity and any selected policy.

This example converts linear sRGB into Lab, applies an application-defined chroma adjustment,
and returns tagged linear sRGB. Keep these arrays for subsequent edits rather than allocating them
on every slider update:

```csharp
using Mu3D.Color;

LinearRgba[] source =
[
    new(0.1f, 0.3f, 0.8f, 1, StandardColorSpaces.LinearSrgb),
    new(2, 0.5f, -0.05f, 0.4f, StandardColorSpaces.LinearSrgb),
];
CieLabColor[] coordinates = new CieLabColor[source.Length];
LinearRgba[] result = new LinearRgba[source.Length];

ColorBatch.Transform<LinearRgba, CieLabColor, LinearRgbToLabTransform>(
    source, coordinates, default);
ColorBatch.Transform<CieLabColor, CieLabColor, LabChromaAdjustment>(
    coordinates, coordinates, new(0.8f));
ColorBatch.Transform<CieLabColor, LinearRgba, LabToLinearRgbTransform>(
    coordinates, result, new(StandardColorSpaces.LinearSrgb));

readonly struct LabChromaAdjustment(float scale) : IColorTransform<CieLabColor, CieLabColor>
{
    public CieLabColor Transform(CieLabColor source) => new(
        source.Lightness, source.A * scale, source.B * scale, source.Alpha);
}
```

The built-in adapters preserve alpha; the general batch interface does not impose that policy on
custom operations. It allocates no output storage, while allocations inside a custom transform
remain the application's responsibility. The transform is passed by value, so a struct is copied
once per batch. Empty input is allowed, destination tails remain unchanged, and exact in-place
conversion is supported only for identical source/destination types and starting locations.
Other overlap is rejected before conversion. If a transform throws, earlier destination elements
remain written; this is not a transactional conversion.

## Reuse the existing GPU LUT path

An RGB-only editing operation can be composed as `RGB → Lab → edit → RGB`, then baked into a
bounded <xref:Mu3D.Color.LinearRgbLut3D>. It must depend only on RGB and fixed editing parameters,
preserve alpha, and return the declared RGB identity. Neighborhood, alpha-dependent and evolving
state rules cannot be represented by an ordinary RGB cube.

```csharp
using System.Numerics;
using Mu3D.Color;
using Mu3D.Rendering;

static LinearRgba AdjustLabChroma(LinearRgba source)
{
    CieLabColor lab = PerceptualColorConverter.ToLab(source);
    return PerceptualColorConverter.FromLab(
        new(lab.Lightness, lab.A * 0.8f, lab.B * 0.8f, source.Alpha),
        StandardColorSpaces.LinearSrgb);
}

LinearRgbLut3D table = LinearRgbLut3D.Bake(33,
    new Vector3(-0.25f), new Vector3(4),
    StandardColorSpaces.LinearSrgb, StandardColorSpaces.LinearSrgb,
    AdjustLabChroma, ColorLutRangePolicy.Clamp);
using LinearRgbLutGpuTransform gpu = new(device, table, PixelPrecision.Float16);
gpu.Apply(sourceTexture, StandardColorSpaces.LinearSrgb,
    destinationTexture, StandardColorSpaces.LinearSrgb);
```

The chosen `[-0.25,4]` domain and 33-point edge are application choices, not general accuracy
guarantees. GPU evaluation requires explicit domain clamping: RGB outside this domain is clamped
before lookup; output RGB remains unclipped and alpha is copied unchanged. The table and trilinear
interpolation are FP32; output storage is explicitly Float16 or Float32. Float16 tables must remain
within finite half range. Input is straight alpha; premultiplied data needs an explicit
unpremultiply/transform/premultiply sequence.

Validate two separate errors: CPU LUT versus the direct mathematical operation at off-grid points,
then actual GPU output versus that CPU LUT. Also cover domain boundaries, negative channels, HDR
and alpha. Changing editing parameters requires rebaking; the immutable CPU table can be shared,
while each GPU instance belongs to one device and must be disposed by its owner. GPU application
reuses the table and pipeline without CPU pixel readback, but is not a zero-allocation promise.

This path does not turn arbitrary C# into GPU code and does not make a Lab image a `LinearRgba`
texture. A GPU operation that exposes non-RGB intermediate images needs a distinct coordinate
contract. See [GPU color lookup tables](color-lut.md) for texture requirements and
[linear RGB lookup tables](../color-lookup-tables.md) for domain and approximation policy.

## Gallery host

The native **Painting Color Spaces** case uses the actual automatic draw boundary:

```xaml
<mu3d:Mu3DView
    x:Name="SurfaceView"
    HeightRequest="520"
    Draw="OnDraw"
    PresentationSessionChanged="OnSessionChanged"
    SurfaceError="OnSurfaceError" />
```

Its sample-owned adapters batch a two-dimensional coordinate plane and three 64-step channel
gradients into reused buffers. Each gradient previews the final RGB obtained by varying its own
coordinate while holding the other two at their current values. The Gallery chooses a/b or chroma
planes, hue/chroma disks, an HSL triangle or HSV square; these UI policies are not Mu3D library
defaults. Native and Web share the same state, hit layout, selection policy and Graphics-only HDR
renderer. One presentation surface draws the plane, gradients and swatches; native overlays use
ordinary Slider thumbs and focus, while Web retains ordinary range inputs for keyboard adjustment.
Pointer, numeric and slider input update the same current color. Only atlas sections whose fixed
coordinates changed are reuploaded. The right swatch shows the selected color. The twelve-color row
explores hue at fixed OKLCh lightness/chroma, without claiming equal physical luminance.

The Gallery selection policy requires nonnegative final linear sRGB components. Gray regions mark
unavailable results. A valid requested endpoint is accepted directly, even when the path from the
current color crosses an unavailable region. Only an invalid target invokes the boundary search.
HDR components above one remain valid in the unbounded models. Negative model coordinates such as
Lab a/b or YUV chroma can still be valid when their resulting RGB is nonnegative. Fixed selector ranges do
not clamp numeric coordinates; HSL/HSV remain bounded SDR representations. This policy belongs
only to the example: the standard converters and `ColorBatch` above still preserve negative RGB
and do not acquire an implicit clamp.

For an invalid requested endpoint, the sample scans 128 intervals toward the requested coordinates,
stops at the first detected invalid interval, and refines its preceding boundary with
up to 24 bisection iterations. This is a finite-resolution search, not an exact gamut boundary or
proof that no narrow invalid interval exists between scan points. Preview texture interpolation is
also separate from FP32 selection validation. Applications copying the example should choose and
validate their own boundary policy.

HDR FP16 output retains extended linear sRGB without gamut or tone mapping. Native drawing passes
the control's negotiated encoding into the helper; negotiated SDR with explicit `ColorEncoding.Srgb`
hard clips and encodes once, using hardware sRGB encoding when the attachment provides it. The Web
page requests the Canvas handler's default HDR output and does not expose an HDR/SDR selector.
The reference white is relative, not a fixed nit value, and physical display brightness depends
on the presentation environment. The control owns its surface session; the sample retires only
its renderer resources on session replacement/navigation.

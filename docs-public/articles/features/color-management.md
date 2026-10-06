---
title: Color management
description: Explicit AgX, Filmic and ACES display views, OCIO processing and ICC proofing.
feature_id: color-management
---

# Color management

Mu3D keeps scene lighting and reference accumulation in linear light. Select a display view explicitly
to render that radiance for a display. The same view works with the ordinary scene renderer and the
interactive/reference OpenPBR passes.

## XAML

```xaml
<mu3d:Mu3DSceneView>
    <mu3d:Mu3DSceneView.DisplayTransform>
        <mu3d:ColorView3D Preset="AgXHdr1000"
                          ExposureStops="0" ReferenceWhiteNits="100" />
    </mu3d:Mu3DSceneView.DisplayTransform>
    <!-- Declare Scene3D, a camera and scene content here. -->
</mu3d:Mu3DSceneView>
```

The component is bindable. Exposure changes request a frame and reuse the GPU program; they do not
reset OpenPBR's accumulated radiance. Leaving `DisplayTransform` null retains linear presentation.
To adjust exposure without selecting a display view, set `Mu3DSceneView.SceneLinearExposureStops`:

```xaml
<mu3d:Mu3DSceneView SceneLinearExposureStops="1" />
```

This multiplies final scene-linear RGB by `2^EV`, preserves alpha and applies no tone mapping.
The default zero adds no pass. Nonzero values, bounded to -32 through +32 stops, require an
extended-linear HDR Float16 or Float32 output; SDR output is rejected explicitly. The property only
applies while `DisplayTransform` is null; attached display views retain their own `ExposureStops`.
Changing either exposure requests a frame without changing lighting or resetting progressive
accumulation. The Gallery's shared EV slider controls the active path in both Scene linear and
display-view modes.

The control owns the floating-point intermediate and uses its existing automatic surface and
scheduling. Custom render pipelines must produce premultiplied scene-linear RGB. Clear colors are
associated with alpha before rendering; the view unassociates color for nonlinear processing and
restores the negotiated presentation alpha association. A custom session must report its actual
`AlphaMode`; unresolved Automatic, Inherit or Unknown association is rejected for display views.

| Preset | Rendering transform | Peak / limiting gamut |
| --- | --- | --- |
| `AgXSdr` | Blender 5.0 AgX Base | 100 nits / Rec.709 |
| `AgXHdr1000` | Blender 5.0 AgX HDR | 1000 nits / P3 D65 |
| `Aces2Sdr` | ACES 2.0 | 100 nits / Rec.709 |
| `Aces2Hdr1000` | ACES 2.0 | 1000 nits / Rec.2020 |
| `Aces2Hdr1000P3` | ACES 2.0 | 1000 nits / P3 D65 |
| `AgXSdrRec2020` | Blender 5.0 AgX Base | 100 nits / Rec.2020 |
| `FilmicSdr` | Blender 5.0 Filmic base, no additional Look | 100 nits / Rec.709 |

These are pinned official transforms: original AgX data with tetrahedral interpolation and analytic
ACES 2 math with its original hue tables. The output is display-linear sRGB, including extended and
negative components needed to represent wide gamut. Reference white specifies how many nits one
output unit represents; it does not change the fixed mastering peak or measure the connected display.
The default is 100 nits. Exposure is bounded to -32…+32 stops, reference white to 1…10000 nits.
The native compositor still controls actual physical luminance.

HDR presets require negotiated HDR extended-linear sRGB presentation and fail on SDR surfaces.
Select an SDR preset explicitly when needed. Native P3, PQ and HLG presentation remain separate
capability contracts; implementing their transfer equations does not enable a surface automatically.
The portable views require no native OCIO installation.

## CPU and custom GPU use

```csharp
var view = new ColorViewTransform(ColorViewPreset.Aces2Hdr1000,
    StandardColorSpaces.AcesCg, exposureStops: 0, referenceWhiteNits: 100);
LinearRgba displayed = view.Transform(sceneColor);

using var gpu = new ColorViewGpuTransform(device, view,
    GraphicsTextureFormat.Rgba16Float,
    inputPremultiplied: true, outputPremultiplied: true);
gpu.Apply(sceneTexture, view.SourceSpace, outputTexture, view.DestinationSpace);
```

Namespaces are `Mu3D.Color`, `Mu3D.Graphics` and `Mu3D.Rendering`. Source space can be linear sRGB, Display P3, Adobe RGB (1998), ProPhoto RGB, Rec.2020
or ACEScg. Additional wide-gamut inputs are converted to ACEScg before the official view, on both CPU and GPU. Exposed straight RGB must stay within ±1e30 per component; CPU processing rejects larger
values and GPU callers must enforce this data contract. CPU colors are straight alpha. The GPU requires a distinct, same-sized floating-point
sampled source and a renderable output; it preserves alpha, uses FP32 math, and does no CPU readback.
`UpdateTransform` changes exposure/reference white while retaining the same preset and source space.
SDR output can use an explicitly selected `ColorEncoding.Srgb` with an eight-bit sRGB or UNORM target;
hardware encoding and explicit shader encoding are selected according to that format.

## Optional OpenColorIO configurations

The separate `Mu3D.Native.OpenColorIO` package reads actual `.ocio` configurations and creates CPU
color-space or display/view processors. Configurations, context defaults and assets are explicit;
it never replaces the global process configuration.

```csharp
using Mu3D.Native.OpenColorIO;

using var config = OcioConfiguration.LoadFile("/assets/color/config.ocio");
using var processor = config.CreateColorSpaceProcessor("ACEScg", "ACES2065-1");
Vector3 converted = processor.ApplyRgb(rgb);
```

The adapter pins OCIO 2.5.2 and validates its private wrapper ABI. Native command-line processing is verified on macOS arm64 and on a genuine Mac Catalyst arm64
build. The Catalyst archive uses the macabi target and passes its own C ABI/oracle checks; this is
separate from linking and running a complete MAUI application. iOS, Android and Windows native
runtimes remain pending; missing/unsupported runtimes fail explicitly. Consumer builds do not download or compile OCIO.
This limitation does not affect portable AgX/ACES views.

Arbitrary loaded configurations currently execute through native CPU processors; this is not a
runtime GPU shader compiler for every OCIO config. The built-in AgX/Filmic/ACES programs have a
separate, verified GPU path generated at maintainer time. Gallery does not currently offer a
general `.ocio` file picker or dynamic Display/View/Look browser.

A display processor normally returns encoded RGB. `CreateDisplayViewToLinearProcessor` requires
explicit view-output and linear-destination OCIO identities in the same reference space, so decoding
does not undo the rendering transform. `AsLinearTransform` asserts caller-verified linear endpoint
metadata; it does not decode an arbitrary encoded transform. A processor can be baked into an
explicitly bounded `LinearRgbLut3D`; this is an approximation whose off-grid error must be measured.

## ICC proofing, transfer functions and spectra

`IccSoftProofTransform` adds a proof-device round trip followed by absolute-colorimetric decoding to
simulate media white. `IccSoftProofOptions` selects proof/display intents, optional explicit proof BPC and
reject-or-clamp range policies. The result reports explicit clipping. RGB ICC profiles are supported;
CMYK is available through the separate [optional printing extension](cmyk-printing.md); spectral printer models remain unsupported. Tone mapping into print-relative
light is an application choice. Do not apply a monitor ICC profile a second time when the platform
compositor owns that conversion.

`HdrTransferFunctions` supplies absolute-nit PQ and BT.2100 HLG functions. RGB HLG includes the
luminance-coupled OOTF and requires reference white, peak luminance and system gamma explicitly.
These are numerical conversions, independent of native display support.

The spectral APIs include immutable power distributions, observer-tagged integration and measured
RGB display-primary models. `Cie2015Observers` uses unchanged official 2° and 10° CIE 2015 tables
with attribution and SHA-verified metadata. These are fixed standard observers. Arbitrary age or
field-angle models are not inferred from them; applications may supply documented custom CMFs.

The Gallery **Color Management** example compares high-saturation HDR bands side by side and
also retains its XAML HDR-lit sphere scene. **OpenPBR + MaterialX** lets you select the same display stage in all four rendering modes; its initial
selection retains scene-linear HDR.
Numerical and headless GPU validation is separate from physical display appearance testing.

## Standard RGB input and interchange

`StandardRgbEncodingConverter` decodes sRGB, Display P3, Adobe RGB (1998), ProPhoto RGB
and Rec.2020 into tagged linear FP32 colors/images. Display P3 is D65, not cinema DCI-P3;
Rec.2020 here means the continuous BT.2020 OETF, not PQ or HLG. ProPhoto uses D50 and
its linear toe. Conversion includes white-point adaptation. Alpha is unchanged and input
must be straight/unpremultiplied.

```csharp
var input = new StandardEncodedRgba(0.8f, 0.2f, 0.1f, 1f, StandardRgbEncoding.DisplayP3);
LinearRgba working = StandardRgbEncodingConverter.Decode(input, StandardColorSpaces.AcesCg);
var output = StandardRgbEncodingConverter.Encode(working, StandardRgbEncoding.ProPhotoRgb);
IccProfile profile = StandardRgbProfiles.Get(StandardRgbEncoding.ProPhotoRgb);
```

Extended negative and above-one RGB are preserved by default. `RgbEncodingRangePolicy.Reject`
and `.Clip` are explicit alternatives; clipping is not perceptual gamut mapping or tone mapping.
`DecodeImage`/`EncodeImage` support bulk pixels. Encoded arrays are not self-describing files:
the selected encoding/profile must accompany the pixels. The supplied ICC v4 matrix/TRC profiles
are deterministic mathematical standard profiles, not measured monitor profiles; ICC fixed-point
quantization is approximate. They have no perceptual rendering tables.

The renderer retains HDR floating-point presentation through extended-linear sRGB/scRGB.
These document/input spaces do not select native P3, PQ or HLG swapchains. The OS compositor
and display determine the final visible gamut. No implicit SDR view is added.
Transfer-function definitions follow [CSS Color 4](https://www.w3.org/TR/css-color-4/#color-conversion-code);
profile interchange follows [ICC v4](https://www.color.org/v4spec/).


## High-saturation comparison scene

The default Gallery chart generates identical **scene-linear Rec.2020** RGB/CMY/white rows
in FP32 for raw output and both display views. The horizontal peak-component ramp is
`0.18 × 2^stops`, with stops from -6 through +12; it is not a luminance-normalized chart.
Exposure and input saturation are synchronized. Saturation zero turns all rows neutral.
A second pattern sweeps hue continuously, making high-intensity hue discontinuities easier
to inspect. No PNG, eight-bit image or sRGB clipping lies between the source and the HDR views.

A **Standard / no tone mapping** column applies only shared exposure and the necessary
linear Rec.2020-to-extended-sRGB conversion. It does not tone map, compress gamut or cap output
at 1000 nits. Negative and above-one components survive into FP16; only values beyond its finite
storage range (±65504, possible at the extreme +6 EV end) are clipped to avoid infinity.
The platform compositor still performs monitor conversion and the screen limits visible output;
this is not a switch that disables operating-system color management. In explicitly selected SDR
mode, the raw column instead hard-clips linear channels to 0–1 before sRGB encoding. That contrast
shows highlight clipping versus the AgX/ACES roll-off. This follows Blender Standard's intent;
it is not Blender Raw, which is for inspecting numerical values without color-space conversion.

Explicit SDR comparison also includes **Filmic**, using Blender's original Filmic sRGB base
view without an extra Look. Filmic processes the same HDR source as the other views, but this
particular output transform maps it to SDR. The pinned Blender config offers Filmic on the
sRGB display, not as a dedicated PQ/HLG HDR output view. No HDR scene data is lost before the view.
Four SDR charts form a 2x2 layout on wide windows; HDR retains three charts, and narrow windows
stack them. See [Blender's display/view configuration](https://github.com/blender/blender/blob/a37564c4df7a9b604809e5192918400e46a0205f/release/datafiles/colormanagement/config.ocio).

The HDR comparison uses **AgX HDR 1000 nits / P3 D65** versus
**ACES 2.0 HDR 1000 nits / P3 D65**, both with 100-nit reference white. The existing
ACES Rec.2020 preset remains available through the API; it is intentionally not used
for this matched-gamut comparison. The SDR comparison uses 100-nit Rec.709 for both.
SDR is explicitly selected; an unavailable HDR surface produces a clear status, not
an implicit SDR transform. Physical brightness still depends on display headroom.

ACES 2.0 reworks rendering in JMh appearance correlates, separating tone mapping from
chroma/gamut compression to address hue behavior and consistency across outputs.
It should not be judged as the older per-channel ACES rendering, nor does this chart
establish one transform as universally better. See the
[official ACES 2 rendering structure](https://docs.acescentral.com/system-components/output-transforms/).
This sample uses the pinned OCIO 2.5.2 ACES 2.0 implementation, not a fitted “ACES filmic” curve.

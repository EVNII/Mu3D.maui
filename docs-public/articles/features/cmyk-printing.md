---
title: Optional CMYK printing and soft proof
description: Optional ICC CMYK conversion, HDR print mapping and soft-proof diagnostics.
feature_id: cmyk-printing
---

# Optional CMYK printing and soft proof

`Mu3D.Color.Printing` provides portable managed ICC CMYK conversion and soft proofing. It depends
only on `Mu3D.Color`. The MAUI/Core/renderer packages do not reference it. Gallery explicitly opts into its example.
No native dependencies or automatic ICC downloads are required.

API entry points: <xref:Mu3D.Color.Printing.CmykProfile>,
<xref:Mu3D.Color.Printing.CmykTransform> and <xref:Mu3D.Color.Printing.PrintRgbTransform>.

## Include only when needed

For a source consumer:

```xml
<PropertyGroup>
  <EnableMu3DPrinting>false</EnableMu3DPrinting>
</PropertyGroup>
<ItemGroup Condition="'$(EnableMu3DPrinting)' == 'true'">
  <ProjectReference Include="../mu3d/src/Mu3D.Color.Printing/Mu3D.Color.Printing.csproj" />
</ItemGroup>
```

Enable with `-p:EnableMu3DPrinting=true`. Code using its types must also be in an optional project
or guarded by an application-owned conditional compilation symbol. Package consumers can condition
the corresponding PackageReference, using the same release version as their other Mu3D packages.
The extension has not been published as part of this local implementation.

Gallery enables the example by default. Use `-p:EnableMu3DPrinting=false` on restore/build to exclude
its reference, page, navigation entry and source assets. The extension is not a direct project in
`Mu3D.slnx`; with Gallery disabled it is outside that build graph. Explicit extension/test
builds use `dotnet build Mu3D.Printing.slnx -m:1`. No shared source is injected into base projects.

## Convert and proof

```csharp
using Mu3D.Color;
using Mu3D.Color.Printing;

var profile = new CmykProfile(File.ReadAllBytes("printer-supplied.icc"));
var print = new CmykTransform(profile, new PrintTransformOptions
{
    Intent = IccRenderingIntent.MediaRelativeColorimetric,
    RangePolicy = PrintRangePolicy.Reject,
    // Optional measured/application-selected source and destination black L*:
    // BlackPointCompensation = new IccBlackPointCompensation(0, 10)
});

// Input must already be opaque and print-relative. Select HDR mapping beforehand.
var source = new LinearRgba(0.2f, 0.3f, 0.4f, 1, StandardColorSpaces.LinearSrgb);
CmykColor ink = print.Separate(source);
PrintProofResult proof = print.Proof(source, StandardColorSpaces.LinearSrgb,
    simulatePaperWhite: true);
// proof.Preview goes through the calibrated display pipeline, not another creative view.
// ink.Channels = C,M,Y,K in [0,1]; ink.TotalInkPercent is in [0,400].
```

`CmykImage` preserves original pixels and the exact ICC payload; decoding/proofing never changes
its K channel. Use `Separate(LinearRgbaImage)` and `Decode(CmykImage, space)` for bulk conversion.
`FindExcessInk(limitPercent)` reports violations without changing the separations. The application
selects the limit; it is not inferred from a profile filename. Existing CMYK with a different profile
is rejected rather than silently reassigned. There is no automatic reseparation or black-preserving
DeviceLink conversion in this package.

Default range policy rejects negative/above-one source RGB and out-of-domain PCS. Explicit `Clip`
is available but is not perceptual HDR tone mapping. RGB alpha must be 1: composite before printing.
Preview RGB stays floating point and may exceed display gamut. `OutsideDisplayGamut` checks nominal
[0,1] display RGB. `DeltaE76` and `ExceedsTolerance` describe source/print round-trip Lab error;
they are not a certified gamut boundary, Delta E 2000 or instrument-measured proof accuracy.

Paper white uses the profile media white and absolute colorimetry. Ink black comes from the actual
profile AToB response. BPC uses explicit black-point lightness values and only affects relative
RGB-to-CMYK separation; decoding existing separations does not remap their black. Calibrated
monitor, viewing illumination and print conditions remain essential to physical proof acceptance.

## Regional conditions

Load the profile specified by the printer rather than selecting a country alone:

| Condition | Common profile family |
|---|---|
| Japan coated offset | Japan Color 2011 Coated; historic Japan Color 2001 where required |
| US commercial print | GRACoL 2013 / CRPC6 |
| US publication web offset | SWOP 2013 / CRPC5; older SWOP where required |
| European coated paper | PSO Coated v3 / FOGRA51 |
| European uncoated paper | PSO Uncoated v3 / FOGRA52 |
| Specific printer, paper and ink | Vendor or measured custom ICC |

[ICC registry](https://registry.color.org/profile-registry/) lists conditions, profiles and individual
license terms. Profiles are not bundled; some permit embedding but restrict redistribution.

Supported: ICC v2/v4 CMYK output profiles with Lab/XYZ PCS, lut8/lut16 and v4 mAB/mBA, including
3D and 4D CLUTs. Missing requested intent tables fail explicitly. MPE, spectral, DeviceLink and
spot-color profiles are unsupported. Rendering remains RGB; PDF/X, TIFF codecs, RIP/printer
jobs, layout, trapping and overprint belong to the consuming application or other format adapters.

## Gallery example

The preview uses ordinary MAUI controls; assign an application-owned `IDrawable` to each
`GraphicsView` to draw the mapped RGB and soft-proof pixels, as the Gallery page does:

```xaml
<Grid xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
      xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
      ColumnDefinitions="*,*" RowDefinitions="Auto,240" ColumnSpacing="12">
    <Label Text="Print RGB (sRGB screen preview)" />
    <Label Grid.Column="1" Text="CMYK soft proof" />
    <GraphicsView x:Name="OriginalView" Grid.Row="1" />
    <GraphicsView x:Name="ProofView" Grid.Row="1" Grid.Column="1" />
</Grid>
```

Open **Examples → CMYK + Soft Proof**. Load an ICC with the file picker, compare the original
RGB chart and print preview, select relative/perceptual/saturation intent, toggle paper-white
simulation or proof-error highlighting, and adjust the total ink warning threshold.
This example is explicitly SDR and uses MAUI sRGB drawing; out-of-display-gamut preview values
are explicitly clipped and counted. It does not alter native HDR presentation elsewhere.

For local verification only, `-p:Mu3DPrintProfileDirectory=/path/to/profiles` bundles supplied
`.icc` files into that Gallery build. The five documented regional filenames appear automatically.
No third-party profiles are checked into the repository. Respect provider redistribution terms
before sharing a build containing those files. Without that property, use the file picker.

Validation currently includes five real regional profiles and 1,280 independent macOS ColorSync
patch comparisons. Maximum observed XYZ component difference is 0.0044503 and ink coverage
difference is 0.0318303 (3.183 percentage points). This is a bounded interoperability baseline,
not bit-exact agreement with every CMM or certified contract-proof accuracy.

## HDR to Adobe RGB

The optional printing package exposes an explicit scene-linear print mapping:

```csharp
LinearRgba printRgb = HdrPrintMapper.ToAdobeRgb(hdrColor, exposureStops: 0);
CmykColor separated = transform.Separate(printRgb);
// For an RGB file, encode only after mapping; the file writer must attach its Adobe RGB profile.
StandardEncodedRgba encoded = StandardRgbEncodingConverter.Encode(
    printRgb, StandardRgbEncoding.AdobeRgb);
```

This is Mu3D's simple luminance/chroma mapping, not an official AgX or ACES view.
It converts directly to linear Adobe RGB, applies exposure, compresses luminance with
Y/(1+Y), and contracts chroma toward the resulting gray only when needed to fit [0,1].
Thus it avoids an intermediate sRGB gamut restriction. This linear-RGB contraction is
not a perceptually uniform hue-preserving mapping. Exposure is explicit (-20 to +20 EV);
nonpositive luminance maps to black. Source data and alpha are preserved; composite
alpha before separation. Decode PQ/HLG upstream, and do not tone map the proof again.

Gallery uses an immutable FP32 Adobe RGB HDR chart over twelve stops, with an exposure slider.
Both charts are explicit SDR sRGB screen previews; the Adobe RGB data entering ICC retains
its wider gamut even when the screen preview clips.

The ink slider is a **warning threshold**, not an ink limiter. Its full range is 0–400%.
Black/white diagonal marks identify cells with C+M+Y+K strictly above that threshold.
400% marks none; reducing the threshold reveals more cells. Changing it redraws a cached
overlay without changing separation, black generation, or requiring another ICC conversion.

## AgX with independently selected RGB output

```csharp
var mapping = new PrintRgbTransform(
    StandardColorSpaces.LinearRec2020, // actual source tag
    StandardColorSpaces.LinearAdobeRgb, // or LinearProPhotoRgb
    PrintToneMapping.AgXRec2020,
    exposureStops: 0);
LinearRgba printRgb = mapping.Transform(hdrColor);
CmykColor cmyk = transform.Separate(printRgb);
```

Select `PrintToneMapping.SimpleLuminance` to retain the simple mapping. The existing
`HdrPrintMapper.ToAdobeRgb` remains numerically unchanged. Algorithms, target RGB space
and printing ICC are independent choices; ProPhoto does not require Adobe RGB as an
intermediate before CMYK.

The AgX option uses the pinned Blender 5.0 **AgX Base Rec.2020** LUT and original
tetrahedral interpolation. Its view is exposed separately as
`ColorViewPreset.AgXSdrRec2020`, including CPU and GPU programs. Extended linear sRGB
is only the view's transport representation: it is never clipped to sRGB before print
conversion. The print transform converts that result to the selected target and
contracts out-of-gamut chroma toward a neutral of equal D50 PCS luminance, with
nonpositive luminance mapped to black and luminance at/above one mapped to white.
No second tone curve is applied after AgX.

This is **Blender AgX Rec.2020 plus Mu3D target-gamut mapping**, not an official Adobe RGB
or ProPhoto AgX view. Chroma contraction is not perceptually uniform hue preservation.
ProPhoto holds the wide-gamut rendered result, but cannot restore colors already
compressed by AgX's Rec.2020 rendering. A future target-native AgX implementation
would be a separately identified algorithm requiring its own appearance validation.

Gallery exposes both algorithms and Adobe RGB/ProPhoto selection. Its screen charts
remain explicitly sRGB SDR previews; print data is independent of that display clipping.


## Understanding exposure in the comparison

Zero EV is a multiplier of one; it does not make arbitrary scene data normally exposed.
The original chart multiplied all its colored rows by eight, giving even the darkest
row a peak component of 1.2. Its pale appearance after AgX was a highlight-heavy input,
not evidence that zero exposure should bypass the transform.

The chart now has logarithmically spaced colored rows with peak components from
0.0028125 to 11.52. Most of the chart therefore includes shadows and midtones.
Peak component is not luminance: different hues at the same row have different Y.
The bottom eight neutral patches are 0, .018, .18, .5, 1, 2, 4 and 8. The accompanying
readout lists their mapped linear D50 Y, including the selected exposure.

A native Mu3DView shows the original chart with exposure only, using FP16
extended-linear HDR; it is a source-value diagnostic, not another AgX view.
The mapped RGB and CMYK charts below remain explicit SDR sRGB previews.
An unsupported HDR surface is left black and reported; actual highlight brightness
depends on display headroom. The two print charts remain usable without an HDR display.
This color-band comparison is independent of the separate Color Management 3D scene.

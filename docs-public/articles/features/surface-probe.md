---
title: Surface probe
description: Inspect presentation capabilities and exercise explicit HDR or SDR fallback policy.
feature_id: surface-probe
---

# Surface probe

<xref:Mu3D.Maui.Controls.Mu3DView> owns the normal native surface and presentation session.
Applications receive the backend-neutral <xref:Mu3D.Graphics.IPresentationSurfaceSession> boundary;
they do not construct a backend session or poll the view size every frame.

## XAML host

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DView
        x:Name="SurfaceView"
        BackgroundColor="#202020"
        OutputSettings="{Binding OutputSettings}" />
    <VerticalStackLayout Grid.Row="1" Padding="16" Spacing="12">
        <Button Command="{Binding ProbeCommand}" Text="Probe surface capabilities" />
        <Button Command="{Binding PresentCommand}" Text="Present reference pattern" />
        <Label LineBreakMode="WordWrap" Text="{Binding ProbeDetails}" />
    </VerticalStackLayout>
</Grid>
```

The Gallery uses <xref:Mu3D.Native.Wgpu.WgpuSurfaceProbe> to report the formats, alpha modes and
present modes available for the current native surface. <xref:Mu3D.Graphics.SurfaceOutputNegotiator>
then combines those capabilities with explicit <xref:Mu3D.Graphics.OutputSettings>.

HDR capability is a format/color-space pair, not a property of the texture format alone.
<xref:Mu3D.Graphics.SurfaceCapabilities.FormatCapabilities> carries explicit pairs when the backend
can report them. The pinned wgpu-native v29 ABI reports formats only, so Mu3D retains one narrow
legacy rule: `Rgba16Float` may select the already verified extended-linear sRGB path. No other
format is promoted to HDR by assumption. When paired capabilities are present, the pair must
explicitly include `ExtendedSrgbLinear` before the current renderer selects it.

<xref:Mu3D.Graphics.IPresentationSurfaceSession.QueryDisplayHdrInfo> is a separate point-in-time
display query for luminance, EDR headroom, primaries, bit depth and coarse range information. Its
fields are optional: unavailable means unknown, never numeric zero and never an inferred SDR
display. The pinned v29 backend returns an unknown snapshot; the API exists now so a later native
backend can supply live data without changing application or renderer interfaces.

## What to verify

### Presentation white

Windows matching is directly configurable in shared XAML on either control:

```xaml
<mu3d:Mu3DView WindowsMatchSdrWhite="True" />
<!-- The scene control exposes the same property. -->
<mu3d:Mu3DSceneView WindowsMatchSdrWhite="False" />
```

The switch defaults to true and only affects Windows in System white mode. False preserves
native Windows values; Android/iOS/Mac Catalyst ignore it and keep system management. Changing
it requests a frame without restarting the session. The probe includes a Windows-only UI switch
bound to this property, and reports the latest system SDR white. Its reference renderer uses
the control's Draw callback so this conversion also applies to reference bands and resize redraws.
Direct backend presentation calls bypass this automatic control policy.

Automatic `Mu3DView` (including `Mu3DSceneView`) defaults to
`OutputSettings.WhiteMode = OutputWhiteMode.System`. On Windows extended-linear HDR output,
display-linear RGB is multiplied by the current system SDR white in nits divided by 80, once,
after the complete drawing/display-transform pipeline. Thus logical white 1 matches ordinary
SDR UI white. The window-scoped query follows system changes and monitor moves; an on-demand
frame is requested when white changes. `Mu3DView.SystemSdrWhiteNits` is null when unavailable.
An unavailable query passes native values through until information becomes available.

Android, iOS and Mac Catalyst retain compositor-managed relative white without this multiplier.
Mu3D does not set screen brightness. Ordinary SDR surfaces also retain system management.
This policy does not establish that a device's HDR path is physically correct.

For explicitly calibrated absolute HDR content on Windows:

```csharp
sceneView.OutputSettings = new OutputSettings
{
    DynamicRange = OutputDynamicRange.Hdr,
    WhiteMode = OutputWhiteMode.FixedAbsolute,
    ReferenceWhiteNits = 100,
};
```

Match `ReferenceWhiteNits` to the selected `ColorView3D.ReferenceWhiteNits`; SceneView rejects
a mismatch. FixedAbsolute uses that value divided by 80 and does not follow the SDR slider.
It is rejected on SDR surfaces and platforms lacking a known absolute nit mapping, including
the current mobile/Apple hosts. It requests an encoding, not a guarantee of measured luminance.
Use this mode for a fixed-mastering-peak view when retaining its absolute nit intent matters;
System intentionally adapts its normalized output to the user's current white.

Use `OutputWhiteMode.PlatformNative` for output that already includes platform white calibration
or to restore the previous native-value behavior. Do not compensate again in `Draw` with System
selected. Manual hosts and direct backend sessions own their conversion and are not automatically
scaled. Alpha, scene accumulation and exposure are unchanged by presentation white adaptation.

### Hardware checks

1. Confirm the selected format, color encoding, capability source, present mode and fallback reason.
2. Present the extended-linear reference pattern only after configuration succeeds.
3. Treat `Rgba16Float` capability and a successful present as software-path evidence.
4. Verify compositor/display HDR engagement separately on real hardware.

On Android, a requested or reported HDR/SDR ratio above one proves headroom availability, not
correct HDR pixels. Check that the reference values 1, 2 and 4 remain distinguishable with
sufficient display headroom. If they collapse, investigate actual buffer format/dataspace and
composition separately from the display request. Ordinary SDR screenshots cannot verify HDR
content or luminance. Remaining Android device and quantitative checks stay separate from
the observed visual recovery on the tested Xiaomi/Android16 device.

The native Android Gallery probe also reads its current producer window after configuration,
manual probing and reference presentation. It reports device/API version, buffer format and size,
current buffers dataspace (API 28+) and the separate consumer-default dataspace (API 34+).
The expected FP16/linear-scRGB producer values are format `22` and dataspace `0x18410000`.
Unavailable, unknown and native errors remain distinct. These queries observe producer
configuration; they do not read the queued buffer, SurfaceFlinger layer or physical display.
This diagnostic uses the control's borrowed native source on the UI thread and does not alter
its pixels, output settings or ownership. Use **Copy diagnostics** to share the report after
presenting the reference pattern; copying refreshes the producer/display snapshot at click time.

The Android report also reads the control host's attached display and window: attachment,
hardware acceleration, supported HDR types, current HDR/SDR ratio when reporting is available
(API 34+), highest possible ratio (API 36+), and requested window color mode/headroom. Window
settings are independent of the SurfaceView and are labeled accordingly. A missing ratio query
remains unknown rather than the platform getter's default 1. For a settled reading, present the
pattern, wait a few seconds, then use **Probe surface capabilities** and **Copy diagnostics**.
An available ratio above 1 reports brightness headroom; it does not verify preserved reference pixels.

The Android example also provides **Read actual reference pixels**. This explicitly enables
<xref:Mu3D.Native.Wgpu.WgpuSurfaceSession.TryEnableReadback> only on the current diagnostic session,
after checking the surface's native copy-source capabilities. Unsupported surfaces report
unavailable. Ordinary sessions keep their default usage; enabling copies may affect performance
and remains enabled through resize until that session is retired.

The normal control-owned Draw callback renders the same reference pattern, then submits six
one-pixel copies from the acquired presentation texture before returning. The report includes
native-verified texture properties, original FP16 channel bits, decoded RGBA, expected values
and the frame's present status. The asynchronous read retains a small staging buffer, not the
surface texture. It performs no tone mapping or normalization. Session replacement, navigation
and timeout cancel the managed wait while native cleanup retains the necessary resources.
This checks the GPU attachment before presentation; it does not read the queued buffer,
compositor layer or physical display. The diagnostic changes the session's copy usage, so its
result must be identified as that opt-in path. Use the button, wait for the result, then
**Copy diagnostics** to share both the pixel data and a fresh display snapshot.

On Android 16 and later, automatic alpha with Automatic/Hdr output uses an alpha-capable
SurfaceView carrier to avoid the opaque-FP16 branch in upstream Android composition. The native
backend still configures FP16/scRGB buffers. Pixels with alpha 1 keep opaque coverage; alpha
below 1 may blend with the content behind the surface. The carrier retains the existing z-order
and control-owned surface lifecycle. Explicit alpha modes and SDR output keep their existing
policy. A tested Xiaomi/Android16 device reports visible HDR recovery, with the consumer's opaque
flag cleared. The exact vendor shader remains unverified; other-device, quantitative and lifecycle
checks remain open.

Display HDR information helps an application choose an explicit display-mapping target. It does not
decide whether a surface can be configured for HDR; the advertised format/color-space pair remains
the capability gate. Re-query live display information after a window moves, resizes, changes
monitors or the operating system HDR setting changes.

The normal view lifecycle owns physical-pixel resize, surface reconfiguration and final disposal.
It follows MAUI `Loaded`/`Unloaded`, Handler carrier and Window lifecycle events. Navigating away
while the first adapter or device request is still pending cooperatively abandons that automatic
session; no page-level `Dispose`, forced collection or application quit hook is required. A native
request that completes after the view has left releases its result instead of publishing a stale
session.

`SurfaceManagementMode.Manual` is reserved for engines that intentionally own swapchain policy; it
is not required for ordinary rendering or resize.

---
title: Device probe
description: Verify backend adapter, device, ShaderF16 and offscreen GPU command execution.
feature_id: device-probe
---

# Device probe

<xref:Mu3D.Native.Wgpu.WgpuDeviceProbe> creates a short-lived adapter and device using the pinned
default backend. It reports adapter/backend information, ShaderF16 capability and whether an
`Rgba16Float` texture can be created.

## XAML page

The probe is an asynchronous diagnostic rather than a visual Mu3D control. Its Gallery page is
still authored in XAML and keeps progress/status UI in the normal MAUI tree:

```xaml
<VerticalStackLayout Padding="24" Spacing="14">
    <Button Clicked="OnRunProbeClicked" Text="Run device probe" />
    <ActivityIndicator
        IsRunning="{Binding IsProbeRunning}"
        IsVisible="{Binding IsProbeRunning}" />
    <Label FontAttributes="Bold" Text="{Binding ProbeStatus}" />
    <Label LineBreakMode="WordWrap" Text="{Binding ProbeDetails}" />
</VerticalStackLayout>
```

<xref:Mu3D.Native.Wgpu.WgpuOffscreenTriangleProbe> goes one step further: it creates an FP16
offscreen target, compiles WGSL, submits a real triangle and verifies GPU command completion through
the backend-independent graphics API.

## Interpreting the result

A pass proves that the current process can load the pinned native backend, select an adapter, create
a device and execute the tested offscreen workload. <xref:Mu3D.Graphics.GraphicsCapabilities>
describes the features and limits that higher layers may use.

It does **not** prove that a window surface supports FP16 presentation, that the compositor selected
an HDR path, or that the physical display produced the requested luminance/color. Use **Surface
Probe** for presentation negotiation and a physical measurement workflow for final HDR claims.

Probe objects are intentionally short-lived. Do not keep a diagnostic device alive as a
process-global renderer or infer production adapter policy from this sample.

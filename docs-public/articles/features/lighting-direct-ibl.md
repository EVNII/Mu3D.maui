---
title: Direct and image-based lighting
description: Load a bounded Radiance HDR environment and combine it with direct PBR lighting.
feature_id: lighting-direct-ibl
---

# Direct and image-based lighting

The Gallery feature `lighting-direct-ibl` demonstrates two independent lighting contributions:
direct GGX lighting and a Radiance HDR image-based environment. The environment remains explicitly
tagged linear RGB and is prepared without display conversion or silent tone mapping.

## XAML host

The bounded environment asset and renderer preparation remain asynchronous application logic. XAML
declares the surface and binds the independent direct/IBL controls.

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DView
        x:Name="SurfaceView"
        BackgroundColor="Black"
        Draw="OnSurfaceDraw" />
    <VerticalStackLayout Grid.Row="1" Padding="16">
        <Switch IsToggled="{Binding IsDirectLightingEnabled, Mode=TwoWay}" />
        <Switch IsToggled="{Binding IsImageBasedLightingEnabled, Mode=TwoWay}" />
        <Slider Maximum="4" Value="{Binding EnvironmentIntensity, Mode=TwoWay}" />
    </VerticalStackLayout>
</Grid>
```

## Load ownership

Use <xref:Mu3D.SceneGraph.RadianceHdrEnvironmentLoader> when source acquisition must be asynchronous
and bounded. A caller-supplied stream stays open. A stream returned by a keyed resolver transfers to
the loader and is closed on success, failure or cancellation.

```csharp
var cache = new RadianceHdrSourceCache(2 * 1024 * 1024);
var options = new RadianceHdrEnvironmentLoadOptions(StandardColorSpaces.LinearSrgb)
{
    Name = "Studio Small 02",
    MaximumSourceByteCount = 2 * 1024 * 1024,
    MaximumOutputByteCount = 8 * 1024 * 1024,
    RetainEncodedSource = false,
};

RadianceHdrEnvironmentAsset asset = await RadianceHdrEnvironmentLoader.LoadAsync(
    "app://Hdri/studio_small_02_1k.hdr",
    cancellationToken => OpenEnvironmentAsync(cancellationToken),
    cache,
    options,
    cancellationToken);

await SceneRenderer.PrepareImageBasedLightingAsync(asset.Environment, cancellationToken);
```

<xref:Mu3D.SceneGraph.RadianceHdrSourceCache> is application-owned and reusable. Clearing it does
not invalidate `asset.Environment`. Exact encoded bytes are not retained by default; enable
`RetainEncodedSource` only when the asset itself must recreate or archive the original container.
The immutable decoded FP32 RGB output is always retained by the asset.

## Limits and memory

`MaximumSourceByteCount` bounds the active encoded input. `MaximumOutputByteCount` bounds the final
FP32 RGB environment before its pixel arrays are allocated. Cache capacity and optional asset source
retention are separate, visible ownership decisions rather than hidden process-global memory.

For the bundled 1024×512 environment, the retained FP32 output is 6 MiB. The Gallery uses an 8 MiB
output limit and a 2 MiB encoded-source cache.

## Lifecycle

Cancel page-owned loading when the page stops being visible. GPU presenters and resources should be
released when the presentation session becomes unavailable, not merely because a cached Shell page
temporarily disappears. <xref:Mu3D.Maui.Controls.Mu3DView> continues to own its automatic surface
session; the page owns its cache, cancellation source, decoded asset and presenter.

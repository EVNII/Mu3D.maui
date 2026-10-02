---
title: KTX2 GPU sampling
description: Select decoded or GPU-compressed KTX2 material representations from explicit capabilities.
feature_id: ktx2-gpu-sampling
---

# KTX2 GPU sampling

Set <xref:Mu3D.Native.Ktx.Ktx2TextureLoadPreference> to `PreferGpuCompressed` and provide explicit
<xref:Mu3D.Graphics.GraphicsCapabilities>. The loader chooses a supported transcode target or falls
back to decoded FP32 RGBA.

## XAML host

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DView
        x:Name="SurfaceView"
        BackgroundColor="Black"
        Draw="OnSurfaceDraw" />
    <VerticalStackLayout Grid.Row="1" Padding="16" Spacing="10">
        <Label Text="Decoded RGBA8 / GPU-compressed sampling" />
        <Button Command="{Binding ReloadCommand}" Text="Reload and transcode fixtures" />
        <Label LineBreakMode="WordWrap" Text="{Binding Details}" />
    </VerticalStackLayout>
</Grid>
```

Inspect <xref:Mu3D.Native.Ktx.Ktx2TextureRepresentation> on the returned asset:

- `DecodedColor` supplies an explicitly tagged linear color image.
- `DecodedData` supplies normalized non-color texels.
- `GpuCompressed` supplies a <xref:Mu3D.SceneGraph.CompressedMaterialTexture> with owned mip data.

Assign exactly one representation to the matching material slot. Color-bearing maps and numerical
maps must keep their semantic classification; compression does not change color-space meaning.

The Gallery compares decoded and compressed sampling through the same material/UV path. It is a
visual integration example, not a PSNR conformance harness. The asset-owned decoded/compressed
source stays available for renderer upload and device recreation; encoded KTX2 retention remains an
independent option.

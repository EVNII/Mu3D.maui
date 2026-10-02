---
title: Material conformance
description: Validate supported glTF PBR extensions with official focused assets and exact variants.
feature_id: material-conformance
---

# Material conformance

Material Conformance is the Gallery's advanced validation harness. It exercises core
metallic/roughness, emissive strength, clearcoat, anisotropy, sheen, iridescence, specular, IOR,
transmission/volume/dispersion, diffuse transmission, unlit and authored material variants with
focused official Khronos assets.

## XAML harness shell

The advanced harness keeps its model matrix and renderer policy in C#, but its complete control
surface is ordinary XAML. The minimal structural pattern is:

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DView x:Name="SurfaceView" BackgroundColor="Black" />
    <ScrollView Grid.Row="1" MaximumHeightRequest="420">
        <VerticalStackLayout Padding="16" Spacing="12">
            <Picker ItemsSource="{Binding Models}" SelectedItem="{Binding SelectedModel}" />
            <Picker ItemsSource="{Binding RenderOutputs}" SelectedItem="{Binding SelectedOutput}" />
            <Switch IsToggled="{Binding ShadowsEnabled}" />
            <Switch IsToggled="{Binding AmbientOcclusionEnabled}" />
        </VerticalStackLayout>
    </ScrollView>
</Grid>
```

<xref:Mu3D.SceneGraph.PbrMaterial> retains compositional extension blocks and texture sources.
<xref:Mu3D.SceneGraph.MaterialShaderTemplate> is an allowed-feature policy, not a Cartesian product
of possible shaders. <xref:Mu3D.SceneGraph.MaterialVariantManifest> records only exact structures
observed in the imported asset, and <xref:Mu3D.Rendering.SceneRenderer> can prewarm those variants
through its AOT-safe API.

## Portable resource budget

The compact binding plan supports at most ten material sampled textures plus six shared renderer
textures at the WebGPU portable default of sixteen fragment-stage sampled textures. Unsupported
combinations fail closed; the renderer does not silently drop an authored map.

## Use this page correctly

- Use focused pages for ordinary integration and copyable code.
- Use this harness for extension isolation, official asset comparison and compound-variant stress.
- Treat physical appearance as platform/device evidence only for the exact build and display tested.
- Keep official asset licenses and hashes adjacent to the bundled files.

The harness intentionally remains larger than the Gallery's 500-line focused-example limit and is
isolated under **Advanced**.

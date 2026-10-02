---
title: PBR material and textures
description: Author metallic-roughness values and explicitly classified color/data textures.
feature_id: pbr-material-textures
---

# PBR material and textures

<xref:Mu3D.SceneGraph.PbrMaterial> is the compositional PBR authoring root. Its `Base` block owns
base color, metallic and roughness values; additional blocks enable emissive, clearcoat,
anisotropy, transmission/volume, sheen, iridescence, specular and diffuse transmission.

## XAML host

The focused example keeps advanced texture source creation in C#, but declares its renderer and
live factor controls in XAML:

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DView
        x:Name="SurfaceView"
        BackgroundColor="Black"
        Draw="OnSurfaceDraw" />
    <VerticalStackLayout Grid.Row="1" Padding="16">
        <Slider Maximum="1" Value="{Binding Metallic, Mode=TwoWay}" />
        <Slider Maximum="1" Minimum="0.04" Value="{Binding Roughness, Mode=TwoWay}" />
        <Switch IsToggled="{Binding IsBaseColorTextureEnabled, Mode=TwoWay}" />
        <Switch IsToggled="{Binding IsNormalTextureEnabled, Mode=TwoWay}" />
    </VerticalStackLayout>
</Grid>
```

Color textures and numerical data textures are distinct:

- Base color, emissive and other color-bearing maps use explicitly tagged linear color images.
- Normal, occlusion, roughness, metallic and scalar-factor maps use normalized non-color data.
- <xref:Mu3D.SceneGraph.MaterialTextureMapping> carries UV set, transform and sampler policy.

```csharp
var material = new PbrMaterial(baseColor, 0.15f, 0.42f, "Paint");
material.Base.ColorTexture = colorImage;
material.Base.ColorTextureMapping = mapping;
material.Base.NormalTexture = normalData;
material.Base.NormalTextureMapping = mapping;
```

Changing factors does not create a new shader variant. Structural extension/texture combinations
are observed through <xref:Mu3D.SceneGraph.MaterialVariantManifest> and may be explicitly prewarmed
on <xref:Mu3D.Rendering.SceneRenderer>. The renderer owns uploaded GPU copies while the material
retains the application image or compressed mip source needed for device recreation.

---
title: glTF material variants
description: Apply authored KHR_materials_variants choices without duplicating geometry.
feature_id: gltf-material-variants
---

# glTF material variants

<xref:Mu3D.Formats.Gltf.GltfAsset> exposes authored
<xref:Mu3D.Formats.Gltf.GltfMaterialVariant> choices imported from `KHR_materials_variants`.
Variant selection swaps material references on affected primitives while preserving scene nodes,
geometry and renderer geometry caches.

## XAML host

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DView
        x:Name="SurfaceView"
        BackgroundColor="Black"
        Draw="OnSurfaceDraw" />
    <Picker
        Grid.Row="1"
        ItemsSource="{Binding MaterialVariants}"
        SelectedItem="{Binding SelectedVariant, Mode=TwoWay}"
        Title="Authored material variant" />
</Grid>
```

The binding changes application selection state; the view model or code-behind calls the asset API
and invalidates the existing scene.

```csharp
foreach (GltfMaterialVariant variant in asset.MaterialVariants)
{
    Console.WriteLine(variant.Name);
}

asset.ApplyMaterialVariant(selectedIndex);
sceneView.InvalidateScene();

// Null restores every primitive's authored default material.
asset.ApplyMaterialVariant(index: null);
```

Authored variants are different from shader variants. `GltfAsset.MaterialVariants` contains named
asset choices for application UI. `GltfAsset.MaterialShaderVariants` is a structural
<xref:Mu3D.SceneGraph.MaterialVariantManifest> used for exact AOT-safe pipeline prewarming.

The application owns selection UI and decides whether variant changes participate in undo/history.
The asset does not clone textures or GPU geometry per choice.

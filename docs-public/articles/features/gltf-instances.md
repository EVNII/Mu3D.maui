---
title: Reusable glTF assets and instances
description: Import once, then create independent scene instances that share geometry and texture sources.
feature_id: gltf-instances
---

# Reusable glTF assets and instances

<xref:Mu3D.Formats.Gltf.GltfAsset.CreateSceneAsset(System.String)> captures an imported glTF asset
as a reusable <xref:Mu3D.Formats.Gltf.GltfSceneAsset>. Create the definition once after loading and
cache it at the application asset layer. Creating an instance does not re-read the GLB, parse JSON,
decode images or transcode texture data.

## XAML host

Bind the application-created scene/camera into the high-level view. Variant UI can bind to each
instance's independent selection without creating another renderer surface.

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DSceneView
        x:Name="SceneView"
        BackgroundColor="Black"
        Camera="{Binding Camera}"
        Scene="{Binding RoomScene}" />
    <Grid Grid.Row="1" ColumnDefinitions="*,*" Padding="16">
        <Picker
            ItemsSource="{Binding Variants}"
            SelectedItem="{Binding LeftVariant, Mode=TwoWay}" />
        <Picker
            Grid.Column="1"
            ItemsSource="{Binding Variants}"
            SelectedItem="{Binding RightVariant, Mode=TwoWay}" />
    </Grid>
</Grid>
```

```csharp
GltfAsset imported = await GltfAssetLoader.LoadAsync(stream, options, cancellationToken);
GltfSceneAsset product = imported.CreateSceneAsset("Chair product");

GltfSceneInstance first = product.CreateInstance("Chair 1");
GltfSceneInstance second = product.CreateInstance("Chair 2");
first.AttachTo(roomScene);
second.AttachTo(roomScene);

second.Root.Transform.Position = new Vector3(2f, 0f, 0f);
second.ApplyMaterialVariant("Blue fabric");
```

Every instance receives independent nodes, transforms, materials, skins, morph weights, animation
targets and active `KHR_materials_variants` selection. Immutable geometry and decoded or compressed
texture sources retain object identity across instances, allowing renderer caches to reuse their GPU
uploads within a device. A newly created definition always begins at the authored glTF default
material selection, even if the compatibility `GltfAsset.Scene` had a variant selected earlier.

The definition and its instances are managed scene data. They do not own a renderer, presentation
surface, native GPU resource or `Mu3DSceneView`, so instances can be detached, reparented or placed
in different application scenes. Each destination view continues to own its presentation lifecycle
and device-local renderer cache.

## Product lists and bounded lifetime

For a product grid, keep one bounded application cache of `GltfSceneAsset` definitions keyed by
product/resource identity. Create instances only for visible or near-visible cells; detach and drop
those instances when cells leave the virtualization window. Use
<xref:Mu3D.Formats.Gltf.GltfSourceRetentionMode.None> unless exact encoded bytes are required for
offline or export behavior. The asynchronous loader's source and external-resource limits still
apply before the reusable definition is created.

This API establishes definition/instance reuse only. Viewport virtualization, load prioritization,
placeholder UI, cancellation and eviction remain application policy so a library component cannot
silently retain an unbounded catalog.

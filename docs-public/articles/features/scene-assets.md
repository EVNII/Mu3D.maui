---
title: Scene assets and instances
description: Share immutable 3D resources while keeping transforms, materials, skins and animation state independent.
feature_id: scene-assets
---

# Scene assets and instances

<xref:Mu3D.Assets.SceneAsset> captures a reusable backend-independent snapshot. Each
<xref:Mu3D.Assets.SceneInstance> receives independent mutable nodes, transforms, materials, skins,
morph weights and animation targets. Immutable mesh geometry and decoded or compressed texture
sources remain shared.

## XAML host

The reusable asset and instances remain Core objects. Bind their composed destination scene and
camera into separate XAML-owned views:

```xaml
<Grid ColumnDefinitions="*,*" ColumnSpacing="12">
    <mu3d:Mu3DSceneView
        Camera="{Binding FirstCamera}"
        Scene="{Binding FirstScene}" />
    <mu3d:Mu3DSceneView
        Grid.Column="1"
        Camera="{Binding SecondCamera}"
        Scene="{Binding SecondScene}" />
</Grid>
```

```csharp
Scene source = BuildFurnitureScene();
SceneAsset chairAsset = new(source, importedAnimations);

SceneInstance firstChair = chairAsset.CreateInstance("Chair 1");
SceneInstance secondChair = chairAsset.CreateInstance("Chair 2");
firstChair.AttachTo(roomScene);
secondChair.AttachTo(roomScene);

secondChair.Root.Transform.Position = new Vector3(2f, 0f, 0f);
```

The instance root moves the complete captured hierarchy. `AttachTo` uses normal scene-node
reparenting, so the same instance can move from one application scene to another. `Detach` removes
it without releasing shared asset data.

An asset and its instances never own a `Mu3DSceneView`, renderer, presentation surface or native
GPU object. A host moves the instance data and assigns the destination scene to its view; each view
continues to own its own presentation lifecycle.

## Snapshot and mutation rules

The constructor captures source state immediately. Later edits to the source scene do not alter the
asset. Instances preserve shared-material identity inside one hierarchy, while material objects are
different between instances. Applying an animation clip from one instance therefore cannot mutate
another instance.

Built-in nodes, PBR/unlit materials, skins and all built-in animation tracks are supported. When an
application-defined node or material participates, supply explicit factories through
<xref:Mu3D.Assets.SceneAssetCloneOptions>. This is static, AOT-analyzable composition; Mu3D does not
scan assemblies or discover runtime plugins.

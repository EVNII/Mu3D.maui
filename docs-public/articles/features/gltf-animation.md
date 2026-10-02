---
title: glTF animation
description: Select and play animation clips imported with a glTF asset.
feature_id: gltf-animation
---

# glTF animation

<xref:Mu3D.Formats.Gltf.GltfAsset> exposes imported
<xref:Mu3D.SceneGraph.AnimationClip> instances through its animation list. Clip names come from the
asset and may be absent or duplicated, so application UI should keep a stable index as well as a
display label.

## XAML host

The imported scene and playback clock remain application-owned. XAML declares the presentation
surface and ordinary clip controls:

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DView
        x:Name="SurfaceView"
        BackgroundColor="Black"
        Draw="OnSurfaceDraw" />
    <VerticalStackLayout Grid.Row="1" Padding="16" Spacing="10">
        <Picker
            ItemsSource="{Binding Clips}"
            SelectedItem="{Binding SelectedClip, Mode=TwoWay}" />
        <Switch IsToggled="{Binding IsPlaying, Mode=TwoWay}" />
    </VerticalStackLayout>
</Grid>
```

```csharp
AnimationClip clip = asset.Animations[selectedIndex];
var layer = new AnimationLayer(clip)
{
    WrapMode = AnimationWrapMode.Loop,
    Weight = 1f,
};
var mixer = new AnimationMixer([layer]);
mixer.CaptureBasePose();
```

Advance the layer time using the host's frame clock, apply the mixer, then invalidate the scene.
Changing clips is application playback policy; Mu3D does not create a global clock or start a timer
for an imported asset.

The imported scene owns no presentation session. Keep the <xref:Mu3D.Formats.Gltf.GltfAsset> alive
while its scene/material/animation data is in use, and release renderer-owned GPU resources through
the normal scene-view or renderer lifecycle.

For a finite packaged example, page disappearance can invalidate the current load between file,
import and preparation stages; a completed stale stage must not publish a presenter into a replaced
session. Application loaders for remote or genuinely long-running work should still expose and
honor ordinary .NET cancellation tokens.

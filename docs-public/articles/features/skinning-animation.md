---
title: Skinning and animation
description: Blend FP32 transform and morph animation over a skinned mesh.
feature_id: skinning-animation
---

# Skinning and animation

<xref:Mu3D.SceneGraph.Skin> binds mesh joints and inverse bind matrices.
<xref:Mu3D.SceneGraph.MorphTarget> stores position/normal/tangent deltas applied before skinning.
Animation tracks target scene-node transforms or mesh morph weights.

<xref:Mu3D.SceneGraph.AnimationClip> groups tracks. Wrap one or more clips in
<xref:Mu3D.SceneGraph.AnimationLayer> instances and combine them through
<xref:Mu3D.SceneGraph.AnimationMixer>:

## XAML host

Skin, clips and mixer are application-owned scene data. The view and playback controls remain
declarative:

```xaml
<Grid RowDefinitions="*,Auto">
    <mu3d:Mu3DView x:Name="SurfaceView" BackgroundColor="Black" />
    <VerticalStackLayout Grid.Row="1" Padding="16" Spacing="10">
        <Switch IsToggled="{Binding IsPlaying, Mode=TwoWay}" />
        <Slider Maximum="2" Value="{Binding Time, Mode=TwoWay}" />
        <Slider Maximum="1" Value="{Binding Blend, Mode=TwoWay}" />
        <Slider Maximum="1.5" Minimum="-0.5" Value="{Binding MorphWeight, Mode=TwoWay}" />
    </VerticalStackLayout>
</Grid>
```

```csharp
var idleLayer = new AnimationLayer(idleClip) { Weight = 0.75f };
var actionLayer = new AnimationLayer(actionClip) { Weight = 0.25f };
var mixer = new AnimationMixer([idleLayer, actionLayer]);

mixer.CaptureBasePose();
idleLayer.Time = timeSeconds;
actionLayer.Time = timeSeconds;
mixer.Apply();
```

Times, transforms, skin matrices and morph accumulation remain FP32. Apply animation before
invalidating/drawing the scene. Beauty, shadow and AO depth paths use the same resulting deformation,
so animated silhouettes remain consistent.

The mixer borrows clips, layers and targets. Application playback state decides time progression,
looping, transition policy and when to request another frame.

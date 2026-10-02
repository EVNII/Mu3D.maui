---
title: MAUI animation and progress bridge
description: Drive Mu3D node and camera paths from explicit MAUI animation, binding, or scroll progress.
feature_id: progress-bridge
---

# MAUI animation and progress bridge

`ProgressTool` maps one bindable finite value onto ordered 3D targets and requests one coalesced
viewport frame. It deliberately does not own a timer, `ScrollView`, page navigation, MAUI animation,
camera, node, or material. Your application chooses the progress source.

## Declare mappings in XAML

Place the tool in the same `ViewportTools` collection as Orbit, selection, or Gizmo tools. A
declarative camera derives from `SceneNode3D`, so `NodeTransformProgress` can drive camera and object
paths with the same syntax:

```xaml
<mu3d:Mu3DSceneView>
  <mu3d:Mu3DSceneView.SceneContent>
    <mu3d:Scene3D>
      <mu3d:Scene3D.Camera>
        <mu3d:PerspectiveCamera3D x:Name="Camera" Z="5.4" />
      </mu3d:Scene3D.Camera>
      <mu3d:Cone3D x:Name="Product" />
    </mu3d:Scene3D>
  </mu3d:Mu3DSceneView.SceneContent>
  <mu3d:Mu3DSceneView.Features>
    <toolkit:ViewportTools>
      <toolkit:ProgressTool x:Name="SceneProgress">
        <toolkit:NodeTransformProgress
            Target="{x:Reference Product}"
            Property="RotationY"
            From="0"
            To="360" />
        <toolkit:NodeTransformProgress
            Target="{x:Reference Camera}"
            Property="X"
            From="-1.2"
            To="1.2" />
      </toolkit:ProgressTool>
    </toolkit:ViewportTools>
  </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

Update only `SceneProgress.Progress`. The controller applies every mapping in declaration order and
issues one advisory frame request after the mutations. Values below zero or above one are clamped,
which gives scroll overshoot stable endpoint behavior; non-finite values are rejected.

## Supply progress explicitly

A slider or view-model property can bind directly. A scroll callback converts the application's
chosen scroll range into normalized progress:

```csharp
private void OnScrolled(object? sender, ScrolledEventArgs e)
{
    double range = Math.Max(1d, ContentHeight - ViewportHeight);
    SceneProgress.Progress = e.ScrollY / range;
}
```

A MAUI animation uses the same property. The Gallery retrieves MAUI's `IAnimationManager`, creates
one ordinary VSync animation, and assigns each animation value to `Progress`. `ProgressTool` does not
start, stop, repeat, or dispose that animation. When the page disappears, the page removes its own
animation; the viewport feature follows normal Handler attachment and requests no detached frames.

## Extend the mapping set

`Mu3D.Toolkit.Animation.IViewportProgressMapping` is UI- and backend-independent. Downstream
packages can implement it for clip time, a scene instance, a material parameter, or an application
camera rig and place that implementation inside `ProgressTool.Mappings`. C# callers can use
`DelegateViewportProgressMapping` with `ViewportProgressController` directly. No reflection or
base-scene-node property dictionary is involved.

Autonomous clip playback remains different: it advances from the viewport's suspend-aware VSync
clock. The progress bridge is for explicit application/MAUI/scroll time and never substitutes a
generic timer for presentation scheduling.

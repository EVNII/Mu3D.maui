---
title: Declarative scenes
description: Declare a Mu3D camera, primitives, transforms, lights, and materials directly in MAUI XAML.
feature_id: declarative-scene
---

# Declarative scenes

<xref:Mu3D.Maui.Controls.Mu3DSceneView> accepts one nested
<xref:Mu3D.Maui.Controls.Scene3D>. The facade supplies the lower-level Core scene and camera, then
requests a coalesced frame whenever a bindable node or material changes. Renderer, device, surface,
resize, and native-resource ownership stay with the view.

```xaml
<mu3d:Mu3DSceneView SurfaceError="OnSurfaceError">
  <mu3d:Scene3D Name="Product preview">
    <mu3d:Scene3D.Camera>
      <mu3d:PerspectiveCamera3D FieldOfView="52" Z="5.5" />
    </mu3d:Scene3D.Camera>

    <mu3d:Sphere3D Radius="0.9" X="-1.2">
      <mu3d:PbrMaterial3D Color="#4A9DFF" Metallic="0.15" Roughness="0.22" />
    </mu3d:Sphere3D>

    <mu3d:Cone3D Height="1.8" Radius="0.82" X="1.2">
      <mu3d:UnlitMaterial3D Color="#FF7A28" />
    </mu3d:Cone3D>

    <mu3d:DirectionalLight3D Intensity="3.5" RotationX="-35" RotationY="-30" />
  </mu3d:Scene3D>
</mu3d:Mu3DSceneView>
```

The facade uses individual FP32 translation, degree rotation, and scale properties so MAUI XAML,
bindings, and styles can address them without a custom vector converter. MAUI colors are interpreted
as encoded sRGB UI colors and decoded to explicitly tagged linear sRGB before reaching Core. This
does not introduce implicit tone mapping or display encoding.

## Imperative interoperation

The declarative wrappers do not hide Core. `Scene3D.Scene`, `SceneNode3D.CoreNode`,
`Primitive3D.Mesh`, and `Material3D.CoreMaterial` expose the corresponding backend-independent
objects for selection, controllers, imported assets, or advanced properties. After a direct Core
mutation, call `Mu3DSceneView.InvalidateScene()` because Mu3D cannot observe arbitrary imperative
property changes.

`Scene3D` holds only managed scene state and may be observed by more than one SceneView. It does not
transfer or share presentation sessions or native GPU resources. For reusable furniture, product,
or glTF data, keep using <xref:Mu3D.Assets.SceneAsset> and independent instances; declarative
primitives are the concise authoring path, not an infinite-catalog cache policy.

Input, selection ranking, undo/redo, and editor state remain application policy. Optional statically
composed SceneView Features can be declared beside `Scene3D` without changing scene ownership.

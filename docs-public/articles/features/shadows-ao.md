---
title: Shadows and ambient occlusion
description: Configure directional shadowing and screen-space ambient occlusion independently.
feature_id: shadows-ao
---

# Shadows and ambient occlusion

Directional shadows and screen-space ambient occlusion solve different visibility problems.
<xref:Mu3D.SceneGraph.DirectionalLight> can cast a shadow map from one direct-light direction.
<xref:Mu3D.Rendering.SceneRenderer> can independently run a camera-depth prepass and apply
screen-space ambient occlusion to indirect diffuse lighting.

## XAML host

The current declarative light and primitive expose shadow authoring directly. Renderer-wide AO
remains application renderer policy and can be bound through the page/view model controls.

```xaml
<mu3d:Mu3DSceneView>
    <mu3d:Scene3D>
        <mu3d:Sphere3D ShadowCastingMode="ShadowsOnly">
            <mu3d:PbrMaterial3D Color="#7586FF" Roughness="0.35" />
        </mu3d:Sphere3D>
        <mu3d:DirectionalLight3D
            AngularDiameterDegrees="0.5"
            CastsShadows="True"
            ShadowOpacity="0.65" />
    </mu3d:Scene3D>
</mu3d:Mu3DSceneView>
```

```csharp
directionalLight.CastsShadows = true;
directionalLight.ShadowOpacity = 0.65f;
directionalLight.AngularDiameterRadians = 0.15f;

occluder.ShadowCastingMode = MeshShadowCastingMode.ShadowsOnly;

renderer.AmbientOcclusionEnabled = true;
renderer.AmbientOcclusionRadius = 0.65f;
renderer.AmbientOcclusionStrength = 1f;
```

`MeshShadowCastingMode.On` is the default and submits an opaque or alpha-masked mesh to both camera
and directional-shadow passes. `Off` keeps the mesh camera-visible but omits it from the shadow map.
`ShadowsOnly` omits the mesh from camera color/depth, AO and transmission work while retaining its
opaque or masked geometry in the shadow map. This is explicit pass participation, not transparency:
the material remains opaque and the camera pass does not draw it. Setting a node's ordinary
`IsVisible` to false still suppresses that node from every pass.

`ShadowOpacity` mixes sampled shadow visibility toward fully lit and does not change material alpha
or claim colored/transmissive shadows. The light's angular diameter controls PCSS source size;
together with measured blocker-to-receiver separation it makes nearby blockers sharper and distant
blockers softer. Declarative MAUI scenes expose the corresponding `ShadowCastingMode`,
`ShadowOpacity` and `AngularDiameterDegrees` properties. Directional shadows remain attached to the
radiance-producing scene light; screen-aligned input-tool shadows belong in a separate render
feature rather than pretending to be another scene light.

Shadowing depends on the light and scene geometry. AO is view-dependent: its radius is measured in
world units and it samples information visible in the current camera depth buffer. It should not be
treated as a replacement for direct-light shadows.

Both features add render work and retain renderer-owned GPU resources. Toggle them on the existing
renderer rather than rebuilding the presentation session. Renderer disposal releases the shadow/AO
attachments; the application continues to own the light, scene and camera.

The current focused example demonstrates directional shadows only. Point/spot-light shadow maps are
not implied.

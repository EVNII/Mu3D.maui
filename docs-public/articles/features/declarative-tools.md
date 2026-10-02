---
title: Declarative viewport tools
description: Connect XAML selection, Orbit and Transform Gizmo tools to declarative scene elements.
feature_id: declarative-tools
---

# Declarative viewport tools

<xref:Mu3D.Maui.Toolkit.Controls.ViewportTools> groups explicitly declared tools under one
`Mu3DSceneView` Feature. It creates one attachment-scoped
<xref:Mu3D.Toolkit.Controls.ViewportControlArbiter>, attaches children in declaration order, and
detaches them in reverse order. Selection, Orbit and Gizmo therefore coordinate input without page
code constructing controllers, passes, or arbitration state.

```xaml
<mu3d:Mu3DSceneView>
  <mu3d:Mu3DSceneView.SceneContent>
    <mu3d:Scene3D>
      <mu3d:Scene3D.Camera>
        <mu3d:PerspectiveCamera3D
            x:Name="Camera"
            X="3.2" Y="2.4" Z="6.2" />
      </mu3d:Scene3D.Camera>

      <mu3d:Cone3D
          x:Name="ProductA"
          toolkit:SceneSelection.Mask="1"
          X="-1.1">
        <mu3d:PbrMaterial3D Color="#3296FF" Roughness="0.28" />
      </mu3d:Cone3D>
      <mu3d:Sphere3D
          x:Name="ProductB"
          toolkit:SceneSelection.Mask="2"
          X="1.15">
        <mu3d:PbrMaterial3D Color="#FF8A38" Roughness="0.32" />
      </mu3d:Sphere3D>
    </mu3d:Scene3D>
  </mu3d:Mu3DSceneView.SceneContent>

  <mu3d:Mu3DSceneView.Features>
    <toolkit:ViewportTools>
      <toolkit:SceneSelectionTool
          x:Name="Selection"
          SelectedNode="{x:Reference ProductA}"
          SelectionMask="3" />
      <toolkit:OrbitTool
          Camera="{x:Reference Camera}"
          MinimumDistance="2.5"
          MaximumDistance="15" />
      <toolkit:TransformGizmoTool
          Target="{Binding SelectedNode,
                           Source={x:Reference Selection},
                           x:DataType=toolkit:SceneSelectionTool}"
          IsRotateEnabled="True"
          IsScaleEnabled="True"
          TranslationStep="0.1"
          RotationStep="15"
          ScaleStep="0.1"
          ScreenSize="110" />
    </toolkit:ViewportTools>
  </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

When a compiled binding uses an explicit `Source`, enable
`MauiEnableXamlCBindingWithSourceCompilation` in the application project and provide the binding's
`x:DataType`, as shown above. The Gallery does both and therefore keeps this retargeting path
compile-checked.

<xref:Mu3D.Maui.Toolkit.Controls.SceneSelectionTool> installs one ordinary MAUI tap recognizer only
while its Feature attachment is active. The UI-independent
<xref:Mu3D.Toolkit.Selection.SceneRaycaster> intersects visible base mesh triangles in physical
viewport coordinates, returns one closest hit per mesh in camera-distance order, and the MAUI tool
maps hit descendants to their declarative owner. `SelectedNode` is a two-way bindable property, so
the Gizmo retargets without a click handler or `SelectionChanged` handler. Set the optional attached
property `toolkit:SceneSelection.IsSelectable="False"` on an individual ground, background, or helper
node that must not become a candidate. For groups, a node is eligible only when its attached
`toolkit:SceneSelection.Mask` intersects the tool's `SelectionMask`; the example uses bits 1 and 2
and accepts both with 3. Selection masks are independent of renderer visibility masks.

Selection metadata is deliberately not part of `SceneNode3D`. MAUI attached properties let Toolkit
add this opt-in state without affecting applications that omit Toolkit. A downstream package can
define its own strongly typed attached properties for `ProductId`, `Locked`, `Category`, or other
application concepts. Mu3D does not add an untyped string/object property bag whose cloning,
serialization, trimming, and lifetime rules would be ambiguous.

Declaring `SceneSelectionTool` is an explicit application choice, not global View behavior. Before
the proposal is committed, `SelectionRequested` exposes every ordered candidate and allows the
application to replace the proposed node or cancel it. This is where product filtering, grouping,
multi-selection, permission checks, or custom ranking belongs. `SelectionChanged` reports changes
from input, binding, or code. CPU raycasting follows current rigid transforms and morph position
weights, so shape animation updates geometric hit positions. It does not evaluate skin deformation
or material alpha masks; applications can replace the selection proposal or provide their own
hit source for those cases.

<xref:Mu3D.Maui.Toolkit.Controls.OrbitTool> creates an
<xref:Mu3D.Toolkit.Controls.OrbitController> for the referenced declarative camera only while the
view has an active Feature attachment. Its damping tail uses the existing MAUI VSync-aware input
adapter and stops while idle or detached; it does not add a timer.

<xref:Mu3D.Maui.Toolkit.Controls.TransformGizmoTool> creates the UI-independent gizmo, pointer
adapter, and HDR-linear overlay pass for the explicit `Target`. It never searches the scene or
decides what a click selects. A null target displays no gizmo, which is how a cleared selection is
represented. Translation and scale steps use scene/factor units; `RotationStep`
uses degrees for XAML. A zero step disables snapping. Mixed translate, rotate, and scale switches
remain independent, and `ScreenSize` is measured in physical pixels.

## Ownership and extension

The tools expose their current attachment-owned `Controller` or `Gizmo` for status and interaction
events. Those values become null after detach and must not be retained or disposed by the page. The
camera, target, scene, selection, undo records, and editor state always remain application-owned.
Applications that already own controllers and gizmos can continue using the lower-level
`OrbitSceneViewFeature` and `TransformGizmoSceneViewFeature` wrappers.

A downstream package can implement <xref:Mu3D.Maui.Toolkit.Controls.IViewportTool> and receive a
narrow <xref:Mu3D.Maui.Toolkit.Controls.ViewportToolContext>. The context exposes the current view,
scene, camera, renderer/session observations, shared arbiter, coalesced invalidation, and explicit
render-pass registration. Attachment still returns a deterministic lease. There is no reflection
discovery, global service locator, or ownership transfer.

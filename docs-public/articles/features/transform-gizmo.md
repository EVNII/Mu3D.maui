---
title: Transform gizmo
description: Compose mixed translate, rotate and scale handles over a Mu3D scene view.
feature_id: transform-gizmo
---

# Transform gizmo

The Gallery feature `transform-gizmo` composes a UI-independent
<xref:Mu3D.Toolkit.Gizmos.TransformGizmo> with a
<xref:Mu3D.Maui.Controls.Mu3DSceneView>. The application still owns target selection, undo/redo
records and tool policy. The gizmo changes only the explicitly assigned scene node.

## Configure mixed operations

The three operation switches are independent. Set all three to create a mixed gizmo, or enable only
the operations appropriate for the current application tool:

```csharp
var gizmo = new TransformGizmo(target)
{
    IsTranslateEnabled = true,
    IsRotateEnabled = true,
    IsScaleEnabled = true,
    ScreenSizePixels = 110f,
    TranslationSnap = 0.1f,
    RotationSnapRadians = MathF.PI / 12f,
    ScaleSnap = 0.1f,
};
```

`ScreenSizePixels` is a desired physical-pixel size, so the handle remains usable as the camera
moves. A snap value of zero disables that operation's snapping. Translation uses scene units,
rotation uses radians and scale uses a cumulative scale-factor interval around one.

<xref:Mu3D.Toolkit.Gizmos.TransformGizmo.Space> selects world or target-local orientation for
translation and rotation. Scale handles are always target-local because they edit the target's
local scale components; this prevents a visible world-axis handle from implying a shear that Mu3D's
TRS transform cannot represent.

## Attach through SceneView features

The Gallery declares two statically referenced features in XAML. They attach in collection order
when the scene view receives a native Handler and detach in reverse order when that Handler is
released:

```xaml
<mu3d:Mu3DSceneView x:Name="SceneView">
  <mu3d:Mu3DSceneView.Features>
    <toolkit:OrbitSceneViewFeature x:Name="OrbitFeature" />
    <toolkit:TransformGizmoSceneViewFeature x:Name="GizmoFeature" />
  </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

Assign application-owned controller state explicitly. A shared
<xref:Mu3D.Toolkit.Controls.ViewportControlArbiter> gives an accepted gizmo handle priority over
orbit while leaving an empty-space drag available for camera rotation:

```csharp
var controls = new ViewportControlArbiter();
OrbitFeature.Controller = orbitController;
OrbitFeature.ControlArbiter = controls;
GizmoFeature.Gizmo = gizmo;
GizmoFeature.ControlArbiter = controls;
```

<xref:Mu3D.Maui.Toolkit.Controls.TransformGizmoSceneViewFeature> installs the existing replaceable
<xref:Mu3D.Maui.Toolkit.Controls.TransformGizmoPointerBehavior> and registers its overlay pass after
the default scene pass. It hit-tests only the assigned gizmo. It does not select scene objects,
create an undo record or define stylus semantics. Subscribe to the gizmo's interaction events when
the host needs transaction boundaries: capture an undo baseline at `InteractionStarted`, update UI
during `InteractionChanged`, commit at `InteractionCompleted` and discard it at
`InteractionCanceled`.

Native input follows the handles' scene eligibility. The target must belong to the attached
SceneView's current scene, be visible through its ordinary ancestors and match the camera's layers.
Losing eligibility during a drag cancels the edit, restores its initial pose and releases pointer
capture and its control lease. Replacing the scene, camera or target, resizing the physical
viewport, or clearing the presented-frame boundary also cancels the old mapping. The borrowed
target remains assigned. After mutating scene visibility or hierarchy, call `InvalidateScene` so
the next successful frame also checks the interaction.

The Gallery's **SceneView Features attached** switch removes and restores the same XAML-created
objects. Turning it off removes Gizmo and then Orbit, releases the feature-owned pass, recognizers
and subscriptions, but preserves the page-owned scene, camera, controller, gizmo and transformed
target.

## Extend the lifecycle explicitly

A downstream package implements <xref:Mu3D.Maui.Controls.ISceneViewFeature> directly. Attach returns
one deterministic lease; the context also guarantees removal of every pass registration it issued:

```csharp
public sealed class OutlineFeature : ISceneViewFeature
{
    public IDisposable Attach(SceneViewFeatureContext context) =>
        context.RegisterRenderPass(
            renderer => new OutlineRenderPass(renderer),
            SceneViewRenderPassPlacement.AfterScene,
            order: 20);
}
```

Equal-order passes retain registration order. A pass factory creates a fresh pass for the current
renderer generation, and `Mu3DSceneView` disposes disposable factory results before releasing that
renderer. An explicitly assigned `RenderPipeline` remains application-owned and takes precedence;
features never rewrite it.

## Low-level ordered overlay

<xref:Mu3D.Toolkit.Rendering.TransformGizmoRenderPass> is an explicit HDR-linear render pass. Put it
after the scene pass manually when the application intentionally owns the complete pipeline:

```csharp
var scenePass = new SceneRenderPass(
    renderer,
    new SceneRenderPassOptions(GraphicsLoadOperation.Clear, sceneView.ClearColor));
var gizmoPass = new TransformGizmoRenderPass(
    gizmo,
    TransformGizmoRenderStyle.Default,
    workingColorSpace);

sceneView.RenderPipeline = new RenderPassPipeline([scenePass, gizmoPass]);
```

The default style uses <xref:Mu3D.Toolkit.Rendering.TransformGizmoDepthMode.Overlay>, so geometry
cannot hide the handles. Choose `SceneDepthTested` explicitly when occlusion is desired. Handle
colors remain tagged scene-linear values; the pass performs no display conversion or tone mapping.

The explicit render pipeline borrows its passes. Before disposing the gizmo render pass, remove or
replace the pipeline that references it. Changing page visibility alone does not transfer target,
controller or undo ownership to Mu3D.

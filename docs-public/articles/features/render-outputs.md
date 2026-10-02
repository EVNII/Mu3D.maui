---
title: Render outputs
description: Select built-in scene signals or register explicit AOT-safe output pass factories.
feature_id: render-outputs
---

# Render outputs

<xref:Mu3D.Rendering.RenderOutputId> identifies the signal requested from a render pass. It does not
select which scene nodes are visible and it does not determine where a pass runs in an ordered
pipeline. Keeping those concepts separate lets one camera visibility mask, one output choice and one
pass sequence evolve independently.

The default <xref:Mu3D.Maui.Controls.Mu3DSceneView> pipeline selects Beauty. Change it after the
scene or material state changes; the bindable property requests the replacement frame:

```csharp
SceneView.RenderOutput = RenderOutputIds.SurfaceNormal;
```

For the concise XAML Toolkit path, declare
<xref:Mu3D.Maui.Toolkit.Controls.RenderOutputToolbar> inside the view's
<xref:Mu3D.Maui.Toolkit.Controls.ViewportTools>. The value remains the same extensible identifier,
not a second closed enum:

```xaml
<mu3d:Mu3DSceneView
    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
    xmlns:mu3d="clr-namespace:Mu3D.Maui.Controls;assembly=Mu3D.Maui"
    xmlns:rendering="clr-namespace:Mu3D.Rendering;assembly=Mu3D.Core"
    xmlns:toolkit="clr-namespace:Mu3D.Maui.Toolkit.Controls;assembly=Mu3D.Maui.Toolkit">
    <mu3d:Mu3DSceneView.Features>
        <toolkit:ViewportTools>
            <toolkit:RenderOutputToolbar
                x:Name="OutputTool"
                Margin="14"
                MaximumWidth="620"
                Output="{x:Static rendering:RenderOutputIds.AmbientOcclusion}"
                Placement="TopLeft" />
        </toolkit:ViewportTools>
    </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

The control renders its built-in choices as a compact MAUI toolbar inside the viewport. It is not
painted into the HDR scene: MAUI owns its layout, scrolling, hit testing and accessibility, while
the underlying Mu3D Surface remains GPU-native. It is hosted by the same
<xref:Mu3D.Maui.Toolkit.Overlays.ViewportOverlay> system available to application-defined MAUI
content and scene anchors. `Placement` accepts all nine viewport alignments; `Margin` and
`MaximumWidth` control logical layout, and overflowing buttons scroll horizontally. The value is the same
<xref:Mu3D.Toolkit.Helpers.ViewportOverlayPlacement> used by `AxesHelper`. `IsVisible` hides only the
toolbar, while `IsInteractive` disables its buttons without changing the current output.

`Output` and `IsEnabled` remain bindable. Disabling or detaching the tool restores the value it
found at attachment only if the view still contains the tool's last assignment. An application
that subsequently assigns the view directly therefore keeps ownership. Built-in buttons may be
removed with `IncludeDefaultOutputs="False"`; nested
<xref:Mu3D.Maui.Toolkit.Controls.RenderOutputOption> elements add registered third-party outputs.

<xref:Mu3D.Rendering.RenderOutputIds> includes Beauty, ambient occlusion, direct lighting,
image-based lighting, view depth, shadow visibility, normals, material channels and the supported
PBR lobes. These are stable identifiers rather than integer enum positions. The older
<xref:Mu3D.Rendering.SceneRenderLayer> property remains a compatibility API for code that invokes
<xref:Mu3D.Rendering.SceneRenderer> directly.

## Explicit extension registration

Each scene view owns a <xref:Mu3D.Rendering.RenderOutputRegistry> preloaded with Mu3D's built-in
factories. A downstream library can register a stable namespaced ID and a normal delegate. There is
no assembly scan, reflection-discovered plugin or runtime-loaded executable:

```csharp
RenderOutputId outline = new("com.example.outline");

SceneView.RenderOutputRegistry.Register(
    outline,
    static (renderer, options, name) =>
        new MyOutlineRenderPass(renderer, options, name));

OutputTool.Output = outline;
OutputTool.Items.Add(new RenderOutputOption
{
    Label = "Outline",
    Description = "Application outline pass",
    Output = outline,
});
```

The factory returns an <xref:Mu3D.Rendering.IRenderPass>, so a custom output may use its own
resources and shaders. `RenderOutputRegistry.CreatePass` gives ownership to its caller;
`Mu3DSceneView` owns factory-created default passes and disposes them when they implement
`IDisposable`. Register or replace factories on the UI/render coordination thread before requesting
the next frame.

For built-in output-specific composition, use
<xref:Mu3D.Rendering.SceneRenderOutputPass>. Its output is immutable and does not mutate the
renderer-wide compatibility selection, so separate passes cannot accidentally overwrite one
another's requested signal.

The Gallery page includes `gallery.normal-alias`, an application-owned ID whose explicit factory
creates the built-in surface-normal pass. It is intentionally simple: selecting it verifies the
extension route before an external package supplies a custom shader implementation.

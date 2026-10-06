---
title: Frame statistics
description: Collect immutable frame samples and show a throttled rolling diagnostics overlay.
feature_id: frame-statistics
---

# Frame statistics

<xref:Mu3D.Toolkit.Diagnostics.FrameStatisticsCollector> is UI-independent. It accepts completed
frame timing/resource data and publishes immutable
<xref:Mu3D.Toolkit.Diagnostics.FrameStatisticsSnapshot> values at a caller-selected cadence.

<xref:Mu3D.Maui.Toolkit.Diagnostics.FrameStatisticsOverlay> is the concise XAML path. It creates an
attachment-owned <xref:Mu3D.Maui.Toolkit.Diagnostics.FrameStatisticsBehavior> and
<xref:Mu3D.Maui.Toolkit.Diagnostics.FrameStatisticsView>, then mounts the clickable panel through
the viewport's common overlay manager:

```xaml
<mu3d:Mu3DSceneView>
  <mu3d:Mu3DSceneView.Features>
    <toolkit:ViewportTools>
      <diagnostics:FrameStatisticsOverlay
          x:Name="Statistics"
          Placement="TopRight"
          SnapshotInterval="0:0:0.1"
          DisplayMode="Compact"
          IsGraphVisible="True" />
    </toolkit:ViewportTools>
  </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

The overlay exposes its application-visible `Collector`, latest snapshot, reset/immediate-publish
methods and optional application-known draw/primitive counts. Visibility controls only the panel;
`IsEnabled` independently controls collection. Tapping the panel cycles **Compact → Normal → Detail →
Compact**. Compact is a small box showing only FPS; Normal shows a short summary and the optional
graph; Detail adds frame, presentation, renderer and resource breakdowns. The panel handles input
within its bounds; the surrounding viewport remains available for camera, selection and Gizmo input.

The backend-independent <xref:Mu3D.Toolkit.Diagnostics.FrameStatisticsDisplayMode> selects the mode.
Library controls default to Normal; Gallery indicators start in Compact. Switching modes preserves
collected samples and graph history. Compact hides the graph without changing `IsGraphVisible`.
`DisplayMode` supports two-way binding so tapping also updates the application's selection.
The legacy `IsDetailed` selector remains available: setting true selects Detail and setting false
selects Normal. Its default binding mode is now two-way. Bind one selector, preferably `DisplayMode`.
On MAUI, assigning the CLR alias selects a mode even if the bool is unchanged; an unchanged
`SetValue(IsDetailedProperty, ...)` does not trigger a mode change.

Web's `createFrameStatisticsView` and `createFrameStatisticsOverlay` use the same cycle with
`displayMode: 'compact' | 'normal' | 'detail'`. The focused panel also accepts Enter or Space.
`isDetailed` remains the corresponding compatibility selector.

Applications that place diagnostics outside the viewport may still attach
<xref:Mu3D.Maui.Toolkit.Diagnostics.FrameStatisticsBehavior> directly and let a standalone
<xref:Mu3D.Maui.Toolkit.Diagnostics.FrameStatisticsView> borrow it.

## Scheduling

The Gallery's continuous mode uses MAUI's shared platform VSync animation service only while the
page is visible and the renderer has a non-placeholder physical extent. Presentation and diagnostic
snapshot cadence are separate: a 100 ms text/graph refresh does not cap rendering at 10 FPS.

The overlay, behavior and view do not create a generic timer-based render mode and do not own the
scene or renderer. Stop/remove the application-owned host animation on hide or renderer loss. The
overlay's normal `ViewportTools` attachment removes its behavior/view subscriptions on feature
detachment; standalone behavior/view instances remain the application's disposal responsibility.

The displayed acquire/render/present intervals are host wall-clock diagnostics. They are not GPU
timestamp queries; use a platform GPU profiler when command execution timing matters.

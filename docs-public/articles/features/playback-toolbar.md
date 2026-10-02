---
title: Playback toolbar and IPlayable
description: Connect one reusable MAUI viewport transport bar to a glTF clip, camera path, MAUI bridge, or custom timeline.
feature_id: playback-toolbar
---

# Playback toolbar and IPlayable

`PlaybackToolbar` is a Toolkit-provided MAUI control built on the common `ViewportOverlay` host.
It displays Start, Play/Pause, a seek timeline, current/total time and an optional Loop toggle. The
bar stays in the MAUI visual tree for layout, input, accessibility and styling while the 3D image
remains HDR-linear GPU output.

The bar does not create a timer or assume a Mu3D animation type. It operates one application-owned
`Mu3D.Toolkit.Animation.IPlayable`:

```csharp
public sealed class ProductPlayer : IPlayable
{
    public event EventHandler? PlaybackChanged;

    public PlaybackState State { get; private set; }
    public TimeSpan Position { get; private set; }
    public TimeSpan Duration { get; }
    public bool CanSeek => true;
    public bool IsLooping { get; set; }

    public void Play() { /* start the application's clock */ }
    public void Pause() { /* pause without changing Position */ }
    public void Stop() { /* stop according to application policy */ }
    public void Seek(TimeSpan position) { /* evaluate the source */ }
}
```

Raise `PlaybackChanged` whenever state, time, capabilities or looping changes. Notifications may
come from any thread; the MAUI adapter dispatches its visual refresh. The implementation retains
ownership of scheduling, clip evaluation and disposal.

## Declare the transport in XAML

Place the toolbar beside the feature that applies animation values. `Playable` can come from a page
property or view model:

```xaml
<mu3d:Mu3DSceneView>
  <mu3d:Mu3DSceneView.Features>
    <toolkit:ViewportTools>
      <toolkit:ProgressTool x:Name="SceneProgress">
        <!-- Application-selected mappings -->
      </toolkit:ProgressTool>
      <toolkit:PlaybackToolbar
          Playable="{Binding Player}"
          Placement="BottomCenter"
          MaximumWidth="680"
          ShowLoop="True"
          ShowTime="True" />
    </toolkit:ViewportTools>
  </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

`IsVisible` controls mounting, while `IsEnabled` and `IsInteractive` control operation and pointer
participation. A noninteractive bar becomes pass-through, so it cannot block Orbit, selection or
Gizmo input beneath it. Narrow viewports horizontally scroll the transport content instead of
stretching the overlay.

## Adapt different sources

- A glTF player evaluates its selected `AnimationClip` from `Position` and advances with the
  viewport's suspend-aware frame clock.
- A camera-path player maps `Position / Duration` through `ProgressTool` or a custom
  `IViewportProgressMapping`.
- A MAUI animation adapter owns one `IAnimationManager` animation and publishes its current time.
- A downstream timeline can prepare or stream data, report `Buffering`, and temporarily disable
  seeking through `CanSeek`.

`IPlayable` is intentionally UI- and format-independent. It does not put MAUI into Core or make the
toolbar the owner of glTF assets, page navigation, playback policy or native resources.

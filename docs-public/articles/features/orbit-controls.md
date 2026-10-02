---
title: Target-centered orbit controls
description: Rotate around an explicit target, screen-pan, and dolly through a declarative OrbitTool.
feature_id: orbit-controls
---

# Target-centered orbit controls

<xref:Mu3D.Maui.Toolkit.Controls.OrbitTool> is the general target-centered camera-navigation tool.
It creates an attachment-owned <xref:Mu3D.Toolkit.Controls.OrbitController> for one explicit
perspective camera. Rotation moves the camera around a world-space target, screen-space pan moves
the camera and target together, and dolly changes their distance without changing field of view.

This is the ordinary model-viewer mode. <xref:Mu3D.Maui.Toolkit.Controls.MapTool> specializes it
with world-up-plane pan and an upper-hemisphere constraint; Fly controls have no orbit target and
change a free camera pose directly.

## Declare the tool

Place `OrbitTool` in the same `ViewportTools` collection as the camera it borrows:

```xaml
<mu3d:Mu3DSceneView>
  <mu3d:Mu3DSceneView.SceneContent>
    <mu3d:Scene3D>
      <mu3d:Scene3D.Camera>
        <mu3d:PerspectiveCamera3D
            x:Name="OrbitCamera"
            X="5.2"
            Y="3.8"
            Z="7.6" />
      </mu3d:Scene3D.Camera>
      <!-- Scene objects... -->
    </mu3d:Scene3D>
  </mu3d:Mu3DSceneView.SceneContent>

  <mu3d:Mu3DSceneView.Features>
    <toolkit:ViewportTools>
      <toolkit:OrbitTool
          x:Name="OrbitNavigation"
          Camera="{x:Reference OrbitCamera}"
          TargetY="0.5"
          PinchDollyMultiplier="1.6"
          MinimumDistance="2.5"
          MaximumDistance="24"
          DampingEnabled="True"
          DampingTime="0.1">
        <toolkit:OrbitTool.Input>
          <toolkit:ViewportInput>
            <toolkit:ViewportInput.Mouse>
              <toolkit:MouseInput
                  LeftButtonDragAction="Rotate"
                  RightButtonDragAction="Pan" />
            </toolkit:ViewportInput.Mouse>
            <toolkit:ViewportInput.Trackpad>
              <toolkit:TrackpadInput TwoFingerDragAction="Pan" />
            </toolkit:ViewportInput.Trackpad>
            <toolkit:ViewportInput.Touchscreen>
              <toolkit:TouchscreenInput
                  OneFingerDragAction="Rotate"
                  TwoFingerDragAction="Pan" />
            </toolkit:ViewportInput.Touchscreen>
            <toolkit:ViewportInput.Keyboard>
              <toolkit:KeyboardInput IsEnabled="False" />
            </toolkit:ViewportInput.Keyboard>
          </toolkit:ViewportInput>
        </toolkit:OrbitTool.Input>
      </toolkit:OrbitTool>
    </toolkit:ViewportTools>
  </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

`TargetX`, `TargetY`, and `TargetZ` define the point that rotation and distance use. The camera and
optional frame requester remain borrowed; detaching the tool disposes its controller and input
adapters without taking ownership of the camera.

## Pointer and touch mapping

Input is divided by physical device rather than by ambiguous primary and secondary routes:

- `MouseInput` names left, middle and right button drag actions plus the wheel action.
- `TrackpadInput` controls native continuous two-finger translation and trackpad Pinch.
- `TouchscreenInput` controls one-finger drag, two-finger centroid drag and direct Pinch.
- `KeyboardInput` owns the optional focused-key adapter and portable Rotate/Pan/Dolly assignments.

Each device object has its own bindable `IsEnabled` switch. A drag action may be `Rotate`, `Pan`,
or `None`; wheel and Pinch use `Dolly` or `None`. The default Orbit profile is left mouse and
one-finger Rotate, right mouse, direct two-finger and trackpad two-finger Pan, plus Pinch/wheel
Dolly. Orbit, Map and Fly consume this same `ViewportInput` shape; only the attached controller's
interpretation changes.

`IsEnabled` is a bindable gate for the whole device group. It can bind to a stable view-model
property such as `IsAltOrOptionPressed`; XAML binding syntax does not evaluate a C# expression such
as `IsAltPressed || IsOptionPressed`, so expose one computed property or converter when the host
owns that state. This gate does not create process-global modifier tracking inside Mu3D. Input
actions are captured when a drag begins, and changing a group ends the active route before the
native recognizers are refreshed.

Two-pointer pan and pinch share one camera-control lease, so adding a second finger does not cancel
the navigation session. Android and Windows use MAUI Pinch. Apple uses an attachment-owned native
`UIPinchGestureRecognizer` for direct iPad touch and Mac trackpad transform Pinch and allows it to
overlap the two-pointer Pan. Apple applications that need indirect trackpad transform events declare
[`UIApplicationSupportsIndirectInputEvents`](https://developer.apple.com/documentation/bundleresources/information-property-list/uiapplicationsupportsindirectinputevents)
in Info.plist; Gallery declares it for iOS and Mac Catalyst.

On Apple trackpads, continuous two-finger translation uses
`TrackpadInput.TwoFingerDragAction`; it does not impersonate a right mouse button. A real mouse or a
configured trackpad right click instead uses `MouseInput.RightButtonDragAction`.
`TouchscreenInput.TwoFingerDragAction` remains independent and defaults to `Pan` for Orbit; MapTool
changes it to `Rotate` while keeping direct one-finger drag as world-plane Pan.

Apple system-navigation arbitration follows the configured device route. On iOS, direct touch
inside the View suppresses interactive pop only when
`TouchscreenInput.OneFingerDragAction` is not `None`; on Mac Catalyst, trackpad navigation is
suppressed only while the cursor is over the View and `TrackpadInput.TwoFingerDragAction` is not
`None`. Disabling the relevant route restores system navigation, and the normal Back control
remains available. Android instead retains its reported system-edge inset and lets a back-edge
gesture win without also moving the camera.

`RotationSensitivity`, `PanSensitivity`, and `DollySensitivity` scale the controller commands.
`MinimumDistance`, `MaximumDistance`, `MinimumPolarAngle`, and `MaximumPolarAngle` constrain the
pose at the UI-independent controller level. `PanMode` selects camera screen-plane or fixed
world-up-plane translation. MapTool is only a named fixed combination of these Orbit parameters and
input mappings; other DCC-style presets should reuse the same controller and provide different
binding profiles rather than duplicating camera math. Damping consumes accepted residual motion
only while it is active and uses the existing MAUI frame clock rather than creating an idle timer.

## Bind commands without viewport focus

`NavigationCommand` accepts an
<xref:Mu3D.Maui.Toolkit.Controls.OrbitNavigationAction>. Any normal MAUI button, menu item, gesture,
or view model can issue the same discrete action:

```xaml
<Button
    Text="Rotate left"
    Command="{Binding Source={x:Reference OrbitNavigation}, Path=NavigationCommand}"
    CommandParameter="{x:Static toolkit:OrbitNavigationAction.RotateLeft}" />
```

Applications that already own held-key, gamepad, or other continuous state can call the atomic
`TryNavigate(rotationRadians, panViewportDelta, dollyDelta)` boundary once per UI frame. Normalize
simultaneous axes and scale them by elapsed time; do not depend on operating-system key repeat.

The UI-independent controller acquires no mouse, touch, pen, key, focus, selection, undo state, or
timer. Raw acquisition and application policy stay replaceable, while the MAUI tool supplies the
declarative default adapter.

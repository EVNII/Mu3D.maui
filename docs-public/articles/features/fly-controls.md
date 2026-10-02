---
title: First-person fly controls
description: Use the shared device input model to move and look through a first-person camera.
feature_id: fly-controls
---

# First-person fly controls

<xref:Mu3D.Toolkit.Controls.FlyController> controls one borrowed perspective camera without an
orbit target. Movement uses world units: local X strafes, local Y follows world up, and local Z
moves along the current forward direction. Look commands change yaw around world up and clamp pitch
away from the poles.

The UI-independent controller implements
<xref:Mu3D.Toolkit.Controls.IViewportNavigationController>. The shared actions are interpreted as:

- `Rotate` changes yaw and pitch.
- `Pan` strafes and lifts.
- `Dolly` moves forward and backward without changing field of view.

The controller acquires no keys, pointers, focus or timer. A host can still call `Move`, `Look`,
atomic `Navigate` and `SynchronizeFromCamera` directly.

## Declare the MAUI tool

<xref:Mu3D.Maui.Toolkit.Controls.FlyTool> uses the same
<xref:Mu3D.Maui.Toolkit.Controls.ViewportInput> object as Orbit and Map. The object is divided by
physical device; it is not divided by camera-controller type:

```xaml
<mu3d:Mu3DSceneView>
  <mu3d:Mu3DSceneView.SceneContent>
    <mu3d:Scene3D>
      <mu3d:Scene3D.Camera>
        <mu3d:PerspectiveCamera3D x:Name="FlyCamera" Y="2" Z="8" />
      </mu3d:Scene3D.Camera>
      <!-- Scene objects... -->
    </mu3d:Scene3D>
  </mu3d:Mu3DSceneView.SceneContent>

  <mu3d:Mu3DSceneView.Features>
    <toolkit:ViewportTools>
      <toolkit:FlyTool
          x:Name="FlyNavigation"
          Camera="{x:Reference FlyCamera}"
          DampingEnabled="True"
          DampingTime="0.1"
          RotationRadiansPerViewport="3.1415927"
          PanUnitsPerViewport="4"
          PinchDollyMultiplier="6"
          WheelDollyStep="0.75"
          MovementStep="0.75"
          LookStepRadians="0.08726646">
        <toolkit:FlyTool.Input>
          <toolkit:ViewportInput>
            <toolkit:ViewportInput.Mouse>
              <toolkit:MouseInput
                  LeftButtonDragAction="Rotate"
                  RightButtonDragAction="Pan"
                  WheelAction="Dolly" />
            </toolkit:ViewportInput.Mouse>
            <toolkit:ViewportInput.Trackpad>
              <toolkit:TrackpadInput
                  TwoFingerDragAction="Pan"
                  PinchAction="Dolly" />
            </toolkit:ViewportInput.Trackpad>
            <toolkit:ViewportInput.Touchscreen>
              <toolkit:TouchscreenInput
                  OneFingerDragAction="Rotate"
                  TwoFingerDragAction="Pan"
                  PinchAction="Dolly" />
            </toolkit:ViewportInput.Touchscreen>
            <toolkit:ViewportInput.Keyboard>
              <toolkit:KeyboardInput
                  RotateLeftKey="LeftArrow"
                  RotateRightKey="RightArrow"
                  RotateUpKey="UpArrow"
                  RotateDownKey="DownArrow"
                  PanLeftKey="A"
                  PanRightKey="D"
                  PanUpKey="E"
                  PanDownKey="Q"
                  DollyInKey="W"
                  DollyOutKey="S" />
            </toolkit:ViewportInput.Keyboard>
          </toolkit:ViewportInput>
        </toolkit:FlyTool.Input>
      </toolkit:FlyTool>
    </toolkit:ViewportTools>
  </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

The nested input is optional because `FlyTool` constructs this first-person profile by default.
Declare it when bindings come from a view model or when a host needs a different route. Every
device group has `IsEnabled`; every drag or scalar route can be set to `None`. A disabled route is
not consumed, so platform navigation arbitration also leaves that gesture available to the host.

Look and translation use the viewport's shorter side as a common normalization length.
`RotationRadiansPerViewport`, `PanUnitsPerViewport`, `PinchDollyMultiplier` and `WheelDollyStep`
control acquisition scale. `RotationSensitivity`, `PanSensitivity` and `DollySensitivity` are the
controller-level multipliers for the same three normalized actions. Fly applies Pan sensitivity to
local X/Y strafe and lift, Dolly sensitivity to local Z forward/back movement, and Rotation
sensitivity to yaw/pitch. The split also applies to direct `Move`, `Navigate` and
`FlyNavigationAction` commands. `MovementStep` and `LookStepRadians` control discrete commands and
the optional focused-key adapter.

Android and Windows use MAUI Pinch. Apple uses an attachment-owned native
`UIPinchGestureRecognizer` for direct iPad touch and Mac trackpad transform Pinch, while continuous
trackpad two-finger translation uses `TrackpadInput.TwoFingerDragAction`. Applications that need
indirect Apple trackpad events declare
[`UIApplicationSupportsIndirectInputEvents`](https://developer.apple.com/documentation/bundleresources/information-property-list/uiapplicationsupportsindirectinputevents)
in Info.plist.

Apple system-navigation arbitration follows the configured route. Direct iOS navigation is
suppressed only when `TouchscreenInput.OneFingerDragAction` is not `None`; Mac trackpad navigation
is suppressed only while the pointer is over the view and `TrackpadInput.TwoFingerDragAction` is
not `None`. Android retains its platform-reported system back-edge inset.

## Bind application input

`NavigationCommand` accepts a
<xref:Mu3D.Maui.Toolkit.Controls.FlyNavigationAction> for ordinary MAUI buttons and menu items:

```xaml
<Button
    Text="Forward"
    Command="{Binding Source={x:Reference FlyNavigation}, Path=NavigationCommand}"
    CommandParameter="{x:Static toolkit:FlyNavigationAction.MoveForward}" />
```

Do not use OS keyboard repeat for continuous motion. An application that owns held KeyDown/KeyUp
state should read its assignments from `FlyTool.Input.Keyboard`, normalize simultaneous axes,
scale by elapsed time and call
`TryNavigate(Vector3 localMovement, Vector2 lookDeltaRadians)` once per UI frame. Set
`Input.Keyboard.IsEnabled="False"` when that host-owned session is active so the optional focused
adapter does not submit the same key twice.

With damping disabled, `FlyController.Navigate` applies orientation and movement atomically. With
damping enabled, the shared MAUI navigation behavior consumes pending motion on its active-only
VSync chain and owns no idle timer.

Fly and Map remain different camera modes even though input acquisition is shared. Map retains an
orbit target and fixed pan plane; Fly changes a free camera pose directly.

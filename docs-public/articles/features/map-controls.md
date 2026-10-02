---
title: Map and CAD camera controls
description: Add world-plane pan, zoom and device-separated rotation mappings through XAML.
feature_id: map-controls
---

# Map and CAD camera controls

<xref:Mu3D.Maui.Toolkit.Controls.MapTool> is the declarative navigation preset for maps, floor
plans and CAD-like scenes. Its left-button or direct one-finger drag translates both the camera and target on
the plane perpendicular to world up. With the default Y-up convention, the target therefore moves
on XZ and never drifts vertically. Two-pointer centroid drag rotates around the retained target,
while pinch dollies logarithmically. Supported discrete pointer wheels also dolly. Native
Right-button drag uses the same rotation route on Windows, Apple and Android pointer devices.
On a Mac trackpad, continuous two-finger translation has its own Map rotation route; a real mouse
or configured right click uses the separate right-button route.
MAUI commands supply focus-independent discrete navigation.

## Declare the tool

Reference the same declarative camera used by the scene:

```xaml
<mu3d:Mu3DSceneView>
  <mu3d:Mu3DSceneView.SceneContent>
    <mu3d:Scene3D>
      <mu3d:Scene3D.Camera>
        <mu3d:PerspectiveCamera3D
            x:Name="MapCamera"
            X="7" Y="7" Z="7" />
      </mu3d:Scene3D.Camera>

      <!-- Scene objects... -->
    </mu3d:Scene3D>
  </mu3d:Mu3DSceneView.SceneContent>

  <mu3d:Mu3DSceneView.Features>
    <toolkit:ViewportTools>
      <toolkit:MapTool
          x:Name="MapNavigation"
          Camera="{x:Reference MapCamera}"
          MinimumDistance="2.5"
          MaximumDistance="40"
          DampingEnabled="True"
          DampingTime="0.1"
          PinchDollyMultiplier="1.6">
        <toolkit:OrbitTool.Input>
          <toolkit:ViewportInput>
            <toolkit:ViewportInput.Mouse>
              <toolkit:MouseInput
                  LeftButtonDragAction="Pan"
                  RightButtonDragAction="Rotate" />
            </toolkit:ViewportInput.Mouse>
            <toolkit:ViewportInput.Trackpad>
              <toolkit:TrackpadInput TwoFingerDragAction="Rotate" />
            </toolkit:ViewportInput.Trackpad>
            <toolkit:ViewportInput.Touchscreen>
              <toolkit:TouchscreenInput
                  OneFingerDragAction="Pan"
                  TwoFingerDragAction="Rotate" />
            </toolkit:ViewportInput.Touchscreen>
            <toolkit:ViewportInput.Keyboard>
              <toolkit:KeyboardInput
                  IsEnabled="False"
                  RotateLeftKey="A"
                  RotateRightKey="D"
                  RotateUpKey="W"
                  RotateDownKey="S"
                  DollyInKey="E"
                  DollyOutKey="Q"
                  AlternateDollyInKey="PageUp"
                  AlternateDollyOutKey="PageDown" />
            </toolkit:ViewportInput.Keyboard>
          </toolkit:ViewportInput>
        </toolkit:OrbitTool.Input>
      </toolkit:MapTool>
    </toolkit:ViewportTools>
  </mu3d:Mu3DSceneView.Features>
</mu3d:Mu3DSceneView>
```

The tool is a fixed declarative preset over <xref:Mu3D.Maui.Toolkit.Controls.OrbitTool>. It selects
<xref:Mu3D.Toolkit.Controls.OrbitPanMode.WorldUpPlane>, sets an upper-hemisphere maximum polar
angle, and changes the mouse, trackpad and touchscreen mappings. It still creates and configures the
same attachment-owned <xref:Mu3D.Toolkit.Controls.OrbitController> as ordinary OrbitTool; there is
no separate Map gesture or controller pipeline. Direct non-MAUI callers may still construct
<xref:Mu3D.Toolkit.Controls.MapController> as the equivalent low-level convenience preset.

## Bind navigation through MAUI

`NavigationCommand` is the preferred application-composition boundary. It accepts an
<xref:Mu3D.Maui.Toolkit.Controls.OrbitNavigationAction> parameter, so ordinary MAUI buttons,
menus and gestures can invoke map movement without transferring keyboard focus to the native 3D
surface:

```xaml
<Button
    Text="Pan left"
    Command="{Binding Source={x:Reference MapNavigation}, Path=NavigationCommand}"
    CommandParameter="{x:Static toolkit:OrbitNavigationAction.PanLeft}" />
```

Desktop keyboard accelerators are appropriate for single discrete commands. A `MenuFlyoutItem`
can invoke the same command as a click:

```xaml
<ContentPage.MenuBarItems>
  <MenuBarItem Text="Map navigation">
    <MenuFlyoutItem
        Text="Pan left"
        Command="{Binding Source={x:Reference MapNavigation}, Path=NavigationCommand}"
        CommandParameter="{x:Static toolkit:OrbitNavigationAction.PanLeft}">
      <MenuFlyoutItem.KeyboardAccelerators>
        <KeyboardAccelerator Key="A" />
      </MenuFlyoutItem.KeyboardAccelerators>
    </MenuFlyoutItem>
  </MenuBarItem>
</ContentPage.MenuBarItems>
```

This is normal MAUI commanding rather than a Mu3D-specific `OnKeyClick` event. It remains usable
from MVVM and `TryNavigate(action)` exposes the same discrete boundary to imperative application
code. Rotation, viewport-relative pan and dolly increments are independently configurable through
`NavigationRotationStepRadians`, `NavigationPanStep` and `NavigationDollyStep`.

## Held keys and composite motion

A keyboard accelerator is an invocation, not a pressed-key state source. Do not use operating-
system key repeat for continuous movement: it cannot represent A+W atomically and its initial
repeat delay produces a visible pause. An application that needs held chords owns KeyDown/KeyUp
state and advances it from its UI frame clock. Submit the normalized combined delta once per frame:

```csharp
Vector2 rotationAxis = GetPressedRotationAxis();
if (rotationAxis.LengthSquared() > 1f)
{
    rotationAxis = Vector2.Normalize(rotationAxis);
}

mapTool.TryNavigate(
    rotationAxis * radiansPerSecond * elapsedSeconds,
    panAxis * viewportFractionPerSecond * elapsedSeconds,
    dollyAxis * logarithmicDollyPerSecond * elapsedSeconds);
```

The three-argument `TryNavigate` overload validates finite input and applies rotate, pan and dolly
under one camera-priority arbitration lease. Opposite keys cancel; diagonal input should be
normalized so it is not faster than one-axis input. Clamp unusually large elapsed-time values to
avoid a camera jump after a debugger pause or UI stall.

The Gallery's Windows example installs routed KeyDown and KeyUp handlers only while the Map page is
visible, applies the first press immediately, and then uses MAUI's VSync animation manager instead
of OS repeat. It clears every held key on release, page departure and window deactivation. The
Picker values remain live: changing a key updates both the held-key resolver and the menu hint.
Other hosts may acquire equivalent state from their platform or input abstraction and call the
same backend-neutral overload; raw input ownership does not move into Mu3D.

Set `KeyboardInput.IsEnabled="False"` when the application owns shortcut routing this way. The
focused native-key adapter remains an optional convenience for a standalone viewport and does not
affect `NavigationCommand`.

See [.NET MAUI commanding](https://learn.microsoft.com/dotnet/maui/fundamentals/data-binding/commanding)
and [.NET MAUI keyboard accelerators](https://learn.microsoft.com/dotnet/maui/user-interface/keyboard-accelerators)
for the host UI contracts.

## Input and ownership

Direct one-finger drag, configurable direct two-finger centroid drag and pinch are the portable
touchscreen baseline across Android, iOS, Mac Catalyst and Windows. MapTool defaults the
two-finger route to rotation while ordinary OrbitTool defaults it to screen-space pan. Drag and
Pinch retain independent cumulative
state under one camera-control lease, so rotation and dolly can overlap without canceling each
other. Pointer wheel and an optional focused hardware-key adapter are also available on supported
targets. Right-button drag accepts native mouse-compatible input on Windows, Apple and Android;
query `MapTool.Behavior.IsMouseButtonInputAvailable` before advertising it in adaptive UI.

Android and Windows use MAUI Pinch. Apple uses an attachment-owned native
`UIPinchGestureRecognizer` for direct iPad touch and Mac trackpad transform Pinch, with a local
delegate that permits overlap with the two-pointer Pan without mutating application-global gesture
policy. Apple applications that need indirect trackpad transform events declare
[`UIApplicationSupportsIndirectInputEvents`](https://developer.apple.com/documentation/bundleresources/information-property-list/uiapplicationsupportsindirectinputevents)
in Info.plist; Gallery declares it for iOS and Mac Catalyst.

`MouseInput.LeftButtonDragAction`, `MiddleButtonDragAction` and `RightButtonDragAction` configure
mouse-compatible button routes. `TrackpadInput.TwoFingerDragAction` configures native continuous
trackpad translation, while `TouchscreenInput.OneFingerDragAction` and `TwoFingerDragAction`
configure direct touch. Any drag route may select Pan, Rotate or `None`. Trackpad and touchscreen
Pinch actions are independent; `PinchDollyMultiplier` scales their shared response, while
`MouseInput.WheelAction` independently selects Dolly or `None`. `KeyboardInput` holds Rotate, Pan
and Dolly properties using the portable <xref:Mu3D.Maui.Toolkit.Controls.ViewportKey> enum directly
in XAML. Assigning `None` disables an individual key without introducing Windows, Android or Apple
native key types into shared XAML.

Because these are ordinary bindable properties, a MAUI menu can change them without rebuilding the
tool or scene. For the optional focused-key adapter, bind the tool and a `Picker` to the same
view-model property:

```xaml
<toolkit:MapTool Camera="{x:Reference MapCamera}">
  <toolkit:OrbitTool.Input>
    <toolkit:ViewportInput>
      <toolkit:ViewportInput.Keyboard>
        <toolkit:KeyboardInput RotateLeftKey="{Binding RotateLeftKey}" />
      </toolkit:ViewportInput.Keyboard>
    </toolkit:ViewportInput>
  </toolkit:OrbitTool.Input>
</toolkit:MapTool>

<Picker
    ItemsSource="{Binding AvailableOrbitKeys}"
    SelectedItem="{Binding RotateLeftKey, Mode=TwoWay}" />
```

When MAUI keyboard accelerators are used for discrete actions, update the matching
`MenuFlyoutItem.KeyboardAccelerators` collection from the same selected value. For continuous
chords, update the application-owned pressed-key resolver instead and keep the menu item as a
discrete click command. If an application enables the focused native adapter, the viewport must
own focus; on Windows that adapter accepts both WinUI's mapped key and its original physical-key
fallback for configured A-Z controls.

Every gesture and navigation command participates in the attachment's
<xref:Mu3D.Toolkit.Controls.ViewportControlArbiter>, and the damping tail runs only on the shared
MAUI VSync ticker while motion is active. Detach, unload, disable and handler replacement remove
the platform subscriptions and release any active control lease. The camera and scene remain
application-owned, and MapTool does not select objects or create a process-wide input policy.

Apple system-navigation arbitration follows the configured input route rather than the mere
presence of a SceneView. On iOS, a direct touch inside the View suppresses interactive pop only
while `TouchscreenInput.OneFingerDragAction` is not `None`. On Mac Catalyst, system trackpad
navigation is suppressed only while the cursor is over the View and
`TrackpadInput.TwoFingerDragAction` is not `None`. Setting the relevant route to `None` restores
system navigation, and the normal Back control remains application UI.

Applications that acquire input themselves can construct `MapController` directly and feed its
normalized `Pan`, `Rotate`, `Dolly` and `Update` methods without referencing MAUI.

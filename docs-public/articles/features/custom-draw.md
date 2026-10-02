---
title: Custom draw
description: Encode a low-level HDR frame through Mu3DView's automatic presentation boundary.
feature_id: custom-draw
---

# Custom draw

Use <xref:Mu3D.Maui.Controls.Mu3DView> when the application needs unrestricted command encoding
instead of a scene renderer. The control owns the default presentation session and raises a Draw
event with <xref:Mu3D.Maui.Controls.SurfaceDrawEventArgs>.

## XAML host

Declare the control and its lifecycle events in XAML; keep GPU command encoding in the event
handler so the page does not create or present a second surface session.

```xaml
<mu3d:Mu3DView
    x:Name="SurfaceView"
    BackgroundColor="Black"
    Draw="OnSurfaceDraw"
    FramePresented="OnFramePresented"
    PresentationSessionChanged="OnPresentationSessionChanged"
    SurfaceError="OnSurfaceError" />
```

```csharp
private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
{
    using GraphicsCommandEncoder encoder = e.Device.CreateCommandEncoder("Frame");
    using GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(renderPassDescriptor);
    pass.SetPipeline(pipeline);
    pass.Draw(3);
    using GraphicsCommandBuffer commands = encoder.Finish();
    e.Device.Queue.Submit(commands);
}
```

Create device-dependent pipelines when the presentation session appears and release them when it
changes to null. The view presents after a successful callback; application code must not acquire or
present the surface a second time.

The first ready surface and resize request a frame automatically. Call `InvalidateSurface()` after
application state changes. One invalidation is coalesced with pending work; it is not a continuous
render-loop contract.

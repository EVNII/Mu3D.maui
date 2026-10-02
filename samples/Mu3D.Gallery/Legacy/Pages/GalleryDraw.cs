using Mu3D.Graphics;
using Mu3D.Maui.Controls;

namespace Mu3D.GalleryApp.Pages;

internal static class GalleryDraw
{
    internal static void Clear(SurfaceDrawEventArgs frame, string label)
    {
        using GraphicsCommandEncoder encoder = frame.Device.CreateCommandEncoder(label);
        using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
            new GraphicsRenderPassColorAttachment(
                frame.Target,
                clearColor: new GraphicsClearColor(0, 0, 0, 1)),
            label)))
        {
        }
        using GraphicsCommandBuffer commands = encoder.Finish();
        frame.Device.Queue.Submit(commands);
    }
}

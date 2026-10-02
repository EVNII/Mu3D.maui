using Mu3D.Toolkit.Viewports;

namespace Mu3D.Maui.Toolkit.Controls;

internal sealed class ViewportToolFrameRequester(ViewportToolContext context)
    : IViewportFrameRequester
{
    public void RequestFrame() => context.InvalidateScene();
}

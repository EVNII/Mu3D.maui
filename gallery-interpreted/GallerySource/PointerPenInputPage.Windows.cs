#if WINDOWS
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Mu3D.GalleryApp.Pages;

public partial class PointerPenInputPage
{
    private UIElement? drawingCursorPlatformElement;
    private Visual? drawingCursorVisual;
    private Visual? drawingCursorTiltVisual;

    private partial void UpdateDrawingCursorPlatform(
        float x,
        float y,
        float rotation,
        ref bool handled)
    {
        if (DrawingCursor.Handler?.PlatformView is not UIElement cursorElement ||
            DrawingCursorTiltLine.Handler?.PlatformView is not UIElement tiltElement)
        {
            return;
        }
        if (!ReferenceEquals(drawingCursorPlatformElement, cursorElement))
        {
            drawingCursorPlatformElement = cursorElement;
            drawingCursorVisual = ElementCompositionPreview.GetElementVisual(cursorElement);
            drawingCursorTiltVisual = ElementCompositionPreview.GetElementVisual(tiltElement);
        }
        Visual? cursorVisual = drawingCursorVisual;
        Visual? tiltVisual = drawingCursorTiltVisual;
        if (cursorVisual is null || tiltVisual is null)
        {
            return;
        }
        cursorVisual.Offset = new Vector3(x, y, cursorVisual.Offset.Z);
        tiltVisual.CenterPoint = new Vector3(
            tiltVisual.Size.X * 0.5f,
            tiltVisual.Size.Y * 0.78f,
            0f);
        tiltVisual.RotationAngleInDegrees = rotation;
        handled = true;
    }
}
#endif

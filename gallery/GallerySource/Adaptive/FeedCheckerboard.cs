using Microsoft.Maui.Graphics;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Mu3D.Gallery.Pages;

/// <summary>Draws a MAUI checkerboard visible through a feed preview's transparent background.</summary>
public sealed class FeedCheckerboard : GraphicsView
{
    /// <summary>Creates a passive checkerboard that follows the preview's layout.</summary>
    public FeedCheckerboard()
    {
        InputTransparent = true;
        Drawable = new CheckerboardDrawable();
    }

    private sealed class CheckerboardDrawable : IDrawable
    {
        private const float CellSize = 12f;
        private static readonly MauiColor Light = MauiColor.FromArgb("#F4F4F4");
        private static readonly MauiColor Dark = MauiColor.FromArgb("#A5A5A5");

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            canvas.FillColor = Light;
            canvas.FillRectangle(dirtyRect);
            int firstColumn = (int)MathF.Floor(dirtyRect.Left / CellSize);
            int firstRow = (int)MathF.Floor(dirtyRect.Top / CellSize);
            int lastColumn = (int)MathF.Ceiling(dirtyRect.Right / CellSize);
            int lastRow = (int)MathF.Ceiling(dirtyRect.Bottom / CellSize);
            canvas.FillColor = Dark;
            for (int row = firstRow; row < lastRow; row++)
            {
                for (int column = firstColumn; column < lastColumn; column++)
                {
                    if (((row + column) & 1) != 0)
                        canvas.FillRectangle(column * CellSize, row * CellSize, CellSize, CellSize);
                }
            }
        }
    }
}

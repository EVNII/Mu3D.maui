using Microsoft.Maui.Graphics;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Mu3D.Maui.Toolkit.Diagnostics;

internal sealed class FrameStatisticsGraphDrawable(FrameStatisticsHistory history) : IDrawable
{
    public MauiColor GraphColor { get; set; } = Colors.Cyan;

    public MauiColor BackgroundColor { get; set; } = Colors.Black;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = BackgroundColor;
        canvas.FillRectangle(dirtyRect.Left, dirtyRect.Top, dirtyRect.Width, dirtyRect.Height);
        if (history.Count == 0 || dirtyRect.Width <= 0f || dirtyRect.Height <= 0f)
        {
            return;
        }

        canvas.StrokeColor = GraphColor;
        canvas.StrokeSize = 1f;
        canvas.Alpha = 0.18f;
        for (int row = 1; row < 4; row++)
        {
            float y = dirtyRect.Top + (dirtyRect.Height * row / 4f);
            canvas.DrawLine(dirtyRect.Left, y, dirtyRect.Right, y);
        }

        double scaleMaximum = Math.Max(60d, history.Maximum);
        float step = history.Capacity == 1
            ? dirtyRect.Width
            : dirtyRect.Width / (history.Capacity - 1);
        float barWidth = MathF.Max(1f, step * 0.72f);
        float firstX = dirtyRect.Right - ((history.Count - 1) * step);
        float previousX = 0f;
        float previousY = 0f;

        for (int index = 0; index < history.Count; index++)
        {
            float x = firstX + (index * step);
            float normalized = (float)Math.Clamp(history[index] / scaleMaximum, 0d, 1d);
            float y = dirtyRect.Bottom - (normalized * dirtyRect.Height);
            canvas.FillColor = GraphColor;
            canvas.Alpha = 0.22f;
            canvas.FillRectangle(x - (barWidth / 2f), y, barWidth, dirtyRect.Bottom - y);
            if (index > 0)
            {
                canvas.StrokeColor = GraphColor;
                canvas.StrokeSize = 1.5f;
                canvas.Alpha = 1f;
                canvas.DrawLine(previousX, previousY, x, y);
            }
            previousX = x;
            previousY = y;
        }
        canvas.Alpha = 1f;
    }
}

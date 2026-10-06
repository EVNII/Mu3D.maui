namespace Mu3D.Maui.Toolkit.Controls;

internal readonly record struct ViewportTouchTransformSample(
    double CenterX,
    double CenterY,
    double ScaleRatio,
    bool IsRebased);

internal sealed class ViewportTouchTransformState
{
    private double previousSpan;

    internal int FirstPointerId { get; private set; } = -1;

    internal int SecondPointerId { get; private set; } = -1;

    internal bool TryUpdate(
        int firstPointerId,
        double firstX,
        double firstY,
        int secondPointerId,
        double secondX,
        double secondY,
        bool rebase,
        out ViewportTouchTransformSample sample)
    {
        sample = default;
        if (firstPointerId < 0 || secondPointerId < 0 || firstPointerId == secondPointerId ||
            !double.IsFinite(firstX) || !double.IsFinite(firstY) ||
            !double.IsFinite(secondX) || !double.IsFinite(secondY))
        {
            return false;
        }

        double centerX = firstX * 0.5d + secondX * 0.5d;
        double centerY = firstY * 0.5d + secondY * 0.5d;
        double spanX = secondX - firstX;
        double spanY = secondY - firstY;
        double span = Math.Sqrt(spanX * spanX + spanY * spanY);
        if (!double.IsFinite(span))
        {
            return false;
        }

        bool samePair =
            (FirstPointerId == firstPointerId && SecondPointerId == secondPointerId) ||
            (FirstPointerId == secondPointerId && SecondPointerId == firstPointerId);
        bool isRebased = rebase || !samePair;
        // Coincident fingers still pan. The first nonzero span establishes a new scale
        // baseline instead of producing an infinite or discontinuous zoom increment.
        double ratio = !isRebased && previousSpan > 0d && span > 0d
            ? span / previousSpan
            : 1d;
        if (!double.IsFinite(ratio) || ratio <= 0d)
        {
            ratio = 1d;
        }

        FirstPointerId = firstPointerId;
        SecondPointerId = secondPointerId;
        previousSpan = span;
        sample = new(centerX, centerY, ratio, isRebased);
        return true;
    }

    internal void Reset()
    {
        FirstPointerId = -1;
        SecondPointerId = -1;
        previousSpan = 0d;
    }
}

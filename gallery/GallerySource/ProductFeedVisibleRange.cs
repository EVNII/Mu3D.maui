namespace Mu3D.GalleryApp.Pages;

// Keep native item identity separate from the order/number of realized containers.
internal struct ProductFeedVisibleRange
{
    public ProductFeedVisibleRange() { }

    internal int First { get; private set; } = -1;

    internal int Last { get; private set; } = -1;

    internal void Include(int itemIndex, double left, double top, double width, double height,
        double viewportWidth, double viewportHeight, double minimumMeasuredHeight = 0d)
    {
        if (itemIndex < 0 || !double.IsFinite(left) || !double.IsFinite(top) ||
            !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) ||
            width <= 0 || height <= 0 || height < minimumMeasuredHeight - 0.5d ||
            viewportWidth <= 0 || viewportHeight <= 0 ||
            top >= viewportHeight || top + height <= 0 ||
            left >= viewportWidth || left + width <= 0)
        {
            return;
        }
        First = First < 0 ? itemIndex : Math.Min(First, itemIndex);
        Last = Math.Max(Last, itemIndex);
    }
}

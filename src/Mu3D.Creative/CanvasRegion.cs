namespace Mu3D.Creative;

/// <summary>Identifies a nonempty integer pixel rectangle with exclusive right and bottom edges.</summary>
public readonly record struct CanvasRegion
{
    /// <summary>Creates a rectangle with nonnegative origin and positive extent.</summary>
    public CanvasRegion(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if ((long)x + width > int.MaxValue || (long)y + height > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Rectangle edges must fit in Int32.");
        }
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Gets the inclusive left pixel coordinate.</summary>
    public int X { get; }

    /// <summary>Gets the inclusive top pixel coordinate.</summary>
    public int Y { get; }

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the exclusive right pixel coordinate.</summary>
    public int Right => X + Width;

    /// <summary>Gets the exclusive bottom pixel coordinate.</summary>
    public int Bottom => Y + Height;
}

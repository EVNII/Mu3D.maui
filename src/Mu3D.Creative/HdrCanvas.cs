using System.Numerics;
using Mu3D.Color;

namespace Mu3D.Creative;

/// <summary>
/// Stores a sparse HDR canvas with FP32 compositing and explicit working, compositing and storage
/// policy. Public pixels are straight-alpha linear colors; tiles store premultiplied linear color.
/// </summary>
/// <remarks>
/// Instances are not thread-safe. Mutations validate and stage changes before committing; invalid
/// colors, numeric overflow and configured allocation-budget failures leave pixels, revision and
/// dirty regions unchanged. Fully transparent pixels are canonical transparent black. No display,
/// gamut, tone-map, input, undo, gesture or stroke-spacing policy is applied.
/// </remarks>
public sealed class HdrCanvas
{
    private readonly Dictionary<(int X, int Y), Tile> tiles = [];
    private readonly Dictionary<(int X, int Y), CanvasRegion> dirty = [];
    private readonly HdrCanvasOptions options;
    private readonly int bytesPerPixel;

    /// <summary>Creates a transparent sparse canvas without allocating pixel tiles.</summary>
    /// <param name="width">Canvas width from 1 through 1048576 pixels.</param>
    /// <param name="height">Canvas height from 1 through 1048576 pixels.</param>
    /// <param name="workingSpace">The standard linear working-space identity for reads and snapshots.</param>
    /// <param name="options">Optional storage, compositing and resource limits.</param>
    public HdrCanvas(
        int width,
        int height,
        StandardRgbColorSpaceReference workingSpace,
        HdrCanvasOptions? options = null)
    {
        if (width is < 1 or > 1048576 || height is < 1 or > 1048576)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Canvas dimensions must be in [1, 1048576].");
        }
        ArgumentNullException.ThrowIfNull(workingSpace);
        this.options = options ?? new HdrCanvasOptions();
        if (!Enum.IsDefined(this.options.Precision) || this.options.TileSize is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Unknown precision or tile size outside [1, 1024].");
        }
        if (this.options.MaxStorageBytes <= 0 || this.options.MaxOperationBytes <= 0 ||
            this.options.MaxSnapshotBytes <= 0 || this.options.MaxTileCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Allocation budgets must be positive.");
        }
        Width = width;
        Height = height;
        WorkingSpace = workingSpace;
        CompositingSpace = this.options.CompositingSpace ?? workingSpace;
        bytesPerPixel = Precision == PixelPrecision.Float16 ? 8 : 16;
    }

    /// <summary>Gets the canvas width in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the canvas height in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the explicit linear working space used by reads and snapshots.</summary>
    public StandardRgbColorSpaceReference WorkingSpace { get; }

    /// <summary>Gets the separately selected linear space used by premultiplied storage and blending.</summary>
    public StandardRgbColorSpaceReference CompositingSpace { get; }

    /// <summary>Gets the actual tile storage precision.</summary>
    public PixelPrecision Precision => options.Precision;

    /// <summary>Gets the tile edge length; right and bottom edge tiles allocate only their actual extent.</summary>
    public int TileSize => options.TileSize;

    /// <summary>Gets the number of currently allocated nontransparent tiles.</summary>
    public int TileCount => tiles.Count;

    /// <summary>Gets retained tile-pixel payload bytes, excluding collection and object overhead.</summary>
    public long AllocatedStorageBytes { get; private set; }

    /// <summary>Gets the version, incremented once per operation that changes stored pixels.</summary>
    public ulong Revision { get; private set; }

    /// <summary>Reads a straight-alpha FP32 pixel in the canvas working space.</summary>
    public LinearRgba GetPixel(int x, int y)
    {
        ValidatePixel(x, y);
        return ToWorkingColor(ReadPremultiplied(x, y));
    }

    /// <summary>
    /// Replaces one pixel after converting the explicitly tagged color to the compositing space.
    /// A fully transparent input discards hidden RGB.
    /// </summary>
    public void SetPixel(int x, int y, LinearRgba color)
    {
        ValidatePixel(x, y);
        Fill(new CanvasRegion(x, y, 1, 1), color);
    }

    /// <summary>Replaces all pixels in an in-bounds rectangle with the converted straight-alpha color.</summary>
    public void Fill(CanvasRegion region, LinearRgba color)
    {
        ValidateRegion(region);
        Vector4 value = Quantize(ToPremultiplied(color, 1f));
        Mutate(region, (_, _, _) => value);
    }

    /// <summary>
    /// Blends and source-over composites a constant tagged color over an in-bounds rectangle.
    /// Opacity is an additional multiplier in [0, 1]; RGB is neither clipped nor tone mapped.
    /// </summary>
    public void Composite(
        CanvasRegion region,
        LinearRgba color,
        CanvasBlendMode blendMode = CanvasBlendMode.Normal,
        float opacity = 1f)
    {
        ValidateRegion(region);
        ValidateBlendMode(blendMode);
        BrushDab.ValidateUnit(opacity, nameof(opacity));
        Vector4 source = ToPremultiplied(color, opacity);
        if (source.W == 0f)
        {
            return;
        }
        Mutate(region, (_, _, backdrop) => Blend(backdrop, source, blendMode));
    }

    /// <summary>
    /// Converts, blends and source-over composites a tagged immutable image at an in-bounds integer
    /// origin. The complete image must fit; no scaling, resampling or implicit clipping is performed.
    /// </summary>
    public void CompositeImage(
        LinearRgbaImage image,
        int x = 0,
        int y = 0,
        CanvasBlendMode blendMode = CanvasBlendMode.Normal,
        float opacity = 1f)
    {
        ArgumentNullException.ThrowIfNull(image);
        ValidateBlendMode(blendMode);
        BrushDab.ValidateUnit(opacity, nameof(opacity));
        if ((long)image.Width * image.Height != image.Pixels.Count)
        {
            throw new ArgumentException("Image dimensions do not match its pixel buffer.", nameof(image));
        }
        if (image.Width > Width || image.Height > Height || x < 0 || y < 0 ||
            (long)x + image.Width > Width || (long)y + image.Height > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(image), "The complete image must fit inside the canvas.");
        }
        _ = ToPremultiplied(new LinearRgba(0f, 0f, 0f, 0f, image.ColorSpace), opacity);
        if (opacity == 0f)
        {
            return;
        }
        int imageWidth = (int)image.Width;
        Mutate(new CanvasRegion(x, y, imageWidth, (int)image.Height), (pixelX, pixelY, backdrop) =>
        {
            Vector4 pixel = image.Pixels[(pixelY - y) * imageWidth + pixelX - x];
            Vector4 source = ToPremultiplied(new LinearRgba(pixel.X, pixel.Y, pixel.Z, pixel.W,
                image.ColorSpace), opacity);
            return Blend(backdrop, source, blendMode);
        });
    }

    /// <summary>
    /// Composites one circular application-supplied dab, clipped to the canvas. Coverage is evaluated
    /// at pixel centers, with a linear fade outside the hardness radius and zero at the outer radius.
    /// </summary>
    public void ApplyDab(BrushDab dab)
    {
        // Reconstruct to reject the default struct as well as future invalid construction paths.
        _ = new BrushDab(dab.CenterX, dab.CenterY, dab.Radius, dab.Color,
            dab.Opacity, dab.Hardness, dab.BlendMode);
        Vector4 source = ToPremultiplied(dab.Color, dab.Opacity);
        if (source.W == 0f)
        {
            return;
        }
        int left = (int)Math.Clamp(Math.Floor((double)dab.CenterX - dab.Radius), 0d, Width);
        int top = (int)Math.Clamp(Math.Floor((double)dab.CenterY - dab.Radius), 0d, Height);
        int right = (int)Math.Clamp(Math.Ceiling((double)dab.CenterX + dab.Radius), 0d, Width);
        int bottom = (int)Math.Clamp(Math.Ceiling((double)dab.CenterY + dab.Radius), 0d, Height);
        if (left == right || top == bottom)
        {
            return;
        }
        Mutate(new CanvasRegion(left, top, right - left, bottom - top), (x, y, backdrop) =>
        {
            // Double coordinates avoid overflow when finite off-canvas centers/radii are very large;
            // color, alpha and all compositing arithmetic below remain FP32.
            double dx = x + 0.5d - dab.CenterX;
            double dy = y + 0.5d - dab.CenterY;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance >= dab.Radius)
            {
                return backdrop;
            }
            float coverage = dab.Hardness == 1f ? 1f :
                (float)Math.Clamp((dab.Radius - distance) / (dab.Radius * (1d - dab.Hardness)), 0d, 1d);
            return Blend(backdrop, source * coverage, dab.BlendMode);
        });
    }

    /// <summary>Clears the complete canvas to transparent black and releases all tile-pixel storage.</summary>
    public void Clear()
    {
        if (tiles.Count == 0)
        {
            return;
        }
        long dirtyCount = (long)dirty.Count + tiles.Keys.Count(key => !dirty.ContainsKey(key));
        CheckTileCount(dirtyCount);
        ulong nextRevision = checked(Revision + 1);
        foreach (((int X, int Y) key, Tile tile) in tiles)
        {
            MergeDirty(key, new CanvasRegion(key.X * TileSize, key.Y * TileSize, tile.Width, tile.Height));
        }
        tiles.Clear();
        AllocatedStorageBytes = 0;
        Revision = nextRevision;
    }

    /// <summary>
    /// Gets a detached, row-major list of dirty pixel bounds, at most one rectangle per tile.
    /// Results include cleared tiles and remain dirty until <see cref="ClearDirtyRegions"/> is called.
    /// </summary>
    public IReadOnlyList<CanvasRegion> GetDirtyRegions() => Array.AsReadOnly(
        dirty.OrderBy(pair => pair.Key.Y).ThenBy(pair => pair.Key.X).Select(pair => pair.Value).ToArray());

    /// <summary>Clears dirty bookkeeping after the application has consumed updates; pixels and revision are unchanged.</summary>
    public void ClearDirtyRegions() => dirty.Clear();

    /// <summary>
    /// Creates an immutable straight-alpha FP32 image in the working space, suitable for material
    /// presentation. A region snapshots only that crop; null selects the full canvas. Dirty state is retained.
    /// </summary>
    /// <exception cref="InvalidOperationException">The snapshot exceeds its pixel-buffer budget or managed array limit.</exception>
    public LinearRgbaImage Snapshot(CanvasRegion? region = null, string? name = null)
    {
        CanvasRegion area = region ?? new CanvasRegion(0, 0, Width, Height);
        ValidateRegion(area);
        long count = (long)area.Width * area.Height;
        if (count > Array.MaxLength || count * 32 > options.MaxSnapshotBytes)
        {
            throw new InvalidOperationException("The snapshot exceeds the configured pixel-buffer budget or managed array limit.");
        }
        Vector4[] pixels = new Vector4[(int)count];
        int index = 0;
        for (int y = area.Y; y < area.Bottom; y++)
        {
            for (int x = area.X; x < area.Right; x++)
            {
                LinearRgba pixel = ToWorkingColor(ReadPremultiplied(x, y));
                pixels[index++] = new Vector4(pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha);
            }
        }
        return new LinearRgbaImage((uint)area.Width, (uint)area.Height, pixels, WorkingSpace, name);
    }

    private void Mutate(CanvasRegion area, Func<int, int, Vector4, Vector4> operation)
    {
        int firstX = area.X / TileSize;
        int firstY = area.Y / TileSize;
        int lastX = (area.Right - 1) / TileSize;
        int lastY = (area.Bottom - 1) / TileSize;
        long intersectedCount = (long)(lastX - firstX + 1) * (lastY - firstY + 1);
        CheckTileCount(intersectedCount);
        long stagedWidth = Math.Min(Width, (lastX + 1) * TileSize) - firstX * TileSize;
        long stagedHeight = Math.Min(Height, (lastY + 1) * TileSize) - firstY * TileSize;
        if (stagedWidth * stagedHeight * bytesPerPixel > options.MaxOperationBytes)
        {
            throw new InvalidOperationException("The mutation's intersected tiles exceed the configured staging budget.");
        }

        Dictionary<(int X, int Y), (Tile Tile, CanvasRegion Dirty)> staged = [];
        long retainedBytes = AllocatedStorageBytes;
        long retainedCount = tiles.Count;
        long dirtyCount = dirty.Count;
        for (int tileY = firstY; tileY <= lastY; tileY++)
        {
            for (int tileX = firstX; tileX <= lastX; tileX++)
            {
                (int X, int Y) key = (tileX, tileY);
                tiles.TryGetValue(key, out Tile? oldTile);
                Tile? replacement = null;
                int originX = tileX * TileSize;
                int originY = tileY * TileSize;
                int tileWidth = Math.Min(TileSize, Width - originX);
                int tileHeight = Math.Min(TileSize, Height - originY);
                int changedLeft = int.MaxValue, changedTop = int.MaxValue, changedRight = 0, changedBottom = 0;
                for (int y = Math.Max(area.Y, originY); y < Math.Min(area.Bottom, originY + tileHeight); y++)
                {
                    for (int x = Math.Max(area.X, originX); x < Math.Min(area.Right, originX + tileWidth); x++)
                    {
                        int index = (y - originY) * tileWidth + x - originX;
                        Vector4 oldValue = oldTile?.Get(index) ?? Vector4.Zero;
                        Vector4 newValue = Quantize(operation(x, y, oldValue));
                        if (newValue == oldValue)
                        {
                            continue;
                        }
                        replacement ??= oldTile?.Clone() ?? new Tile(tileWidth, tileHeight, Precision);
                        replacement.Set(index, newValue);
                        changedLeft = Math.Min(changedLeft, x);
                        changedTop = Math.Min(changedTop, y);
                        changedRight = Math.Max(changedRight, x + 1);
                        changedBottom = Math.Max(changedBottom, y + 1);
                    }
                }
                if (replacement is null)
                {
                    continue;
                }
                long tileBytes = (long)tileWidth * tileHeight * bytesPerPixel;
                if (oldTile is null && replacement.NonzeroPixels > 0)
                {
                    retainedBytes += tileBytes;
                    retainedCount++;
                }
                else if (oldTile is not null && replacement.NonzeroPixels == 0)
                {
                    retainedBytes -= tileBytes;
                    retainedCount--;
                }
                if (!dirty.ContainsKey(key))
                {
                    dirtyCount++;
                }
                staged.Add(key, (replacement, new CanvasRegion(changedLeft, changedTop,
                    changedRight - changedLeft, changedBottom - changedTop)));
            }
        }
        if (staged.Count == 0)
        {
            return;
        }
        if (retainedBytes > options.MaxStorageBytes)
        {
            throw new InvalidOperationException("The mutation exceeds the configured retained tile-pixel budget.");
        }
        CheckTileCount(retainedCount);
        CheckTileCount(dirtyCount);
        ulong nextRevision = checked(Revision + 1);
        foreach (((int X, int Y) key, (Tile replacement, CanvasRegion changed)) in staged)
        {
            if (replacement.NonzeroPixels == 0)
            {
                tiles.Remove(key);
            }
            else
            {
                tiles[key] = replacement;
            }
            MergeDirty(key, changed);
        }
        AllocatedStorageBytes = retainedBytes;
        Revision = nextRevision;
    }

    private static Vector4 Blend(Vector4 backdrop, Vector4 source, CanvasBlendMode mode)
    {
        if (source.W == 0f)
        {
            return backdrop;
        }
        if (backdrop.W == 0f)
        {
            return source;
        }
        Vector3 src = new(source.X, source.Y, source.Z);
        Vector3 dst = new(backdrop.X, backdrop.Y, backdrop.Z);
        Vector3 rgb;
        if (mode == CanvasBlendMode.Normal)
        {
            rgb = src + dst * (1f - source.W);
        }
        else
        {
            // Premultiplied forms of As*Ab*B(Cb,Cs), avoiding an unpremultiply/re-premultiply.
            Vector3 overlap = mode switch
            {
                CanvasBlendMode.Multiply => src * dst,
                CanvasBlendMode.Screen => src * backdrop.W + dst * source.W - src * dst,
                CanvasBlendMode.Add => src * backdrop.W + dst * source.W,
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            rgb = src * (1f - backdrop.W) + dst * (1f - source.W) + overlap;
        }
        return new Vector4(rgb, source.W + backdrop.W * (1f - source.W));
    }

    private Vector4 ToPremultiplied(LinearRgba color, float opacity)
    {
        LinearRgba converted = StandardLinearRgbConverter.Convert(color, CompositingSpace);
        float alpha = converted.Alpha * opacity;
        return alpha == 0f ? Vector4.Zero :
            new Vector4(converted.Red * alpha, converted.Green * alpha, converted.Blue * alpha, alpha);
    }

    private LinearRgba ToWorkingColor(Vector4 pixel)
    {
        Vector3 rgb = pixel.W == 0f ? Vector3.Zero : new Vector3(pixel.X, pixel.Y, pixel.Z) / pixel.W;
        return StandardLinearRgbConverter.Convert(new LinearRgba(rgb.X, rgb.Y, rgb.Z, pixel.W,
            CompositingSpace), WorkingSpace);
    }

    private Vector4 Quantize(Vector4 value)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z) ||
            !float.IsFinite(value.W) || value.W is < 0f or > 1f)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Compositing produced non-finite color or invalid alpha.");
        }
        if (Precision == PixelPrecision.Float16)
        {
            // Reject even finite values that would round to a finite half beyond the stated limit.
            const float halfMaximum = 65504f;
            if (MathF.Abs(value.X) > halfMaximum || MathF.Abs(value.Y) > halfMaximum || MathF.Abs(value.Z) > halfMaximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Compositing exceeds finite Float16 storage; select Float32 explicitly.");
            }
            value = new Vector4((float)(Half)value.X, (float)(Half)value.Y,
                (float)(Half)value.Z, (float)(Half)value.W);
        }
        if (value.W == 0f)
        {
            return Vector4.Zero;
        }
        // Reads/snapshots are part of the public straight-alpha boundary; reject values that cannot
        // cross it, including finite stored FP32 divided by a very small nonzero alpha.
        _ = ToWorkingColor(value);
        return value;
    }

    private Vector4 ReadPremultiplied(int x, int y) =>
        tiles.TryGetValue((x / TileSize, y / TileSize), out Tile? tile)
            ? tile.Get((y % TileSize) * tile.Width + x % TileSize)
            : Vector4.Zero;

    private void MergeDirty((int X, int Y) key, CanvasRegion region)
    {
        if (dirty.TryGetValue(key, out CanvasRegion previous))
        {
            int left = Math.Min(previous.X, region.X), top = Math.Min(previous.Y, region.Y);
            region = new CanvasRegion(left, top, Math.Max(previous.Right, region.Right) - left,
                Math.Max(previous.Bottom, region.Bottom) - top);
        }
        dirty[key] = region;
    }

    private void ValidatePixel(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Pixel coordinates must lie inside the canvas.");
        }
    }

    private void ValidateRegion(CanvasRegion region)
    {
        if (region.Width <= 0 || region.Height <= 0 || region.Right > Width || region.Bottom > Height)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "A nonempty region must lie entirely inside the canvas.");
        }
    }

    private void CheckTileCount(long count)
    {
        if (count > options.MaxTileCount)
        {
            throw new InvalidOperationException("The operation exceeds the retained, dirty or intersected tile-count budget.");
        }
    }

    private static void ValidateBlendMode(CanvasBlendMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    private sealed class Tile
    {
        private readonly Half[]? half;
        private readonly Vector4[]? full;

        internal Tile(int width, int height, PixelPrecision precision)
        {
            Width = width;
            Height = height;
            if (precision == PixelPrecision.Float16)
            {
                half = new Half[width * height * 4];
            }
            else
            {
                full = new Vector4[width * height];
            }
        }

        internal int Width { get; }
        internal int Height { get; }
        internal int NonzeroPixels { get; private set; }

        internal Tile Clone()
        {
            Tile clone = new(Width, Height, half is null ? PixelPrecision.Float32 : PixelPrecision.Float16);
            if (half is not null)
            {
                half.CopyTo(clone.half!, 0);
            }
            else
            {
                full!.CopyTo(clone.full!, 0);
            }
            clone.NonzeroPixels = NonzeroPixels;
            return clone;
        }

        internal Vector4 Get(int index) => half is null ? full![index] :
            new Vector4((float)half[index * 4], (float)half[index * 4 + 1],
                (float)half[index * 4 + 2], (float)half[index * 4 + 3]);

        internal void Set(int index, Vector4 value)
        {
            bool oldPresent = Get(index).W != 0f;
            bool newPresent = value.W != 0f;
            NonzeroPixels += (newPresent ? 1 : 0) - (oldPresent ? 1 : 0);
            if (half is null)
            {
                full![index] = value;
            }
            else
            {
                half[index * 4] = (Half)value.X;
                half[index * 4 + 1] = (Half)value.Y;
                half[index * 4 + 2] = (Half)value.Z;
                half[index * 4 + 3] = (Half)value.W;
            }
        }
    }
}

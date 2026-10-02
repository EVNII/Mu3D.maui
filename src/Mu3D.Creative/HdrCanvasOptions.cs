using Mu3D.Color;

namespace Mu3D.Creative;

/// <summary>Specifies immutable storage, compositing and allocation policy for an HDR canvas.</summary>
public sealed record HdrCanvasOptions
{
    /// <summary>Gets the storage precision; defaults to IEEE binary16. Computation uses FP32.</summary>
    public PixelPrecision Precision { get; init; } = PixelPrecision.Float16;

    /// <summary>Gets the tile edge length in pixels, from 1 through 1024; defaults to 64.</summary>
    public int TileSize { get; init; } = 64;

    /// <summary>
    /// Gets the explicit linear compositing/storage space. Null selects the canvas working space.
    /// Reads and snapshots convert to the separately retained working space.
    /// </summary>
    public StandardRgbColorSpaceReference? CompositingSpace { get; init; }

    /// <summary>
    /// Gets the maximum retained tile-pixel payload in bytes; defaults to 256 MiB.
    /// Managed collection and object overhead is additional and bounded by <see cref="MaxTileCount"/>.
    /// </summary>
    public long MaxStorageBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>
    /// Gets the maximum tile-pixel payload staged by one atomic mutation; defaults to 64 MiB.
    /// The complete tiles intersecting the mutation bounds must fit, including unchanged pixels.
    /// </summary>
    public long MaxOperationBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// Gets the combined FP32 pixel-buffer budget for creating a snapshot; defaults to 256 MiB.
    /// A snapshot requires two buffers, or 32 bytes per pixel, while its immutable image is copied.
    /// </summary>
    public long MaxSnapshotBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>
    /// Gets the maximum retained, dirty, or operation-intersected tile count; defaults to 65536.
    /// This bounds metadata separately from tile-pixel payload, including after tiles are cleared.
    /// </summary>
    public int MaxTileCount { get; init; } = 65536;
}

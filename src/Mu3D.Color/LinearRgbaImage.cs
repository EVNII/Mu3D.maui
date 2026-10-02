using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.Color;

/// <summary>
/// Stores an immutable two-dimensional FP32 image with unpremultiplied linear-light RGBA pixels
/// and one explicit color-space identity.
/// </summary>
public sealed class LinearRgbaImage
{
    private readonly Vector4[] pixelStorage;

    /// <summary>Initializes an image and copies all supplied pixels.</summary>
    /// <param name="width">The non-zero width in pixels.</param>
    /// <param name="height">The non-zero height in pixels.</param>
    /// <param name="pixels">Row-major, top-to-bottom unpremultiplied linear RGBA pixels.</param>
    /// <param name="colorSpace">The explicit linear-light RGB color space of every pixel.</param>
    /// <param name="name">An optional diagnostic name.</param>
    public LinearRgbaImage(
        uint width,
        uint height,
        IEnumerable<Vector4> pixels,
        ColorSpaceReference colorSpace,
        string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(colorSpace);
        Vector4[] copiedPixels = [.. pixels];
        ValidatePixels(width, height, copiedPixels);
        pixelStorage = copiedPixels;
        Width = width;
        Height = height;
        Pixels = new ReadOnlyCollection<Vector4>(pixelStorage);
        ColorSpace = colorSpace;
        Name = name;
    }

    private LinearRgbaImage(
        uint width,
        uint height,
        Vector4[] ownedPixels,
        ColorSpaceReference colorSpace,
        string? name)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentNullException.ThrowIfNull(ownedPixels);
        ArgumentNullException.ThrowIfNull(colorSpace);
        int expectedCount = checked((int)(width * height));
        if (ownedPixels.Length != expectedCount)
        {
            throw new ArgumentException(
                $"The image requires exactly {expectedCount} pixels.",
                nameof(ownedPixels));
        }
        pixelStorage = ownedPixels;
        Width = width;
        Height = height;
        Pixels = new ReadOnlyCollection<Vector4>(pixelStorage);
        ColorSpace = colorSpace;
        Name = name;
    }

    internal static LinearRgbaImage FromTrustedOwnedPixels(
        uint width,
        uint height,
        Vector4[] ownedPixels,
        ColorSpaceReference colorSpace,
        string? name) => new(width, height, ownedPixels, colorSpace, name);

    // Only newly allocated arrays whose ownership is transferred by the decoder may enter here.
    // Keep external/native results validated without a second full FP32 image allocation.
    internal static LinearRgbaImage FromOwnedPixels(
        uint width,
        uint height,
        Vector4[] ownedPixels,
        ColorSpaceReference colorSpace,
        string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentNullException.ThrowIfNull(ownedPixels);
        ArgumentNullException.ThrowIfNull(colorSpace);
        ValidatePixels(width, height, ownedPixels);
        return new(width, height, ownedPixels, colorSpace, name);
    }

    internal Vector4[] PixelStorage => pixelStorage;

    private static void ValidatePixels(uint width, uint height, Vector4[] pixels)
    {
        int expectedCount = checked((int)(width * height));
        if (pixels.Length != expectedCount)
        {
            throw new ArgumentException(
                $"The image requires exactly {expectedCount} pixels.",
                nameof(pixels));
        }
        foreach (Vector4 pixel in pixels)
        {
            if (!float.IsFinite(pixel.X) ||
                !float.IsFinite(pixel.Y) ||
                !float.IsFinite(pixel.Z) ||
                !float.IsFinite(pixel.W))
            {
                throw new ArgumentOutOfRangeException(nameof(pixels), "Image components must be finite.");
            }
            if (pixel.W is < 0f or > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(pixels), "Image alpha must be in [0, 1].");
            }
        }
    }

    /// <summary>Gets the width in pixels.</summary>
    public uint Width { get; }

    /// <summary>Gets the height in pixels.</summary>
    public uint Height { get; }

    /// <summary>Gets row-major, top-to-bottom unpremultiplied linear FP32 RGBA pixels.</summary>
    public IReadOnlyList<Vector4> Pixels { get; }

    /// <summary>Gets the explicit linear-light color space shared by the RGB channels.</summary>
    public ColorSpaceReference ColorSpace { get; }

    /// <summary>Gets the optional diagnostic name.</summary>
    public string? Name { get; }
}

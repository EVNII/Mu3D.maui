using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>
/// Stores immutable normalized RGBA data that must not undergo a color-space or transfer-function
/// transform. Typical uses include normal and packed material-property maps.
/// </summary>
public sealed class NormalizedRgbaDataImage
{
    private readonly Vector4[] texelStorage;

    /// <summary>Initializes a normalized data image and copies all supplied texels.</summary>
    /// <param name="width">The non-zero width in texels.</param>
    /// <param name="height">The non-zero height in texels.</param>
    /// <param name="texels">Row-major, top-to-bottom RGBA values in the inclusive range zero to one.</param>
    /// <param name="name">An optional diagnostic name.</param>
    public NormalizedRgbaDataImage(
        uint width,
        uint height,
        IEnumerable<Vector4> texels,
        string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentNullException.ThrowIfNull(texels);
        Vector4[] copiedTexels = [.. texels];
        ValidateTexels(width, height, copiedTexels);
        texelStorage = copiedTexels;
        Width = width;
        Height = height;
        Texels = new ReadOnlyCollection<Vector4>(texelStorage);
        Name = name;
    }

    private NormalizedRgbaDataImage(
        uint width,
        uint height,
        Vector4[] ownedTexels,
        string? name)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentNullException.ThrowIfNull(ownedTexels);
        int expectedCount = checked((int)(width * height));
        if (ownedTexels.Length != expectedCount)
        {
            throw new ArgumentException(
                $"The image requires exactly {expectedCount} texels.",
                nameof(ownedTexels));
        }
        texelStorage = ownedTexels;
        Width = width;
        Height = height;
        Texels = new ReadOnlyCollection<Vector4>(texelStorage);
        Name = name;
    }

    internal static NormalizedRgbaDataImage FromTrustedOwnedTexels(
        uint width,
        uint height,
        Vector4[] ownedTexels,
        string? name) => new(width, height, ownedTexels, name);

    // Decoders transfer their newly allocated array; native results still need full validation.
    internal static NormalizedRgbaDataImage FromOwnedTexels(
        uint width,
        uint height,
        Vector4[] ownedTexels,
        string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentNullException.ThrowIfNull(ownedTexels);
        ValidateTexels(width, height, ownedTexels);
        return new(width, height, ownedTexels, name);
    }

    internal Vector4[] TexelStorage => texelStorage;

    private static void ValidateTexels(uint width, uint height, Vector4[] texels)
    {
        int expectedCount = checked((int)(width * height));
        if (texels.Length != expectedCount)
        {
            throw new ArgumentException(
                $"The image requires exactly {expectedCount} texels.",
                nameof(texels));
        }
        foreach (Vector4 texel in texels)
        {
            if (!IsNormalized(texel.X) ||
                !IsNormalized(texel.Y) ||
                !IsNormalized(texel.Z) ||
                !IsNormalized(texel.W))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(texels),
                    "Data-image components must be finite and in [0, 1].");
            }
        }
    }

    /// <summary>Gets the width in texels.</summary>
    public uint Width { get; }

    /// <summary>Gets the height in texels.</summary>
    public uint Height { get; }

    /// <summary>Gets immutable row-major normalized RGBA data.</summary>
    public IReadOnlyList<Vector4> Texels { get; }

    /// <summary>Gets the optional diagnostic name.</summary>
    public string? Name { get; }

    private static bool IsNormalized(float value) => float.IsFinite(value) && value is >= 0f and <= 1f;
}

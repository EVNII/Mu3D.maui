using System.Collections.ObjectModel;
using System.Numerics;
using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>Specifies texture boundary handling independently for each UV axis.</summary>
public enum OpenPbrAddressMode
{
    /// <summary>Repeats the image at integer UV boundaries.</summary>
    Periodic,
    /// <summary>Extends the edge texel.</summary>
    Clamp,
    /// <summary>Alternates forward and reflected image repeats.</summary>
    Mirror,
}

/// <summary>Specifies nearest-texel or bilinear level-zero sampling.</summary>
public enum OpenPbrTextureFilter
{
    /// <summary>Selects the nearest texel.</summary>
    Closest,
    /// <summary>Interpolates four neighboring texels.</summary>
    Linear,
}

/// <summary>Owns an immutable, bounded FP32 material texture; no file access or decoding is implicit.</summary>
/// <remarks>UV (0,0) addresses the bottom-left corner, as in MaterialX. Supplied pixels run
/// top-to-bottom. Color textures are converted once to ACEScg; data textures retain their numbers.
/// Sampling is level zero, without automatic mip filtering. Replace the texture to edit it.</remarks>
public sealed class OpenPbrTexture
{
    /// <summary>Maximum texels per texture, limiting copied storage to 64 MiB.</summary>
    public const int MaximumPixels = 4_194_304;

    private OpenPbrTexture(int width, int height, Vector4[] pixels, bool isColor, string? source, string sourceColorSpace)
    {
        Width = width; Height = height; Pixels = new ReadOnlyCollection<Vector4>(pixels);
        IsColor = isColor; Source = source; SourceColorSpace = sourceColorSpace;
        Vector4 minimum = new(float.MaxValue), maximum = new(-float.MaxValue);
        foreach (Vector4 p in pixels)
        {
            RequireFinite(p);
            minimum = Vector4.Min(minimum, p); maximum = Vector4.Max(maximum, p);
        }
        Minimum = minimum; Maximum = maximum;
    }

    /// <summary>Copies linear color pixels and converts RGB to ACEScg, preserving alpha.</summary>
    /// <param name="image">Explicitly tagged, decoded linear pixels.</param>
    /// <param name="sourceColorSpace">Optional original resource color assignment retained for MaterialX export; pixels are already decoded linear.</param>
    /// <param name="source">Optional MaterialX filename identity; never automatically opened.</param>
    public static OpenPbrTexture FromColor(LinearRgbaImage image, string? source = null, string? sourceColorSpace = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        CheckSize(image.Width, image.Height);
        Vector4[] pixels = new Vector4[image.Pixels.Count];
        for (int i = 0; i < pixels.Length; i++)
        {
            Vector4 p = image.Pixels[i];
            LinearRgba c = StandardLinearRgbConverter.Convert(new(p.X, p.Y, p.Z, 1, image.ColorSpace), StandardColorSpaces.AcesCg);
            pixels[i] = p.X == p.Y && p.Y == p.Z ? p : new(c.Red, c.Green, c.Blue, p.W);
        }
        return new((int)image.Width, (int)image.Height, pixels, true, source, sourceColorSpace ?? (image.ColorSpace == StandardColorSpaces.AcesCg ? "acescg" :
            image.ColorSpace == StandardColorSpaces.LinearSrgb ? "lin_rec709" : throw new NotSupportedException("Supply the source resource colorspace identity for this wide-gamut image.")));
    }

    /// <summary>Copies raw channels for roughness, normals, opacity or other numeric inputs.</summary>
    public static OpenPbrTexture FromData(uint width, uint height, IEnumerable<Vector4> pixels, string? source = null)
    {
        CheckSize(width, height); ArgumentNullException.ThrowIfNull(pixels);
        int count = checked((int)(width * height));
        Vector4[] copy = pixels.Take(count + 1).ToArray();
        if (copy.Length != count) throw new ArgumentException("Texture pixel count must equal width times height.", nameof(pixels));
        return new((int)width, (int)height, copy, false, source, "raw");
    }

    /// <summary>Gets the texture width.</summary>
    public int Width { get; }
    /// <summary>Gets the texture height.</summary>
    public int Height { get; }
    /// <summary>Gets immutable top-to-bottom RGBA texels.</summary>
    public IReadOnlyList<Vector4> Pixels { get; }
    /// <summary>Gets whether RGB contains ACEScg color rather than raw data.</summary>
    public bool IsColor { get; }
    /// <summary>Gets the optional external resource identity for interchange.</summary>
    public string? Source { get; }
    /// <summary>Gets the original resource color assignment retained for interchange; it does not alter decoded pixels.</summary>
    public string SourceColorSpace { get; }
    /// <summary>Gets conservative per-component minimum values.</summary>
    public Vector4 Minimum { get; }
    /// <summary>Gets conservative per-component maximum values.</summary>
    public Vector4 Maximum { get; }

    internal static void RequireFinite(Vector4 p)
    {
        if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z) || !float.IsFinite(p.W))
            throw new ArgumentOutOfRangeException(nameof(p), "Material graph values must be finite.");
    }
    private static void CheckSize(uint width, uint height)
    {
        if (width == 0 || height == 0 || (ulong)width * height > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(width), "Texture dimensions exceed the material texture budget.");
    }
}

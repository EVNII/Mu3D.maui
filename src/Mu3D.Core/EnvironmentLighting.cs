using System.Collections.ObjectModel;
using System.Numerics;
using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>
/// Stores an immutable latitude-longitude HDR environment in an explicit linear-light RGB space.
/// Pixel values are scene-referred RGB radiance samples in row-major order; negative and above-one
/// components are preserved without tone mapping, gamut mapping or clipping.
/// </summary>
public sealed class EquirectangularHdrEnvironment
{
    /// <summary>Initializes an immutable explicitly tagged HDR environment.</summary>
    /// <param name="width">The non-zero image width.</param>
    /// <param name="height">The non-zero image height.</param>
    /// <param name="pixels">Exactly width times height row-major linear RGB values.</param>
    /// <param name="colorSpace">The linear-light RGB identity shared by every pixel.</param>
    /// <param name="name">An optional application-facing name.</param>
    public EquirectangularHdrEnvironment(
        uint width,
        uint height,
        IEnumerable<Vector3> pixels,
        ColorSpaceReference colorSpace,
        string? name = null)
    {
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(colorSpace);
        if (!colorSpace.IsLinear)
        {
            throw new ArgumentException("HDR environment pixels must be encoded in linear light.", nameof(colorSpace));
        }

        ulong expectedCount = checked((ulong)width * height);
        if (expectedCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The environment exceeds managed image limits.");
        }
        Vector3[] copiedPixels = [.. pixels];
        if ((ulong)copiedPixels.Length != expectedCount)
        {
            throw new ArgumentException("The pixel count must equal width times height.", nameof(pixels));
        }
        foreach (Vector3 pixel in copiedPixels)
        {
            if (!float.IsFinite(pixel.X) || !float.IsFinite(pixel.Y) || !float.IsFinite(pixel.Z))
            {
                throw new ArgumentOutOfRangeException(nameof(pixels), "Environment pixels must be finite.");
            }
        }

        Width = width;
        Height = height;
        Pixels = new ReadOnlyCollection<Vector3>(copiedPixels);
        ColorSpace = colorSpace;
        Name = name;
    }

    private EquirectangularHdrEnvironment(
        uint width,
        uint height,
        Vector3[] ownedPixels,
        ColorSpaceReference colorSpace,
        string? name)
    {
        Width = width;
        Height = height;
        Pixels = new ReadOnlyCollection<Vector3>(ownedPixels);
        ColorSpace = colorSpace;
        Name = name;
    }

    internal static EquirectangularHdrEnvironment FromTrustedOwnedPixels(
        uint width,
        uint height,
        Vector3[] ownedPixels,
        ColorSpaceReference colorSpace,
        string? name) =>
        new(width, height, ownedPixels, colorSpace, name);

    /// <summary>Gets the image width.</summary>
    public uint Width { get; }

    /// <summary>Gets the image height.</summary>
    public uint Height { get; }

    /// <summary>Gets the copied immutable row-major FP32 RGB samples.</summary>
    public IReadOnlyList<Vector3> Pixels { get; }

    /// <summary>Gets the explicit linear-light RGB space shared by the samples.</summary>
    public ColorSpaceReference ColorSpace { get; }

    /// <summary>Gets the optional application-facing name.</summary>
    public string? Name { get; }
}

/// <summary>
/// Represents a transformable image-based light source. Rotation controls environment orientation;
/// translation has no lighting effect. The current renderer provides diffuse irradiance and
/// roughness-filtered split-sum GGX specular reflection.
/// </summary>
public sealed class ImageBasedLight : SceneNode
{
    private EquirectangularHdrEnvironment environment;
    private float intensity;

    /// <summary>Initializes an image-based light.</summary>
    public ImageBasedLight(
        EquirectangularHdrEnvironment environment,
        float intensity = 1f,
        string? name = null)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ValidateIntensity(intensity);
        this.environment = environment;
        this.intensity = intensity;
    }

    /// <summary>Gets or sets the explicitly tagged immutable HDR environment.</summary>
    public EquirectangularHdrEnvironment Environment
    {
        get => environment;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            environment = value;
        }
    }

    /// <summary>Gets or sets the non-negative finite radiance multiplier.</summary>
    public float Intensity
    {
        get => intensity;
        set
        {
            ValidateIntensity(value);
            intensity = value;
        }
    }

    private static void ValidateIntensity(float value)
    {
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "IBL intensity must be non-negative and finite.");
        }
    }
}

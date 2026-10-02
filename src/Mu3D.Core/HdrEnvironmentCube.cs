using System.Collections.ObjectModel;
using System.Numerics;
using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>
/// Identifies one cube-map face and its zero-based GPU array-layer order.
/// </summary>
public enum EnvironmentCubeFace
{
    /// <summary>The positive X face at array layer 0.</summary>
    PositiveX = 0,

    /// <summary>The negative X face at array layer 1.</summary>
    NegativeX = 1,

    /// <summary>The positive Y face at array layer 2.</summary>
    PositiveY = 2,

    /// <summary>The negative Y face at array layer 3.</summary>
    NegativeY = 3,

    /// <summary>The positive Z face at array layer 4.</summary>
    PositiveZ = 4,

    /// <summary>The negative Z face at array layer 5.</summary>
    NegativeZ = 5,
}

/// <summary>
/// Stores an immutable square, six-face HDR environment cube in an explicit linear-light RGB
/// space. Pixels are face-major in <see cref="EnvironmentCubeFace"/> order and row-major within
/// each face. FP32 negative and above-one components are retained without clipping or tone mapping.
/// </summary>
public sealed class HdrEnvironmentCube
{
    internal HdrEnvironmentCube(
        uint faceSize,
        Vector3[] pixels,
        StandardRgbColorSpaceReference colorSpace,
        string? name)
    {
        FaceSize = faceSize;
        Pixels = new ReadOnlyCollection<Vector3>(pixels);
        ColorSpace = colorSpace;
        Name = name;
    }

    /// <summary>Gets the width and height of every square face.</summary>
    public uint FaceSize { get; }

    /// <summary>
    /// Gets the copied immutable FP32 RGB samples in face-major, then row-major, order.
    /// </summary>
    public IReadOnlyList<Vector3> Pixels { get; }

    /// <summary>Gets the explicit standard linear-light RGB space shared by the samples.</summary>
    public StandardRgbColorSpaceReference ColorSpace { get; }

    /// <summary>Gets the optional application-facing name.</summary>
    public string? Name { get; }

    /// <summary>Gets one pixel without changing its scene-linear numeric range.</summary>
    /// <param name="face">The cube face and corresponding array layer.</param>
    /// <param name="x">The horizontal coordinate within the face.</param>
    /// <param name="y">The vertical coordinate within the face.</param>
    /// <returns>The stored FP32 linear RGB sample.</returns>
    public Vector3 GetPixel(EnvironmentCubeFace face, uint x, uint y)
    {
        if ((uint)face >= 6u)
        {
            throw new ArgumentOutOfRangeException(nameof(face));
        }
        if (x >= FaceSize)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
        if (y >= FaceSize)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }

        ulong pixelsPerFace = checked((ulong)FaceSize * FaceSize);
        ulong index = checked((ulong)(uint)face * pixelsPerFace + (ulong)y * FaceSize + x);
        return Pixels[checked((int)index)];
    }
}

/// <summary>
/// Converts explicitly tagged latitude-longitude HDR environments into deterministic cube maps.
/// Filtering is performed in linear light. The horizontal longitude seam wraps and the poles clamp;
/// the conversion performs no gamut mapping, tone mapping or clipping.
/// </summary>
public static partial class HdrEnvironmentConverter
{
    private const int FaceCount = 6;

    /// <summary>
    /// Converts a latitude-longitude environment to six GPU-ready cube layers. Longitude zero maps
    /// to negative X, longitude one quarter to negative Z, one half to positive X and three quarters
    /// to positive Z. Face texels follow the conventional WebGPU cube directions.
    /// </summary>
    /// <param name="source">The immutable linear-light source environment.</param>
    /// <param name="faceSize">The non-zero output width and height of every face.</param>
    /// <param name="destinationColorSpace">The standard linear RGB space of the output pixels.</param>
    /// <param name="name">An optional output name; the source name is retained when omitted.</param>
    /// <returns>An immutable, face-major FP32 cube.</returns>
    /// <exception cref="NotSupportedException">
    /// The source has a color-space identity not yet handled by the standard RGB converter.
    /// </exception>
    public static HdrEnvironmentCube ConvertToCube(
        EquirectangularHdrEnvironment source,
        uint faceSize,
        StandardRgbColorSpaceReference destinationColorSpace,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfZero(faceSize);
        ArgumentNullException.ThrowIfNull(destinationColorSpace);

        (ulong pixelsPerFace, ulong outputCount) = GetOutputCounts(faceSize);
        Vector3[] convertedSource = ConvertSourcePixels(source, destinationColorSpace);

        Vector3[] output = new Vector3[checked((int)outputCount)];
        for (int faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            EnvironmentCubeFace face = (EnvironmentCubeFace)faceIndex;
            for (uint y = 0; y < faceSize; y++)
            {
                float t = 2f * ((y + 0.5f) / faceSize) - 1f;
                for (uint x = 0; x < faceSize; x++)
                {
                    float s = 2f * ((x + 0.5f) / faceSize) - 1f;
                    Vector3 direction = Vector3.Normalize(GetDirection(face, s, t));
                    Vector3 sampled = SampleEquirectangular(
                        convertedSource,
                        source.Width,
                        source.Height,
                        direction);
                    ulong outputIndex = checked((ulong)faceIndex * pixelsPerFace + (ulong)y * faceSize + x);
                    output[checked((int)outputIndex)] = sampled;
                }
            }
        }

        return new HdrEnvironmentCube(
            faceSize,
            output,
            destinationColorSpace,
            name ?? source.Name);
    }

    /// <summary>
    /// Produces a cosine-convolved diffuse irradiance cube from a latitude-longitude radiance map.
    /// Each output texel stores irradiance divided by pi, so a Lambertian shader multiplies it by
    /// diffuse reflectance directly. The environment is projected deterministically into three
    /// spherical-harmonic bands (nine coefficients), then convolved analytically with the Lambert
    /// kernel. This avoids high-frequency Monte Carlo noise from small, intense HDR light sources.
    /// </summary>
    /// <param name="source">The immutable scene-linear radiance environment.</param>
    /// <param name="faceSize">The non-zero output width and height of every face.</param>
    /// <param name="destinationColorSpace">The standard linear RGB space of the output pixels.</param>
    /// <param name="name">An optional output name.</param>
    /// <returns>An immutable FP32 diffuse-irradiance-over-pi cube.</returns>
    public static HdrEnvironmentCube CreateDiffuseIrradianceCube(
        EquirectangularHdrEnvironment source,
        uint faceSize,
        StandardRgbColorSpaceReference destinationColorSpace,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfZero(faceSize);
        ArgumentNullException.ThrowIfNull(destinationColorSpace);

        (ulong pixelsPerFace, ulong outputCount) = GetOutputCounts(faceSize);
        Vector3[] convertedSource = ConvertSourcePixels(source, destinationColorSpace);
        Vector3[] coefficients = ProjectThreeBandSphericalHarmonics(
            convertedSource,
            source.Width,
            source.Height);
        Vector3[] output = new Vector3[checked((int)outputCount)];
        Span<float> basis = stackalloc float[9];
        for (int faceIndex = 0; faceIndex < FaceCount; faceIndex++)
        {
            EnvironmentCubeFace face = (EnvironmentCubeFace)faceIndex;
            for (uint y = 0; y < faceSize; y++)
            {
                float t = 2f * ((y + 0.5f) / faceSize) - 1f;
                for (uint x = 0; x < faceSize; x++)
                {
                    float s = 2f * ((x + 0.5f) / faceSize) - 1f;
                    Vector3 normal = Vector3.Normalize(GetDirection(face, s, t));
                    EvaluateThreeBandSphericalHarmonics(normal, basis);
                    Vector3 sum = Vector3.Zero;
                    for (int coefficientIndex = 0; coefficientIndex < coefficients.Length; coefficientIndex++)
                    {
                        float lambertKernelOverPi = coefficientIndex switch
                        {
                            0 => 1f,
                            <= 3 => 2f / 3f,
                            _ => 1f / 4f,
                        };
                        sum += coefficients[coefficientIndex] *
                            (basis[coefficientIndex] * lambertKernelOverPi);
                    }

                    ulong outputIndex = checked((ulong)faceIndex * pixelsPerFace + (ulong)y * faceSize + x);
                    output[checked((int)outputIndex)] = sum;
                }
            }
        }

        return new HdrEnvironmentCube(
            faceSize,
            output,
            destinationColorSpace,
            name ?? $"{source.Name ?? "environment"} diffuse irradiance");
    }

    private static Vector3[] ProjectThreeBandSphericalHarmonics(
        IReadOnlyList<Vector3> pixels,
        uint width,
        uint height)
    {
        Vector3[] coefficients = new Vector3[9];
        Vector3 firstPixel = pixels[0];
        bool isConstant = true;
        for (int index = 1; index < pixels.Count; index++)
        {
            if (pixels[index] != firstPixel)
            {
                isConstant = false;
                break;
            }
        }
        if (isConstant)
        {
            coefficients[0] = firstPixel * (4f * MathF.PI * 0.2820947918f);
            return coefficients;
        }

        Span<float> basis = stackalloc float[9];
        float longitudeStep = 2f * MathF.PI / width;
        for (uint y = 0; y < height; y++)
        {
            float theta = MathF.PI * (y + 0.5f) / height;
            float rowSolidAngle = longitudeStep *
                (MathF.Cos(MathF.PI * y / height) - MathF.Cos(MathF.PI * (y + 1f) / height));
            float sinTheta = MathF.Sin(theta);
            float directionY = MathF.Cos(theta);
            for (uint x = 0; x < width; x++)
            {
                float phi = 2f * MathF.PI * ((x + 0.5f) / width - 0.5f);
                Vector3 direction = new(
                    sinTheta * MathF.Cos(phi),
                    directionY,
                    sinTheta * MathF.Sin(phi));
                EvaluateThreeBandSphericalHarmonics(direction, basis);
                Vector3 radiance = pixels[checked((int)((ulong)y * width + x))];
                for (int coefficientIndex = 0; coefficientIndex < coefficients.Length; coefficientIndex++)
                {
                    coefficients[coefficientIndex] += radiance * (basis[coefficientIndex] * rowSolidAngle);
                }
            }
        }
        return coefficients;
    }

    private static void EvaluateThreeBandSphericalHarmonics(Vector3 direction, Span<float> basis)
    {
        float x = direction.X;
        float y = direction.Y;
        float z = direction.Z;
        basis[0] = 0.2820947918f;
        basis[1] = 0.4886025119f * y;
        basis[2] = 0.4886025119f * z;
        basis[3] = 0.4886025119f * x;
        basis[4] = 1.092548431f * x * y;
        basis[5] = 1.092548431f * y * z;
        basis[6] = 0.3153915653f * (3f * z * z - 1f);
        basis[7] = 1.092548431f * x * z;
        basis[8] = 0.5462742153f * (x * x - y * y);
    }

    private static (ulong PixelsPerFace, ulong OutputCount) GetOutputCounts(uint faceSize)
    {
        ulong pixelsPerFace = checked((ulong)faceSize * faceSize);
        ulong outputCount = checked(pixelsPerFace * FaceCount);
        if (outputCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(faceSize), "The environment cube exceeds managed image limits.");
        }
        return (pixelsPerFace, outputCount);
    }

    private static Vector3[] ConvertSourcePixels(
        EquirectangularHdrEnvironment source,
        StandardRgbColorSpaceReference destinationColorSpace)
    {
        if (source.ColorSpace is not StandardRgbColorSpaceReference sourceColorSpace)
        {
            throw new NotSupportedException(
                $"Cube conversion cannot transform {source.ColorSpace.Name}; ICC-backed transforms are not implemented yet.");
        }

        Vector3[] convertedSource = new Vector3[source.Pixels.Count];
        for (int index = 0; index < source.Pixels.Count; index++)
        {
            Vector3 pixel = source.Pixels[index];
            LinearRgba converted = StandardLinearRgbConverter.Convert(
                new LinearRgba(pixel.X, pixel.Y, pixel.Z, 1f, sourceColorSpace),
                destinationColorSpace);
            convertedSource[index] = new Vector3(converted.Red, converted.Green, converted.Blue);
        }
        return convertedSource;
    }

    private static float RadicalInverseBaseTwo(uint bits)
    {
        float result = 0f;
        float place = 0.5f;
        while (bits != 0)
        {
            result += (bits & 1u) * place;
            bits >>= 1;
            place *= 0.5f;
        }
        return result;
    }

    private static Vector3 GetDirection(EnvironmentCubeFace face, float s, float t) => face switch
    {
        EnvironmentCubeFace.PositiveX => new Vector3(1f, -t, -s),
        EnvironmentCubeFace.NegativeX => new Vector3(-1f, -t, s),
        EnvironmentCubeFace.PositiveY => new Vector3(s, 1f, t),
        EnvironmentCubeFace.NegativeY => new Vector3(s, -1f, -t),
        EnvironmentCubeFace.PositiveZ => new Vector3(s, -t, 1f),
        EnvironmentCubeFace.NegativeZ => new Vector3(-s, -t, -1f),
        _ => throw new ArgumentOutOfRangeException(nameof(face)),
    };

    private static Vector3 SampleEquirectangular(
        Vector3[] pixels,
        uint width,
        uint height,
        Vector3 direction)
    {
        float u = MathF.Atan2(direction.Z, direction.X) / (2f * MathF.PI) + 0.5f;
        float v = MathF.Acos(Math.Clamp(direction.Y, -1f, 1f)) / MathF.PI;
        float sourceX = u * width - 0.5f;
        float sourceY = v * height - 0.5f;
        int x0Unwrapped = (int)MathF.Floor(sourceX);
        int y0Unclamped = (int)MathF.Floor(sourceY);
        float xBlend = sourceX - MathF.Floor(sourceX);
        float yBlend = sourceY - MathF.Floor(sourceY);
        int sourceWidth = checked((int)width);
        int sourceHeight = checked((int)height);
        int x0 = PositiveModulo(x0Unwrapped, sourceWidth);
        int x1 = PositiveModulo(x0Unwrapped + 1, sourceWidth);
        int y0 = Math.Clamp(y0Unclamped, 0, sourceHeight - 1);
        int y1 = Math.Clamp(y0Unclamped + 1, 0, sourceHeight - 1);

        Vector3 top = Vector3.Lerp(
            pixels[checked(y0 * sourceWidth + x0)],
            pixels[checked(y0 * sourceWidth + x1)],
            xBlend);
        Vector3 bottom = Vector3.Lerp(
            pixels[checked(y1 * sourceWidth + x0)],
            pixels[checked(y1 * sourceWidth + x1)],
            xBlend);
        return Vector3.Lerp(top, bottom, yBlend);
    }

    private static int PositiveModulo(int value, int divisor)
    {
        int remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}

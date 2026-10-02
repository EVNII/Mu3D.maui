using System.Collections.ObjectModel;
using System.Numerics;
using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>
/// Stores an immutable roughness-ordered mip chain of scene-linear specular environment cubes.
/// Mip zero represents roughness zero and the final mip represents roughness one.
/// </summary>
public sealed class PrefilteredEnvironmentCube
{
    internal PrefilteredEnvironmentCube(
        HdrEnvironmentCube[] mipLevels,
        StandardRgbColorSpaceReference colorSpace,
        string? name)
    {
        MipLevels = new ReadOnlyCollection<HdrEnvironmentCube>(mipLevels);
        ColorSpace = colorSpace;
        Name = name;
    }

    /// <summary>Gets the immutable cubes ordered from sharpest to roughest.</summary>
    public IReadOnlyList<HdrEnvironmentCube> MipLevels { get; }

    /// <summary>Gets the base face width and height.</summary>
    public uint BaseFaceSize => MipLevels[0].FaceSize;

    /// <summary>Gets the number of roughness mip levels.</summary>
    public uint MipLevelCount => checked((uint)MipLevels.Count);

    /// <summary>Gets the standard scene-linear RGB identity shared by all levels.</summary>
    public StandardRgbColorSpaceReference ColorSpace { get; }

    /// <summary>Gets the optional application-facing name.</summary>
    public string? Name { get; }

    /// <summary>Gets one roughness mip level.</summary>
    public HdrEnvironmentCube GetMipLevel(uint mipLevel)
    {
        if (mipLevel >= MipLevelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(mipLevel));
        }
        return MipLevels[checked((int)mipLevel)];
    }
}

/// <summary>
/// Stores immutable GGX split-sum and Charlie sheen integrations. X scales the base Fresnel
/// reflectance, Y supplies its grazing-angle remainder, and the matching sheen value supplies the
/// directional albedo of a unit-color Charlie lobe.
/// </summary>
public sealed class SplitSumBrdfLut
{
    internal SplitSumBrdfLut(uint size, Vector2[] values, float[] sheenDirectionalAlbedo)
    {
        Size = size;
        Values = new ReadOnlyCollection<Vector2>(values);
        SheenDirectionalAlbedo = new ReadOnlyCollection<float>(sheenDirectionalAlbedo);
    }

    /// <summary>Gets the square table width and height.</summary>
    public uint Size { get; }

    /// <summary>Gets the immutable row-major FP32 integration values.</summary>
    public IReadOnlyList<Vector2> Values { get; }

    /// <summary>Gets the immutable row-major Charlie sheen directional albedo.</summary>
    public IReadOnlyList<float> SheenDirectionalAlbedo { get; }

    /// <summary>Gets one table value.</summary>
    public Vector2 GetValue(uint x, uint y)
    {
        if (x >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
        if (y >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
        return Values[checked((int)((ulong)y * Size + x))];
    }

    /// <summary>Gets one Charlie sheen directional-albedo value.</summary>
    public float GetSheenDirectionalAlbedo(uint x, uint y)
    {
        if (x >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
        if (y >= Size)
        {
            throw new ArgumentOutOfRangeException(nameof(y));
        }
        return SheenDirectionalAlbedo[checked((int)((ulong)y * Size + x))];
    }
}

public static partial class HdrEnvironmentConverter
{
    /// <summary>
    /// Builds a GGX importance-sampled environment mip chain for split-sum specular IBL. Every
    /// level remains in the requested linear working space with no tone mapping or clipping. An
    /// energy-preserving source-radiance mip chain is sampled at a LOD derived from each GGX
    /// sample's probability density and spherical footprint, preventing isolated HDR texels from
    /// becoming Monte Carlo fireflies.
    /// </summary>
    public static PrefilteredEnvironmentCube CreateSpecularPrefilteredCube(
        EquirectangularHdrEnvironment source,
        uint baseFaceSize,
        uint mipLevelCount,
        uint sampleCount,
        StandardRgbColorSpaceReference destinationColorSpace,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfZero(baseFaceSize);
        ArgumentOutOfRangeException.ThrowIfZero(mipLevelCount);
        ArgumentOutOfRangeException.ThrowIfZero(sampleCount);
        ArgumentNullException.ThrowIfNull(destinationColorSpace);
        uint maximumMipCount = checked((uint)BitOperations.Log2(baseFaceSize) + 1u);
        if (mipLevelCount > maximumMipCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mipLevelCount),
                "The specular mip count exceeds the base cube's complete mip chain.");
        }

        Vector3[] convertedSource = ConvertSourcePixels(source, destinationColorSpace);
        IReadOnlyList<EquirectangularRadianceMip> sourceMipLevels =
            CreateEquirectangularRadianceMipChain(convertedSource, source.Width, source.Height);
        HdrEnvironmentCube[] mipLevels = new HdrEnvironmentCube[checked((int)mipLevelCount)];
        for (uint mipLevel = 0; mipLevel < mipLevelCount; mipLevel++)
        {
            uint faceSize = Math.Max(1u, baseFaceSize >> checked((int)mipLevel));
            float roughness = mipLevelCount == 1 ? 0f : mipLevel / (float)(mipLevelCount - 1u);
            (_, ulong outputCount) = GetOutputCounts(faceSize);
            Vector3[] output = new Vector3[checked((int)outputCount)];
            int faceSide = checked((int)faceSize);
            int texelsPerFace = checked(faceSide * faceSide);
            Parallel.For(0, checked(6 * texelsPerFace), outputIndex =>
            {
                int faceIndex = outputIndex / texelsPerFace;
                int facePixelIndex = outputIndex % texelsPerFace;
                uint y = checked((uint)(facePixelIndex / faceSide));
                uint x = checked((uint)(facePixelIndex % faceSide));
                EnvironmentCubeFace face = (EnvironmentCubeFace)faceIndex;
                float t = 2f * ((y + 0.5f) / faceSize) - 1f;
                float s = 2f * ((x + 0.5f) / faceSize) - 1f;
                Vector3 normal = Vector3.Normalize(GetDirection(face, s, t));
                output[outputIndex] = roughness == 0f
                    ? SampleEquirectangular(convertedSource, source.Width, source.Height, normal)
                    : PrefilterSpecular(
                        sourceMipLevels,
                        normal,
                        roughness,
                        sampleCount);
            });
            mipLevels[checked((int)mipLevel)] = new HdrEnvironmentCube(
                faceSize,
                output,
                destinationColorSpace,
                $"{name ?? source.Name ?? "environment"} specular mip {mipLevel}");
        }

        return new PrefilteredEnvironmentCube(
            mipLevels,
            destinationColorSpace,
            name ?? $"{source.Name ?? "environment"} specular prefilter");
    }

    /// <summary>
    /// Builds the Charlie-distribution environment mip chain required by sheen IBL. Renderers can
    /// pack this chain beside the GGX chain in one cube array without consuming another sampled
    /// texture binding.
    /// </summary>
    public static PrefilteredEnvironmentCube CreateSheenPrefilteredCube(
        EquirectangularHdrEnvironment source,
        uint baseFaceSize,
        uint mipLevelCount,
        uint sampleCount,
        StandardRgbColorSpaceReference destinationColorSpace,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfZero(baseFaceSize);
        ArgumentOutOfRangeException.ThrowIfZero(mipLevelCount);
        ArgumentOutOfRangeException.ThrowIfZero(sampleCount);
        ArgumentNullException.ThrowIfNull(destinationColorSpace);
        uint maximumMipCount = checked((uint)BitOperations.Log2(baseFaceSize) + 1u);
        if (mipLevelCount > maximumMipCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mipLevelCount),
                "The sheen mip count exceeds the base cube's complete mip chain.");
        }

        Vector3[] convertedSource = ConvertSourcePixels(source, destinationColorSpace);
        IReadOnlyList<EquirectangularRadianceMip> sourceMipLevels =
            CreateEquirectangularRadianceMipChain(convertedSource, source.Width, source.Height);
        HdrEnvironmentCube[] mipLevels = new HdrEnvironmentCube[checked((int)mipLevelCount)];
        for (uint mipLevel = 0; mipLevel < mipLevelCount; mipLevel++)
        {
            uint faceSize = Math.Max(1u, baseFaceSize >> checked((int)mipLevel));
            float roughness = mipLevelCount == 1
                ? 0.07f
                : MathF.Max(0.07f, mipLevel / (float)(mipLevelCount - 1u));
            (_, ulong outputCount) = GetOutputCounts(faceSize);
            Vector3[] output = new Vector3[checked((int)outputCount)];
            int faceSide = checked((int)faceSize);
            int texelsPerFace = checked(faceSide * faceSide);
            Parallel.For(0, checked(6 * texelsPerFace), outputIndex =>
            {
                int faceIndex = outputIndex / texelsPerFace;
                int facePixelIndex = outputIndex % texelsPerFace;
                uint y = checked((uint)(facePixelIndex / faceSide));
                uint x = checked((uint)(facePixelIndex % faceSide));
                EnvironmentCubeFace face = (EnvironmentCubeFace)faceIndex;
                float t = 2f * ((y + 0.5f) / faceSize) - 1f;
                float s = 2f * ((x + 0.5f) / faceSize) - 1f;
                Vector3 normal = Vector3.Normalize(GetDirection(face, s, t));
                output[outputIndex] = PrefilterSheen(
                    sourceMipLevels,
                    normal,
                    roughness,
                    sampleCount);
            });
            mipLevels[checked((int)mipLevel)] = new HdrEnvironmentCube(
                faceSize,
                output,
                destinationColorSpace,
                $"{name ?? source.Name ?? "environment"} sheen mip {mipLevel}");
        }

        return new PrefilteredEnvironmentCube(
            mipLevels,
            destinationColorSpace,
            name ?? $"{source.Name ?? "environment"} sheen prefilter");
    }

    /// <summary>
    /// Integrates the view-dependent half of the split-sum GGX approximation into a deterministic
    /// square FP32 lookup table. Texel centers cover N dot V and perceptual roughness in [0, 1].
    /// </summary>
    public static SplitSumBrdfLut CreateSplitSumBrdfLut(uint size, uint sampleCount)
    {
        ArgumentOutOfRangeException.ThrowIfZero(size);
        ArgumentOutOfRangeException.ThrowIfZero(sampleCount);
        ulong valueCount = checked((ulong)size * size);
        if (valueCount > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "The BRDF table exceeds managed image limits.");
        }

        Vector2[] values = new Vector2[checked((int)valueCount)];
        float[] sheenDirectionalAlbedo = new float[checked((int)valueCount)];
        int lutSide = checked((int)size);
        Parallel.For(0, checked((int)valueCount), valueIndex =>
        {
            uint y = checked((uint)(valueIndex / lutSide));
            uint x = checked((uint)(valueIndex % lutSide));
            float roughness = (y + 0.5f) / size;
            float normalDotView = (x + 0.5f) / size;
            values[valueIndex] = IntegrateBrdf(normalDotView, roughness, sampleCount);
            sheenDirectionalAlbedo[valueIndex] =
                IntegrateSheenDirectionalAlbedo(normalDotView, roughness, sampleCount);
        });
        return new SplitSumBrdfLut(size, values, sheenDirectionalAlbedo);
    }

    private static Vector3 PrefilterSpecular(
        IReadOnlyList<EquirectangularRadianceMip> sourceMipLevels,
        Vector3 normal,
        float roughness,
        uint sampleCount)
    {
        Vector3 view = normal;
        Vector3 sum = Vector3.Zero;
        float totalWeight = 0f;
        for (uint sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            Vector2 sequence = Hammersley(sampleIndex, sampleCount);
            Vector3 halfVector = ImportanceSampleGgx(sequence, normal, roughness);
            Vector3 light = Vector3.Normalize(2f * Vector3.Dot(view, halfVector) * halfVector - view);
            float normalDotLight = MathF.Max(Vector3.Dot(normal, light), 0f);
            if (normalDotLight > 0f)
            {
                float normalDotHalf = MathF.Max(Vector3.Dot(normal, halfVector), 0f);
                float viewDotHalf = MathF.Max(Vector3.Dot(view, halfVector), 0.000001f);
                float distribution = DistributionGgx(normalDotHalf, roughness);
                float probabilityDensity = MathF.Max(
                    distribution * normalDotHalf / (4f * viewDotHalf),
                    0.000001f);
                float sampleSolidAngle = 1f / (sampleCount * probabilityDensity);
                float sourceTexelSolidAngle = GetEquirectangularTexelSolidAngle(
                    sourceMipLevels[0].Width,
                    sourceMipLevels[0].Height,
                    light);
                float sourceLod = 0.5f * MathF.Log2(
                    MathF.Max(sampleSolidAngle / sourceTexelSolidAngle, 1f));
                sum += SampleEquirectangularLod(sourceMipLevels, light, sourceLod) * normalDotLight;
                totalWeight += normalDotLight;
            }
        }
        return totalWeight > 0f ? sum / totalWeight : Vector3.Zero;
    }

    private static Vector3 PrefilterSheen(
        IReadOnlyList<EquirectangularRadianceMip> sourceMipLevels,
        Vector3 normal,
        float roughness,
        uint sampleCount)
    {
        Vector3 view = normal;
        Vector3 sum = Vector3.Zero;
        float totalWeight = 0f;
        for (uint sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            Vector3 halfVector = ImportanceSampleCharlie(Hammersley(sampleIndex, sampleCount), normal, roughness);
            Vector3 light = Vector3.Normalize(2f * Vector3.Dot(view, halfVector) * halfVector - view);
            float normalDotLight = MathF.Max(Vector3.Dot(normal, light), 0f);
            if (normalDotLight <= 0f)
            {
                continue;
            }

            float normalDotHalf = MathF.Max(Vector3.Dot(normal, halfVector), 0f);
            float viewDotHalf = MathF.Max(Vector3.Dot(view, halfVector), 0.000001f);
            float probabilityDensity = MathF.Max(
                DistributionCharlie(normalDotHalf, roughness) * normalDotHalf /
                    (4f * viewDotHalf),
                0.000001f);
            float sampleSolidAngle = 1f / (sampleCount * probabilityDensity);
            float sourceTexelSolidAngle = GetEquirectangularTexelSolidAngle(
                sourceMipLevels[0].Width,
                sourceMipLevels[0].Height,
                light);
            float sourceLod = 0.5f * MathF.Log2(
                MathF.Max(sampleSolidAngle / sourceTexelSolidAngle, 1f));
            sum += SampleEquirectangularLod(sourceMipLevels, light, sourceLod) * normalDotLight;
            totalWeight += normalDotLight;
        }
        return totalWeight > 0f ? sum / totalWeight : Vector3.Zero;
    }

    private static IReadOnlyList<EquirectangularRadianceMip> CreateEquirectangularRadianceMipChain(
        Vector3[] basePixels,
        uint width,
        uint height)
    {
        List<EquirectangularRadianceMip> levels = [new(width, height, basePixels)];
        while (width > 1u || height > 1u)
        {
            EquirectangularRadianceMip source = levels[^1];
            uint nextWidth = Math.Max(1u, (source.Width + 1u) / 2u);
            uint nextHeight = Math.Max(1u, (source.Height + 1u) / 2u);
            Vector3[] nextPixels = new Vector3[checked((int)((ulong)nextWidth * nextHeight))];
            for (uint y = 0; y < nextHeight; y++)
            {
                uint sourceY0 = Math.Min(y * 2u, source.Height - 1u);
                uint sourceY1 = Math.Min(sourceY0 + 1u, source.Height - 1u);
                float rowWeight0 = GetEquirectangularRowSolidAngleWeight(sourceY0, source.Height);
                float rowWeight1 = sourceY1 == sourceY0
                    ? 0f
                    : GetEquirectangularRowSolidAngleWeight(sourceY1, source.Height);
                for (uint x = 0; x < nextWidth; x++)
                {
                    uint sourceX0 = Math.Min(x * 2u, source.Width - 1u);
                    uint sourceX1 = Math.Min(sourceX0 + 1u, source.Width - 1u);
                    Vector3 sum = source.Pixels[checked((int)((ulong)sourceY0 * source.Width + sourceX0))] *
                        rowWeight0;
                    float totalWeight = rowWeight0;
                    if (sourceX1 != sourceX0)
                    {
                        sum += source.Pixels[checked((int)((ulong)sourceY0 * source.Width + sourceX1))] *
                            rowWeight0;
                        totalWeight += rowWeight0;
                    }
                    if (sourceY1 != sourceY0)
                    {
                        sum += source.Pixels[checked((int)((ulong)sourceY1 * source.Width + sourceX0))] *
                            rowWeight1;
                        totalWeight += rowWeight1;
                        if (sourceX1 != sourceX0)
                        {
                            sum += source.Pixels[checked((int)((ulong)sourceY1 * source.Width + sourceX1))] *
                                rowWeight1;
                            totalWeight += rowWeight1;
                        }
                    }
                    nextPixels[checked((int)((ulong)y * nextWidth + x))] = sum / totalWeight;
                }
            }
            levels.Add(new EquirectangularRadianceMip(nextWidth, nextHeight, nextPixels));
            width = nextWidth;
            height = nextHeight;
        }
        return levels;
    }

    private static Vector3 SampleEquirectangularLod(
        IReadOnlyList<EquirectangularRadianceMip> levels,
        Vector3 direction,
        float lod)
    {
        float clampedLod = Math.Clamp(lod, 0f, levels.Count - 1f);
        int lowerIndex = (int)MathF.Floor(clampedLod);
        int upperIndex = Math.Min(lowerIndex + 1, levels.Count - 1);
        EquirectangularRadianceMip lower = levels[lowerIndex];
        Vector3 lowerValue = SampleEquirectangular(
            lower.Pixels,
            lower.Width,
            lower.Height,
            direction);
        if (lowerIndex == upperIndex)
        {
            return lowerValue;
        }
        EquirectangularRadianceMip upper = levels[upperIndex];
        Vector3 upperValue = SampleEquirectangular(
            upper.Pixels,
            upper.Width,
            upper.Height,
            direction);
        return Vector3.Lerp(lowerValue, upperValue, clampedLod - lowerIndex);
    }

    private static float GetEquirectangularTexelSolidAngle(
        uint width,
        uint height,
        Vector3 direction)
    {
        float verticalCoordinate = MathF.Acos(Math.Clamp(direction.Y, -1f, 1f)) / MathF.PI;
        uint row = Math.Min((uint)(verticalCoordinate * height), height - 1u);
        return (2f * MathF.PI / width) * GetEquirectangularRowSolidAngleWeight(row, height);
    }

    private static float GetEquirectangularRowSolidAngleWeight(uint row, uint height) =>
        (float)(
            Math.Cos(Math.PI * row / height) -
            Math.Cos(Math.PI * (row + 1u) / height));

    private static float DistributionGgx(float normalDotHalf, float roughness)
    {
        float alpha = roughness * roughness;
        float alphaSquared = alpha * alpha;
        float denominator = normalDotHalf * normalDotHalf * (alphaSquared - 1f) + 1f;
        return alphaSquared / MathF.Max(MathF.PI * denominator * denominator, 0.000001f);
    }

    private static float DistributionCharlie(float normalDotHalf, float roughness)
    {
        float alpha = MathF.Max(roughness * roughness, 0.0001f);
        float inverseAlpha = 1f / alpha;
        float sineSquared = MathF.Max(1f - normalDotHalf * normalDotHalf, 0f);
        return (2f + inverseAlpha) *
            MathF.Pow(sineSquared, inverseAlpha * 0.5f) /
            (2f * MathF.PI);
    }

    private readonly record struct EquirectangularRadianceMip(
        uint Width,
        uint Height,
        Vector3[] Pixels);

    private static Vector2 IntegrateBrdf(float normalDotView, float roughness, uint sampleCount)
    {
        Vector3 normal = Vector3.UnitZ;
        Vector3 view = new(MathF.Sqrt(MathF.Max(1f - normalDotView * normalDotView, 0f)), 0f, normalDotView);
        float scale = 0f;
        float bias = 0f;
        for (uint sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            Vector3 halfVector = ImportanceSampleGgx(Hammersley(sampleIndex, sampleCount), normal, roughness);
            Vector3 light = Vector3.Normalize(2f * Vector3.Dot(view, halfVector) * halfVector - view);
            float normalDotLight = MathF.Max(light.Z, 0f);
            float normalDotHalf = MathF.Max(halfVector.Z, 0f);
            float viewDotHalf = MathF.Max(Vector3.Dot(view, halfVector), 0f);
            if (normalDotLight > 0f && normalDotHalf > 0f)
            {
                float geometry = GeometrySmithIbl(normalDotView, normalDotLight, roughness);
                float visibility = geometry * viewDotHalf /
                    MathF.Max(normalDotHalf * normalDotView, 0.000001f);
                float fresnel = MathF.Pow(1f - viewDotHalf, 5f);
                scale += (1f - fresnel) * visibility;
                bias += fresnel * visibility;
            }
        }
        return new Vector2(scale / sampleCount, bias / sampleCount);
    }

    private static float IntegrateSheenDirectionalAlbedo(
        float normalDotView,
        float roughness,
        uint sampleCount)
    {
        Vector3 view = new(
            MathF.Sqrt(MathF.Max(1f - normalDotView * normalDotView, 0f)),
            0f,
            normalDotView);
        float alpha = MathF.Max(roughness * roughness, 0.0001f);
        float sum = 0f;
        for (uint sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            Vector2 sequence = Hammersley(sampleIndex, sampleCount);
            float normalDotLight = sequence.Y;
            float sineTheta = MathF.Sqrt(MathF.Max(1f - normalDotLight * normalDotLight, 0f));
            float phi = 2f * MathF.PI * sequence.X;
            Vector3 light = new(sineTheta * MathF.Cos(phi), sineTheta * MathF.Sin(phi), normalDotLight);
            Vector3 halfVector = Vector3.Normalize(view + light);
            float normalDotHalf = MathF.Max(halfVector.Z, 0f);
            float brdf = DistributionCharlie(normalDotHalf, roughness) *
                VisibilitySheen(normalDotView, normalDotLight, alpha);
            // Uniform-hemisphere PDF is 1 / (2 pi).
            sum += brdf * normalDotLight * (2f * MathF.PI);
        }
        return Math.Clamp(sum / sampleCount, 0f, 1f);
    }

    private static Vector2 Hammersley(uint index, uint count) =>
        new((index + 0.5f) / count, RadicalInverseBaseTwo(index));

    private static Vector3 ImportanceSampleGgx(Vector2 sequence, Vector3 normal, float roughness)
    {
        float alpha = roughness * roughness;
        float alphaSquared = alpha * alpha;
        float phi = 2f * MathF.PI * sequence.X;
        float cosineTheta = MathF.Sqrt(
            (1f - sequence.Y) / MathF.Max(1f + (alphaSquared - 1f) * sequence.Y, 0.000001f));
        float sineTheta = MathF.Sqrt(MathF.Max(1f - cosineTheta * cosineTheta, 0f));
        Vector3 local = new(sineTheta * MathF.Cos(phi), sineTheta * MathF.Sin(phi), cosineTheta);
        Vector3 helper = MathF.Abs(normal.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(helper, normal));
        Vector3 bitangent = Vector3.Cross(normal, tangent);
        return Vector3.Normalize(tangent * local.X + bitangent * local.Y + normal * local.Z);
    }

    private static Vector3 ImportanceSampleCharlie(
        Vector2 sequence,
        Vector3 normal,
        float roughness)
    {
        float alpha = MathF.Max(roughness * roughness, 0.0001f);
        float inverseAlpha = 1f / alpha;
        float sineTheta = MathF.Pow(sequence.Y, 1f / (inverseAlpha + 2f));
        float cosineTheta = MathF.Sqrt(MathF.Max(1f - sineTheta * sineTheta, 0f));
        float phi = 2f * MathF.PI * sequence.X;
        Vector3 local = new(sineTheta * MathF.Cos(phi), sineTheta * MathF.Sin(phi), cosineTheta);
        Vector3 helper = MathF.Abs(normal.Z) < 0.999f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(helper, normal));
        Vector3 bitangent = Vector3.Cross(normal, tangent);
        return Vector3.Normalize(tangent * local.X + bitangent * local.Y + normal * local.Z);
    }

    private static float VisibilitySheen(float normalDotView, float normalDotLight, float alpha)
    {
        if (normalDotView <= 0.0001f || normalDotLight <= 0.0001f)
        {
            return 0f;
        }
        return MathF.Min(
            64f,
            1f / MathF.Max(
                (1f + SheenLambda(normalDotView, alpha) + SheenLambda(normalDotLight, alpha)) *
                    (4f * normalDotView * normalDotLight),
                0.0001f));
    }

    private static float SheenLambda(float cosine, float alpha)
    {
        float fitted = MathF.Abs(cosine) < 0.5f
            ? 2f * SheenLambdaFit(0.5f, alpha) - SheenLambdaFit(1f - cosine, alpha)
            : SheenLambdaFit(cosine, alpha);
        return MathF.Exp(fitted);
    }

    private static float SheenLambdaFit(float cosine, float alpha)
    {
        float weight = (1f - alpha) * (1f - alpha);
        float a = Lerp(21.5473f, 25.3245f, weight);
        float b = Lerp(3.82987f, 3.32435f, weight);
        float c = Lerp(0.19823f, 0.16801f, weight);
        float d = Lerp(-1.97760f, -1.27393f, weight);
        float e = Lerp(-4.32054f, -4.85967f, weight);
        return a / (1f + b * MathF.Pow(MathF.Max(cosine, 0.000001f), c)) + d * cosine + e;
    }

    private static float Lerp(float start, float end, float amount) =>
        start + (end - start) * amount;

    private static float GeometrySmithIbl(float normalDotView, float normalDotLight, float roughness)
    {
        float k = roughness * roughness * 0.5f;
        float view = normalDotView / MathF.Max(normalDotView * (1f - k) + k, 0.000001f);
        float light = normalDotLight / MathF.Max(normalDotLight * (1f - k) + k, 0.000001f);
        return view * light;
    }
}

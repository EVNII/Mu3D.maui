using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

// ADR 0030 Fast environment: Core's public split-sum converters (GGX prefiltered cube, Charlie
// sheen chain, BRDF integration LUT, SH diffuse irradiance) prepared once per source environment
// and uploaded as a diffuse cube, one 12-layer GGX+Charlie FP16 cube array and an FP16 LUT.
// The reference modes' unfilterable FP32 equirect path is untouched.
internal sealed class OpenPbrFastEnvironment : IDisposable
{
    internal const uint DiffuseFaceSize = 32;
    internal const uint SpecularFaceSize = 64;
    internal const uint SpecularMipCount = 7;
    private const uint SampleCount = 256;

    private static readonly ConditionalWeakTable<EquirectangularHdrEnvironment, Lazy<Prepared>> PreparedCache = new();
    private static readonly Lazy<SplitSumBrdfLut> SharedLut =
        new(() => HdrEnvironmentConverter.CreateSplitSumBrdfLut(64, SampleCount), LazyThreadSafetyMode.ExecutionAndPublication);

    internal sealed class Prepared
    {
        internal required HdrEnvironmentCube Diffuse { get; init; }
        internal required PrefilteredEnvironmentCube Ggx { get; init; }
        internal required PrefilteredEnvironmentCube Sheen { get; init; }
        internal required SplitSumBrdfLut Lut { get; init; }
    }

    internal GraphicsTexture DiffuseTexture { get; }
    internal GraphicsTextureView DiffuseView { get; }
    internal GraphicsTexture SpecularTexture { get; }
    internal GraphicsTextureView SpecularView { get; }
    internal GraphicsTexture LutTexture { get; }
    internal GraphicsTextureView LutView { get; }
    internal GraphicsSampler Sampler { get; }

    private OpenPbrFastEnvironment(GraphicsTexture diffuse, GraphicsTextureView diffuseView,
        GraphicsTexture specular, GraphicsTextureView specularView,
        GraphicsTexture lut, GraphicsTextureView lutView, GraphicsSampler sampler)
    {
        DiffuseTexture = diffuse; DiffuseView = diffuseView;
        SpecularTexture = specular; SpecularView = specularView;
        LutTexture = lut; LutView = lutView; Sampler = sampler;
    }

    internal static Prepared Prepare(EquirectangularHdrEnvironment environment) =>
        PreparedCache.GetValue(environment, e => new Lazy<Prepared>(() => PrepareCore(e), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private static Prepared PrepareCore(EquirectangularHdrEnvironment environment)
    {
        // The reference upload rejects negative ACEScg radiance; keep the same contract here.
        foreach (Vector3 pixel in environment.Pixels)
            OpenPbrRenderPass.Radiance(new(pixel.X, pixel.Y, pixel.Z, 1, environment.ColorSpace));
        return new Prepared
        {
            Diffuse = HdrEnvironmentConverter.CreateDiffuseIrradianceCube(environment, DiffuseFaceSize, StandardColorSpaces.AcesCg),
            Ggx = HdrEnvironmentConverter.CreateSpecularPrefilteredCube(environment, SpecularFaceSize, SpecularMipCount, SampleCount, StandardColorSpaces.AcesCg),
            Sheen = HdrEnvironmentConverter.CreateSheenPrefilteredCube(environment, SpecularFaceSize, SpecularMipCount, SampleCount, StandardColorSpaces.AcesCg),
            Lut = SharedLut.Value,
        };
    }

    internal static OpenPbrFastEnvironment Create(GraphicsDevice device, EquirectangularHdrEnvironment? environment)
    {
        ArgumentNullException.ThrowIfNull(device);
        Prepared? prepared = environment is null ? null : Prepare(environment);
        List<IDisposable> owned = [];
        try
        {
            GraphicsTexture diffuse = device.CreateTexture(new GraphicsTextureDescriptor(
                new GraphicsExtent3D(DiffuseFaceSize, DiffuseFaceSize, 6), GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                label: "OpenPBR Fast diffuse irradiance cube"));
            owned.Add(diffuse);
            WriteCube(device, diffuse, 0, prepared?.Diffuse, DiffuseFaceSize);
            GraphicsTextureView diffuseView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                diffuse, GraphicsTextureViewDimension.Cube, mipLevelCount: 1, arrayLayerCount: 6,
                label: "OpenPBR Fast diffuse cube view"));
            owned.Add(diffuseView);

            GraphicsTexture specular = device.CreateTexture(new GraphicsTextureDescriptor(
                new GraphicsExtent3D(SpecularFaceSize, SpecularFaceSize, 12), GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                mipLevelCount: SpecularMipCount, label: "OpenPBR Fast GGX+Charlie cube array"));
            owned.Add(specular);
            for (uint mip = 0; mip < SpecularMipCount; mip++)
            {
                uint face = Math.Max(1, SpecularFaceSize >> (int)mip);
                WriteCube(device, specular, mip, prepared?.Ggx.GetMipLevel(mip), face, 0);
                WriteCube(device, specular, mip, prepared?.Sheen.GetMipLevel(mip), face, 6);
            }
            GraphicsTextureView specularView = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                specular, GraphicsTextureViewDimension.CubeArray, mipLevelCount: SpecularMipCount, arrayLayerCount: 12,
                label: "OpenPBR Fast specular cube-array view"));
            owned.Add(specularView);

            SplitSumBrdfLut lut = SharedLut.Value;
            GraphicsTexture lutTexture = device.CreateTexture(new GraphicsTextureDescriptor(
                new GraphicsExtent3D(lut.Size, lut.Size), GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                label: "OpenPBR Fast BRDF LUT"));
            owned.Add(lutTexture);
            Half[] lutTexels = new Half[checked(lut.Values.Count * 4)];
            for (int i = 0; i < lut.Values.Count; i++)
            {
                lutTexels[i * 4] = CheckedHalf(lut.Values[i].X, "BRDF LUT");
                lutTexels[i * 4 + 1] = CheckedHalf(lut.Values[i].Y, "BRDF LUT");
                lutTexels[i * 4 + 2] = CheckedHalf(lut.SheenDirectionalAlbedo[i], "BRDF LUT");
                lutTexels[i * 4 + 3] = (Half)1f;
            }
            device.Queue.WriteTexture(lutTexture, 0, default, new GraphicsExtent3D(lut.Size, lut.Size),
                MemoryMarshal.AsBytes(lutTexels.AsSpan()), checked(lut.Size * 4u * sizeof(ushort)), lut.Size);
            GraphicsTextureView lutView = device.CreateTextureView(new GraphicsTextureViewDescriptor(lutTexture, label: "OpenPBR Fast BRDF LUT view"));
            owned.Add(lutView);

            GraphicsSampler sampler = device.CreateSampler(new GraphicsSamplerDescriptor(
                magFilter: GraphicsFilterMode.Linear, minFilter: GraphicsFilterMode.Linear,
                mipmapFilter: GraphicsFilterMode.Linear, label: "OpenPBR Fast environment sampler"));
            owned.Add(sampler);
            owned.Clear();
            return new OpenPbrFastEnvironment(diffuse, diffuseView, specular, specularView, lutTexture, lutView, sampler);
        }
        catch
        {
            for (int i = owned.Count - 1; i >= 0; i--) owned[i].Dispose();
            throw;
        }
    }

    private static void WriteCube(GraphicsDevice device, GraphicsTexture texture, uint mip,
        HdrEnvironmentCube? cube, uint faceSize, uint baseLayer = 0)
    {
        int texelCount = checked((int)(faceSize * faceSize * 6));
        Half[] texels = new Half[texelCount * 4];
        if (cube is not null)
        {
            if (cube.FaceSize != faceSize || cube.Pixels.Count != texelCount)
                throw new ArgumentException("The environment cube dimensions do not match its chain level.", nameof(cube));
            for (int i = 0; i < texelCount; i++)
            {
                Vector3 p = cube.Pixels[i];
                texels[i * 4] = CheckedHalf(p.X, "environment");
                texels[i * 4 + 1] = CheckedHalf(p.Y, "environment");
                texels[i * 4 + 2] = CheckedHalf(p.Z, "environment");
                texels[i * 4 + 3] = (Half)1f;
            }
        }
        device.Queue.WriteTexture(texture, mip, new GraphicsOrigin3D(0, 0, baseLayer),
            new GraphicsExtent3D(faceSize, faceSize, 6), MemoryMarshal.AsBytes(texels.AsSpan()),
            checked(faceSize * 4u * sizeof(ushort)), faceSize);
    }

    internal static Half CheckedHalf(float value, string what) =>
        Half.IsFinite((Half)value) ? (Half)value :
        throw new InvalidOperationException($"OpenPBR Fast {what} data exceeds finite FP16 storage.");

    public void Dispose()
    {
        Sampler.Dispose(); LutView.Dispose(); LutTexture.Dispose();
        SpecularView.Dispose(); SpecularTexture.Dispose(); DiffuseView.Dispose(); DiffuseTexture.Dispose();
    }
}

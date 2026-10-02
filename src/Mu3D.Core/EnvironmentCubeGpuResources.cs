using System.Numerics;
using System.Runtime.InteropServices;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering;

/// <summary>
/// Owns the backend-independent GPU resources prepared from one scene-linear environment cube.
/// This remains internal so scene APIs do not expose renderer-owned texture or sampler handles.
/// </summary>
internal sealed class EnvironmentCubeGpuResources : IDisposable
{
    private bool disposed;

    private EnvironmentCubeGpuResources(
        GraphicsTexture texture,
        GraphicsTextureView view,
        GraphicsSampler sampler)
    {
        Texture = texture;
        View = view;
        Sampler = sampler;
    }

    internal GraphicsTexture Texture { get; }

    internal GraphicsTextureView View { get; }

    internal GraphicsSampler Sampler { get; }

    internal static EnvironmentCubeGpuResources Create(
        GraphicsDevice device,
        HdrEnvironmentCube environment) =>
        Create(device, [environment], environment.Name);

    internal static EnvironmentCubeGpuResources Create(
        GraphicsDevice device,
        PrefilteredEnvironmentCube environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        return Create(device, environment.MipLevels, environment.Name);
    }

    internal static EnvironmentCubeGpuResources CreateSpecularArray(
        GraphicsDevice device,
        PrefilteredEnvironmentCube ggx,
        PrefilteredEnvironmentCube sheen)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(ggx);
        ArgumentNullException.ThrowIfNull(sheen);
        if (ggx.BaseFaceSize != sheen.BaseFaceSize || ggx.MipLevelCount != sheen.MipLevelCount)
        {
            throw new ArgumentException("GGX and sheen cube chains must have identical dimensions.");
        }

        ValidateMipChain(ggx.MipLevels, nameof(ggx));
        ValidateMipChain(sheen.MipLevels, nameof(sheen));
        string label = ggx.Name ?? sheen.Name ?? "environment specular";
        GraphicsTexture? texture = null;
        GraphicsTextureView? view = null;
        GraphicsSampler? sampler = null;
        try
        {
            texture = device.CreateTexture(new GraphicsTextureDescriptor(
                new GraphicsExtent3D(ggx.BaseFaceSize, ggx.BaseFaceSize, 12),
                GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                mipLevelCount: ggx.MipLevelCount,
                label: $"{label} GGX+Charlie FP16 cube array"));
            WriteMipChain(device, texture, ggx.MipLevels, 0);
            WriteMipChain(device, texture, sheen.MipLevels, 6);
            view = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                texture,
                GraphicsTextureViewDimension.CubeArray,
                mipLevelCount: ggx.MipLevelCount,
                arrayLayerCount: 12,
                label: $"{label} GGX+Charlie cube-array view"));
            sampler = device.CreateSampler(new GraphicsSamplerDescriptor(
                magFilter: GraphicsFilterMode.Linear,
                minFilter: GraphicsFilterMode.Linear,
                mipmapFilter: GraphicsFilterMode.Linear,
                label: $"{label} sampler"));
            return new EnvironmentCubeGpuResources(texture, view, sampler);
        }
        catch
        {
            sampler?.Dispose();
            view?.Dispose();
            texture?.Dispose();
            throw;
        }
    }

    internal static EnvironmentCubeGpuResources CreateSpecularArray(
        GraphicsDevice device,
        HdrEnvironmentCube environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        PrefilteredEnvironmentCube chain = new(
            [environment],
            environment.ColorSpace,
            environment.Name);
        return CreateSpecularArray(device, chain, chain);
    }

    private static EnvironmentCubeGpuResources Create(
        GraphicsDevice device,
        IReadOnlyList<HdrEnvironmentCube> mipLevels,
        string? name)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (mipLevels.Count == 0)
        {
            throw new ArgumentException("At least one environment mip is required.", nameof(mipLevels));
        }
        ValidateMipChain(mipLevels, nameof(mipLevels));

        GraphicsTexture? texture = null;
        GraphicsTextureView? view = null;
        GraphicsSampler? sampler = null;
        try
        {
            texture = device.CreateTexture(new GraphicsTextureDescriptor(
                new GraphicsExtent3D(mipLevels[0].FaceSize, mipLevels[0].FaceSize, 6),
                GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                mipLevelCount: checked((uint)mipLevels.Count),
                label: $"{name ?? "environment"} FP16 cube"));
            WriteMipChain(device, texture, mipLevels, 0);
            view = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                texture,
                GraphicsTextureViewDimension.Cube,
                mipLevelCount: checked((uint)mipLevels.Count),
                arrayLayerCount: 6,
                label: $"{name ?? "environment"} cube view"));
            sampler = device.CreateSampler(new GraphicsSamplerDescriptor(
                magFilter: GraphicsFilterMode.Linear,
                minFilter: GraphicsFilterMode.Linear,
                mipmapFilter: GraphicsFilterMode.Linear,
                label: $"{name ?? "environment"} sampler"));
            return new EnvironmentCubeGpuResources(texture, view, sampler);
        }
        catch
        {
            sampler?.Dispose();
            view?.Dispose();
            texture?.Dispose();
            throw;
        }
    }

    private static void ValidateMipChain(IReadOnlyList<HdrEnvironmentCube> mipLevels, string parameterName)
    {
        for (int mipLevel = 0; mipLevel < mipLevels.Count; mipLevel++)
        {
            uint expectedSize = Math.Max(1u, mipLevels[0].FaceSize >> mipLevel);
            if (mipLevels[mipLevel].FaceSize != expectedSize)
            {
                throw new ArgumentException(
                    "Environment mip dimensions must halve at every level.",
                    parameterName);
            }
        }
    }

    private static void WriteMipChain(
        GraphicsDevice device,
        GraphicsTexture texture,
        IReadOnlyList<HdrEnvironmentCube> mipLevels,
        uint baseArrayLayer)
    {
        for (int mipLevel = 0; mipLevel < mipLevels.Count; mipLevel++)
        {
            HdrEnvironmentCube environmentMip = mipLevels[mipLevel];
            Half[] texels = PackFp16(environmentMip);
            uint bytesPerRow = checked(environmentMip.FaceSize * 4u * sizeof(ushort));
            device.Queue.WriteTexture(
                texture,
                checked((uint)mipLevel),
                new GraphicsOrigin3D(0, 0, baseArrayLayer),
                new GraphicsExtent3D(environmentMip.FaceSize, environmentMip.FaceSize, 6),
                MemoryMarshal.AsBytes(texels.AsSpan()),
                bytesPerRow,
                environmentMip.FaceSize);
        }
    }

    private static Half[] PackFp16(HdrEnvironmentCube environment)
    {
        Half[] texels = new Half[checked(environment.Pixels.Count * 4)];
        for (int pixelIndex = 0; pixelIndex < environment.Pixels.Count; pixelIndex++)
        {
            Vector3 pixel = environment.Pixels[pixelIndex];
            Half red = (Half)pixel.X;
            Half green = (Half)pixel.Y;
            Half blue = (Half)pixel.Z;
            if (!Half.IsFinite(red) || !Half.IsFinite(green) || !Half.IsFinite(blue))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(environment),
                    "An environment component exceeds finite FP16 storage; use a future FP32 environment preset.");
            }

            int componentIndex = checked(pixelIndex * 4);
            texels[componentIndex] = red;
            texels[componentIndex + 1] = green;
            texels[componentIndex + 2] = blue;
            texels[componentIndex + 3] = (Half)1f;
        }
        return texels;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        Sampler.Dispose();
        View.Dispose();
        Texture.Dispose();
    }
}

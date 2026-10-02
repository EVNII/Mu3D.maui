using System.Runtime.InteropServices;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering;

internal sealed class BrdfLutGpuResources : IDisposable
{
    private bool disposed;

    private BrdfLutGpuResources(GraphicsTexture texture, GraphicsTextureView view)
    {
        Texture = texture;
        View = view;
    }

    internal GraphicsTexture Texture { get; }

    internal GraphicsTextureView View { get; }

    internal static BrdfLutGpuResources Create(GraphicsDevice device, SplitSumBrdfLut lut)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(lut);
        Half[] texels = new Half[checked(lut.Values.Count * 4)];
        for (int index = 0; index < lut.Values.Count; index++)
        {
            Half scale = (Half)lut.Values[index].X;
            Half bias = (Half)lut.Values[index].Y;
            Half sheenDirectionalAlbedo = (Half)lut.SheenDirectionalAlbedo[index];
            if (!Half.IsFinite(scale) || !Half.IsFinite(bias) || !Half.IsFinite(sheenDirectionalAlbedo))
            {
                throw new ArgumentOutOfRangeException(nameof(lut), "The BRDF table exceeds finite FP16 storage.");
            }
            texels[index * 4] = scale;
            texels[index * 4 + 1] = bias;
            texels[index * 4 + 2] = sheenDirectionalAlbedo;
            texels[index * 4 + 3] = (Half)1f;
        }

        GraphicsTexture? texture = null;
        GraphicsTextureView? view = null;
        try
        {
            texture = device.CreateTexture(new GraphicsTextureDescriptor(
                new GraphicsExtent3D(lut.Size, lut.Size),
                GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.CopyDestination | GraphicsTextureUsage.TextureBinding,
                label: "GGX and Charlie BRDF LUT"));
            device.Queue.WriteTexture(
                texture,
                0,
                new GraphicsOrigin3D(),
                new GraphicsExtent3D(lut.Size, lut.Size),
                MemoryMarshal.AsBytes(texels.AsSpan()),
                checked(lut.Size * 4u * sizeof(ushort)),
                lut.Size);
            view = device.CreateTextureView(new GraphicsTextureViewDescriptor(
                texture,
                label: "GGX and Charlie BRDF LUT view"));
            return new BrdfLutGpuResources(texture, view);
        }
        catch
        {
            view?.Dispose();
            texture?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        View.Dispose();
        Texture.Dispose();
    }
}

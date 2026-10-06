using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Mu3D.Graphics;

namespace Mu3D.Gallery.Pages;

/// <summary>Reads six neutral reference patches from the actual acquired Draw target.</summary>
internal static class SurfaceReferenceReadback
{
    private const int SampleCount = 6;
    private const int SampleStride = 256;
    private const int StagingSize = SampleCount * SampleStride;

    /// <summary>Submits copies synchronously while the target is alive, then reads only staging.</summary>
    /// <remarks>Call immediately after SurfaceReferencePattern.Draw submits its pattern commands.</remarks>
    internal static Task<string> Begin(
        GraphicsDevice device,
        GraphicsTexture target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(target);

        GraphicsTextureDescriptor descriptor = target.Descriptor;
        string description = Describe(descriptor);
        if (target.IsDisposed)
        {
            return Unsupported(description, "the acquired target is already disposed");
        }
        if (descriptor.Format != GraphicsTextureFormat.Rgba16Float)
        {
            return Unsupported(description, "requires Rgba16Float");
        }
        if ((descriptor.Usage & GraphicsTextureUsage.CopySource) == 0)
        {
            return Unsupported(description, "the acquired target does not allow CopySource");
        }
        if (descriptor.SampleCount != 1 || descriptor.Size.DepthOrArrayLayers != 1)
        {
            return Unsupported(description, "requires a single-sample, single-layer target");
        }
        if (descriptor.Size.Width < SampleCount || descriptor.Size.Height == 0)
        {
            return Unsupported(description, "requires width >= 6 and nonzero height");
        }
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<string>(cancellationToken);
        }

        var centers = new GraphicsOrigin3D[SampleCount];
        uint y = (uint)(3UL * descriptor.Size.Height / 4);
        for (int index = 0; index < SampleCount; index++)
        {
            uint x = (uint)((ulong)(2 * index + 1) * descriptor.Size.Width / (2 * SampleCount));
            centers[index] = new GraphicsOrigin3D(x, y);
        }

        GraphicsBuffer? staging = null;
        try
        {
            staging = device.CreateBuffer(new GraphicsBufferDescriptor(
                StagingSize,
                GraphicsBufferUsage.MapRead | GraphicsBufferUsage.CopyDestination,
                "Acquired surface six-patch readback"));
            using (GraphicsCommandEncoder encoder = device.CreateCommandEncoder("Acquired surface reference copies"))
            {
                for (int index = 0; index < SampleCount; index++)
                {
                    encoder.CopyTextureToBuffer(
                        target, 0, centers[index], new GraphicsExtent3D(1, 1), staging,
                        (ulong)(index * SampleStride), SampleStride, 1);
                }
                using GraphicsCommandBuffer commands = encoder.Finish();
                device.Queue.Submit(commands);
            }

            // The continuation has no source texture or session reference; only staging is owned.
            return CompleteAsync(staging, description, centers, cancellationToken);
        }
        catch (Exception error)
        {
            staging?.Dispose();
            return Task.FromException<string>(error);
        }
    }

    private static async Task<string> CompleteAsync(
        GraphicsBuffer staging,
        string description,
        GraphicsOrigin3D[] centers,
        CancellationToken cancellationToken)
    {
        using (staging)
        {
            byte[] bytes = await staging.ReadAsync(0, StagingSize, cancellationToken).ConfigureAwait(false);
            var text = new StringBuilder(description);
            text.AppendLine("Six bottom-band center texels; expected neutral RGB: 0 | 0.18 | 0.5 | 1 | 2 | 4; expected alpha: 1.");
            text.AppendLine("Raw channel bits are the original little-endian FP16 bytes; decoding applies no normalization or tone mapping. Authored references are rounded by FP16 storage.");
            for (int index = 0; index < SampleCount; index++)
            {
                int offset = index * SampleStride;
                ushort rBits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
                ushort gBits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 2, 2));
                ushort bBits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4, 2));
                ushort aBits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 6, 2));
                float r = (float)BitConverter.UInt16BitsToHalf(rBits);
                float g = (float)BitConverter.UInt16BitsToHalf(gBits);
                float b = (float)BitConverter.UInt16BitsToHalf(bBits);
                float a = (float)BitConverter.UInt16BitsToHalf(aBits);
                bool finite = float.IsFinite(r) && float.IsFinite(g) && float.IsFinite(b) && float.IsFinite(a);
                float expected = ExpectedValue(index);
                text.AppendLine($"Patch {index + 1} at ({centers[index].X}, {centers[index].Y}): expected RGB={FormatValue(expected)}, A=1; actual RGBA=({FormatValue(r)}, {FormatValue(g)}, {FormatValue(b)}, {FormatValue(a)}); finite={finite}");
                text.AppendLine($"  Raw FP16 RGBA bits: 0x{rBits:X4} 0x{gBits:X4} 0x{bBits:X4} 0x{aBits:X4}");
            }
            return text.ToString();
        }
    }

    private static string Describe(GraphicsTextureDescriptor descriptor)
    {
        var text = new StringBuilder();
        text.AppendLine("Readback source: actual acquired presentation texture before present; not queued-buffer, compositor or physical output.");
        text.AppendLine($"Target descriptor provenance: {descriptor.Label}");
        text.AppendLine($"Target descriptor extent: {descriptor.Size.Width} x {descriptor.Size.Height} x {descriptor.Size.DepthOrArrayLayers}");
        text.AppendLine($"Target descriptor format: {descriptor.Format}");
        text.AppendLine($"Target descriptor usage: {descriptor.Usage}");
        text.AppendLine($"Target descriptor samples/mips: {descriptor.SampleCount}/{descriptor.MipLevelCount}");
        return text.ToString();
    }

    private static Task<string> Unsupported(string description, string reason) =>
        Task.FromResult(description + $"Reference readback unsupported: {reason}.\n");

    private static string FormatValue(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

    private static float ExpectedValue(int index) => index switch
    {
        0 => 0,
        1 => 0.18f,
        2 => 0.5f,
        3 => 1,
        4 => 2,
        5 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };
}

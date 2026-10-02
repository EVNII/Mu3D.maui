using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Mu3D.Color;
using Mu3D.Native.UltraHdr.Interop;
using Mu3D.SceneGraph;

namespace Mu3D.Native.UltraHdr;

internal static unsafe class UltraHdrGainMapCodec
{
    static UltraHdrGainMapCodec() => UltraHdrNativeLibraryResolver.Register();

    internal static LinearRgbaImage DecodeColor(
        ReadOnlyMemory<byte> source,
        string? name) =>
        WithDecoder(source, decoder =>
        {
            ConfigureOutput(decoder, UltraHdrImageFormat.RgbaHalfFloat, UltraHdrColorTransfer.Linear);
            UltraHdrRawImage* image = Decode(decoder);
            if (image->Format != UltraHdrImageFormat.RgbaHalfFloat ||
                image->ColorTransfer != UltraHdrColorTransfer.Linear)
            {
                throw new InvalidDataException("libultrahdr returned an unexpected color output format.");
            }
            int pixelCount = checked((int)(image->Width * image->Height));
            Vector4[] pixels = new Vector4[pixelCount];
            ushort* firstPixel = (ushort*)image->Plane0;
            if (firstPixel is null || image->Stride0 < image->Width)
            {
                throw new InvalidDataException("libultrahdr returned an invalid color image plane.");
            }
            for (uint y = 0; y < image->Height; y++)
            {
                ushort* row = firstPixel + checked(y * image->Stride0 * 4u);
                for (uint x = 0; x < image->Width; x++)
                {
                    ushort* rgba = row + x * 4u;
                    pixels[checked((int)(y * image->Width + x))] = new Vector4(
                        (float)BitConverter.UInt16BitsToHalf(rgba[0]),
                        (float)BitConverter.UInt16BitsToHalf(rgba[1]),
                        (float)BitConverter.UInt16BitsToHalf(rgba[2]),
                        (float)BitConverter.UInt16BitsToHalf(rgba[3]));
                }
            }
            return LinearRgbaImage.FromOwnedPixels(
                image->Width,
                image->Height,
                pixels,
                ResolveColorSpace(image->ColorGamut),
                name);
        });

    private static T WithDecoder<T>(ReadOnlyMemory<byte> source, Func<nint, T> action)
    {
        nint decoder = UltraHdrNative.uhdr_create_decoder();
        if (decoder == 0)
        {
            throw new InvalidOperationException("libultrahdr could not allocate a decoder.");
        }
        try
        {
            using MemoryHandle handle = source.Pin();
            UltraHdrCompressedImage input = new()
            {
                Data = handle.Pointer,
                DataSize = (nuint)source.Length,
                Capacity = (nuint)source.Length,
                ColorGamut = UltraHdrColorGamut.Unspecified,
                ColorTransfer = UltraHdrColorTransfer.Unspecified,
                ColorRange = UltraHdrColorRange.Unspecified,
            };
            ThrowIfCodecError(
                UltraHdrNative.uhdr_dec_set_image(decoder, &input),
                "register compressed image");
            return action(decoder);
        }
        finally
        {
            UltraHdrNative.uhdr_release_decoder(decoder);
        }
    }

    private static void ConfigureOutput(
        nint decoder,
        UltraHdrImageFormat format,
        UltraHdrColorTransfer transfer)
    {
        ThrowIfCodecError(
            UltraHdrNative.uhdr_dec_set_out_img_format(decoder, format),
            "select output pixel format");
        ThrowIfCodecError(
            UltraHdrNative.uhdr_dec_set_out_color_transfer(decoder, transfer),
            "select output transfer");
    }

    private static UltraHdrRawImage* Decode(nint decoder)
    {
        ThrowIfCodecError(UltraHdrNative.uhdr_dec_probe(decoder), "probe image");
        ThrowIfCodecError(UltraHdrNative.uhdr_decode(decoder), "decode image");
        UltraHdrRawImage* image = UltraHdrNative.uhdr_get_decoded_image(decoder);
        if (image is null || image->Width == 0 || image->Height == 0)
        {
            throw new InvalidDataException("libultrahdr returned no decoded image.");
        }
        return image;
    }

    private static StandardRgbColorSpaceReference ResolveColorSpace(UltraHdrColorGamut gamut) =>
        gamut switch
        {
            UltraHdrColorGamut.Unspecified or UltraHdrColorGamut.Bt709 =>
                StandardColorSpaces.LinearSrgb,
            UltraHdrColorGamut.DisplayP3 => StandardColorSpaces.LinearDisplayP3,
            UltraHdrColorGamut.Bt2100 => StandardColorSpaces.LinearRec2020,
            _ => throw new InvalidDataException(
                $"libultrahdr returned unknown color gamut {gamut}."),
        };

    internal static void ThrowIfCodecError(UltraHdrErrorInfo error, string operation)
    {
        if (error.ErrorCode == UltraHdrCodecError.Ok)
        {
            return;
        }
        string detail = "no native detail";
        if (error.HasDetail != 0)
        {
            ReadOnlySpan<byte> bytes = MemoryMarshal.CreateReadOnlySpan(ref error.Detail[0], 256);
            int terminator = bytes.IndexOf((byte)0);
            detail = Encoding.UTF8.GetString(terminator < 0 ? bytes : bytes[..terminator]);
        }
        throw new InvalidDataException(
            $"libultrahdr failed to {operation}: {error.ErrorCode}: {detail}");
    }
}

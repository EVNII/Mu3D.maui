using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Mu3D.Native.UltraHdr.Interop;

internal enum UltraHdrImageFormat
{
    Rgba8888 = 3,
    RgbaHalfFloat = 4,
}

internal enum UltraHdrColorGamut
{
    Unspecified = -1,
    Bt709 = 0,
    DisplayP3 = 1,
    Bt2100 = 2,
}

internal enum UltraHdrColorTransfer
{
    Unspecified = -1,
    Linear = 0,
    Srgb = 3,
}

internal enum UltraHdrColorRange
{
    Unspecified = -1,
    Limited = 0,
    Full = 1,
}

internal enum UltraHdrImageLabel
{
    Hdr,
    Sdr,
    Base,
    GainMap,
}

internal enum UltraHdrEncoderPreset
{
    Realtime,
    BestQuality,
}

internal enum UltraHdrCodec
{
    Jpeg,
    Heif,
    Avif,
}

internal enum UltraHdrCodecError
{
    Ok,
}

[InlineArray(256)]
internal struct UltraHdrErrorDetail
{
    private byte element0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct UltraHdrErrorInfo
{
    internal UltraHdrCodecError ErrorCode;
    internal int HasDetail;
    internal UltraHdrErrorDetail Detail;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UltraHdrCompressedImage
{
    internal void* Data;
    internal nuint DataSize;
    internal nuint Capacity;
    internal UltraHdrColorGamut ColorGamut;
    internal UltraHdrColorTransfer ColorTransfer;
    internal UltraHdrColorRange ColorRange;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct UltraHdrRawImage
{
    internal UltraHdrImageFormat Format;
    internal UltraHdrColorGamut ColorGamut;
    internal UltraHdrColorTransfer ColorTransfer;
    internal int ColorRange;
    internal uint Width;
    internal uint Height;
    internal void* Plane0;
    internal void* Plane1;
    internal void* Plane2;
    internal uint Stride0;
    internal uint Stride1;
    internal uint Stride2;
}

internal static unsafe partial class UltraHdrNative
{
    private const string LibraryName = "uhdr";

    [LibraryImport(LibraryName)]
    internal static partial nint uhdr_create_decoder();

    [LibraryImport(LibraryName)]
    internal static partial void uhdr_release_decoder(nint decoder);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_dec_set_image(
        nint decoder,
        UltraHdrCompressedImage* image);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_dec_set_out_img_format(
        nint decoder,
        UltraHdrImageFormat format);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_dec_set_out_color_transfer(
        nint decoder,
        UltraHdrColorTransfer colorTransfer);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_dec_probe(nint decoder);

    [LibraryImport(LibraryName)]
    internal static partial int uhdr_dec_get_gainmap_width(nint decoder);

    [LibraryImport(LibraryName)]
    internal static partial int uhdr_dec_get_gainmap_height(nint decoder);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_decode(nint decoder);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrRawImage* uhdr_get_decoded_image(nint decoder);

    [LibraryImport(LibraryName)]
    internal static partial int is_uhdr_image(void* data, int size);

    [LibraryImport(LibraryName)]
    internal static partial nint uhdr_create_encoder();

    [LibraryImport(LibraryName)]
    internal static partial void uhdr_release_encoder(nint encoder);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_enc_set_raw_image(
        nint encoder,
        UltraHdrRawImage* image,
        UltraHdrImageLabel intent);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_enc_set_quality(
        nint encoder,
        int quality,
        UltraHdrImageLabel intent);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_enc_set_using_multi_channel_gainmap(
        nint encoder,
        int useMultiChannelGainMap);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_enc_set_gainmap_scale_factor(
        nint encoder,
        int gainMapScaleFactor);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_enc_set_target_display_peak_brightness(
        nint encoder,
        float nits);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_enc_set_preset(
        nint encoder,
        UltraHdrEncoderPreset preset);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_enc_set_output_format(
        nint encoder,
        UltraHdrCodec codec);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrErrorInfo uhdr_encode(nint encoder);

    [LibraryImport(LibraryName)]
    internal static partial UltraHdrCompressedImage* uhdr_get_encoded_stream(nint encoder);
}

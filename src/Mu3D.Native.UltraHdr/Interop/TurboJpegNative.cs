using System.Runtime.InteropServices;

namespace Mu3D.Native.UltraHdr.Interop;

internal enum TurboJpegInitialization
{
    Compress = 0,
    Decompress = 1,
}

internal enum TurboJpegParameter
{
    Quality = 3,
    Subsampling = 4,
    JpegWidth = 5,
    JpegHeight = 6,
}

internal enum TurboJpegSubsampling
{
    S444 = 0,
    S422 = 1,
    S420 = 2,
}

internal enum TurboJpegPixelFormat
{
    Rgb = 0,
    Rgba = 7,
}

internal static unsafe partial class TurboJpegNative
{
    private const string LibraryName = "turbojpeg";

    [LibraryImport(LibraryName)]
    internal static partial nint tj3Init(TurboJpegInitialization initialization);

    [LibraryImport(LibraryName)]
    internal static partial void tj3Destroy(nint handle);

    [LibraryImport(LibraryName)]
    internal static partial nint tj3GetErrorStr(nint handle);

    [LibraryImport(LibraryName)]
    internal static partial int tj3Get(nint handle, TurboJpegParameter parameter);

    [LibraryImport(LibraryName)]
    internal static partial int tj3Set(
        nint handle,
        TurboJpegParameter parameter,
        int value);

    [LibraryImport(LibraryName)]
    internal static partial int tj3Compress8(
        nint handle,
        byte* source,
        int width,
        int pitch,
        int height,
        TurboJpegPixelFormat pixelFormat,
        byte** jpegBuffer,
        nuint* jpegSize);

    [LibraryImport(LibraryName)]
    internal static partial void tj3Free(void* buffer);

    [LibraryImport(LibraryName)]
    internal static partial int tj3DecompressHeader(
        nint handle,
        byte* source,
        nuint sourceLength);

    [LibraryImport(LibraryName)]
    internal static partial int tj3Decompress8(
        nint handle,
        byte* source,
        nuint sourceLength,
        byte* destination,
        int pitch,
        TurboJpegPixelFormat pixelFormat);
}

using System.Runtime.InteropServices;

namespace Mu3D.Native.Ktx.Interop;

internal enum KtxError
{
    Success = 0,
    FileDataError = 1,
    FileIsPipe = 2,
    FileOpenFailed = 3,
    FileOverflow = 4,
    FileReadError = 5,
    FileSeekError = 6,
    FileUnexpectedEnd = 7,
    FileWriteError = 8,
    GlError = 9,
    InvalidOperation = 10,
    InvalidValue = 11,
    NotFound = 12,
    OutOfMemory = 13,
    TranscodeFailed = 14,
    UnknownFileFormat = 15,
    UnsupportedTextureType = 16,
    UnsupportedFeature = 17,
    LibraryNotLinked = 18,
    DecompressLengthError = 19,
    DecompressChecksumError = 20,
}

internal enum KtxTranscodeFormat
{
    Etc2Rgba = 1,
    Bc7Rgba = 6,
    Astc4x4Rgba = 10,
    Rgba32 = 13,
}

[StructLayout(LayoutKind.Sequential)]
internal struct KtxOrientation
{
    internal int X;
    internal int Y;
    internal int Z;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KtxTexture2
{
    internal int ClassId;
    internal nint VTable;
    internal nint VulkanVTable;
    internal nint Protected;
    internal byte IsArray;
    internal byte IsCubemap;
    internal byte IsCompressed;
    internal byte GenerateMipmaps;
    internal uint BaseWidth;
    internal uint BaseHeight;
    internal uint BaseDepth;
    internal uint NumDimensions;
    internal uint NumLevels;
    internal uint NumLayers;
    internal uint NumFaces;
    internal KtxOrientation Orientation;
    internal nint KvDataHead;
    internal uint KvDataLength;
    internal nint KvData;
    internal nuint DataSize;
    internal nint Data;
    internal uint VulkanFormat;
    internal nint DataFormatDescriptor;
    internal uint SupercompressionScheme;
    internal byte IsVideo;
    internal uint Duration;
    internal uint Timescale;
    internal uint LoopCount;
    internal nint Private;
}

internal static unsafe partial class KtxNative
{
    private const string LibraryName = "ktx";

    [LibraryImport(LibraryName)]
    internal static partial KtxError ktxTexture2_CreateFromMemory(
        byte* bytes,
        nuint size,
        uint createFlags,
        nint* texture);

    [LibraryImport(LibraryName)]
    internal static partial void ktxTexture2_Destroy(nint texture);

    [LibraryImport(LibraryName)]
    internal static partial byte ktxTexture2_NeedsTranscoding(nint texture);

    [LibraryImport(LibraryName)]
    internal static partial int ktxTexture2_GetTransferFunction_e(nint texture);

    [LibraryImport(LibraryName)]
    internal static partial int ktxTexture2_GetPrimaries_e(nint texture);

    [LibraryImport(LibraryName)]
    internal static partial byte ktxTexture2_GetPremultipliedAlpha(nint texture);

    [LibraryImport(LibraryName)]
    internal static partial KtxError ktxTexture2_TranscodeBasis(
        nint texture,
        KtxTranscodeFormat format,
        uint flags);

    [LibraryImport(LibraryName)]
    internal static partial KtxError ktxTexture2_GetImageOffset(
        nint texture,
        uint level,
        uint layer,
        uint faceSlice,
        nuint* offset);

    [LibraryImport(LibraryName)]
    internal static partial nint ktxTexture_GetData(nint texture);

    [LibraryImport(LibraryName)]
    internal static partial nuint ktxTexture_GetDataSize(nint texture);

    [LibraryImport(LibraryName)]
    internal static partial KtxError ktxHashList_FindValue(
        nint head,
        byte* key,
        uint* valueLength,
        nint* value);
}

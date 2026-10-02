using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Formats.Gltf;

internal static class PngImageReader
{
    private const int ParallelPixelThreshold = 256 * 256;
    private static readonly uint[] CrcTable = CreateCrcTable();
    private static readonly float[] LinearByteTable = CreateLinearByteTable();
    private static readonly float[] SrgbByteTable = CreateSrgbByteTable();
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    internal static LinearRgbaImage ReadSrgb(ReadOnlyMemory<byte> source, string? name)
    {
        DecodedPng decoded = Decode(source, decodeSrgb: true);
        return LinearRgbaImage.FromTrustedOwnedPixels(
            decoded.Width,
            decoded.Height,
            decoded.Pixels,
            StandardColorSpaces.LinearSrgb,
            name);
    }

    internal static NormalizedRgbaDataImage ReadData(ReadOnlyMemory<byte> source, string? name)
    {
        DecodedPng decoded = Decode(source, decodeSrgb: false);
        return NormalizedRgbaDataImage.FromTrustedOwnedTexels(
            decoded.Width,
            decoded.Height,
            decoded.Pixels,
            name);
    }

    private static DecodedPng Decode(ReadOnlyMemory<byte> source, bool decodeSrgb)
    {
        ReadOnlySpan<byte> bytes = source.Span;
        if (bytes.Length < Signature.Length || !bytes[..Signature.Length].SequenceEqual(Signature))
        {
            throw new InvalidDataException("The glTF image is not a PNG stream.");
        }

        uint width = 0;
        uint height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        int components = 0;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        bool sawHeader = false;
        bool sawEnd = false;
        using MemoryStream compressed = new();
        int offset = Signature.Length;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 12)
            {
                throw new InvalidDataException("PNG contains a truncated chunk header.");
            }
            uint lengthValue = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (lengthValue > int.MaxValue)
            {
                throw new InvalidDataException("PNG chunk length exceeds managed limits.");
            }
            int length = (int)lengthValue;
            int chunkEnd = checked(offset + 12 + length);
            if (chunkEnd > bytes.Length)
            {
                throw new InvalidDataException("PNG chunk exceeds the image bounds.");
            }
            ReadOnlySpan<byte> type = bytes.Slice(offset + 4, 4);
            ReadOnlySpan<byte> data = bytes.Slice(offset + 8, length);
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes[(offset + 8 + length)..]);
            if (ComputeCrc(type, data) != expectedCrc)
            {
                throw new InvalidDataException("PNG chunk CRC does not match its payload.");
            }

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || length != 13 || offset != Signature.Length)
                {
                    throw new InvalidDataException("PNG requires one leading IHDR chunk.");
                }
                width = BinaryPrimitives.ReadUInt32BigEndian(data);
                height = BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                bitDepth = data[8];
                colorType = data[9];
                bool supportedDirectColor = colorType is 0 or 2 or 6 && bitDepth == 8;
                bool supportedIndexed = colorType == 3 && bitDepth is 1 or 2 or 4 or 8;
                if (width == 0 || height == 0 ||
                    data[10] != 0 || data[11] != 0 || data[12] != 0 ||
                    (!supportedDirectColor && !supportedIndexed))
                {
                    throw new NotSupportedException(
                        "The current glTF PNG slice requires non-interlaced 8-bit grayscale, " +
                        "RGB/RGBA, or 1/2/4/8-bit indexed color.");
                }
                components = colorType switch
                {
                    0 => 1,
                    2 => 3,
                    6 => 4,
                    _ => 1,
                };
                sawHeader = true;
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (!sawHeader || compressed.Length != 0 || palette is not null ||
                    length == 0 || length % 3 != 0 || length > 768)
                {
                    throw new InvalidDataException("PNG palette chunk is invalid or out of order.");
                }
                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                if (!sawHeader || compressed.Length != 0 || palette is null ||
                    paletteAlpha is not null || colorType != 3)
                {
                    throw new InvalidDataException("PNG palette transparency is invalid or out of order.");
                }
                paletteAlpha = data.ToArray();
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!sawHeader || sawEnd)
                {
                    throw new InvalidDataException("PNG IDAT chunk ordering is invalid.");
                }
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!sawHeader || length != 0)
                {
                    throw new InvalidDataException("PNG IEND chunk is invalid.");
                }
                sawEnd = true;
                offset = chunkEnd;
                break;
            }
            offset = chunkEnd;
        }
        if (!sawHeader || !sawEnd || compressed.Length == 0 || offset != bytes.Length)
        {
            throw new InvalidDataException("PNG is missing required image chunks or has trailing data.");
        }
        int paletteEntryCount = palette?.Length / 3 ?? 0;
        if (colorType == 3 &&
            (paletteEntryCount == 0 || paletteEntryCount > (1 << bitDepth) ||
             (paletteAlpha?.Length ?? 0) > paletteEntryCount))
        {
            throw new InvalidDataException("Indexed PNG palette data does not match its bit depth.");
        }

        int stride = colorType == 3
            ? checked((int)(((ulong)width * bitDepth + 7u) / 8u))
            : checked((int)width * components);
        int decodedLength = checked((stride + 1) * (int)height);
        byte[] filtered = new byte[decodedLength];
        compressed.Position = 0;
        using (ZLibStream inflater = new(compressed, CompressionMode.Decompress, leaveOpen: true))
        {
            inflater.ReadExactly(filtered);
            if (inflater.ReadByte() != -1)
            {
                throw new InvalidDataException("PNG decompressed data exceeds its dimensions.");
            }
        }

        byte[] scanlines = new byte[checked(stride * (int)height)];
        for (int row = 0; row < height; row++)
        {
            int filteredOffset = row * (stride + 1);
            int rowOffset = row * stride;
            byte filter = filtered[filteredOffset];
            for (int column = 0; column < stride; column++)
            {
                byte encoded = filtered[filteredOffset + 1 + column];
                int filterBytesPerPixel = colorType == 3 ? 1 : components;
                byte left = column >= filterBytesPerPixel
                    ? scanlines[rowOffset + column - filterBytesPerPixel]
                    : (byte)0;
                byte above = row > 0 ? scanlines[rowOffset - stride + column] : (byte)0;
                byte upperLeft = row > 0 && column >= filterBytesPerPixel
                    ? scanlines[rowOffset - stride + column - filterBytesPerPixel]
                    : (byte)0;
                scanlines[rowOffset + column] = filter switch
                {
                    0 => encoded,
                    1 => unchecked((byte)(encoded + left)),
                    2 => unchecked((byte)(encoded + above)),
                    3 => unchecked((byte)(encoded + ((left + above) >> 1))),
                    4 => unchecked((byte)(encoded + Paeth(left, above, upperLeft))),
                    _ => throw new InvalidDataException("PNG uses an unknown scanline filter."),
                };
            }
        }

        Vector4[] pixels = new Vector4[checked((int)(width * height))];
        if (colorType == 3)
        {
            int mask = (1 << bitDepth) - 1;
            int pixelsPerByte = 8 / bitDepth;
            for (int row = 0; row < height; row++)
            {
                int rowOffset = row * stride;
                for (int column = 0; column < width; column++)
                {
                    int packed = scanlines[rowOffset + (column / pixelsPerByte)];
                    int shift = 8 - bitDepth - ((column % pixelsPerByte) * bitDepth);
                    int paletteIndex = (packed >> shift) & mask;
                    if (paletteIndex >= paletteEntryCount)
                    {
                        throw new InvalidDataException("Indexed PNG references a missing palette entry.");
                    }
                    int paletteOffset = paletteIndex * 3;
                    pixels[checked((int)((ulong)(uint)row * width + (uint)column))] = new Vector4(
                        DecodeChannel(palette![paletteOffset], decodeSrgb),
                        DecodeChannel(palette[paletteOffset + 1], decodeSrgb),
                        DecodeChannel(palette[paletteOffset + 2], decodeSrgb),
                        paletteAlpha is not null && paletteIndex < paletteAlpha.Length
                            ? paletteAlpha[paletteIndex] / 255f
                            : 1f);
                }
            }
            return new DecodedPng(width, height, pixels);
        }
        void DecodePixel(int pixel)
        {
            int sourceOffset = pixel * components;
            if (components == 1)
            {
                float gray = DecodeChannel(scanlines[sourceOffset], decodeSrgb);
                pixels[pixel] = new Vector4(gray, gray, gray, 1f);
                return;
            }
            pixels[pixel] = new Vector4(
                DecodeChannel(scanlines[sourceOffset], decodeSrgb),
                DecodeChannel(scanlines[sourceOffset + 1], decodeSrgb),
                DecodeChannel(scanlines[sourceOffset + 2], decodeSrgb),
                components == 4 ? scanlines[sourceOffset + 3] / 255f : 1f);
        }
        if (pixels.Length >= ParallelPixelThreshold)
        {
            Parallel.For(0, pixels.Length, DecodePixel);
        }
        else
        {
            for (int pixel = 0; pixel < pixels.Length; pixel++)
            {
                DecodePixel(pixel);
            }
        }
        return new DecodedPng(width, height, pixels);
    }

    private static byte Paeth(byte left, byte above, byte upperLeft)
    {
        int estimate = left + above - upperLeft;
        int leftDistance = Math.Abs(estimate - left);
        int aboveDistance = Math.Abs(estimate - above);
        int upperLeftDistance = Math.Abs(estimate - upperLeft);
        return leftDistance <= aboveDistance && leftDistance <= upperLeftDistance
            ? left
            : aboveDistance <= upperLeftDistance ? above : upperLeft;
    }

    private static float DecodeSrgb(float encoded) => encoded <= 0.04045f
        ? encoded / 12.92f
        : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);

    private static float DecodeChannel(byte encoded, bool decodeSrgb) => decodeSrgb
        ? SrgbByteTable[encoded]
        : LinearByteTable[encoded];

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in type)
        {
            crc = UpdateCrc(crc, value);
        }
        foreach (byte value in data)
        {
            crc = UpdateCrc(crc, value);
        }
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte value)
        => CrcTable[(crc ^ value) & 0xFFu] ^ (crc >> 8);

    private static uint[] CreateCrcTable()
    {
        uint[] table = new uint[256];
        for (uint value = 0; value < table.Length; value++)
        {
            uint remainder = value;
            for (int bit = 0; bit < 8; bit++)
            {
                remainder = (remainder & 1) != 0
                    ? 0xEDB88320u ^ (remainder >> 1)
                    : remainder >> 1;
            }
            table[value] = remainder;
        }
        return table;
    }

    private static float[] CreateLinearByteTable()
    {
        float[] table = new float[256];
        for (int value = 0; value < table.Length; value++)
        {
            table[value] = value / 255f;
        }
        return table;
    }

    private static float[] CreateSrgbByteTable()
    {
        float[] table = new float[256];
        for (int value = 0; value < table.Length; value++)
        {
            table[value] = DecodeSrgb(value / 255f);
        }
        return table;
    }

    private readonly record struct DecodedPng(uint Width, uint Height, Vector4[] Pixels);
}

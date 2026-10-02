using System.Globalization;
using System.Numerics;
using System.Text;
using Mu3D.Color;

namespace Mu3D.SceneGraph;

/// <summary>Decodes a Radiance RGBE latitude-longitude image into explicitly tagged linear FP32 RGB.</summary>
public static class RadianceHdrReader
{
    /// <summary>
    /// Reads a Radiance <c>.hdr</c> stream. The file format does not reliably identify modern RGB
    /// working spaces, so callers must explicitly supply the linear-light interpretation instead of
    /// inheriting display RGB. Standard scanline RLE and legacy RGBE streams are supported.
    /// </summary>
    /// <param name="stream">A readable stream positioned at the Radiance signature.</param>
    /// <param name="colorSpace">The explicit linear RGB identity assigned to decoded values.</param>
    /// <param name="name">An optional application-facing environment name.</param>
    /// <returns>An immutable decoded equirectangular HDR environment.</returns>
    public static EquirectangularHdrEnvironment Read(
        Stream stream,
        ColorSpaceReference colorSpace,
        string? name = null) =>
        Read(stream, colorSpace, name, long.MaxValue);

    internal static EquirectangularHdrEnvironment Read(
        Stream stream,
        ColorSpaceReference colorSpace,
        string? name,
        long maximumOutputByteCount)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(colorSpace);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The Radiance stream must be readable.", nameof(stream));
        }
        if (!colorSpace.IsLinear)
        {
            throw new ArgumentException("Radiance RGBE values must be assigned a linear-light RGB space.", nameof(colorSpace));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOutputByteCount);

        string signature = ReadAsciiLine(stream);
        if (signature is not "#?RADIANCE" and not "#?RGBE")
        {
            throw new InvalidDataException("The stream does not begin with a Radiance RGBE signature.");
        }

        bool supportedFormat = false;
        while (true)
        {
            string line = ReadAsciiLine(stream);
            if (line.Length == 0)
            {
                break;
            }
            if (line.Equals("FORMAT=32-bit_rle_rgbe", StringComparison.Ordinal))
            {
                supportedFormat = true;
            }
        }
        if (!supportedFormat)
        {
            throw new NotSupportedException("Only Radiance FORMAT=32-bit_rle_rgbe is supported.");
        }

        string resolution = ReadAsciiLine(stream);
        string[] fields = resolution.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 4 || fields[0] != "-Y" || fields[2] != "+X" ||
            !uint.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out uint height) ||
            !uint.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out uint width) ||
            width == 0 || height == 0)
        {
            throw new NotSupportedException(
                "Only the standard top-to-bottom '-Y height +X width' Radiance orientation is supported.");
        }
        ulong pixelCount = checked((ulong)width * height);
        if (pixelCount > int.MaxValue / 4)
        {
            throw new InvalidDataException("The Radiance image exceeds managed image limits.");
        }
        long outputByteCount = checked((long)pixelCount * 3 * sizeof(float));
        if (outputByteCount > maximumOutputByteCount)
        {
            throw new InvalidDataException(
                $"The decoded Radiance environment requires {outputByteCount} bytes, exceeding the configured {maximumOutputByteCount}-byte output limit.");
        }

        byte[] firstPixel = ReadExactly(stream, 4);
        byte[] rgbe = new byte[checked((int)pixelCount * 4)];
        bool modernRle = width is >= 8 and <= 32767 &&
            firstPixel[0] == 2 && firstPixel[1] == 2 && (firstPixel[2] & 0x80) == 0;
        if (modernRle)
        {
            DecodeModernScanlines(stream, width, height, firstPixel, rgbe);
        }
        else
        {
            DecodeLegacyPixels(stream, firstPixel, checked((int)pixelCount), rgbe);
        }

        Vector3[] pixels = new Vector3[checked((int)pixelCount)];
        for (int index = 0; index < pixels.Length; index++)
        {
            int offset = index * 4;
            byte exponent = rgbe[offset + 3];
            if (exponent == 0)
            {
                pixels[index] = Vector3.Zero;
                continue;
            }
            float scale = MathF.ScaleB(1f, exponent - 136);
            pixels[index] = new Vector3(
                rgbe[offset] * scale,
                rgbe[offset + 1] * scale,
                rgbe[offset + 2] * scale);
        }
        return EquirectangularHdrEnvironment.FromTrustedOwnedPixels(
            width,
            height,
            pixels,
            colorSpace,
            name);
    }

    private static void DecodeModernScanlines(
        Stream stream,
        uint width,
        uint height,
        byte[] firstHeader,
        byte[] destination)
    {
        int scanlineWidth = checked((int)width);
        byte[] channels = new byte[checked(scanlineWidth * 4)];
        byte[] header = firstHeader;
        for (uint y = 0; y < height; y++)
        {
            if (y != 0)
            {
                header = ReadExactly(stream, 4);
            }
            int encodedWidth = (header[2] << 8) | header[3];
            if (header[0] != 2 || header[1] != 2 || encodedWidth != scanlineWidth)
            {
                throw new InvalidDataException("A Radiance scanline has an invalid RLE header.");
            }

            for (int channel = 0; channel < 4; channel++)
            {
                int written = 0;
                int channelOffset = channel * scanlineWidth;
                while (written < scanlineWidth)
                {
                    int code = ReadByte(stream);
                    if (code > 128)
                    {
                        int count = code - 128;
                        if (count == 0 || written + count > scanlineWidth)
                        {
                            throw new InvalidDataException("A Radiance RLE run exceeds its scanline.");
                        }
                        byte value = checked((byte)ReadByte(stream));
                        channels.AsSpan(channelOffset + written, count).Fill(value);
                        written += count;
                    }
                    else
                    {
                        int count = code;
                        if (count == 0 || written + count > scanlineWidth)
                        {
                            throw new InvalidDataException("A Radiance RLE literal exceeds its scanline.");
                        }
                        ReadExactly(stream, channels.AsSpan(channelOffset + written, count));
                        written += count;
                    }
                }
            }

            int rowOffset = checked((int)((ulong)y * width * 4));
            for (int x = 0; x < scanlineWidth; x++)
            {
                destination[rowOffset + x * 4] = channels[x];
                destination[rowOffset + x * 4 + 1] = channels[scanlineWidth + x];
                destination[rowOffset + x * 4 + 2] = channels[scanlineWidth * 2 + x];
                destination[rowOffset + x * 4 + 3] = channels[scanlineWidth * 3 + x];
            }
        }
    }

    private static void DecodeLegacyPixels(
        Stream stream,
        byte[] firstPixel,
        int pixelCount,
        byte[] destination)
    {
        byte[] encoded = firstPixel;
        int outputIndex = 0;
        int shift = 0;
        while (outputIndex < pixelCount)
        {
            if (encoded[0] == 1 && encoded[1] == 1 && encoded[2] == 1)
            {
                if (outputIndex == 0 || shift > 24)
                {
                    throw new InvalidDataException("A legacy Radiance repeat marker is invalid.");
                }
                long repeatCountValue = (long)encoded[3] << shift;
                if (repeatCountValue == 0 || repeatCountValue > int.MaxValue ||
                    outputIndex + repeatCountValue > pixelCount)
                {
                    throw new InvalidDataException("A legacy Radiance repeat exceeds the image.");
                }
                int repeatCount = checked((int)repeatCountValue);
                ReadOnlySpan<byte> previous = destination.AsSpan((outputIndex - 1) * 4, 4);
                for (int repeat = 0; repeat < repeatCount; repeat++)
                {
                    previous.CopyTo(destination.AsSpan(outputIndex * 4, 4));
                    outputIndex++;
                }
                shift += 8;
            }
            else
            {
                encoded.CopyTo(destination, outputIndex * 4);
                outputIndex++;
                shift = 0;
            }
            if (outputIndex < pixelCount)
            {
                encoded = ReadExactly(stream, 4);
            }
        }
    }

    private static string ReadAsciiLine(Stream stream)
    {
        List<byte> bytes = [];
        while (true)
        {
            int value = stream.ReadByte();
            if (value < 0)
            {
                throw new EndOfStreamException("The Radiance header ended unexpectedly.");
            }
            if (value == '\n')
            {
                break;
            }
            if (value != '\r')
            {
                bytes.Add(checked((byte)value));
            }
            if (bytes.Count > 8192)
            {
                throw new InvalidDataException("A Radiance header line is unreasonably long.");
            }
        }
        return Encoding.ASCII.GetString([.. bytes]);
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        byte[] bytes = new byte[count];
        ReadExactly(stream, bytes);
        return bytes;
    }

    private static void ReadExactly(Stream stream, Span<byte> destination)
    {
        while (!destination.IsEmpty)
        {
            int read = stream.Read(destination);
            if (read == 0)
            {
                throw new EndOfStreamException("The Radiance pixel stream ended unexpectedly.");
            }
            destination = destination[read..];
        }
    }

    private static int ReadByte(Stream stream)
    {
        int value = stream.ReadByte();
        return value >= 0
            ? value
            : throw new EndOfStreamException("The Radiance RLE stream ended unexpectedly.");
    }
}

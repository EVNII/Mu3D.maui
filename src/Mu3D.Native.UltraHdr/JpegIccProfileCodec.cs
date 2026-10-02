using System.Buffers.Binary;
using Mu3D.Color;

namespace Mu3D.Native.UltraHdr;

internal static class JpegIccProfileCodec
{
    private static ReadOnlySpan<byte> Identifier => "ICC_PROFILE\0"u8;
    private const int ChunkHeaderSize = 14;
    private const int MaximumChunkDataSize = ushort.MaxValue - 2 - ChunkHeaderSize;

    internal static IccProfile? Extract(ReadOnlySpan<byte> jpeg)
    {
        ValidateStart(jpeg);
        List<(byte Sequence, byte Count, byte[] Data)> chunks = [];
        int offset = 2;
        while (TryReadSegment(jpeg, ref offset, out byte marker, out ReadOnlySpan<byte> payload))
        {
            if (marker == 0xe2 && payload.Length >= ChunkHeaderSize &&
                payload[..Identifier.Length].SequenceEqual(Identifier))
            {
                byte sequence = payload[12];
                byte count = payload[13];
                if (sequence == 0 || count == 0 || sequence > count)
                {
                    throw new InvalidDataException("JPEG ICC APP2 chunk numbering is invalid.");
                }
                chunks.Add((sequence, count, payload[ChunkHeaderSize..].ToArray()));
            }
        }
        if (chunks.Count == 0)
        {
            return null;
        }

        byte expectedCount = chunks[0].Count;
        if (chunks.Count != expectedCount || chunks.Any(chunk => chunk.Count != expectedCount))
        {
            throw new InvalidDataException("JPEG ICC APP2 chunks are incomplete or disagree on count.");
        }
        byte[][] ordered = new byte[expectedCount][];
        foreach ((byte sequence, _, byte[] data) in chunks)
        {
            int index = sequence - 1;
            if (ordered[index] is not null)
            {
                throw new InvalidDataException($"JPEG ICC APP2 chunk {sequence} is duplicated.");
            }
            ordered[index] = data;
        }
        int profileLength = 0;
        foreach (byte[]? chunk in ordered)
        {
            if (chunk is null)
            {
                throw new InvalidDataException("JPEG ICC APP2 chunk sequence has a gap.");
            }
            profileLength = checked(profileLength + chunk.Length);
        }
        byte[] profile = new byte[profileLength];
        int destination = 0;
        foreach (byte[] chunk in ordered)
        {
            chunk.CopyTo(profile, destination);
            destination += chunk.Length;
        }
        return new IccProfile(profile);
    }

    internal static byte[] Insert(ReadOnlySpan<byte> jpeg, IccProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateStart(jpeg);
        if (Extract(jpeg) is not null)
        {
            throw new InvalidOperationException("The JPEG already contains an ICC profile.");
        }
        byte[] profileData = profile.ToArray();
        int chunkCount = checked((profileData.Length + MaximumChunkDataSize - 1) /
            MaximumChunkDataSize);
        if (chunkCount is < 1 or > byte.MaxValue)
        {
            throw new NotSupportedException("ICC profile requires more than 255 JPEG APP2 chunks.");
        }
        int insertedLength = checked(profileData.Length + chunkCount * (4 + ChunkHeaderSize));
        byte[] output = new byte[checked(jpeg.Length + insertedLength)];
        jpeg[..2].CopyTo(output);
        int sourceOffset = 0;
        int outputOffset = 2;
        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            int chunkLength = Math.Min(MaximumChunkDataSize, profileData.Length - sourceOffset);
            output[outputOffset] = 0xff;
            output[outputOffset + 1] = 0xe2;
            BinaryPrimitives.WriteUInt16BigEndian(
                output.AsSpan(outputOffset + 2, 2),
                checked((ushort)(2 + ChunkHeaderSize + chunkLength)));
            Identifier.CopyTo(output.AsSpan(outputOffset + 4));
            output[outputOffset + 16] = checked((byte)(chunkIndex + 1));
            output[outputOffset + 17] = checked((byte)chunkCount);
            profileData.AsSpan(sourceOffset, chunkLength).CopyTo(
                output.AsSpan(outputOffset + 18, chunkLength));
            sourceOffset += chunkLength;
            outputOffset += 18 + chunkLength;
        }
        jpeg[2..].CopyTo(output.AsSpan(outputOffset));
        return output;
    }

    private static void ValidateStart(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xff || jpeg[1] != 0xd8)
        {
            throw new InvalidDataException("Input does not begin with a JPEG SOI marker.");
        }
    }

    private static bool TryReadSegment(
        ReadOnlySpan<byte> jpeg,
        ref int offset,
        out byte marker,
        out ReadOnlySpan<byte> payload)
    {
        payload = default;
        while (offset < jpeg.Length && jpeg[offset] == 0xff)
        {
            offset++;
        }
        if (offset >= jpeg.Length)
        {
            throw new InvalidDataException("JPEG marker is truncated.");
        }
        marker = jpeg[offset++];
        if (marker is 0xd9 or 0xda)
        {
            return false;
        }
        if (marker == 0x00 || marker == 0xd8 || marker == 0x01 || marker is >= 0xd0 and <= 0xd7)
        {
            return true;
        }
        if (offset > jpeg.Length - 2)
        {
            throw new InvalidDataException("JPEG segment length is truncated.");
        }
        ushort segmentLength = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(offset, 2));
        if (segmentLength < 2 || segmentLength > jpeg.Length - offset)
        {
            throw new InvalidDataException("JPEG segment extends beyond the encoded input.");
        }
        payload = jpeg.Slice(offset + 2, segmentLength - 2);
        offset += segmentLength;
        return true;
    }
}

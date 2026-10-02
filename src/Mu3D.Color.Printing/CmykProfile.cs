using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Mu3D.Color.Printing;

/// <summary>Immutable ICC v2/v4 CMYK output profile. Original bytes and separation tables are preserved.</summary>
/// <remarks>Load the profile supplied by the printer or standards provider; no generic CMYK profile is assumed.</remarks>
public sealed class CmykProfile
{
    private readonly byte[] data;
    private readonly Dictionary<string, (int Offset, int Size)> tags = new();
    /// <summary>Validates and copies a complete CMYK output profile and its bounded tag table.</summary>
    public CmykProfile(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 132 || bytes.Length > 128 * 1024 * 1024 || U32(bytes, 0) != bytes.Length ||
            !bytes.Slice(36, 4).SequenceEqual("acsp"u8)) throw new InvalidDataException("Invalid ICC header or size.");
        if (bytes[8] is not (2 or 4) || !bytes.Slice(12,4).SequenceEqual("prtr"u8) ||
            !bytes.Slice(16,4).SequenceEqual("CMYK"u8)) throw new NotSupportedException("Expected ICC v2/v4 CMYK output profile.");
        ConnectionSpace = Encoding.ASCII.GetString(bytes.Slice(20,4));
        if (ConnectionSpace is not ("Lab " or "XYZ ")) throw new NotSupportedException("Expected Lab or XYZ PCS.");
        int count = U32(bytes,128);
        if (count > (bytes.Length-132)/12) throw new InvalidDataException("Truncated ICC tag table.");
        for (int i=0;i<count;i++)
        {
            int row=132+i*12, offset=U32(bytes,row+4), size=U32(bytes,row+8);
            if (offset%4!=0 || offset<132+count*12 || size<8 || offset>bytes.Length-size)
                throw new InvalidDataException("Invalid ICC tag extent.");
            if (!tags.TryAdd(Encoding.ASCII.GetString(bytes.Slice(row,4)),(offset,size)))
                throw new InvalidDataException("Duplicate ICC tag signature.");
        }
        data=bytes.ToArray(); Fingerprint=Convert.ToHexStringLower(SHA256.HashData(data));
        var white=Tag("wtpt");
        if (white.Length<20 || !white[..4].SequenceEqual("XYZ "u8)) throw new InvalidDataException("Missing XYZ media white.");
        MediaWhite = new(Fixed(white,8),Fixed(white,12),Fixed(white,16));
        if (MediaWhite.X<=0 || MediaWhite.Y<=0 || MediaWhite.Z<=0) throw new InvalidDataException("Invalid media white.");
    }
    /// <summary>Gets the SHA-256 identity used to prevent accidental reassignment of CMYK numbers.</summary>
    public string Fingerprint { get; }
    /// <summary>Gets the ICC PCS signature (Lab or XYZ).</summary>
    public string ConnectionSpace { get; }
    /// <summary>Gets the profile media white in XYZ.</summary>
    public Vector3 MediaWhite { get; }
    /// <summary>Returns the unchanged profile payload for embedding or interchange.</summary>
    public byte[] ToArray() => (byte[])data.Clone();
    internal ReadOnlySpan<byte> Tag(string name)
    {
        if (!tags.TryGetValue(name,out var t)) throw new NotSupportedException($"Profile lacks required {name} tag; no intent substitution is performed.");
        return data.AsSpan(t.Offset,t.Size);
    }
    internal static int U32(ReadOnlySpan<byte> b,int o)
    {
        if (o<0 || o>b.Length-4) throw new InvalidDataException("Truncated ICC value.");
        uint v=BinaryPrimitives.ReadUInt32BigEndian(b.Slice(o,4));
        if (v>int.MaxValue) throw new InvalidDataException("ICC size exceeds implementation limit.");
        return (int)v;
    }
    internal static float Fixed(ReadOnlySpan<byte> b,int o) => BinaryPrimitives.ReadInt32BigEndian(b.Slice(o,4))/65536f;
}

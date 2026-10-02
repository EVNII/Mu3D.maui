using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Mu3D.Color;

/// <summary>
/// Stores an immutable validated ICC v2/v4 profile payload and its header identity without
/// interpreting profile transforms.
/// </summary>
public sealed class IccProfile
{
    private const int HeaderSize = 128;
    private readonly byte[] data;

    /// <summary>Initializes a profile by validating and copying its complete ICC payload.</summary>
    /// <param name="data">The complete ICC profile beginning with its 128-byte header.</param>
    public IccProfile(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("An ICC profile must contain its complete 128-byte header.");
        }
        uint declaredSize = BinaryPrimitives.ReadUInt32BigEndian(data);
        if (declaredSize != data.Length)
        {
            throw new InvalidDataException(
                $"ICC declared size {declaredSize} does not match payload size {data.Length}.");
        }
        if (!data.Slice(36, 4).SequenceEqual("acsp"u8))
        {
            throw new InvalidDataException("ICC profile signature 'acsp' is missing.");
        }
        int majorVersion = data[8];
        if (majorVersion is not (2 or 4))
        {
            throw new NotSupportedException(
                $"Mu3D preserves ICC v2/v4 profiles, not major version {majorVersion}.");
        }
        string dataColorSpace = ReadSignature(data.Slice(16, 4));
        if (dataColorSpace != "RGB ")
        {
            throw new NotSupportedException(
                $"The current image boundary accepts RGB ICC profiles, not '{dataColorSpace}'.");
        }
        string connectionSpace = ReadSignature(data.Slice(20, 4));
        if (connectionSpace is not ("XYZ " or "Lab "))
        {
            throw new NotSupportedException(
                $"ICC profile connection space '{connectionSpace}' is not XYZ or Lab.");
        }

        this.data = data.ToArray();
        MajorVersion = majorVersion;
        MinorVersion = data[9] >> 4;
        ProfileClass = ReadSignature(data.Slice(12, 4));
        DataColorSpace = dataColorSpace;
        ProfileConnectionSpace = connectionSpace;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(this.data));
    }

    /// <summary>Gets the ICC major version, either two or four.</summary>
    public int MajorVersion { get; }

    /// <summary>Gets the ICC minor version nibble.</summary>
    public int MinorVersion { get; }

    /// <summary>Gets the four-byte ICC profile/device class signature.</summary>
    public string ProfileClass { get; }

    /// <summary>Gets the four-byte data color-space signature, currently always <c>RGB </c>.</summary>
    public string DataColorSpace { get; }

    /// <summary>Gets the four-byte profile connection-space signature.</summary>
    public string ProfileConnectionSpace { get; }

    /// <summary>Gets the lowercase SHA-256 identity of the complete profile payload.</summary>
    public string Fingerprint { get; }

    /// <summary>Returns a defensive copy of the complete ICC profile payload.</summary>
    public byte[] ToArray() => (byte[])data.Clone();

    internal ReadOnlySpan<byte> AsSpan() => data;

    private static string ReadSignature(ReadOnlySpan<byte> signature)
    {
        foreach (byte value in signature)
        {
            if (value is < 0x20 or > 0x7e)
            {
                throw new InvalidDataException("ICC header contains a non-printable signature.");
            }
        }
        return Encoding.ASCII.GetString(signature);
    }
}

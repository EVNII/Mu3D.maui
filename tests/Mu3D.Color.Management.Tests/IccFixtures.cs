using System.Buffers.Binary;
using System.Text;
using Mu3D.Color;

internal static class IccFixtures
{
    internal static IccProfile ProofProfile() => new(Profile(
        ("B2D1", Matrix(1 / 0.9642f, 1, 1 / 0.8249f)),
        ("B2D0", Matrix(2 / 0.9642f, 2, 2 / 0.8249f)),
        ("D2B1", Matrix(0.9642f, 1, 0.8249f)),
        ("wtpt", Xyz(0.8f, 0.75f, 0.55f))));

    internal static IccProfile DisplayProfile(float scale) => new(Profile(("B2D3", Matrix(scale, scale, scale))));

    private static byte[] Matrix(float x, float y, float z)
    {
        byte[] data = new byte[84];
        Text(data, 0, "mpet"); Channels(data, 0);
        U32(data, 12, 1); U32(data, 16, 24); U32(data, 20, 60);
        Text(data, 24, "matf"); Channels(data, 24);
        Float(data, 36, x); Float(data, 52, y); Float(data, 68, z);
        return data;
    }

    private static byte[] Xyz(float x, float y, float z)
    {
        byte[] data = new byte[20]; Text(data, 0, "XYZ ");
        Fixed(data, 8, x); Fixed(data, 12, y); Fixed(data, 16, z);
        return data;
    }

    private static byte[] Profile(params (string Name, byte[] Data)[] tags)
    {
        int offset = 132 + 12 * tags.Length;
        byte[] data = new byte[offset + tags.Sum(t => (t.Data.Length + 3) & ~3)];
        U32(data, 0, data.Length); data[8] = 4; data[9] = 0x30;
        Text(data, 12, "mntr"); Text(data, 16, "RGB "); Text(data, 20, "XYZ "); Text(data, 36, "acsp");
        Fixed(data, 68, 0.9642f); Fixed(data, 72, 1); Fixed(data, 76, 0.8249f);
        U32(data, 128, tags.Length);
        for (int i = 0; i < tags.Length; i++)
        {
            Text(data, 132 + 12 * i, tags[i].Name); U32(data, 136 + 12 * i, offset); U32(data, 140 + 12 * i, tags[i].Data.Length);
            tags[i].Data.CopyTo(data, offset); offset += (tags[i].Data.Length + 3) & ~3;
        }
        return data;
    }
    private static void Text(byte[] data, int offset, string text) => Encoding.ASCII.GetBytes(text).CopyTo(data, offset);
    private static void U32(byte[] data, int offset, int value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), checked((uint)value));
    private static void Fixed(byte[] data, int offset, float value) => BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, 4), checked((int)MathF.Round(value * 65536)));
    private static void Float(byte[] data, int offset, float value) => BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));
    private static void Channels(byte[] data, int offset)
    { BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset + 8, 2), 3); BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset + 10, 2), 3); }
}

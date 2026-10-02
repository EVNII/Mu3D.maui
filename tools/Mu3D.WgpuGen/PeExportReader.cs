using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Mu3D.WgpuGen;

internal static class PeExportReader
{
    public static IReadOnlyList<string> Read(string path)
    {
        byte[] image = File.ReadAllBytes(path);
        using MemoryStream stream = new(image, writable: false);
        using PEReader reader = new(stream);
        PEHeaders headers = reader.PEHeaders;
        DirectoryEntry exports = headers.PEHeader?.ExportTableDirectory
            ?? throw new BadImageFormatException("PE image has no optional header.");
        if (exports.RelativeVirtualAddress == 0 || exports.Size == 0)
        {
            return [];
        }

        int directoryOffset = RvaToOffset(headers, exports.RelativeVirtualAddress);
        uint numberOfNames = ReadUInt32(image, directoryOffset + 24);
        uint namesRva = ReadUInt32(image, directoryOffset + 32);
        int namesOffset = RvaToOffset(headers, checked((int)namesRva));
        List<string> names = new(checked((int)numberOfNames));
        for (int index = 0; index < numberOfNames; index++)
        {
            uint nameRva = ReadUInt32(image, namesOffset + (index * sizeof(uint)));
            int nameOffset = RvaToOffset(headers, checked((int)nameRva));
            int terminator = Array.IndexOf(image, (byte)0, nameOffset);
            if (terminator < 0)
            {
                throw new BadImageFormatException("PE export name is not null terminated.");
            }

            names.Add(Encoding.ASCII.GetString(image, nameOffset, terminator - nameOffset));
        }

        return names;
    }

    private static int RvaToOffset(PEHeaders headers, int rva)
    {
        foreach (SectionHeader section in headers.SectionHeaders)
        {
            int size = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + size)
            {
                return checked(rva - section.VirtualAddress + section.PointerToRawData);
            }
        }

        throw new BadImageFormatException($"PE RVA 0x{rva:X8} is outside every section.");
    }

    private static uint ReadUInt32(byte[] image, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset, sizeof(uint)));
    }
}

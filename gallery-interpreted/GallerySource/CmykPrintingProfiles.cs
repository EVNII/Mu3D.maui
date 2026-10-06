using System.Security.Cryptography;
using System.Text.Json;
using Mu3D.Color.Printing;

namespace Mu3D.GalleryApp.Examples;

// One checked-in asset catalog is shared by native and Web; the host supplies its transport.
internal static class CmykPrintingProfiles
{
    internal static readonly string[] OptionalFileNames =
    [
        "JapanColor2011Coated.icc", "GRACoL2013_CRPC6.icc", "SWOP2013C3_CRPC5.icc",
        "PSOcoated_v3.icc", "PSOuncoated_v3_FOGRA52.icc",
    ];

    internal static async Task<IReadOnlyList<CmykPrintingProfileEntry>> LoadDefaultsAsync(
        Func<string, CancellationToken, Task<byte[]>> read,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] catalog = await read("PrintPresets/catalog.json", cancellationToken);
        using JsonDocument document = JsonDocument.Parse(catalog);
        var result = new List<CmykPrintingProfileEntry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in document.RootElement.GetProperty("profiles").EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = item.GetProperty("fileName").GetString() ?? "";
            if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains(':') ||
                !name.EndsWith(".icc", StringComparison.Ordinal) || !names.Add(name))
                throw new InvalidDataException("内置印刷 ICC 目录无效。");
            byte[] bytes = await read("PrintPresets/" + name, cancellationToken);
            string expected = item.GetProperty("sha256").GetString() ?? "";
            if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"内置印刷 ICC 数据不完整：{name}");
            result.Add(new(name, new CmykProfile(bytes)));
        }
        if (result.Count == 0)
            throw new InvalidDataException("内置印刷 ICC 目录为空。");
        return result;
    }
}

internal sealed record CmykPrintingProfileEntry(string FileName, CmykProfile Profile);

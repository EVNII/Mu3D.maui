using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mu3D.GalleryApp.Examples;

namespace Mu3D.Color.Printing.Tests;

internal static class CmykPrintingProfileChecks
{
    private static readonly string[] ExpectedNames = ["GRACoL2013_CRPC6.icc", "SWOP2013C3_CRPC5.icc"];

    internal static string FindDefaultDirectory()
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            string presets = Path.Combine(folder.FullName, "samples", "Mu3D.Gallery", "Resources", "PrintPresets");
            if (File.Exists(Path.Combine(presets, "catalog.json")))
                return presets;
        }
        throw new DirectoryNotFoundException("Checked-in Gallery print presets were not found above the test output.");
    }

    internal static async Task ValidateAsync(string directory, Action<bool, string> check)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["PrintPresets/catalog.json"] = await File.ReadAllBytesAsync(Path.Combine(directory, "catalog.json")),
        };
        foreach (string name in ExpectedNames)
            assets.Add("PrintPresets/" + name, await File.ReadAllBytesAsync(Path.Combine(directory, name)));

        IReadOnlyList<CmykPrintingProfileEntry> loaded = await LoadAsync(assets);
        check(loaded.Select(entry => entry.FileName).SequenceEqual(ExpectedNames), "Shared catalog loads exactly two default regional ICCs in order.");
        foreach (CmykPrintingProfileEntry entry in loaded)
        {
            byte[] original = assets["PrintPresets/" + entry.FileName];
            check(original.AsSpan(12, 4).SequenceEqual("prtr"u8) && original.AsSpan(16, 4).SequenceEqual("CMYK"u8),
                "Default presets are real CMYK output profiles.");
            check(entry.Profile.ToArray().SequenceEqual(original), "Shared loader preserves the exact default ICC bytes.");
        }

        var missingCatalog = new Dictionary<string, byte[]>(assets);
        missingCatalog.Remove("PrintPresets/catalog.json");
        await RejectAsync<FileNotFoundException>(missingCatalog, check, "Missing default catalog fails loading.");
        foreach (string name in ExpectedNames)
        {
            var missing = new Dictionary<string, byte[]>(assets);
            missing.Remove("PrintPresets/" + name);
            await RejectAsync<FileNotFoundException>(missing, check, "Missing declared default ICC fails loading: " + name);

            var corrupted = new Dictionary<string, byte[]>(assets);
            byte[] bytes = assets["PrintPresets/" + name].ToArray();
            bytes[36] ^= 1;
            corrupted["PrintPresets/" + name] = bytes;
            await RejectAsync<InvalidDataException>(corrupted, check, "Changed default ICC fails its manifest hash: " + name);

            corrupted["PrintPresets/catalog.json"] = JsonSerializer.SerializeToUtf8Bytes(new
            {
                profiles = ExpectedNames.Select(fileName => new
                {
                    fileName,
                    sha256 = Convert.ToHexString(SHA256.HashData(corrupted["PrintPresets/" + fileName])),
                }),
            });
            await RejectAsync<InvalidDataException>(corrupted, check, "An invalid ICC fails even with a matching catalog hash: " + name);
        }
        var empty = new Dictionary<string, byte[]>(assets)
        {
            ["PrintPresets/catalog.json"] = Encoding.UTF8.GetBytes("{\"profiles\":[]}"),
        };
        await RejectAsync<InvalidDataException>(empty, check, "Empty default catalog fails loading.");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int reads = 0;
        try
        {
            await CmykPrintingProfiles.LoadDefaultsAsync((_, _) =>
            {
                reads++;
                return Task.FromResult(Array.Empty<byte>());
            }, cancellation.Token);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == cancellation.Token)
        {
            check(reads == 0, "Pre-cancelled default loading fails before any asset read.");
            return;
        }
        throw new InvalidOperationException("Pre-cancelled default loading must propagate cancellation.");
    }

    private static Task<IReadOnlyList<CmykPrintingProfileEntry>> LoadAsync(Dictionary<string, byte[]> assets) =>
        CmykPrintingProfiles.LoadDefaultsAsync((path, token) =>
        {
            token.ThrowIfCancellationRequested();
            return assets.TryGetValue(path, out byte[]? bytes)
                ? Task.FromResult(bytes)
                : throw new FileNotFoundException("Missing fixture asset.", path);
        }, CancellationToken.None);

    private static async Task RejectAsync<T>(Dictionary<string, byte[]> assets, Action<bool, string> check, string message)
        where T : Exception
    {
        try
        {
            await LoadAsync(assets);
        }
        catch (T)
        {
            check(true, message);
            return;
        }
        throw new InvalidOperationException(message);
    }
}

using System.Text.Json;

namespace Mu3D.WgpuGen;

internal static class ManifestIO
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static NativeAssetManifest Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<NativeAssetManifest>(stream, SerializerOptions)
            ?? throw new InvalidDataException($"Manifest '{path}' is empty.");
    }
}

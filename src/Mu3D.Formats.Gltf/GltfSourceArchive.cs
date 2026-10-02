using System.Collections.ObjectModel;

namespace Mu3D.Formats.Gltf;

/// <summary>
/// Owns optional exact encoded source bytes retained with one imported glTF asset.
/// </summary>
/// <remarks>
/// This archive is independent of decoded or transcoded material texture data, which remains
/// referenced by imported materials for normal rendering and device-resource recreation.
/// </remarks>
public sealed class GltfSourceArchive
{
    internal GltfSourceArchive(
        ReadOnlyMemory<byte> mainSource,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> externalResources)
    {
        MainSource = mainSource.ToArray();
        Dictionary<string, ReadOnlyMemory<byte>> ownedResources = new(
            externalResources.Count,
            StringComparer.Ordinal);
        long totalByteCount = MainSource.Length;
        foreach ((string uri, ReadOnlyMemory<byte> source) in externalResources)
        {
            byte[] ownedSource = source.ToArray();
            ownedResources.Add(uri, ownedSource);
            totalByteCount = checked(totalByteCount + ownedSource.Length);
        }
        ExternalResources = new ReadOnlyDictionary<string, ReadOnlyMemory<byte>>(ownedResources);
        TotalByteCount = totalByteCount;
    }

    /// <summary>Gets an owned copy of the exact main glTF or GLB source bytes.</summary>
    public ReadOnlyMemory<byte> MainSource { get; }

    /// <summary>
    /// Gets owned copies of exact external buffer and image bytes, keyed by authored URI.
    /// The dictionary is empty when only the main source was retained.
    /// </summary>
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> ExternalResources { get; }

    /// <summary>Gets the combined number of retained main and external source bytes.</summary>
    public long TotalByteCount { get; }
}

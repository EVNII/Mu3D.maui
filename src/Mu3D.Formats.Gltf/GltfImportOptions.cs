using Mu3D.Assets;
using Mu3D.Graphics;

namespace Mu3D.Formats.Gltf;

/// <summary>Configures application-owned services used while importing one glTF asset.</summary>
public sealed class GltfImportOptions
{
    /// <summary>Gets the optional resolver for non-data external buffer URIs.</summary>
    public Func<string, ReadOnlyMemory<byte>>? ExternalBufferResolver { get; init; }

    /// <summary>
    /// Gets the optional resolver for non-data external image URIs. The application retains
    /// ownership of file, package, network and cache access; the importer only consumes the
    /// returned encoded bytes.
    /// </summary>
    public Func<string, ReadOnlyMemory<byte>>? ExternalImageResolver { get; init; }

    /// <summary>Gets the optional imported scene name override.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the optional decoder used for encoded color and data images.</summary>
    public IEncodedImageDecoder? ImageDecoder { get; init; }

    /// <summary>
    /// Gets the optional application-supplied encoded texture transcoder. No transcoder or native
    /// codec is selected implicitly by the glTF package.
    /// </summary>
    public IEncodedTextureTranscoder? TextureTranscoder { get; init; }

    /// <summary>
    /// Gets the destination device capabilities used by <see cref="TextureTranscoder"/> to select
    /// BC, ETC2 or ASTC output. Required when a transcoder is supplied.
    /// </summary>
    public GraphicsCapabilities? GraphicsCapabilities { get; init; }
}

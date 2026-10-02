using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Assets;

/// <summary>
/// Optionally transcodes an encoded texture container into an immutable GPU-ready compressed
/// mip chain without exposing a native transcoder or container parser to Mu3D Core.
/// </summary>
public interface IEncodedTextureTranscoder
{
    /// <summary>
    /// Attempts to select and transcode a texture for the supplied device capabilities.
    /// Returning null asks the importer to use its decoded RGBA fallback instead.
    /// </summary>
    /// <param name="source">The complete caller-owned encoded texture container.</param>
    /// <param name="mimeType">The normalized source MIME type.</param>
    /// <param name="content">Whether the requested texels represent color or numerical data.</param>
    /// <param name="capabilities">The explicit destination device capabilities.</param>
    /// <param name="name">An optional diagnostic texture name.</param>
    /// <returns>A validated compressed mip chain, or null when no suitable target is available.</returns>
    CompressedMaterialTexture? TryTranscode(
        ReadOnlyMemory<byte> source,
        string mimeType,
        CompressedMaterialTextureContent content,
        GraphicsCapabilities capabilities,
        string? name = null);
}

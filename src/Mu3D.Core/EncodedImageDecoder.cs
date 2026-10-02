using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Assets;

/// <summary>
/// Decodes encoded raster images for asset import without exposing a platform or native codec.
/// Color images return unpremultiplied linear-light pixels with an explicit source color space;
/// data images return normalized channels that have not undergone a color transfer transform.
/// </summary>
public interface IEncodedImageDecoder
{
    /// <summary>
    /// Decodes the full color intent of an encoded image. HDR gain maps must be applied when the
    /// source contains them, and values above one must remain representable in the result.
    /// </summary>
    /// <param name="source">The complete encoded image.</param>
    /// <param name="mimeType">The source MIME type.</param>
    /// <param name="name">An optional diagnostic image name.</param>
    /// <returns>A tagged, unpremultiplied linear-light image.</returns>
    LinearRgbaImage DecodeColor(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null);

    /// <summary>
    /// Decodes normalized non-color channels such as normal, occlusion, roughness and metallic
    /// maps. Encoded channel values are preserved and must not be transfer-decoded as color.
    /// </summary>
    /// <param name="source">The complete encoded image.</param>
    /// <param name="mimeType">The source MIME type.</param>
    /// <param name="name">An optional diagnostic image name.</param>
    /// <returns>An immutable normalized data image.</returns>
    NormalizedRgbaDataImage DecodeData(
        ReadOnlyMemory<byte> source,
        string mimeType,
        string? name = null);
}

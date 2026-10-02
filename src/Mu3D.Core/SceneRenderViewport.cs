using Mu3D.SceneGraph;

namespace Mu3D.Rendering;

/// <summary>
/// Describes one scene/camera pair rendered into a rectangular region of a shared target.
/// </summary>
public sealed class SceneRenderViewport
{
    /// <summary>Initializes one shared-target scene viewport.</summary>
    /// <param name="scene">The scene rendered in the region.</param>
    /// <param name="camera">The camera used for the region.</param>
    /// <param name="x">The region's left edge in physical target pixels.</param>
    /// <param name="y">The region's top edge in physical target pixels.</param>
    /// <param name="width">The non-zero region width in physical target pixels.</param>
    /// <param name="height">The non-zero region height in physical target pixels.</param>
    /// <param name="automaticallyUpdateCameraAspectRatio">
    /// Whether a perspective camera's aspect ratio follows this region before rendering.
    /// </param>
    public SceneRenderViewport(
        Scene scene,
        Camera camera,
        uint x,
        uint y,
        uint width,
        uint height,
        bool automaticallyUpdateCameraAspectRatio = true)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        X = x;
        Y = y;
        Width = width;
        Height = height;
        AutomaticallyUpdateCameraAspectRatio = automaticallyUpdateCameraAspectRatio;
    }

    /// <summary>Gets the scene rendered in this region.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the camera used for this region.</summary>
    public Camera Camera { get; }

    /// <summary>Gets the region's left edge in physical target pixels.</summary>
    public uint X { get; }

    /// <summary>Gets the region's top edge in physical target pixels.</summary>
    public uint Y { get; }

    /// <summary>Gets the region width in physical target pixels.</summary>
    public uint Width { get; }

    /// <summary>Gets the region height in physical target pixels.</summary>
    public uint Height { get; }

    /// <summary>Gets whether a perspective camera follows this region's aspect ratio.</summary>
    public bool AutomaticallyUpdateCameraAspectRatio { get; }
}

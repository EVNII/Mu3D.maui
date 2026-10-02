namespace Mu3D.Graphics;

/// <summary>
/// Represents a configured presentation surface whose physical-pixel extent can be updated by a
/// platform host.
/// </summary>
/// <remarks>
/// A host can report layout changes from its UI thread while presentation occurs on another
/// thread. Implementations must serialize or safely queue <see cref="Resize(uint, uint)"/> with
/// presentation work.
/// </remarks>
public interface IResizablePresentationSurface
{
    /// <summary>Gets the currently configured width in physical pixels.</summary>
    uint Width { get; }

    /// <summary>Gets the currently configured height in physical pixels.</summary>
    uint Height { get; }

    /// <summary>Updates the configured physical-pixel extent.</summary>
    /// <param name="width">The non-zero width in physical pixels.</param>
    /// <param name="height">The non-zero height in physical pixels.</param>
    void Resize(uint width, uint height);
}

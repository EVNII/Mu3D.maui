namespace Mu3D.Toolkit.Viewports;

/// <summary>Requests one future viewport frame without defining a timer or continuous loop.</summary>
/// <remarks>
/// Host adapters implement this narrow boundary by forwarding to their existing coalesced,
/// suspend-aware invalidation mechanism. A request is advisory and may be coalesced with another
/// pending frame.
/// </remarks>
public interface IViewportFrameRequester
{
    /// <summary>Requests one future viewport update and render.</summary>
    void RequestFrame();
}

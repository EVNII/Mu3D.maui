using Mu3D.Graphics;

namespace Mu3D.Web;

/// <summary>Connects a browser viewport to a backend-independent presentation session.</summary>
/// <remarks>
/// The browser adapter supplies physical sizes and schedules frames. The draw callback uses the
/// same Mu3D renderer and Toolkit as native hosts. This handler borrows its session and device;
/// the owner releases them after browser callbacks and any asynchronous readback have settled.
/// Instances are not thread-safe. No scene, GPU backend, render clock or display transform is owned.
/// </remarks>
public sealed class CanvasViewHandler : IDisposable
{
    private IPresentationSurfaceSession? session;
    private bool drawing;
    private bool refreshPending;
    private bool disposed;

    /// <summary>Connects to a presentation session supplied by the browser backend.</summary>
    /// <param name="session">The borrowed session; it is never disposed by this handler.</param>
    public CanvasViewHandler(IPresentationSurfaceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        this.session = session;
    }

    /// <summary>Gets the borrowed session, or null after disconnection completes.</summary>
    /// <remarks>A disconnect requested inside a frame completes when that synchronous frame returns.</remarks>
    public IPresentationSurfaceSession? PresentationSession => session;

    /// <summary>Updates physical sizing, draws into one acquired target and returns its presentation status.</summary>
    /// <param name="width">The non-zero width in physical pixels.</param>
    /// <param name="height">The non-zero height in physical pixels.</param>
    /// <param name="render">The existing scene/custom rendering callback; the target is borrowed for this call.</param>
    /// <param name="refreshSurface">Reconfigures even an unchanged extent after a browser surface change.</param>
    /// <returns>The backend acquisition/submission status, not a GPU or physical-display timing measurement.</returns>
    /// <remarks>
    /// Resize and render run serially. Reentrant draws are rejected. A disconnect requested during
    /// either callback lets the active synchronous frame finish and prevents subsequent frames.
    /// Asynchronous operations started by the caller remain under the caller's lifetime protection.
    /// </remarks>
    public PresentationSurfaceFrameStatus RenderAndPresent(
        uint width, uint height, Action<GraphicsTexture> render, bool refreshSurface = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentOutOfRangeException.ThrowIfZero(width);
        ArgumentOutOfRangeException.ThrowIfZero(height);
        ArgumentNullException.ThrowIfNull(render);
        if (drawing)
        {
            throw new InvalidOperationException("Canvas frames cannot be reentered.");
        }

        IPresentationSurfaceSession current = session!;
        drawing = true;
        try
        {
            if (refreshSurface || refreshPending || current.Width != width || current.Height != height)
            {
                refreshPending = true;
                current.Resize(width, height);
                refreshPending = false;
            }
            return current.RenderAndPresent(render);
        }
        finally
        {
            drawing = false;
            if (disposed) session = null;
        }
    }

    /// <summary>Disconnects without releasing the borrowed session or device.</summary>
    public void Dispose()
    {
        disposed = true;
        if (!drawing) session = null;
    }
}

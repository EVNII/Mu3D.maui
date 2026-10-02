namespace Mu3D.Graphics;

/// <summary>
/// Represents a configured, backend-independent presentation session owned by a platform host.
/// </summary>
/// <remarks>
/// The interface deliberately exposes no wgpu-native types. A MAUI control can own the concrete
/// backend while renderers consume only Mu3D graphics resources and presentation state.
/// Implementations must serialize resize, rendering, presentation and disposal.
/// </remarks>
public interface IPresentationSurfaceSession : IResizablePresentationSurface, IDisposable
{
    /// <summary>Gets the graphics device compatible with this presentation surface.</summary>
    GraphicsDevice Device { get; }

    /// <summary>Gets the capabilities used to configure this surface.</summary>
    SurfaceCapabilities Capabilities { get; }

    /// <summary>Gets the selected format and explicit fallback state.</summary>
    SurfaceOutputPlan OutputPlan { get; }

    /// <summary>Gets the alpha association actually selected for native presentation.</summary>
    /// <remarks>
    /// The default is Unknown for sessions that do not report it. Consumers that apply nonlinear
    /// display transforms must require a resolved Opaque, Premultiplied or Unpremultiplied mode;
    /// the application's requested Automatic or Inherit mode is not an association contract.
    /// </remarks>
    SurfaceAlphaMode AlphaMode => SurfaceAlphaMode.Unknown;

    /// <summary>Gets CPU wall-clock timings for the most recently attempted frame.</summary>
    PresentationSurfaceFrameTimings LastFrameTimings { get; }

    /// <summary>Queries the current HDR characteristics of the display backing this surface.</summary>
    /// <returns>
    /// A point-in-time display snapshot. All fields are unknown when the backend or platform does
    /// not expose live display HDR information.
    /// </returns>
    /// <remarks>
    /// Display state can change after a window moves, resizes, changes monitors or the operating
    /// system changes HDR settings. Callers that use the data for explicit display mapping should
    /// query again after those events. Capability negotiation remains authoritative for whether a
    /// format/color-space pair can be configured. Implementations may require this call on the UI
    /// thread when their platform display API has thread affinity.
    /// </remarks>
    DisplayHdrInfo QueryDisplayHdrInfo() => DisplayHdrInfo.Unknown;

    /// <summary>Renders into one acquired texture and presents it.</summary>
    /// <param name="render">Encodes and submits all work that writes the acquired texture.</param>
    /// <returns>The surface acquisition and presentation result.</returns>
    PresentationSurfaceFrameStatus RenderAndPresent(Action<GraphicsTexture> render);
}

using System.Numerics;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Viewports;

namespace Mu3D.Toolkit.Controls;

/// <summary>
/// Applies map and CAD-style pan, orbit and dolly commands to one perspective camera.
/// </summary>
/// <remarks>
/// Panning is constrained to the plane perpendicular to <see cref="OrbitController.WorldUp"/>,
/// and the default polar constraint keeps the camera on or above that plane. The controller is
/// UI-independent: a host decides which mouse, touch, pen or keyboard input produces each command.
/// The camera and optional frame requester are borrowed.
/// </remarks>
public sealed class MapController : OrbitController
{
    private const float HorizonEpsilon = 0.0001f;

    /// <summary>Initializes a map controller around one world-space target.</summary>
    /// <param name="camera">The borrowed perspective camera whose complete pose is controlled.</param>
    /// <param name="target">The finite world-space map target.</param>
    /// <param name="frameRequester">An optional borrowed one-shot frame requester.</param>
    /// <param name="worldUp">The finite non-zero normal of the map plane.</param>
    public MapController(
        PerspectiveCamera camera,
        Vector3 target,
        IViewportFrameRequester? frameRequester = null,
        Vector3? worldUp = null)
        : base(camera, target, frameRequester, worldUp)
    {
        PanMode = OrbitPanMode.WorldUpPlane;
        MaximumPolarAngle = MathF.PI / 2f - HorizonEpsilon;
    }
}

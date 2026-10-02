using System.Globalization;
using System.Numerics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Overlays;

/// <summary>Projects registered scene-node/local-point bindings for an existing host frame boundary.</summary>
/// <remarks>
/// This optional UI-independent source borrows its scene and targets. It owns no render clock,
/// UI controls, GPU resources or presentation policy. The application serializes scene mutations,
/// registration changes and capture on its owning thread. Capture immediately at the successful
/// frame boundary with that frame's camera matrices and viewport extents.
/// </remarks>
public sealed class SceneNodeAnchorSource : IDisposable
{
    private readonly List<SceneNodeAnchorRegistration> registrations = [];
    private Scene? scene;
    private ulong nextRegistrationId;

    /// <summary>Initializes a source borrowing one scene without changing its hierarchy.</summary>
    /// <param name="scene">The scene whose visible membership determines eligible targets.</param>
    public SceneNodeAnchorSource(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        this.scene = scene;
        SourceId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    }

    /// <summary>Gets the stable opaque source identity, distinct from other source instances.</summary>
    public string SourceId { get; }

    /// <summary>Gets the revision advanced by effective registration, target or local-point changes.</summary>
    public long Revision { get; private set; }

    /// <summary>Registers a borrowed scene target and finite node-local point.</summary>
    /// <param name="node">The borrowed target, or null for a hidden, unbound anchor.</param>
    /// <param name="localPosition">The finite node-local point.</param>
    /// <returns>A disposable registration with a stable source-scoped identity.</returns>
    /// <exception cref="ObjectDisposedException">The source has been disposed.</exception>
    public SceneNodeAnchorRegistration Register(SceneNode? node, Vector3 localPosition = default)
    {
        ThrowIfDisposed();
        ValidatePosition(localPosition);
        ulong id = checked(nextRegistrationId + 1);
        long revision = checked(Revision + 1);
        SceneNodeAnchorRegistration registration = new(
            this, id.ToString(CultureInfo.InvariantCulture), node, localPosition);
        registrations.Add(registration);
        nextRegistrationId = id;
        Revision = revision;
        return registration;
    }

    /// <summary>Captures one complete immutable batch using the supplied frame's exact projection.</summary>
    /// <param name="frame">The immutable camera matrices and physical/logical extents used by the host frame.</param>
    /// <param name="visibilityMask">The camera visibility layers used by that frame.</param>
    /// <returns>A value-only batch retaining no borrowed scene objects.</returns>
    /// <remarks>
    /// Visible scene traversal occurs once per batch. Removed, hidden, masked, unbound,
    /// behind-camera and non-finite targets are unprojected. Finite outside-frustum projections
    /// remain available with <see cref="SceneNodeAnchorPoint.InsideViewport"/> false so the host
    /// adapter can apply its clipping option. Frustum visibility does not establish depth occlusion.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The source has been disposed.</exception>
    public SceneNodeAnchorFrame CaptureFrame(ViewportFrameSnapshot frame, SceneVisibilityMask visibilityMask)
    {
        Scene currentScene = ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(frame);
        HashSet<SceneNode> visible = new(currentScene.EnumerateVisible(visibilityMask), ReferenceEqualityComparer.Instance);
        SceneNodeAnchorPoint[] points = new SceneNodeAnchorPoint[registrations.Count];
        for (int i = 0; i < registrations.Count; i++)
        {
            SceneNodeAnchorRegistration registration = registrations[i];
            SceneNode? node = registration.Node;
            ViewportProjection projection = default;
            bool projected = node is not null && visible.Contains(node) &&
                frame.TryProject(Vector3.Transform(registration.LocalPosition, node.WorldMatrix), out projection) &&
                double.IsFinite(projection.LogicalPosition.X) && double.IsFinite(projection.LogicalPosition.Y);
            points[i] = new(registration.Id, projected, projected && projection.IsInsideViewport,
                projected ? projection.LogicalPosition.X : 0,
                projected ? projection.LogicalPosition.Y : 0,
                projected ? projection.Depth : 0);
        }
        return new(SourceId, Revision, frame.FrameId, frame.LogicalWidth, frame.LogicalHeight, points);
    }

    /// <summary>Releases every registration and the borrowed scene; repeated calls have no effect.</summary>
    public void Dispose()
    {
        if (scene is null)
        {
            return;
        }
        foreach (SceneNodeAnchorRegistration registration in registrations)
        {
            registration.Release();
        }
        registrations.Clear();
        scene = null;
    }

    internal void Update(SceneNodeAnchorRegistration registration, SceneNode? node, Vector3 localPosition)
    {
        ThrowIfDisposed();
        ValidatePosition(localPosition);
        if (ReferenceEquals(registration.Node, node) && registration.LocalPosition == localPosition)
        {
            return;
        }
        long revision = checked(Revision + 1);
        registration.SetTarget(node, localPosition);
        Revision = revision;
    }

    internal void Remove(SceneNodeAnchorRegistration registration)
    {
        if (scene is null)
        {
            return;
        }
        long revision = checked(Revision + 1);
        if (registrations.Remove(registration))
        {
            registration.Release();
            Revision = revision;
        }
    }

    private Scene ThrowIfDisposed() => scene ?? throw new ObjectDisposedException(nameof(SceneNodeAnchorSource));

    private static void ValidatePosition(Vector3 position)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Anchor local positions must be finite.");
        }
    }
}

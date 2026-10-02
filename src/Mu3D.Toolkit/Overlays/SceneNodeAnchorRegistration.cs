using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Overlays;

/// <summary>Represents a replaceable, borrowed scene-node and local-point binding.</summary>
/// <remarks>
/// Dispose the registration to release its target without changing or disposing the target.
/// Access and updates are serialized by the application on the source's owning thread.
/// </remarks>
public sealed class SceneNodeAnchorRegistration : IDisposable
{
    private SceneNodeAnchorSource? owner;

    internal SceneNodeAnchorRegistration(
        SceneNodeAnchorSource owner, string id, SceneNode? node, Vector3 localPosition)
    {
        this.owner = owner;
        Id = id;
        Node = node;
        LocalPosition = localPosition;
    }

    /// <summary>Gets the stable opaque registration identity within the originating source.</summary>
    public string Id { get; }

    /// <summary>Gets the borrowed target, or null when unbound or disposed.</summary>
    public SceneNode? Node { get; private set; }

    /// <summary>Gets the finite node-local point; disposal resets it to zero.</summary>
    public Vector3 LocalPosition { get; private set; }

    /// <summary>Atomically replaces the borrowed target and finite node-local point.</summary>
    /// <param name="node">The new borrowed target, or null to hide this anchor.</param>
    /// <param name="localPosition">The finite node-local point.</param>
    /// <remarks>An unchanged target and point do not advance the source revision.</remarks>
    /// <exception cref="ObjectDisposedException">The registration or its source has been disposed.</exception>
    public void Update(SceneNode? node, Vector3 localPosition = default)
    {
        SceneNodeAnchorSource source = owner ?? throw new ObjectDisposedException(nameof(SceneNodeAnchorRegistration));
        source.Update(this, node, localPosition);
    }

    /// <summary>Releases the registration and its borrowed target; repeated calls have no effect.</summary>
    public void Dispose()
    {
        SceneNodeAnchorSource? source = owner;
        if (source is not null)
        {
            source.Remove(this);
        }
    }

    internal void SetTarget(SceneNode? node, Vector3 localPosition)
    {
        Node = node;
        LocalPosition = localPosition;
    }

    internal void Release()
    {
        owner = null;
        Node = null;
        LocalPosition = default;
    }
}

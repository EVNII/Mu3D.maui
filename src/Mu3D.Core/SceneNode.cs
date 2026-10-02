using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Represents one transformable node in an acyclic scene hierarchy.</summary>
public class SceneNode
{
    private readonly List<SceneNode> children = [];
    private readonly ReadOnlyCollection<SceneNode> readOnlyChildren;
    private bool isPermanentRoot;

    /// <summary>Initializes a scene node.</summary>
    public SceneNode(string? name = null)
    {
        Name = name;
        Transform = new Transform3D();
        readOnlyChildren = children.AsReadOnly();
    }

    /// <summary>Gets or sets the optional application-facing node name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets the mutable local FP32 transform.</summary>
    public Transform3D Transform { get; }

    /// <summary>Gets or sets whether this node participates in visible traversal.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Gets or sets the scene visibility layers assigned to this node. The default is layer zero.
    /// </summary>
    public SceneVisibilityMask VisibilityMask { get; set; } = SceneVisibilityMask.Default;

    /// <summary>Gets the parent node, or null for a hierarchy root.</summary>
    public SceneNode? Parent { get; private set; }

    /// <summary>Gets the ordered, read-only child collection.</summary>
    public IReadOnlyList<SceneNode> Children => readOnlyChildren;

    /// <summary>Gets the current FP32 world matrix without caching stale hierarchy state.</summary>
    public Matrix4x4 WorldMatrix => Parent is null
        ? Transform.LocalMatrix
        : Transform.LocalMatrix * Parent.WorldMatrix;

    /// <summary>Adds or reparents a child while preserving an acyclic hierarchy.</summary>
    public void AddChild(SceneNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.isPermanentRoot)
        {
            throw new InvalidOperationException("A scene's permanent root cannot be reparented.");
        }
        for (SceneNode? ancestor = this; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, child))
            {
                throw new InvalidOperationException("A scene node cannot be parented beneath itself or a descendant.");
            }
        }
        if (ReferenceEquals(child.Parent, this))
        {
            return;
        }

        child.Parent?.RemoveChild(child);
        children.Add(child);
        child.Parent = this;
    }

    /// <summary>Removes a direct child and returns whether it was present.</summary>
    public bool RemoveChild(SceneNode child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (!children.Remove(child))
        {
            return false;
        }
        child.Parent = null;
        return true;
    }

    /// <summary>Removes every direct child while preserving each child's local transform.</summary>
    public void ClearChildren()
    {
        foreach (SceneNode child in children)
        {
            child.Parent = null;
        }
        children.Clear();
    }

    /// <summary>Enumerates this node and all descendants in stable depth-first order.</summary>
    public IEnumerable<SceneNode> EnumerateDepthFirst()
    {
        yield return this;
        foreach (SceneNode child in children)
        {
            foreach (SceneNode descendant in child.EnumerateDepthFirst())
            {
                yield return descendant;
            }
        }
    }

    internal void MarkAsPermanentRoot() => isPermanentRoot = true;
}

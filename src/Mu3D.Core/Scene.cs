namespace Mu3D.SceneGraph;

/// <summary>Owns the root of one backend-independent 3D scene hierarchy.</summary>
public sealed class Scene
{
    /// <summary>Initializes an empty scene.</summary>
    public Scene(string? name = null)
    {
        Name = name;
        Root = new SceneNode("Root");
        Root.MarkAsPermanentRoot();
    }

    /// <summary>Gets or sets the optional scene name.</summary>
    public string? Name { get; set; }

    /// <summary>Gets the permanent hierarchy root.</summary>
    public SceneNode Root { get; }

    /// <summary>Adds a node beneath the permanent root.</summary>
    public void Add(SceneNode node) => Root.AddChild(node);

    /// <summary>Removes a direct root child and returns whether it was present.</summary>
    public bool Remove(SceneNode node) => Root.RemoveChild(node);

    /// <summary>
    /// Enumerates visible descendants in stable depth-first order. An invisible parent suppresses
    /// its complete subtree.
    /// </summary>
    public IEnumerable<SceneNode> EnumerateVisible()
    {
        foreach (SceneNode child in Root.Children)
        {
            foreach (SceneNode visible in EnumerateVisibleSubtree(child, null))
            {
                yield return visible;
            }
        }
    }

    /// <summary>
    /// Enumerates visible descendants whose visibility masks intersect the supplied mask. An
    /// invisible parent suppresses its complete subtree; a parent on another layer does not suppress
    /// independently layered children.
    /// </summary>
    /// <param name="visibilityMask">The layers enabled for the traversal.</param>
    public IEnumerable<SceneNode> EnumerateVisible(SceneVisibilityMask visibilityMask)
    {
        foreach (SceneNode child in Root.Children)
        {
            foreach (SceneNode visible in EnumerateVisibleSubtree(child, visibilityMask))
            {
                yield return visible;
            }
        }
    }

    private static IEnumerable<SceneNode> EnumerateVisibleSubtree(
        SceneNode node,
        SceneVisibilityMask? visibilityMask)
    {
        if (!node.IsVisible)
        {
            yield break;
        }
        if (visibilityMask is null || node.VisibilityMask.Intersects(visibilityMask.Value))
        {
            yield return node;
        }
        foreach (SceneNode child in node.Children)
        {
            foreach (SceneNode visible in EnumerateVisibleSubtree(child, visibilityMask))
            {
                yield return visible;
            }
        }
    }
}

using Mu3D.SceneGraph;

namespace Mu3D.Toolkit.Helpers;

// Match Scene.EnumerateVisible without allocating an iterator or walking unrelated subtrees.
// Permanent-root flags and ancestor layers do not suppress independently layered descendants.
internal static class SceneTargetEligibility
{
    internal static bool TryGetInheritedVisibility(Scene scene, SceneNode target, out bool visible)
    {
        visible = true;
        if (ReferenceEquals(target, scene.Root)) return true;
        for (SceneNode? ancestor = target.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ReferenceEquals(ancestor, scene.Root)) return true;
            visible &= ancestor.IsVisible;
        }
        return false;
    }

    internal static bool IsVisible(Scene scene, SceneNode target, SceneVisibilityMask visibilityMask) =>
        !ReferenceEquals(target, scene.Root) && target.IsVisible && target.VisibilityMask.Intersects(visibilityMask) &&
        TryGetInheritedVisibility(scene, target, out bool inheritedVisible) && inheritedVisible;
}

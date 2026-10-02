using System.Numerics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Overlays;

namespace Mu3D.GalleryApp.Web.Infrastructure;

internal sealed record ViewerNodeHandle(string Token, string Key, string? Name);
internal sealed record ViewerNodeCatalog(string SourceId, ViewerNodeHandle[] Nodes);
internal sealed record ViewerNodeAnchorRegistrationReport(string Id, long Revision, string SourceId);

internal sealed partial class ViewerScene
{
    private SceneNodeAnchorSource nodeAnchorSource = null!;
    private readonly Dictionary<string, SceneNode> nodeHandles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SceneNodeAnchorRegistration> nodeAnchorRegistrations = new(StringComparer.Ordinal);

    internal ViewerNodeCatalog NodeCatalog { get; private set; } = null!;

    private void InitializeNodeAnchors(Scene scene, Mesh[] meshes)
    {
        nodeAnchorSource = new(scene);
        SceneNode[] nodes = [meshes[0], meshes[1], meshes[2], model];
        string[] keys = ["blue", "cone", "green", "model"];
        ViewerNodeHandle[] catalog = new ViewerNodeHandle[nodes.Length];
        for (int index = 0; index < nodes.Length; index++)
        {
            string token = $"node-{index + 1}";
            nodeHandles.Add(token, nodes[index]);
            catalog[index] = new(token, keys[index], nodes[index].Name);
        }
        NodeCatalog = new(nodeAnchorSource.SourceId, catalog);
    }

    internal ViewerNodeAnchorRegistrationReport ConfigureNodeAnchor(string id, string target,
        double x, double y, double z)
    {
        SceneNode? node = target.Length == 0 ? null : nodeHandles.GetValueOrDefault(target) ??
            throw new ArgumentException("The node handle does not belong to this scene source.", nameof(target));
        Vector3 point = new((float)x, (float)y, (float)z);
        SceneNodeAnchorRegistration registration;
        if (id.Length == 0)
        {
            registration = nodeAnchorSource.Register(node, point);
            nodeAnchorRegistrations.Add(registration.Id, registration);
        }
        else
        {
            if (!nodeAnchorRegistrations.TryGetValue(id, out registration!))
                throw new ArgumentException("The anchor registration has been removed or belongs to another source.", nameof(id));
            registration.Update(node, point);
        }
        return new(registration.Id, nodeAnchorSource.Revision, nodeAnchorSource.SourceId);
    }

    internal long RemoveNodeAnchor(string id)
    {
        if (nodeAnchorRegistrations.Remove(id, out SceneNodeAnchorRegistration? registration)) registration.Dispose();
        return nodeAnchorSource.Revision;
    }

    internal SceneNodeAnchorFrame CaptureNodeAnchors(int frame, double logicalWidth, double logicalHeight) =>
        nodeAnchorSource.CaptureFrame(new ViewportFrameSnapshot((ulong)frame, camera.ViewMatrix,
            camera.ProjectionMatrix, viewportWidth, viewportHeight, logicalWidth, logicalHeight), camera.VisibilityMask);

    private void DisposeNodeAnchors()
    {
        nodeAnchorRegistrations.Clear();
        nodeHandles.Clear();
        nodeAnchorSource.Dispose();
    }
}

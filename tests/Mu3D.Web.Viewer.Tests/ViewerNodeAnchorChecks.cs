using System.Text.Json;
using System.Text.Json.Serialization;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Overlays;
using Mu3D.GalleryApp.Web.Infrastructure;
using static ViewerTestChecks;

internal static class ViewerNodeAnchorChecks
{
    internal static void Validate(Action<bool, string> check)
    {
        using ViewerScene viewer = new(SharedValidationScene.Create(), new PerspectiveCamera());
        viewer.SetViewport(1000, 600);
        ViewerNodeCatalog catalog = viewer.NodeCatalog;
        check(catalog.Nodes.Select(node => node.Key).SequenceEqual(["blue", "cone", "green", "model"]),
            "The host exposes stable opaque tokens for its borrowed scene nodes.");
        ViewerNodeAnchorRegistrationReport registration = viewer.ConfigureNodeAnchor("", catalog.Nodes[0].Token, 0, .8, 0);
        SceneNodeAnchorFrame before = viewer.CaptureNodeAnchors(1, 500, 300);
        check(before.SourceId == catalog.SourceId && before.Revision == registration.Revision &&
            before.Points.Single().Id == registration.Id && before.Points.Single().Projected,
            "A host registration is captured through the shared source at logical extents.");
        viewer.ConfigureNodeAnchor(registration.Id, catalog.Nodes[2].Token, 0, .8, 0);
        SceneNodeAnchorFrame after = viewer.CaptureNodeAnchors(2, 500, 300);
        check(after.Revision > before.Revision && after.Points.Single().X > before.Points.Single().X,
            "Retargeting keeps registration identity and advances the frame generation.");
        long revision = after.Revision;
        check(Throws<ArgumentException>(() => viewer.ConfigureNodeAnchor(registration.Id, "foreign-node", 0, 0, 0)) &&
            viewer.CaptureNodeAnchors(3, 500, 300).Revision == revision,
            "Unknown node handles cannot mutate a registered target.");
        check(Throws<ArgumentOutOfRangeException>(() =>
            viewer.ConfigureNodeAnchor(registration.Id, catalog.Nodes[0].Token, double.MaxValue, 0, 0)) &&
            viewer.CaptureNodeAnchors(4, 500, 300).Revision == revision,
            "Out-of-range bridge coordinates cannot mutate a registered target.");
        viewer.ConfigureNodeAnchor(registration.Id, "", 0, 0, 0);
        check(!viewer.CaptureNodeAnchors(5, 500, 300).Points.Single().Projected, "An explicit null target hides its registration.");
        viewer.RemoveNodeAnchor(registration.Id);
        check(viewer.CaptureNodeAnchors(6, 500, 300).Points.Count == 0, "Bridge cleanup removes its managed registration.");
        string json = JsonSerializer.Serialize(before, ViewerNodeAnchorJsonContext.Default.SceneNodeAnchorFrame);
        using JsonDocument parsed = JsonDocument.Parse(json);
        check(parsed.RootElement.GetProperty("SourceId").GetString() == catalog.SourceId &&
            parsed.RootElement.GetProperty("Points")[0].GetProperty("Projected").GetBoolean(),
            "Source-generated frame serialization retains the managed/JS projection contract.");
    }
}

[JsonSerializable(typeof(SceneNodeAnchorFrame))]
internal partial class ViewerNodeAnchorJsonContext : JsonSerializerContext;

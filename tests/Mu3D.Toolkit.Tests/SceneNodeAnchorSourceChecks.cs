using System.Numerics;
using System.Runtime.CompilerServices;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Overlays;

internal static class SceneNodeAnchorSourceChecks
{
    internal static void Validate(Action<bool, string> check)
    {
        Scene scene = new();
        SceneNode parent = new();
        SceneNode first = new();
        SceneNode second = new();
        parent.AddChild(first);
        parent.AddChild(second);
        scene.Add(parent);
        PerspectiveCamera camera = new(aspectRatio: 5f / 3f);
        camera.Transform.Position = new(0, 0, 5);
        ViewportFrameSnapshot Frame(ulong id = 1, uint width = 1000, uint height = 600,
            double logicalWidth = 500, double logicalHeight = 300) => new(
                id, camera.ViewMatrix, camera.ProjectionMatrix, width, height, logicalWidth, logicalHeight);
        using SceneNodeAnchorSource source = new(scene);
        check(source.Revision == 0 && !string.IsNullOrEmpty(source.SourceId) &&
            source.CaptureFrame(Frame(), SceneVisibilityMask.Default).Points.Count == 0,
            "An empty source captures an empty batch without changing registration state.");
        using SceneNodeAnchorRegistration center = source.Register(first);
        using SceneNodeAnchorRegistration top = source.Register(first, Vector3.UnitY);
        using SceneNodeAnchorRegistration unbound = source.Register(null);
        check(source.Revision == 3 && center.Id != top.Id && top.Id != unbound.Id &&
            center.Node == first && top.LocalPosition == Vector3.UnitY,
            "Independent registrations retain distinct opaque IDs and borrowed local targets.");
        SceneNodeAnchorFrame initial = source.CaptureFrame(Frame(ulong.MaxValue), SceneVisibilityMask.Default);
        check(initial.SourceId == source.SourceId && initial.Revision == 3 &&
            initial.FrameId == ulong.MaxValue && initial.Width == 500 && initial.Height == 300,
            "The batch preserves source identity, registration revision, logical extent and full UInt64 frame ID.");
        check(initial.Points[0].Projected && initial.Points[0].InsideViewport &&
            Math.Abs(initial.Points[0].X - 250) < 0.001 && Math.Abs(initial.Points[0].Y - 150) < 0.001 &&
            initial.Points[1].Y < initial.Points[0].Y &&
            !initial.Points[2].Projected && !initial.Points[2].InsideViewport &&
            initial.Points[2].X == 0 && initial.Points[2].Y == 0 && initial.Points[2].Depth == 0,
            "Multiple local points on one node project independently and a null target is explicitly hidden.");
        center.Update(first);
        top.Update(first, Vector3.UnitY);
        check(source.Revision == 3, "Unchanged target/local-point assignments emit no duplicate revision.");
        center.Update(second, new(0.5f, 0, 0));
        check(source.Revision == 4 && center.Node == second && center.LocalPosition == new Vector3(0.5f, 0, 0),
            "One atomic target/local-point replacement advances the source revision exactly once.");
        string originalId = center.Id;
        center.Update(null);
        check(source.Revision == 5 && center.Id == originalId && center.Node is null &&
            !source.CaptureFrame(Frame(2), SceneVisibilityMask.Default).Points[0].Projected,
            "Retargeting to null hides the existing registration without replacing its identity.");
        center.Update(first);

        SceneNodeAnchorFrame atOriginal = source.CaptureFrame(Frame(3), SceneVisibilityMask.Default);
        parent.Transform.Position = new(0.3f, 0.2f, -0.4f);
        parent.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.2f);
        parent.Transform.Scale = new(0.8f, 1.2f, 1);
        first.Transform.Position = new(0.1f, 0.2f, 0);
        first.Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.3f);
        first.Transform.Scale = new(1.5f, 1, 0.6f);
        ViewportFrameSnapshot transformedFrame = Frame(4);
        SceneNodeAnchorFrame transformed = source.CaptureFrame(transformedFrame, SceneVisibilityMask.Default);
        transformedFrame.TryProject(Vector3.Transform(Vector3.UnitY, first.WorldMatrix), out ViewportProjection expected);
        check(Math.Abs(transformed.Points[1].X - expected.LogicalPosition.X) < 0.00001 &&
            Math.Abs(transformed.Points[1].Y - expected.LogicalPosition.Y) < 0.00001 &&
            transformed.Points[1].Depth == expected.Depth && transformed.Points[0] != atOriginal.Points[0],
            "Captured node and ancestor TRS reuse Core's exact row-vector projection.");
        camera.Transform.Position = new(0.3f, 0.4f, 5);
        ViewportFrameSnapshot movedCamera = Frame(5);
        SceneNodeAnchorFrame moved = source.CaptureFrame(movedCamera, SceneVisibilityMask.Default);
        check(moved.Points[1].X != transformed.Points[1].X && moved.Points[1].Y != transformed.Points[1].Y,
            "Camera motion updates the next batch in the rendered frame's camera space.");
        camera.Transform.Position = new(2, 2, 8);
        check(source.CaptureFrame(movedCamera, SceneVisibilityMask.Default).Points.SequenceEqual(moved.Points),
            "A supplied captured camera snapshot does not reread a subsequently changed camera.");
        camera.Transform.Position = new(0.3f, 0.4f, 5);
        check(source.CaptureFrame(Frame(6, 2000, 1200), SceneVisibilityMask.Default).Points.SequenceEqual(moved.Points),
            "A DPR-only backing-pixel change preserves logical host placement.");
        SceneNodeAnchorFrame resized = source.CaptureFrame(Frame(7, 2000, 1200, 1000, 600), SceneVisibilityMask.Default);
        check(Math.Abs(resized.Points[1].X - moved.Points[1].X * 2) < 0.00001 &&
            Math.Abs(resized.Points[1].Y - moved.Points[1].Y * 2) < 0.00001,
            "Logical resizing scales HTML/MAUI placement independently of backing pixels.");
        check(initial.Points[0].X == 250 && initial.Points[1].Y < 150 && initial.Revision == 3 &&
            atOriginal.Points[0].X == 250,
            "Old frame batches retain projection and revision after live node/camera/registration mutations.");

        first.IsVisible = false;
        check(!source.CaptureFrame(Frame(8), SceneVisibilityMask.Default).Points[0].Projected &&
            !source.CaptureFrame(Frame(8), SceneVisibilityMask.Default).Points[1].Projected,
            "Hidden targets suppress every local binding to the same node.");
        first.IsVisible = true;
        parent.IsVisible = false;
        check(source.CaptureFrame(Frame(9), SceneVisibilityMask.Default).Points.All(point => !point.Projected),
            "An invisible ancestor suppresses its complete anchored subtree.");
        parent.IsVisible = true;
        first.VisibilityMask = SceneVisibilityMask.FromLayer(1);
        check(!source.CaptureFrame(Frame(10), SceneVisibilityMask.Default).Points[0].Projected &&
            source.CaptureFrame(Frame(10), SceneVisibilityMask.FromLayer(1)).Points[0].Projected,
            "Camera visibility layers apply to a target without requiring its parent to share the layer.");
        check(source.CaptureFrame(Frame(11), SceneVisibilityMask.None).Points.All(point => !point.Projected),
            "A camera with no enabled layers has no visible anchored targets.");
        first.VisibilityMask = SceneVisibilityMask.Default;
        parent.RemoveChild(first);
        check(!source.CaptureFrame(Frame(12), SceneVisibilityMask.All).Points[0].Projected,
            "Removed targets disappear instead of leaving stale host UI at an old position.");
        Scene otherScene = new();
        otherScene.Add(first);
        check(!source.CaptureFrame(Frame(13), SceneVisibilityMask.All).Points[0].Projected,
            "A target moved into another scene cannot remain visible in the original source.");
        parent.AddChild(first);
        parent.Transform.Position = default;
        parent.Transform.Rotation = Quaternion.Identity;
        parent.Transform.Scale = Vector3.One;
        first.Transform.Rotation = Quaternion.Identity;
        first.Transform.Scale = Vector3.One;
        first.Transform.Position = new(100, 0, 0);
        SceneNodeAnchorPoint outside = source.CaptureFrame(Frame(14), SceneVisibilityMask.All).Points[0];
        check(outside.Projected && !outside.InsideViewport && double.IsFinite(outside.X),
            "A finite point outside the clip volume retains a usable unclipped logical coordinate.");
        first.Transform.Position = new(0, 0, 10);
        check(!source.CaptureFrame(Frame(15), SceneVisibilityMask.All).Points[0].Projected,
            "A behind-camera point is unprojected independently of host clipping options.");
        first.Transform.Position = default;
        first.Transform.Scale = new(float.MaxValue);
        parent.Transform.Scale = new(float.MaxValue);
        check(!source.CaptureFrame(Frame(16), SceneVisibilityMask.All).Points[0].Projected,
            "Overflowed FP32 world matrices are hidden without leaking non-finite JSON/UI values.");
        parent.Transform.Scale = Vector3.One;
        first.Transform.Scale = Vector3.One;
        first.Transform.Position = new(100, 0, 0);
        check(!source.CaptureFrame(Frame(17, logicalWidth: double.MaxValue), SceneVisibilityMask.All).Points[0].Projected,
            "A finite Core extent whose outside logical projection overflows is explicitly unprojected.");
        first.Transform.Position = default;

        long beforeInvalid = source.Revision;
        foreach (Vector3 invalid in new[] { new Vector3(float.NaN, 0, 0), new Vector3(0, float.PositiveInfinity, 0), new Vector3(0, 0, float.NegativeInfinity) })
        {
            check(Rejects<ArgumentOutOfRangeException>(() => center.Update(second, invalid)) &&
                center.Node == first && center.LocalPosition == default && source.Revision == beforeInvalid,
                "Non-finite updates cannot partially retarget an existing registration or advance revision.");
            check(Rejects<ArgumentOutOfRangeException>(() => source.Register(second, invalid)) &&
                source.Revision == beforeInvalid,
                "Non-finite registrations are rejected without changing source state.");
        }
        check(Rejects<ArgumentNullException>(() => source.CaptureFrame(null!, SceneVisibilityMask.All)),
            "Capture requires an actual immutable viewport frame.");

        SceneNodeAnchorPoint[] input = [new("id", true, true, 1, 2, 0.5f)];
        SceneNodeAnchorFrame owned = new("source", 0, ulong.MaxValue, 10, 20, input);
        input[0] = new("changed", false, false, 9, 9, 0);
        check(owned.Points[0].Id == "id" && owned.Points[0].X == 1 && owned.FrameId == ulong.MaxValue,
            "The public frame constructor defensively copies caller-owned point arrays.");
        check(Rejects<NotSupportedException>(() => ((IList<SceneNodeAnchorPoint>)owned.Points)[0] = input[0]),
            "The returned frame collection cannot be used to mutate a captured batch.");
        foreach (double invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            check(Rejects<ArgumentOutOfRangeException>(() => new SceneNodeAnchorFrame("source", 0, 0, invalid, 10, [])) &&
                Rejects<ArgumentOutOfRangeException>(() => new SceneNodeAnchorFrame("source", 0, 0, 10, invalid, [])),
                "Frame logical extents use the same positive/finite contract as Core snapshots.");
        }
        check(Rejects<ArgumentException>(() => new SceneNodeAnchorFrame("", 0, 0, 10, 10, [])) &&
            Rejects<ArgumentOutOfRangeException>(() => new SceneNodeAnchorFrame("source", -1, 0, 10, 10, [])) &&
            Rejects<ArgumentNullException>(() => new SceneNodeAnchorFrame("source", 0, 0, 10, 10, null!)) &&
            Rejects<ArgumentException>(() => new SceneNodeAnchorFrame("source", 0, 0, 10, 10, [default])) &&
            Rejects<ArgumentException>(() => new SceneNodeAnchorFrame("source", 0, 0, 10, 10, [new("id", true, true, double.NaN, 0, 0)])),
            "Public frames reject invalid identity, revision, point batches and non-finite coordinates.");
        check(Rejects<ArgumentException>(() => new SceneNodeAnchorFrame("source", 0, 0, 10, 10,
            [new("id", true, true, 1, 2, 0), new("id", true, true, 3, 4, 0)])) &&
            new SceneNodeAnchorFrame("source", 0, 0, 10, 10,
                [new("id", false, false, 0, 0, 0), new("ID", false, false, 0, 0, 0)]).Points.Count == 2,
            "Frame registration IDs must be unique using the same ordinal identity comparison as the JS map.");

        using SceneNodeAnchorSource another = new(scene);
        using SceneNodeAnchorRegistration anotherRegistration = another.Register(first);
        check(another.SourceId != source.SourceId && anotherRegistration.Id == center.Id,
            "Registration IDs are source-scoped and independent source identities prevent collisions.");
        long beforeRemove = source.Revision;
        top.Dispose();
        check(source.Revision == beforeRemove + 1 && top.Node is null && top.LocalPosition == default &&
            source.CaptureFrame(Frame(18), SceneVisibilityMask.All).Points.Count == 2,
            "Registration disposal advances revision once, removes projection and releases its borrowed node.");
        top.Dispose();
        check(source.Revision == beforeRemove + 1 && Rejects<ObjectDisposedException>(() => top.Update(first)),
            "Registration cleanup is idempotent and disposed updates are explicit errors.");
        source.Dispose();
        source.Dispose();
        check(center.Node is null && unbound.Node is null && scene.Root.Children.Contains(parent) &&
            parent.Children.Contains(first) && another.CaptureFrame(Frame(19), SceneVisibilityMask.All).Points[0].Projected,
            "Source disposal releases bindings while preserving borrowed scene/nodes and another source.");
        check(Rejects<ObjectDisposedException>(() => source.Register(first)) &&
            Rejects<ObjectDisposedException>(() => source.CaptureFrame(Frame(), SceneVisibilityMask.All)) &&
            Rejects<ObjectDisposedException>(() => center.Update(first)),
            "Disposed source/registration calls reject work without mutating borrowed scene state.");
    }

    internal static async Task ValidateBorrowedReferences(Action<bool, string> check)
    {
        var (retainedSource, retainedRegistration, weakScene, weakNode) = BorrowedReferences();
        // Yield past the creation/disposal stack before testing collection of borrowed objects.
        await Task.Delay(1);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        check(!weakScene.IsAlive && !weakNode.IsAlive && retainedRegistration.Node is null,
            "Retaining disposed source/registration handles does not retain their borrowed scene or node.");
        GC.KeepAlive(retainedSource);
        GC.KeepAlive(retainedRegistration);
    }

    private static bool Rejects<TException>(Action action) where TException : Exception
    {
        try { action(); return false; }
        catch (TException) { return true; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (SceneNodeAnchorSource, SceneNodeAnchorRegistration, WeakReference, WeakReference) BorrowedReferences()
    {
        Scene scene = new();
        SceneNode node = new();
        scene.Add(node);
        SceneNodeAnchorSource source = new(scene);
        SceneNodeAnchorRegistration registration = source.Register(node);
        WeakReference weakScene = new(scene), weakNode = new(node);
        source.Dispose();
        return (source, registration, weakScene, weakNode);
    }
}

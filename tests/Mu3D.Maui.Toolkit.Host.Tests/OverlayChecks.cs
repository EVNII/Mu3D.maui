using System.Numerics;
using System.Reflection;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Maui.Toolkit.Overlays;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using MauiPoint = Microsoft.Maui.Graphics.Point;

internal static class OverlayChecks
{
    internal static void VerifyGeneric()
    {
        using OverlayFixture f = new();
        using ViewportOverlayManager manager = new(f.View, f.Arbiter);
        ViewportToolContext context = new(f.View, f.Arbiter, manager);
        SceneNodeAnchor anchor = new() { Node = f.Node, HideWhenOutsideViewport = false };
        ViewportOverlay overlay = new() { Anchor = anchor, Content = new Label { Text = "real MAUI anchored content" } };
        using IDisposable attachment = overlay.Attach(context);
        Grid presenter = (Grid)overlay.Parent;
        Check(!presenter.IsVisible, "An attached node overlay has no placement before a successful frame, even without outside clipping.");
        f.View.PublishFrame();
        Check(presenter.IsVisible, "A matching borrowed scene node appears after a successful frame.");
        CheckPlacement(presenter.TranslationX, presenter.TranslationY, f.Node, Vector3.Zero,
            f.View.LatestFrameSnapshot!, anchor.Offset);
        PointerGestureRecognizer pointer = presenter.GestureRecognizers.OfType<PointerGestureRecognizer>().Single();
        Press(pointer, presenter);
        Check(f.Arbiter.CurrentLease?.Priority == ViewportControlPriorities.OverlayUi,
            "The actual MAUI pointer recognizer acquires a real overlay input lease.");
        f.Parent.IsVisible = false;
        f.View.PublishFrame();
        Check(!presenter.IsVisible && f.Arbiter.CurrentLease is null,
            "Hiding an anchored overlay releases its input lease immediately on the frame boundary.");
        Press(pointer, presenter);
        Check(f.Arbiter.CurrentLease is null, "Queued presses against hidden anchored content acquire no lease.");
        f.Parent.IsVisible = true;
        f.View.PublishFrame();
        Check(presenter.IsVisible, "A retained node reappears after ancestor visibility returns.");
        anchor.X = 1;
        CheckPlacement(presenter.TranslationX, presenter.TranslationY, f.Node, Vector3.UnitX,
            f.View.LatestFrameSnapshot!, anchor.Offset);
        anchor.X = 0;

        foreach (string reason in new[] { "null", "own", "mask", "detached", "foreign", "root", "behind", "no-frame" })
        {
            Action restore = f.MakeInvalid(reason, node => anchor.Node = node);
            if (reason != "no-frame") f.View.PublishFrame();
            Check(!presenter.IsVisible, reason + ": generic overlay hides rather than retaining stale placement.");
            restore();
            f.View.PublishFrame();
            Check(presenter.IsVisible, reason + ": restoring the same borrowed node resumes finite placement.");
            Check(double.IsFinite(presenter.TranslationX) && double.IsFinite(presenter.TranslationY), "Reappearance coordinates remain finite.");
        }
        f.Node.Transform.Position = new(100, 0, 0);
        f.View.PublishFrame();
        Check(presenter.IsVisible && double.IsFinite(presenter.TranslationX),
            "A finite projected off-frustum point remains visible when outside clipping is disabled.");
        Press(pointer, presenter);
        anchor.HideWhenOutsideViewport = true;
        Check(!presenter.IsVisible && f.Arbiter.CurrentLease is null, "Enabling outside clipping hides and releases the active UI lease.");
        anchor.HideWhenOutsideViewport = false;
        Check(presenter.IsVisible, "Disabling outside clipping restores an otherwise valid projection.");
        f.Node.Transform.Position = Vector3.Zero;
        f.View.PublishFrame();

        f.Parent.VisibilityMask = SceneVisibilityMask.FromLayer(2);
        f.Scene.Root.IsVisible = false;
        f.Scene.Root.VisibilityMask = SceneVisibilityMask.None;
        f.View.PublishFrame();
        Check(presenter.IsVisible, "Permanent-root flags and ancestor layer masks do not suppress the independently layered anchor.");
        f.Node.VisibilityMask = SceneVisibilityMask.FromLayer(2);
        f.Camera.VisibilityMask = SceneVisibilityMask.FromLayer(2);
        f.Parent.VisibilityMask = SceneVisibilityMask.Default;
        f.View.PublishFrame();
        Check(presenter.IsVisible, "A camera-matching target remains visible below a differently layered parent.");
        f.Node.VisibilityMask = SceneVisibilityMask.Default;
        f.Camera.VisibilityMask = SceneVisibilityMask.Default;
        f.View.PublishFrame();

        PerspectiveCamera replacement = OverlayFixture.CreateCamera();
        f.View.Camera = replacement;
        Check(!presenter.IsVisible && f.View.LatestFrameSnapshot is null,
            "Camera instance replacement invalidates the manager's placement and the view's prior successful snapshot.");
        anchor.Offset = new(9, -27);
        Check(!presenter.IsVisible, "Bindable anchor refresh cannot revive the stale snapshot after camera replacement.");
        ViewportOverlay late = new() { Anchor = new SceneNodeAnchor { Node = f.Node } };
        using (IDisposable lateAttachment = late.Attach(context))
            Check(!((Grid)late.Parent).IsVisible, "New registration cannot reuse the old view snapshot after context invalidation.");
        f.View.PublishFrame();
        Check(presenter.IsVisible, "A new successful frame accepts the replacement camera context.");
        f.View.Scene = new Scene("replacement context");
        Check(!presenter.IsVisible, "Scene instance replacement immediately invalidates generic placement.");
        f.View.Scene = f.Scene;
        Check(!presenter.IsVisible, "Returning to the previous scene still waits for a successful frame.");
        f.View.PublishFrame();
        Check(presenter.IsVisible, "The original borrowed node resumes under an accepted frame.");

        anchor.Node = null;
        anchor.Target = new SceneNode3D(f.Node);
        Check(presenter.IsVisible, "The actual anchor resolves declarative Target through its borrowed Core-node facade.");
        Press(pointer, presenter);
        attachment.Dispose();
        Check(overlay.Parent is null && f.Arbiter.CurrentLease is null,
            "The actual attachment removes real visual parenting and releases an active input lease.");
        double detachedX = presenter.TranslationX;
        anchor.X = 2;
        Press(pointer, presenter);
        f.View.PublishFrame();
        Check(presenter.TranslationX == detachedX && f.Arbiter.CurrentLease is null,
            "Detached registrations react to no anchor/frame/pointer events.");
        manager.Dispose();
        Check(f.View.FrameSubscriberCount == 0 && f.View.Children.Count == 0,
            "Manager disposal releases its real frame subscription and owned MAUI overlay root.");
        ReentrantGenericBatches();
    }

    internal static void VerifyLayer()
    {
        using OverlayFixture f = new();
        SceneNodeAnchorLayer layer = new() { SceneView = f.View };
        Label child = new() { Text = "real MAUI batched anchor" };
        layer.Children.Add(child);
        SceneNodeAnchorLayer.SetNode(child, f.Node);
        SceneNodeAnchorLayer.SetHideWhenOutsideViewport(child, false);
        layer.Refresh();
        Check(!child.IsVisible && f.View.FrameSubscriberCount == 0,
            "An unloaded standalone layer cannot revive a placement or subscribe to a viewport.");
        Lifecycle(layer, "SendLoaded");
        Check(f.View.FrameSubscriberCount == 1 && !child.IsVisible,
            "The real MAUI Loaded event attaches the standalone layer while an absent frame remains hidden.");
        f.View.PublishFrame();
        Check(child.IsVisible, "The loaded standalone layer projects from a real successful frame.");
        CheckLayerPlacement(child, f.Node, f.View.LatestFrameSnapshot!);
        foreach (string reason in new[] { "null", "ancestor", "own", "mask", "detached", "foreign", "root", "behind", "no-frame" })
        {
            Action restore = f.MakeInvalid(reason, node => SceneNodeAnchorLayer.SetNode(child, node));
            if (reason != "no-frame") f.View.PublishFrame();
            Check(!child.IsVisible, reason + ": layer always hides an invalid anchor even when outside clipping is disabled.");
            restore();
            f.View.PublishFrame();
            Check(child.IsVisible, reason + ": the layer resumes its retained borrowed node under the next valid frame.");
            CheckLayerPlacement(child, f.Node, f.View.LatestFrameSnapshot!);
        }
        f.Node.Transform.Position = new(100, 0, 0);
        f.View.PublishFrame();
        Check(child.IsVisible && double.IsFinite(AbsoluteLayout.GetLayoutBounds(child).X),
            "Layer outside-clipping false permits finite off-frustum coordinates.");
        SceneNodeAnchorLayer.SetHideWhenOutsideViewport(child, true);
        Check(!child.IsVisible, "Layer outside-clipping true suppresses that valid off-frustum point.");
        SceneNodeAnchorLayer.SetHideWhenOutsideViewport(child, false);
        f.Node.Transform.Position = Vector3.Zero;
        f.Parent.VisibilityMask = SceneVisibilityMask.FromLayer(2);
        f.Scene.Root.IsVisible = false;
        f.Scene.Root.VisibilityMask = SceneVisibilityMask.None;
        f.View.PublishFrame();
        Check(child.IsVisible, "Layer root/ancestor-layer policy follows the shared scene eligibility contract.");

        f.View.Camera = OverlayFixture.CreateCamera();
        Check(!child.IsVisible && f.View.LatestFrameSnapshot is null,
            "Standalone camera replacement invalidates its observed frame and the real host protocol clears the view snapshot.");
        layer.Refresh();
        SceneNodeAnchorLayer.SetOffset(child, new(10, -26));
        Check(!child.IsVisible, "Standalone Refresh/attached-property changes cannot revive the stale context frame.");
        f.View.PublishFrame();
        Check(child.IsVisible, "A new frame establishes the replaced camera's placement.");
        f.View.Scene = new Scene("layer replacement scene");
        Check(!child.IsVisible, "Standalone scene replacement immediately hides old accepted anchors.");
        f.View.Scene = f.Scene;
        f.View.PublishFrame();
        Check(child.IsVisible, "Returning to the original borrowed scene resumes on its new frame.");

        using ViewportOverlayManager manager = new(f.View, f.Arbiter);
        ViewportToolContext context = new(f.View, f.Arbiter, manager);
        Check(f.View.FrameSubscriberCount == 2, "Standalone layer and manager initially have independent real subscriptions.");
        using IDisposable attachment = layer.Attach(context);
        Check(f.View.FrameSubscriberCount == 1 && child.IsVisible,
            "Managing an already-loaded same-view layer detaches its standalone subscription exactly once.");
        f.View.PublishInvalidation();
        Check(!child.IsVisible, "Managed null-frame invalidation clears visible placement.");
        layer.Refresh();
        Check(!child.IsVisible, "Managed Refresh keeps the cleared frame hidden.");
        f.View.PublishFrame();
        Check(child.IsVisible, "Managed frames resume the same Core node without replacing the layer.");
        attachment.Dispose();
        Check(f.View.FrameSubscriberCount == 2 && layer.Parent is null,
            "Managed detach restores one intended standalone subscription and releases manager visual ownership.");
        Lifecycle(layer, "SendUnloaded");
        Check(f.View.FrameSubscriberCount == 1 && !child.IsVisible,
            "Real MAUI Unloaded releases standalone observation and hides prior placement.");
        layer.Refresh();
        f.View.PublishFrame();
        Check(!child.IsVisible, "An unloaded layer cannot refresh from a retained view snapshot.");
        Lifecycle(layer, "SendLoaded");
        Check(f.View.FrameSubscriberCount == 2 && child.IsVisible,
            "Reload observes the view again without accumulating duplicate subscriptions.");
        Lifecycle(layer, "SendUnloaded");
        manager.Dispose();
        Check(f.View.FrameSubscriberCount == 0 && f.View.Children.Count == 0,
            "Layer and manager lifetime cleanup leave no frame subscriptions or owned root.");
        ReentrantLayerBatch();
        UnobservedContextTransitions();
    }

    private static void Press(PointerGestureRecognizer pointer, View presenter)
    {
        // The portable Controls assembly retains its real platform event sender internally.
        MethodInfo sender = typeof(PointerGestureRecognizer).GetMethod("SendPointerPressed",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The real MAUI pointer event sender is unavailable.");
        Func<IElement, MauiPoint?> getPosition = _ => new MauiPoint(0, 0);
        sender.Invoke(pointer, [presenter, getPosition, null, ButtonsMask.Primary]);
    }

    private static void ReentrantGenericBatches()
    {
        foreach (string property in new[] { nameof(VisualElement.TranslationX), nameof(VisualElement.IsVisible) })
        foreach (bool replaceScene in new[] { false, true })
        {
            using OverlayFixture f = new();
            using ViewportOverlayManager manager = new(f.View, f.Arbiter);
            ViewportToolContext context = new(f.View, f.Arbiter, manager);
            ViewportOverlay first = new() { Anchor = new SceneNodeAnchor { Node = f.Node } };
            ViewportOverlay second = new() { Anchor = new SceneNodeAnchor { Node = f.Node } };
            using IDisposable firstLease = first.Attach(context), secondLease = second.Attach(context);
            Grid firstPresenter = (Grid)first.Parent, secondPresenter = (Grid)second.Parent;
            bool changed = false;
            firstPresenter.PropertyChanged += (_, args) =>
            {
                if (changed || args.PropertyName != property) return;
                changed = true;
                if (replaceScene) f.View.Scene = new Scene("layout callback replacement");
                else f.View.Camera = OverlayFixture.CreateCamera();
            };
            f.View.PublishFrame();
            Check(changed && !firstPresenter.IsVisible && !secondPresenter.IsVisible && f.View.LatestFrameSnapshot is null,
                property + ": reentrant context replacement hides the active registration and every later old-batch registration.");
            f.View.Scene = f.Scene;
            f.View.PublishFrame();
            Check(firstPresenter.IsVisible && secondPresenter.IsVisible,
                "The next accepted context frame resumes all retained registrations after batch invalidation.");
        }
    }

    private static void ReentrantLayerBatch()
    {
        using OverlayFixture f = new();
        SceneNodeAnchorLayer layer = new() { SceneView = f.View };
        Label first = new(), second = new();
        layer.Children.Add(first); layer.Children.Add(second);
        SceneNodeAnchorLayer.SetNode(first, f.Node); SceneNodeAnchorLayer.SetNode(second, f.Node);
        SceneNodeAnchorLayer.SetHideWhenOutsideViewport(first, false);
        SceneNodeAnchorLayer.SetHideWhenOutsideViewport(second, false);
        Lifecycle(layer, "SendLoaded");
        bool changed = false;
        first.PropertyChanged += (_, args) =>
        {
            if (changed || args.PropertyName != nameof(VisualElement.IsVisible) || !first.IsVisible) return;
            changed = true;
            f.View.Camera = OverlayFixture.CreateCamera();
        };
        f.View.PublishFrame();
        Check(changed && !first.IsVisible && !second.IsVisible,
            "A real child visibility callback changing camera cannot resurrect itself or a later child from the old layer batch.");
        f.View.PublishFrame();
        Check(first.IsVisible && second.IsVisible, "A subsequent successful frame resumes the retained batched children.");
        Lifecycle(layer, "SendUnloaded");
        Check(f.View.FrameSubscriberCount == 0, "Reentrant standalone layer releases its loaded subscription.");
    }

    private static void UnobservedContextTransitions()
    {
        using OverlayFixture f = new();
        f.View.PublishFrame();
        SceneNodeAnchorLayer layer = new() { SceneView = f.View };
        Label child = new(); layer.Children.Add(child);
        SceneNodeAnchorLayer.SetNode(child, f.Node);
        SceneNodeAnchorLayer.SetHideWhenOutsideViewport(child, false);
        Lifecycle(layer, "SendLoaded");
        Check(child.IsVisible, "A loaded layer accepts an existing successful same-context frame.");
        Lifecycle(layer, "SendUnloaded");
        f.View.Camera = OverlayFixture.CreateCamera();
        Lifecycle(layer, "SendLoaded");
        Check(!child.IsVisible, "Camera replacement during unload cannot revive a prior frame on reload.");
        using ViewportOverlayManager manager = new(f.View, f.Arbiter);
        ViewportToolContext context = new(f.View, f.Arbiter, manager);
        using IDisposable attachment = layer.Attach(context);
        Check(!child.IsVisible && f.View.FrameSubscriberCount == 1,
            "A newly constructed manager and same-view managed switch cannot reuse the replaced context's old frame.");
        attachment.Dispose();
        Check(!child.IsVisible && f.View.FrameSubscriberCount == 2,
            "Restoring standalone loading before a fresh frame preserves hidden placement and exactly one subscription.");
        f.View.PublishFrame();
        Check(child.IsVisible, "The first fresh post-transition frame resumes placement.");
        Lifecycle(layer, "SendUnloaded");
        manager.Dispose();
    }

    private static void Lifecycle(VisualElement element, string methodName)
    {
        // MAUI's own lifecycle sender is not public on the portable Controls target. Invoke that
        // bounded sender, rather than duplicate Loaded/Unloaded behavior or invoke adapter callbacks.
        MethodInfo method = typeof(VisualElement).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
            ?? throw new InvalidOperationException($"The real MAUI lifecycle sender {methodName} is unavailable.");
        method.Invoke(element, null);
    }

    private static void CheckLayerPlacement(VisualElement child, SceneNode node, ViewportFrameSnapshot frame)
    {
        Microsoft.Maui.Graphics.Rect bounds = AbsoluteLayout.GetLayoutBounds(child);
        CheckPlacement(bounds.X, bounds.Y, node, SceneNodeAnchorLayer.GetLocalPosition(child), frame,
            SceneNodeAnchorLayer.GetOffset(child));
    }

    private static void CheckPlacement(double x, double y, SceneNode node, Vector3 local,
        ViewportFrameSnapshot frame, MauiPoint offset)
    {
        Check(frame.TryProject(Vector3.Transform(local, node.WorldMatrix), out ViewportProjection projection) &&
            double.IsFinite(x) && double.IsFinite(y) &&
            Math.Abs(x - projection.LogicalPosition.X - offset.X) < .0001 &&
            Math.Abs(y - projection.LogicalPosition.Y - offset.Y) < .0001,
            "Real MAUI logical placement matches real Core snapshot projection plus the documented logical offset.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class OverlayFixture : IDisposable
    {
        internal Scene Scene { get; } = new("actual MAUI anchor fixture");
        internal SceneNode Parent { get; } = new("borrowed visible parent");
        internal SceneNode Node { get; } = new("borrowed anchored child");
        internal PerspectiveCamera Camera { get; } = CreateCamera();
        internal Mu3DSceneView View { get; }
        internal ViewportControlArbiter Arbiter { get; } = new();
        internal OverlayFixture()
        {
            Parent.AddChild(Node);
            Scene.Add(Parent);
            View = new Mu3DSceneView { Scene = Scene, Camera = Camera };
        }
        internal static PerspectiveCamera CreateCamera()
        {
            PerspectiveCamera camera = new(aspectRatio: 1);
            camera.Transform.Position = new(0, 0, 5);
            return camera;
        }
        internal Action MakeInvalid(string reason, Action<SceneNode?> setTarget)
        {
            switch (reason)
            {
                case "null": setTarget(null); return () => setTarget(Node);
                case "ancestor": Parent.IsVisible = false; return () => Parent.IsVisible = true;
                case "own": Node.IsVisible = false; return () => Node.IsVisible = true;
                case "mask": Node.VisibilityMask = SceneVisibilityMask.FromLayer(2); return () => Node.VisibilityMask = SceneVisibilityMask.Default;
                case "detached": Scene.Remove(Parent); return () => Scene.Add(Parent);
                case "foreign":
                    Scene.Remove(Parent); Scene foreign = new("foreign anchor membership"); foreign.Add(Parent);
                    return () => { foreign.Remove(Parent); Scene.Add(Parent); };
                case "root": setTarget(Scene.Root); return () => setTarget(Node);
                case "behind": Node.Transform.Position = new(0, 0, 10); return () => Node.Transform.Position = Vector3.Zero;
                default: View.PublishInvalidation(); return () => { };
            }
        }
        public void Dispose() => Arbiter.Dispose();
    }
}

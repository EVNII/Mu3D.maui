using System.Numerics;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Gizmos;

internal static class PointerBehaviorChecks
{
    internal static void Verify()
    {
        InvalidPresses();
        ValidLayerAncestry();
        foreach (string transition in new[] { "ancestor", "own", "detached", "foreign", "mask" })
        foreach (string boundary in new[] { "move", "release", "frame" })
            InvalidDuringDrag(transition, boundary);
        ContextChanges();
        ReentrantChanges();
        ReentrantInputBoundaries();
        ThrowingCancellation();
        Lifetime();
    }

    private static void InvalidPresses()
    {
        foreach (string reason in new[] { "ancestor", "own", "detached", "foreign", "mask", "null", "root", "scene", "camera", "size" })
        {
            using PointerFixture f = new();
            Vector2 point = f.PressPoint;
            _ = f.MakeInvalid(reason);
            int failures = 0;
            f.Behavior.InteractionFailed += (_, _) => failures++;
            Check(!f.Behavior.FixturePress(point) && !f.Behavior.IsPointerActive &&
                !f.Gizmo.IsInteracting && !f.Behavior.FixtureCaptured && f.Arbiter.CurrentLease is null && failures == 0,
                $"{reason}: an ordinary invalid press acquires no lease/capture and raises no interaction failure.");
            if (reason is not "null" and not "root")
                Check(ReferenceEquals(f.Gizmo.Target, f.Node), reason + ": rejected input retains the explicit target.");
        }
        using PointerFixture invalidCoordinates = new();
        Check(!invalidCoordinates.Behavior.FixturePress(new(-1, 200)) &&
            !invalidCoordinates.Behavior.FixturePress(new(401, 200)), "Out-of-viewport presses are ignored.");
    }

    private static void InvalidDuringDrag(string transition, string boundary)
    {
        using PointerFixture f = new();
        Vector3 initial = f.Node.Transform.Position;
        f.BeginChangedDrag();
        int releases = f.Behavior.FixtureReleaseCount;
        Action restore = f.MakeInvalid(transition);
        switch (boundary)
        {
            case "move": f.Behavior.ProcessPointerMoved(f.PressPoint + new Vector2(45, 0)); break;
            case "release": f.Behavior.ProcessPointerReleased(f.PressPoint + new Vector2(45, 0)); break;
            default: f.View.PublishFrame(); break;
        }
        Check(!f.Behavior.IsPointerActive && !f.Gizmo.IsInteracting && !f.Behavior.FixtureCaptured &&
            f.Arbiter.CurrentLease is null && f.Behavior.FixtureReleaseCount > releases &&
            Vector3.Distance(f.Node.Transform.Position, initial) < .0001f && ReferenceEquals(f.Gizmo.Target, f.Node),
            $"{transition}/{boundary}: invalidation restores the real edited pose and releases ownership/capture while retaining target.");
        int requestsAfterCancel = f.View.InvalidationCount;
        f.View.PublishFrame();
        f.View.PublishFrame();
        Check(f.View.InvalidationCount == requestsAfterCancel, "Idle invalid frames do not create a self-requesting render loop.");
        restore();
        Check(f.Behavior.FixturePress(f.PressPoint), $"{transition}/{boundary}: the retained target is usable after restoring eligibility.");
        f.Behavior.ProcessPointerCanceled();
        Check(f.Arbiter.CurrentLease is null && !f.Behavior.FixtureCaptured, "Reattached pointer cleanup releases the real arbiter.");
    }

    private static void ValidLayerAncestry()
    {
        using PointerFixture f = new();
        f.Parent.VisibilityMask = SceneVisibilityMask.FromLayer(2);
        f.Scene.Root.VisibilityMask = SceneVisibilityMask.None;
        f.Scene.Root.IsVisible = false;
        Check(f.Behavior.FixturePress(f.PressPoint),
            "Root container flags and ancestor layer mismatch preserve an independently layered valid pointer target.");
        f.Behavior.ProcessPointerCanceled();
        f.Node.VisibilityMask = SceneVisibilityMask.FromLayer(2);
        f.Parent.VisibilityMask = SceneVisibilityMask.Default;
        f.Camera.VisibilityMask = SceneVisibilityMask.FromLayer(2);
        Check(f.Behavior.FixturePress(f.PressPoint), "Only the target's own camera layer determines its layer eligibility.");
        f.Behavior.ProcessPointerCanceled();
    }

    private static void ContextChanges()
    {
        foreach (string change in new[] { "scene", "camera", "resize", "no-frame" })
        {
            using PointerFixture f = new();
            f.BeginChangedDrag();
            if (change == "scene") f.View.Scene = new Scene("replacement instance");
            else if (change == "camera") f.View.Camera = NewCamera();
            else if (change == "resize") { f.View.PixelWidth = 800; f.View.PublishFrame(); }
            else f.View.PublishInvalidation();
            Check(!f.Behavior.IsPointerActive && !f.Behavior.FixtureCaptured && !f.Gizmo.IsInteracting &&
                f.Arbiter.CurrentLease is null && f.Node.Transform.Position == Vector3.Zero,
                change + ": a mapping-context change cancels rather than committing under stale camera/extent/frame data.");
            Check(ReferenceEquals(f.Gizmo.Target, f.Node), "Context changes retain application selection.");
        }
    }

    private static void ReentrantChanges()
    {
        foreach (string change in new[] { "hidden", "scene", "camera" })
        {
            using PointerFixture f = new();
            f.Gizmo.InteractionStarted += (_, _) =>
            {
                if (change == "hidden") f.Parent.IsVisible = false;
                else if (change == "scene") f.View.Scene = new Scene("begin replacement");
                else f.View.Camera = NewCamera();
            };
            Check(!f.Behavior.FixturePress(f.PressPoint) && !f.Behavior.IsPointerActive &&
                !f.Behavior.FixtureCaptured && !f.Gizmo.IsInteracting && f.Arbiter.CurrentLease is null,
                change + ": InteractionStarted mutation is rechecked before native capture is accepted.");
        }
        foreach (bool release in new[] { false, true })
        {
            using PointerFixture f = new();
            Check(f.Behavior.FixturePress(f.PressPoint), "Reentrant changed fixture begins a real interaction.");
            f.Gizmo.InteractionChanged += (_, _) => f.Node.IsVisible = false;
            if (release) f.Behavior.ProcessPointerReleased(f.PressPoint + new Vector2(30, 0));
            else f.Behavior.ProcessPointerMoved(f.PressPoint + new Vector2(30, 0));
            Check(!f.Behavior.IsPointerActive && !f.Behavior.FixtureCaptured && !f.Gizmo.IsInteracting &&
                f.Arbiter.CurrentLease is null && f.Node.Transform.Position == Vector3.Zero,
                "InteractionChanged invalidation is rechecked after mapping and cannot complete a now-hidden target.");
        }
    }

    private static void ThrowingCancellation()
    {
        using PointerFixture f = new();
        f.BeginChangedDrag();
        int failed = 0;
        f.Behavior.InteractionFailed += (_, _) => failed++;
        f.Gizmo.InteractionCanceled += (_, _) => throw new InvalidOperationException("Owned cancellation fixture failure.");
        f.Parent.IsVisible = false;
        f.View.PublishFrame();
        Check(failed == 1 && !f.Behavior.IsPointerActive && !f.Gizmo.IsInteracting &&
            !f.Behavior.FixtureCaptured && f.Arbiter.CurrentLease is null && f.Node.Transform.Position == Vector3.Zero,
            "A throwing application cancellation callback still releases capture and lease after restoring pose.");
    }

    private static void ReentrantInputBoundaries()
    {
        foreach (bool duringRelease in new[] { false, true })
        {
            using PointerFixture f = new();
            SceneNode replacementNode = new("replacement borrowed selected node");
            f.Parent.AddChild(replacementNode);
            using TransformGizmo replacement = new(replacementNode) { Mode = TransformGizmoMode.Translate };
            bool nestedAccepted = true;
            EventHandler<TransformGizmoInteractionEventArgs> rebind = (_, _) =>
            {
                f.Behavior.Gizmo = replacement;
                nestedAccepted = f.Behavior.FixturePress(f.PressPoint);
            };
            if (duringRelease)
            {
                Check(f.Behavior.FixturePress(f.PressPoint), "The release reentrancy fixture starts a real edit.");
                f.Gizmo.InteractionChanged += rebind;
                Check(!f.Behavior.ProcessPointerReleased(f.PressPoint + new Vector2(20, 0)),
                    "Changing the Gizmo in Release's update cannot complete the previous context.");
            }
            else
            {
                f.Gizmo.InteractionStarted += rebind;
                Check(!f.Behavior.FixturePress(f.PressPoint), "Changing the Gizmo in Started rejects outer capture.");
            }
            Check(!nestedAccepted && !replacement.IsInteracting && !f.Gizmo.IsInteracting &&
                !f.Behavior.IsPointerActive && !f.Behavior.FixtureCaptured && f.Arbiter.CurrentLease is null &&
                f.Node.Transform.Position == Vector3.Zero,
                "A nested press cannot orphan either controller, old pose, lease or native capture.");
            Check(f.Behavior.FixturePress(f.PressPoint), "The replacement Gizmo can begin after the outer input call returns.");
            f.Behavior.ProcessPointerCanceled();
        }
        using (PointerFixture f = new())
        {
            bool nested = true;
            f.Gizmo.InteractionCompleted += (_, _) => nested = f.Behavior.FixturePress(f.PressPoint);
            Check(f.Behavior.FixturePress(f.PressPoint) &&
                f.Behavior.ProcessPointerReleased(f.PressPoint + new Vector2(20, 0)) && !nested &&
                !f.Behavior.IsPointerActive && f.Arbiter.CurrentLease is null,
                "Completed callbacks cannot begin another pointer until the old release returns.");
            f.Behavior.ProcessPointerCanceled();
        }
        using (PointerFixture f = new())
        {
            f.View.OnInvalidation = f.View.PublishInvalidation;
            Check(!f.Behavior.FixturePress(f.PressPoint) && !f.Behavior.FixtureCaptured &&
                !f.Behavior.IsPointerActive && f.Arbiter.CurrentLease is null && !f.Gizmo.IsInteracting,
                "Synchronous frame invalidation while beginning input cannot be captured after cancellation.");
            f.View.OnInvalidation = null;
        }
        using (PointerFixture f = new())
        {
            int failed = 0;
            f.Behavior.InteractionFailed += (_, _) => failed++;
            f.Gizmo.InteractionStarted += (_, _) => throw new InvalidOperationException("Owned begin callback fixture failure.");
            Check(!f.Behavior.FixturePress(f.PressPoint) && failed == 1 && !f.Gizmo.IsInteracting &&
                !f.Behavior.IsPointerActive && f.Arbiter.CurrentLease is null && !f.Behavior.FixtureCaptured,
                "A throwing Started callback cancels its attempted orphan interaction before reporting failure.");
        }
        using (PointerFixture f = new())
        {
            bool nested = true;
            f.Gizmo.InteractionStarted += (_, _) =>
            {
                f.Behavior.IsEnabled = false;
                f.Behavior.IsEnabled = true;
                nested = f.Behavior.FixturePress(f.PressPoint);
            };
            Check(!f.Behavior.FixturePress(f.PressPoint) && !nested && !f.Gizmo.IsInteracting &&
                !f.Behavior.IsPointerActive && f.Arbiter.CurrentLease is null && !f.Behavior.FixtureCaptured,
                "Disable/reenable in Started cannot reopen the input guard for an orphan nested press.");
        }
        using (PointerFixture f = new())
        {
            f.Gizmo.BeginInteraction(TransformGizmoAxis.X);
            int failed = 0;
            f.Behavior.InteractionFailed += (_, _) => failed++;
            Check(!f.Behavior.FixturePress(f.PressPoint) && f.Gizmo.IsInteracting && !f.Behavior.IsPointerActive,
                "An externally started interaction is rejected without being cancelled or adopted by the behavior.");
            Check(failed == 0, "An external edit is an ordinary input rejection, without an interaction-failure event.");
            f.Gizmo.CancelInteraction();
        }
    }

    private static void Lifetime()
    {
        using PointerFixture f = new();
        Check(f.View.FrameSubscriberCount == 1 && f.Behavior.FixtureAttachCount > 0, "The real behavior attaches its frame/input boundaries.");
        f.BeginChangedDrag();
        f.View.Behaviors.Remove(f.Behavior);
        Check(f.View.FrameSubscriberCount == 0 && !f.Behavior.FixtureCaptured && f.Arbiter.CurrentLease is null &&
            f.Node.Transform.Position == Vector3.Zero, "Real MAUI behavior detachment releases subscriptions and cancels pose/capture.");
        int requests = f.View.InvalidationCount, attached = f.Behavior.FixtureAttachCount;
        f.View.PublishFrame();
        Check(f.View.InvalidationCount == requests, "Detached behavior requests no frames in response to published frames.");
        f.View.Camera = NewCamera();
        Check(f.View.InvalidationCount == requests + 1 && f.Behavior.FixtureAttachCount == attached,
            "A detached behavior adds no request or attachment beyond the view's own context-change invalidation.");
        f.View.Behaviors.Add(f.Behavior);
        f.BeginChangedDrag();
        f.Behavior.Dispose();
        f.Behavior.Dispose();
        Check(f.View.FrameSubscriberCount == 0 && f.Arbiter.CurrentLease is null &&
            !f.Behavior.FixtureCaptured && !f.Gizmo.IsInteracting && f.Node.Transform.Position == Vector3.Zero,
            "Idempotent disposal releases subscriptions, native-capture seam and active interaction.");
        requests = f.View.InvalidationCount;
        f.View.PublishFrame();
        Check(f.View.InvalidationCount == requests, "Disposed behavior requests no frames from a published frame.");
        f.View.Scene = new Scene("post-disposal context");
        Check(f.View.InvalidationCount == requests + 1 && !f.Behavior.FixturePress(new(240, 200)),
            "Disposed behavior adds no request beyond the view's own context-change invalidation and rejects subsequent input.");
    }

    private static PerspectiveCamera NewCamera()
    {
        PerspectiveCamera camera = new(aspectRatio: 1);
        camera.Transform.Position = new(0, 0, 5);
        return camera;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class PointerFixture : IDisposable
    {
        internal Scene Scene { get; } = new("actual MAUI pointer fixture");
        internal SceneNode Parent { get; } = new("borrowed ancestor");
        internal SceneNode Node { get; } = new("borrowed selected node");
        internal PerspectiveCamera Camera { get; } = NewCamera();
        internal Mu3DSceneView View { get; }
        internal ViewportControlArbiter Arbiter { get; } = new();
        internal TransformGizmo Gizmo { get; }
        internal TransformGizmoPointerBehavior Behavior { get; }
        internal PointerFixture()
        {
            Parent.AddChild(Node);
            Scene.Add(Parent);
            View = new Mu3DSceneView { Scene = Scene, Camera = Camera };
            View.PublishFrame();
            Gizmo = new TransformGizmo(Node) { Mode = TransformGizmoMode.Translate };
            Behavior = new TransformGizmoPointerBehavior { Gizmo = Gizmo, ControlArbiter = Arbiter };
            View.Behaviors.Add(Behavior);
        }
        internal Vector2 PressPoint
        {
            get
            {
                PerspectiveCamera camera = (PerspectiveCamera)(View.Camera ?? Camera);
                Vector3 origin = Vector3.Transform(Vector3.Zero, Node.WorldMatrix);
                Vector3 handle = origin + Vector3.UnitX * Gizmo.CalculateWorldSize(camera, View.PixelHeight) * .55f;
                ViewportFrameSnapshot frame = new(1, camera.ViewMatrix, camera.ProjectionMatrix,
                    View.PixelWidth, View.PixelHeight, 200, 200);
                Check(frame.TryProject(handle, out ViewportProjection point), "The actual shared handle projects into the fixture viewport.");
                return point.PixelPosition;
            }
        }
        internal void BeginChangedDrag()
        {
            Vector2 press = PressPoint;
            Check(Behavior.FixturePress(press) && Behavior.FixtureCaptured && Behavior.IsPointerActive &&
                Arbiter.CurrentLease?.Priority == ViewportControlPriorities.Gizmo,
                "Actual hit testing accepts the projected X handle and owns the real Gizmo lease.");
            Check(Behavior.ProcessPointerMoved(press + new Vector2(25, 0)) && Node.Transform.Position.Length() > .01f,
                "Actual pointer state/controller changes the selected node before eligibility invalidation.");
        }
        internal Action MakeInvalid(string reason)
        {
            switch (reason)
            {
                case "ancestor": Parent.IsVisible = false; return () => Parent.IsVisible = true;
                case "own": Node.IsVisible = false; return () => Node.IsVisible = true;
                case "mask": Node.VisibilityMask = SceneVisibilityMask.FromLayer(2); return () => Node.VisibilityMask = SceneVisibilityMask.Default;
                case "detached": Scene.Remove(Parent); return () => Scene.Add(Parent);
                case "foreign":
                    Scene.Remove(Parent); Scene foreign = new("foreign membership"); foreign.Add(Parent);
                    return () => { foreign.Remove(Parent); Scene.Add(Parent); };
                case "null": Gizmo.Target = null; return () => Gizmo.Target = Node;
                case "root": Gizmo.Target = Scene.Root; return () => Gizmo.Target = Node;
                case "scene": View.Scene = null; return () => View.Scene = Scene;
                case "camera": View.Camera = null; return () => View.Camera = Camera;
                default: View.PixelWidth = 0; return () => View.PixelWidth = 400;
            }
        }
        public void Dispose()
        {
            Behavior.Dispose();
            View.Behaviors.Remove(Behavior);
            Gizmo.Dispose();
            Arbiter.Dispose();
        }
    }
}

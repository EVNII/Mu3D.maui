using System.Numerics;
using Mu3D.Graphics;
using Mu3D.Maui.Toolkit.Overlays;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;

// Only the GPU view/feature protocol is replaced. All bindable state, visual trees, behaviors,
// pointer recognizers, projection snapshots and Toolkit controllers are the real implementations.
namespace Mu3D.Maui.Controls
{
    /// <summary>A real MAUI Grid supplying the scene-view protocol without a GPU or native handler.</summary>
    public sealed class Mu3DSceneView : Grid
    {
        private EventHandler<ViewportFrameSnapshotChangedEventArgs>? frameChanged;
        private EventHandler<SurfaceFramePresentedEventArgs>? framePresented;
        private EventHandler<PresentationSessionChangedEventArgs>? sessionChanged;
        private ulong frameId;
        /// <summary>The scene assignment is a real MAUI bindable property.</summary>
        public static readonly BindableProperty SceneProperty = BindableProperty.Create(
            nameof(Scene), typeof(Scene), typeof(Mu3DSceneView), default(Scene),
            propertyChanged: static (bindable, _, _) => ((Mu3DSceneView)bindable).OnFrameSourceChanged());
        /// <summary>The camera assignment is a real MAUI bindable property.</summary>
        public static readonly BindableProperty CameraProperty = BindableProperty.Create(
            nameof(Camera), typeof(Camera), typeof(Mu3DSceneView), default(Camera),
            propertyChanged: static (bindable, _, _) => ((Mu3DSceneView)bindable).OnFrameSourceChanged());
        /// <summary>Gets or sets the borrowed Core scene.</summary>
        public Scene? Scene { get => (Scene?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
        /// <summary>Gets or sets the borrowed Core camera.</summary>
        public Camera? Camera { get => (Camera?)GetValue(CameraProperty); set => SetValue(CameraProperty, value); }
        /// <summary>Gets or sets the physical fixture extent.</summary>
        public uint PixelWidth { get; set; } = 400;
        /// <summary>Gets or sets the physical fixture extent.</summary>
        public uint PixelHeight { get; set; } = 400;
        /// <summary>Gets the latest explicitly published real Core frame snapshot.</summary>
        public ViewportFrameSnapshot? LatestFrameSnapshot { get; private set; }
        /// <summary>Provides the actual adapter's successful-frame/invalidation event boundary.</summary>
        public event EventHandler<ViewportFrameSnapshotChangedEventArgs>? FrameSnapshotChanged
        {
            add { frameChanged += value; FrameSubscriberCount++; }
            remove { frameChanged -= value; FrameSubscriberCount--; }
        }
        internal int FrameSubscriberCount { get; private set; }
        /// <summary>Supplies the presentation-frame subscription protocol without a GPU.</summary>
        public event EventHandler<SurfaceFramePresentedEventArgs>? FramePresented
        {
            add { framePresented += value; }
            remove { framePresented -= value; }
        }
        /// <summary>Supplies the presentation-session subscription protocol without a native surface.</summary>
        public event EventHandler<PresentationSessionChangedEventArgs>? PresentationSessionChanged
        {
            add { sessionChanged += value; }
            remove { sessionChanged -= value; }
        }
        /// <summary>No presentation session is created by this headless fixture.</summary>
        public IPresentationSurfaceSession? PresentationSession => null;
        /// <summary>No renderer is created by this headless fixture.</summary>
        public SceneRenderer? Renderer => null;
        internal int PresentationSubscriberCount => framePresented?.GetInvocationList().Length ?? 0;
        internal int SessionSubscriberCount => sessionChanged?.GetInvocationList().Length ?? 0;
        internal int InvalidationCount { get; private set; }
        internal Action? OnInvalidation { get; set; }
        /// <summary>Records coalescible requests without constructing a render loop.</summary>
        public void InvalidateScene()
        {
            InvalidationCount++;
            OnInvalidation?.Invoke();
        }
        private void OnFrameSourceChanged()
        {
            PublishInvalidation();
            InvalidateScene();
        }
        internal void PublishFrame()
        {
            Camera camera = Camera ?? throw new InvalidOperationException("A fixture frame needs a camera.");
            LatestFrameSnapshot = new ViewportFrameSnapshot(++frameId, camera.ViewMatrix,
                camera.ProjectionMatrix, PixelWidth, PixelHeight, PixelWidth / 2d, PixelHeight / 2d);
            frameChanged?.Invoke(this, new ViewportFrameSnapshotChangedEventArgs(LatestFrameSnapshot));
        }
        internal void PublishInvalidation()
        {
            LatestFrameSnapshot = null;
            frameChanged?.Invoke(this, new ViewportFrameSnapshotChangedEventArgs(null));
        }
    }

    /// <summary>Only the successful-frame event protocol; its snapshot is the real Core value.</summary>
    public sealed class ViewportFrameSnapshotChangedEventArgs(ViewportFrameSnapshot? snapshot) : EventArgs
    {
        /// <summary>Gets the published snapshot or explicit invalidation.</summary>
        public ViewportFrameSnapshot? Snapshot { get; } = snapshot;
    }

    /// <summary>Only the frame-status event protocol consumed by the actual statistics behavior.</summary>
    public sealed class SurfaceFramePresentedEventArgs(PresentationSurfaceFrameStatus status) : EventArgs
    {
        /// <summary>Gets the explicitly supplied fixture status.</summary>
        public PresentationSurfaceFrameStatus Status { get; } = status;
    }

    /// <summary>Only the session-change event protocol consumed by the actual statistics behavior.</summary>
    public sealed class PresentationSessionChangedEventArgs : EventArgs;

    /// <summary>Only the declarative-to-Core facade needed by the real anchor's public contract.</summary>
    public sealed class SceneNode3D(SceneNode node) : BindableObject
    {
        /// <summary>Gets the borrowed node without duplicating scene or transform behavior.</summary>
        public SceneNode CoreNode { get; } = node;
    }

    /// <summary>Marks the declarative protocol referenced by linked adapter documentation.</summary>
    public sealed class Scene3D : BindableObject;
}

namespace Mu3D.Maui.Toolkit.Controls
{
    /// <summary>The unchanged feature attachment protocol consumed by the linked real adapters.</summary>
    public interface IViewportTool
    {
        /// <summary>Attaches a tool and returns its actual owned lease.</summary>
        IDisposable Attach(ViewportToolContext context);
    }

    /// <summary>Only the narrow context protocol; its arbiter and manager are real implementations.</summary>
    public sealed class ViewportToolContext
    {
        internal ViewportToolContext(Mu3D.Maui.Controls.Mu3DSceneView view,
            ViewportControlArbiter arbiter, ViewportOverlayManager manager)
        {
            View = view;
            ControlArbiter = arbiter;
            OverlayManager = manager;
        }
        /// <summary>Gets the real-Grid fixture view borrowed by actual adapters.</summary>
        public Mu3D.Maui.Controls.Mu3DSceneView View { get; }
        /// <summary>Gets the current borrowed Core scene.</summary>
        public Scene? Scene => View.Scene;
        /// <summary>Gets the current borrowed Core camera.</summary>
        public Camera? Camera => View.Camera;
        /// <summary>Gets the real attachment-scoped Toolkit arbiter.</summary>
        public ViewportControlArbiter ControlArbiter { get; }
        internal ViewportOverlayManager OverlayManager { get; }
    }

    /// <summary>Marks the host composition protocol referenced by linked adapter documentation.</summary>
    public sealed class ViewportTools : BindableObject;

    // This partial is a platform-input test seam only. It supplies no hit/projection/drag logic.
    public sealed partial class TransformGizmoPointerBehavior
    {
        internal int FixtureAttachCount { get; private set; }
        internal int FixtureDetachCount { get; private set; }
        internal int FixtureReleaseCount { get; private set; }
        internal bool FixtureCaptured { get; private set; }
        internal bool FixturePress(Vector2 position)
        {
            bool accepted = ProcessPointerPressed(position);
            if (accepted) FixtureCaptured = true;
            return accepted;
        }
        private partial void AttachPlatformInput() => FixtureAttachCount++;
        private partial void DetachPlatformInput() => FixtureDetachCount++;
        private partial void ReleasePlatformPointerCapture()
        {
            FixtureReleaseCount++;
            FixtureCaptured = false;
        }
        private static partial bool GetPlatformPointerInputAvailable() => true;
    }
}

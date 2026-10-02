using System.Collections.ObjectModel;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Overlays;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Defines one explicitly declared tool hosted by <see cref="ViewportTools"/>.</summary>
/// <remarks>
/// Implementations are statically referenced from XAML or application code. Mu3D does not scan
/// assemblies or discover tools through reflection.
/// </remarks>
public interface IViewportTool
{
    /// <summary>Attaches the tool and returns the lease that reverses its owned work.</summary>
    /// <param name="context">The active viewport-tool context.</param>
    /// <returns>A non-null idempotent attachment lease.</returns>
    IDisposable Attach(ViewportToolContext context);
}

/// <summary>Exposes the narrow scene-view and shared-control state available to one viewport tool.</summary>
public sealed class ViewportToolContext
{
    private readonly SceneViewFeatureContext sceneViewContext;
    private bool active = true;

    internal ViewportToolContext(
        SceneViewFeatureContext sceneViewContext,
        ViewportControlArbiter controlArbiter,
        ViewportOverlayManager overlayManager)
    {
        this.sceneViewContext = sceneViewContext;
        ControlArbiter = controlArbiter;
        OverlayManager = overlayManager;
    }

    /// <summary>Gets the borrowed scene view.</summary>
    public Mu3DSceneView View
    {
        get
        {
            EnsureActive();
            return sceneViewContext.View;
        }
    }

    /// <summary>Gets the current scene, if assigned.</summary>
    public Scene? Scene
    {
        get
        {
            EnsureActive();
            return sceneViewContext.Scene;
        }
    }

    /// <summary>Gets the current camera, if assigned.</summary>
    public Camera? Camera
    {
        get
        {
            EnsureActive();
            return sceneViewContext.Camera;
        }
    }

    /// <summary>Gets the current renderer, if a renderable frame created it.</summary>
    public SceneRenderer? Renderer
    {
        get
        {
            EnsureActive();
            return sceneViewContext.Renderer;
        }
    }

    /// <summary>Gets the current control-owned presentation session, if available.</summary>
    public IPresentationSurfaceSession? PresentationSession
    {
        get
        {
            EnsureActive();
            return sceneViewContext.PresentationSession;
        }
    }

    /// <summary>Gets the attachment-owned arbiter shared by sibling tools in the same container.</summary>
    /// <remarks>Tools borrow this object and must not dispose or retain it after detachment.</remarks>
    public ViewportControlArbiter ControlArbiter { get; }

    internal ViewportOverlayManager OverlayManager { get; }

    /// <summary>Requests one coalesced scene frame.</summary>
    public void InvalidateScene()
    {
        EnsureActive();
        sceneViewContext.InvalidateScene();
    }

    /// <summary>Registers an owned pass factory in the scene view's default pipeline.</summary>
    public IDisposable RegisterRenderPass(
        SceneViewRenderPassFactory factory,
        SceneViewRenderPassPlacement placement = SceneViewRenderPassPlacement.AfterScene,
        int order = 0)
    {
        EnsureActive();
        return sceneViewContext.RegisterRenderPass(factory, placement, order);
    }

    internal SceneViewFeatureContext SceneViewContext
    {
        get
        {
            EnsureActive();
            return sceneViewContext;
        }
    }

    internal void Detach() => active = false;

    private void EnsureActive() => ObjectDisposedException.ThrowIf(!active, this);
}

/// <summary>
/// Composes explicitly declared viewport tools under one SceneView Feature and shared arbiter.
/// </summary>
/// <remarks>
/// Tools attach in collection order and detach in reverse. The container owns only the arbiter and
/// returned leases for the current handler attachment; it never owns scenes, cameras, targets, or
/// application selection and undo state.
/// </remarks>
[ContentProperty(nameof(Items))]
public sealed class ViewportTools : BindableObject, ISceneViewFeature
{
    private readonly ToolCollection items;
    private readonly List<AttachedTool> attachedTools = [];
    private SceneViewFeatureContext? attachedContext;
    private ViewportControlArbiter? controlArbiter;
    private ViewportOverlayManager? overlayManager;

    /// <summary>Initializes an empty explicit tool collection.</summary>
    public ViewportTools() => items = new ToolCollection(this);

    /// <summary>Gets the ordered tools populated by nested XAML elements.</summary>
    public IList<IViewportTool> Items => items;

    /// <summary>Gets the current attachment-owned shared arbiter, or null while detached.</summary>
    /// <remarks>Application code may observe this object but must not dispose or retain it.</remarks>
    public ViewportControlArbiter? ControlArbiter => controlArbiter;

    /// <inheritdoc />
    public IDisposable Attach(SceneViewFeatureContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachedContext is not null)
        {
            throw new InvalidOperationException("ViewportTools can attach to only one scene view at a time.");
        }

        attachedContext = context;
        try
        {
            AttachTools();
        }
        catch
        {
            attachedContext = null;
            throw;
        }
        return new Attachment(this);
    }

    private void RefreshTools()
    {
        if (attachedContext is null)
        {
            return;
        }

        DetachTools();
        AttachTools();
        attachedContext.InvalidateScene();
    }

    private void AttachTools()
    {
        SceneViewFeatureContext context = attachedContext ??
            throw new InvalidOperationException("ViewportTools is not attached.");
        controlArbiter = new ViewportControlArbiter();
        try
        {
            overlayManager = new ViewportOverlayManager(context.View, controlArbiter);
            foreach (IViewportTool tool in items)
            {
                ViewportToolContext toolContext = new(context, controlArbiter, overlayManager);
                IDisposable lease;
                try
                {
                    lease = tool.Attach(toolContext) ??
                        throw new InvalidOperationException("A viewport tool returned a null attachment lease.");
                }
                catch
                {
                    toolContext.Detach();
                    throw;
                }
                attachedTools.Add(new AttachedTool(toolContext, lease));
            }
        }
        catch
        {
            DetachTools();
            throw;
        }
    }

    private void Detach()
    {
        if (attachedContext is null)
        {
            return;
        }

        attachedContext = null;
        DetachTools();
    }

    private void DetachTools()
    {
        List<Exception>? failures = null;
        for (int index = attachedTools.Count - 1; index >= 0; index--)
        {
            AttachedTool attached = attachedTools[index];
            try
            {
                attached.Lease.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
            finally
            {
                attached.Context.Detach();
            }
        }
        attachedTools.Clear();
        try
        {
            overlayManager?.Dispose();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
        overlayManager = null;
        try
        {
            controlArbiter?.Dispose();
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
        controlArbiter = null;

        if (failures is not null)
        {
            throw new AggregateException("One or more viewport tools failed to detach.", failures);
        }
    }

    private sealed class ToolCollection(ViewportTools owner) : Collection<IViewportTool>
    {
        protected override void InsertItem(int index, IViewportTool item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (this.Any(candidate => ReferenceEquals(candidate, item)))
            {
                throw new InvalidOperationException("A viewport tool cannot appear twice in one container.");
            }
            base.InsertItem(index, item);
            owner.RefreshTools();
        }

        protected override void SetItem(int index, IViewportTool item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (ReferenceEquals(this[index], item))
            {
                return;
            }
            if (this.Any(candidate => ReferenceEquals(candidate, item)))
            {
                throw new InvalidOperationException("A viewport tool cannot appear twice in one container.");
            }
            base.SetItem(index, item);
            owner.RefreshTools();
        }

        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);
            owner.RefreshTools();
        }

        protected override void ClearItems()
        {
            if (Count == 0)
            {
                return;
            }
            base.ClearItems();
            owner.RefreshTools();
        }
    }

    private sealed record AttachedTool(ViewportToolContext Context, IDisposable Lease);

    private sealed class Attachment(ViewportTools owner) : IDisposable
    {
        private ViewportTools? owner = owner;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Detach();
    }
}

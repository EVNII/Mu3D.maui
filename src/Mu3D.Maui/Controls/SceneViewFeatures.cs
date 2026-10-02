using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.Maui.Controls;

/// <summary>Defines one statically referenced feature attached to a <see cref="Mu3DSceneView"/>.</summary>
/// <remarks>
/// Implementations receive only an explicit viewport context and return a deterministic attachment
/// lease. They are discovered through the view's <see cref="Mu3DSceneView.Features"/> collection,
/// constructors, or XAML references; Mu3D performs no assembly scanning or reflection discovery.
/// The feature object remains application-owned and may be attached again after handler recreation.
/// </remarks>
public interface ISceneViewFeature
{
    /// <summary>Attaches the feature and returns the lease that reverses its owned work.</summary>
    /// <param name="context">The active scene-view context.</param>
    /// <returns>A non-null attachment lease.</returns>
    IDisposable Attach(SceneViewFeatureContext context);
}

/// <summary>Controls where a feature pass is inserted relative to the default scene pass.</summary>
public enum SceneViewRenderPassPlacement
{
    /// <summary>Executes before the default scene output pass.</summary>
    BeforeScene,

    /// <summary>Executes after the default scene output pass.</summary>
    AfterScene,
}

/// <summary>Creates one caller-owned pass for the current control-owned renderer.</summary>
/// <param name="renderer">The borrowed renderer for the current presentation session.</param>
/// <returns>
/// A new pass instance. The scene view disposes it when it implements <see cref="IDisposable"/>.
/// </returns>
public delegate IRenderPass SceneViewRenderPassFactory(SceneRenderer renderer);

/// <summary>Provides the narrow active context shared by statically composed scene-view features.</summary>
/// <remarks>
/// The context borrows the view, scene, camera, renderer and presentation session. A feature owns
/// only registrations and resources it creates. Every render-pass registration is removed when the
/// context detaches even if the feature's returned lease omits that token.
/// </remarks>
public sealed class SceneViewFeatureContext
{
    private readonly Mu3DSceneView view;
    private readonly ISceneViewFeature feature;
    private readonly List<IDisposable> registrations = [];
    private bool active = true;

    internal SceneViewFeatureContext(Mu3DSceneView view, ISceneViewFeature feature)
    {
        this.view = view;
        this.feature = feature;
    }

    /// <summary>Gets the feature that owns this attachment context.</summary>
    public ISceneViewFeature Feature
    {
        get
        {
            EnsureActive();
            return feature;
        }
    }

    /// <summary>Gets the borrowed scene view.</summary>
    public Mu3DSceneView View
    {
        get
        {
            EnsureActive();
            return view;
        }
    }

    /// <summary>Gets the current application-owned scene, if assigned.</summary>
    public Scene? Scene
    {
        get
        {
            EnsureActive();
            return view.Scene;
        }
    }

    /// <summary>Gets the current application-owned camera, if assigned.</summary>
    public Camera? Camera
    {
        get
        {
            EnsureActive();
            return view.Camera;
        }
    }

    /// <summary>Gets the current control-owned renderer, if a renderable frame created it.</summary>
    public SceneRenderer? Renderer
    {
        get
        {
            EnsureActive();
            return view.Renderer;
        }
    }

    /// <summary>Gets the current control-owned presentation session, if available.</summary>
    public IPresentationSurfaceSession? PresentationSession
    {
        get
        {
            EnsureActive();
            return view.PresentationSession;
        }
    }

    /// <summary>Requests one coalesced scene frame.</summary>
    public void InvalidateScene()
    {
        EnsureActive();
        view.InvalidateScene();
    }

    /// <summary>Registers an owned pass factory in the view's default pipeline.</summary>
    /// <param name="factory">A factory that returns a new pass for each renderer generation.</param>
    /// <param name="placement">Whether the pass runs before or after the default scene pass.</param>
    /// <param name="order">The stable order within the selected placement; lower values run first.</param>
    /// <returns>An idempotent token that removes the registration.</returns>
    /// <remarks>
    /// Registration order breaks equal-order ties. Feature passes are ignored while the application
    /// assigns <see cref="Mu3DSceneView.RenderPipeline"/>. The explicit application pipeline remains
    /// borrowed and is never mutated by features.
    /// </remarks>
    public IDisposable RegisterRenderPass(
        SceneViewRenderPassFactory factory,
        SceneViewRenderPassPlacement placement = SceneViewRenderPassPlacement.AfterScene,
        int order = 0)
    {
        EnsureActive();
        ArgumentNullException.ThrowIfNull(factory);
        if (!Enum.IsDefined(placement))
        {
            throw new ArgumentOutOfRangeException(nameof(placement));
        }

        IDisposable registration = view.RegisterFeatureRenderPass(
            feature,
            factory,
            placement,
            order);
        registrations.Add(registration);
        return registration;
    }

    internal void Detach()
    {
        if (!active)
        {
            return;
        }

        active = false;
        List<Exception>? failures = null;
        for (int index = registrations.Count - 1; index >= 0; index--)
        {
            try
            {
                registrations[index].Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }
        registrations.Clear();
        if (failures is not null)
        {
            throw new AggregateException("One or more scene-view feature registrations failed to detach.", failures);
        }
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(!active, this);
    }
}

/// <summary>Identifies the lifecycle operation that failed for a scene-view feature.</summary>
public enum SceneViewFeatureOperation
{
    /// <summary>The feature failed while attaching.</summary>
    Attach,

    /// <summary>The feature or one of its registrations failed while detaching.</summary>
    Detach,

    /// <summary>A registered render-pass factory failed while rebuilding the default pipeline.</summary>
    CreateRenderPass,
}

/// <summary>Reports a scene-view feature lifecycle failure.</summary>
/// <param name="Feature">The feature associated with the failure, if available.</param>
/// <param name="Operation">The lifecycle operation that failed.</param>
/// <param name="Exception">The reported exception.</param>
public sealed class SceneViewFeatureErrorEventArgs(
    ISceneViewFeature? Feature,
    SceneViewFeatureOperation Operation,
    Exception Exception) : EventArgs
{
    /// <summary>Gets the feature associated with the failure, if available.</summary>
    public ISceneViewFeature? Feature { get; } = Feature;

    /// <summary>Gets the lifecycle operation that failed.</summary>
    public SceneViewFeatureOperation Operation { get; } = Operation;

    /// <summary>Gets the reported exception.</summary>
    public Exception Exception { get; } = Exception;
}

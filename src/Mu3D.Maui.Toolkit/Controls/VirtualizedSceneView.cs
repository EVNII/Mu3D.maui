using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Viewports;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Describes the lifecycle state of a <see cref="VirtualizedSceneView"/>.</summary>
public enum SceneViewportState
{
    /// <summary>The item is not in the live virtualized set.</summary>
    Placeholder,
    /// <summary>The item is waiting for a bounded live viewport slot.</summary>
    WaitingForViewport,
    /// <summary>The item owns a viewport slot and is loading content or its first presented frame.</summary>
    Loading,
    /// <summary>The item has presented its loaded scene to an active viewport.</summary>
    Ready,
    /// <summary>Loading failed; call <see cref="VirtualizedSceneView.Refresh"/> to retry.</summary>
    Failed,

    /// <summary>The live viewport is released while loaded managed content remains warm.</summary>
    Suspended,

    /// <summary>The live viewport and control-owned managed content are both released.</summary>
    Unloaded,
}

/// <summary>
/// Supplies one independently owned scene/camera pair for a virtualized viewport activation.
/// </summary>
/// <remarks>
/// Return a new content object for each call. Implementations may use an application-owned bounded
/// asset cache to create lightweight instances without re-reading or re-decoding the source.
/// </remarks>
public interface ISceneViewportSource
{
    /// <summary>Loads one owned scene/camera pair for the current activation generation.</summary>
    ValueTask<SceneViewportContent> LoadAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Owns the scene/camera pair returned for one virtualized viewport activation and an optional
/// application-supplied lifetime token.
/// </summary>
public sealed class SceneViewportContent : IDisposable
{
    private IDisposable? lifetime;

    /// <summary>Initializes owned content for one viewport activation.</summary>
    /// <param name="scene">The scene to render.</param>
    /// <param name="camera">The camera to render it with.</param>
    /// <param name="lifetime">
    /// Optional owned token released after the item leaves the live set or a stale load finishes.
    /// </param>
    public SceneViewportContent(Scene scene, Camera camera, IDisposable? lifetime = null)
    {
        Scene = scene ?? throw new ArgumentNullException(nameof(scene));
        Camera = camera ?? throw new ArgumentNullException(nameof(camera));
        this.lifetime = lifetime;
    }

    /// <summary>Gets the application-created scene for this activation.</summary>
    public Scene Scene { get; }

    /// <summary>Gets the application-created camera for this activation.</summary>
    public Camera Camera { get; }

    /// <summary>Releases the optional application-supplied lifetime token exactly once.</summary>
    public void Dispose() => Interlocked.Exchange(ref lifetime, null)?.Dispose();
}

/// <summary>
/// Creates a <see cref="Mu3DSceneView"/> only while its MAUI item is loaded, requests
/// <see cref="SceneViewportActivity.Active"/> and a shared <see cref="SceneViewportBudget"/> grants
/// a live slot.
/// </summary>
/// <remarks>
/// The control borrows <see cref="Source"/> and <see cref="Budget"/>. It owns each
/// <see cref="SceneViewportContent"/> returned by the source. Suspension removes the internal
/// SceneView and releases its viewport lease while retaining the managed content for a fast return;
/// unloading also releases that content. Removing the SceneView from the visual tree leaves native
/// Handler/session teardown to normal MAUI lifecycle; the control never calls application quit or
/// manually disposes a view-owned surface. Request generations and cancellation prevent a recycled
/// cell from accepting stale content.
/// </remarks>
[ContentProperty(nameof(Placeholder))]
public sealed class VirtualizedSceneView : Grid
{
    private static readonly LinearRgba OpaqueBlack = new(
        0f,
        0f,
        0f,
        1f,
        StandardColorSpaces.LinearSrgb);
    private static readonly BindablePropertyKey StatePropertyKey = BindableProperty.CreateReadOnly(
        nameof(State),
        typeof(SceneViewportState),
        typeof(VirtualizedSceneView),
        SceneViewportState.Unloaded);
    private static readonly BindablePropertyKey ErrorPropertyKey = BindableProperty.CreateReadOnly(
        nameof(Error),
        typeof(Exception),
        typeof(VirtualizedSceneView),
        default(Exception));
    private CancellationTokenSource? activationCancellation;
    private SceneViewportBudgetLease? viewportLease;
    private SceneViewportContent? activeContent;
    private Mu3DSceneView? activeSceneView;
    private int activationGeneration;

    /// <summary>Identifies the <see cref="Activity"/> bindable property.</summary>
    public static readonly BindableProperty ActivityProperty = BindableProperty.Create(
        nameof(Activity),
        typeof(SceneViewportActivity),
        typeof(VirtualizedSceneView),
        SceneViewportActivity.Active,
        validateValue: static (_, value) =>
            value is SceneViewportActivity activity &&
            Enum.IsDefined(activity),
        propertyChanged: static (bindable, _, _) =>
            ((VirtualizedSceneView)bindable).ApplyRequestedLifecycle());

    /// <summary>Identifies the <see cref="Source"/> bindable property.</summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source),
        typeof(ISceneViewportSource),
        typeof(VirtualizedSceneView),
        default(ISceneViewportSource),
        propertyChanged: static (bindable, _, _) =>
            ((VirtualizedSceneView)bindable).RestartActivation());

    /// <summary>Identifies the <see cref="Budget"/> bindable property.</summary>
    public static readonly BindableProperty BudgetProperty = BindableProperty.Create(
        nameof(Budget),
        typeof(SceneViewportBudget),
        typeof(VirtualizedSceneView),
        default(SceneViewportBudget),
        propertyChanged: static (bindable, _, _) =>
            ((VirtualizedSceneView)bindable).RestartActivation());

    /// <summary>Identifies the <see cref="IsActivationEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsActivationEnabledProperty = BindableProperty.Create(
        nameof(IsActivationEnabled),
        typeof(bool),
        typeof(VirtualizedSceneView),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((VirtualizedSceneView)bindable).RestartActivation());

    /// <summary>Identifies the <see cref="Placeholder"/> bindable property.</summary>
    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder),
        typeof(View),
        typeof(VirtualizedSceneView),
        default(View),
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((VirtualizedSceneView)bindable).OnPlaceholderChanged(
                (View?)oldValue,
                (View?)newValue));

    /// <summary>Identifies the <see cref="ClearColor"/> bindable property.</summary>
    public static readonly BindableProperty ClearColorProperty = BindableProperty.Create(
        nameof(ClearColor),
        typeof(LinearRgba),
        typeof(VirtualizedSceneView),
        OpaqueBlack,
        validateValue: static (_, value) =>
            value is LinearRgba color && color.ColorSpace is not null,
        propertyChanged: static (bindable, _, value) =>
            ((VirtualizedSceneView)bindable).ApplyClearColor((LinearRgba)value));

    /// <summary>Identifies the read-only <see cref="State"/> bindable property.</summary>
    public static readonly BindableProperty StateProperty = StatePropertyKey.BindableProperty;

    /// <summary>Identifies the read-only <see cref="Error"/> bindable property.</summary>
    public static readonly BindableProperty ErrorProperty = ErrorPropertyKey.BindableProperty;

    /// <summary>Initializes an empty virtualized scene host.</summary>
    public VirtualizedSceneView()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Gets or sets the borrowed source used for each live activation.</summary>
    public ISceneViewportSource? Source
    {
        get => (ISceneViewportSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Gets or sets the borrowed application-owned live/load budget.</summary>
    public SceneViewportBudget? Budget
    {
        get => (SceneViewportBudget?)GetValue(BudgetProperty);
        set => SetValue(BudgetProperty, value);
    }

    /// <summary>
    /// Gets or sets the application-requested lifecycle level. Use an explicit visible/nearby/cold
    /// range policy instead of relying on MAUI cell <c>Loaded</c> state to imply screen visibility.
    /// </summary>
    public SceneViewportActivity Activity
    {
        get => (SceneViewportActivity)GetValue(ActivityProperty);
        set => SetValue(ActivityProperty, value);
    }

    /// <summary>
    /// Gets or sets whether this item may retain any activation state. Bind page visibility here;
    /// disabling cancels work and fully unloads immediately.
    /// </summary>
    public bool IsActivationEnabled
    {
        get => (bool)GetValue(IsActivationEnabledProperty);
        set => SetValue(IsActivationEnabledProperty, value);
    }

    /// <summary>Gets or sets optional UI displayed until scene content is ready.</summary>
    public View? Placeholder
    {
        get => (View?)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Gets or sets the explicitly tagged scene-linear active viewport clear color.</summary>
    public LinearRgba ClearColor
    {
        get => (LinearRgba)GetValue(ClearColorProperty);
        set => SetValue(ClearColorProperty, value);
    }

    /// <summary>Gets the current activation state.</summary>
    public SceneViewportState State => (SceneViewportState)GetValue(StateProperty);

    /// <summary>Gets the latest load error, or null outside <see cref="SceneViewportState.Failed"/>.</summary>
    public Exception? Error => (Exception?)GetValue(ErrorProperty);

    /// <summary>Gets the current live SceneView, or null while this item holds no viewport slot.</summary>
    public Mu3DSceneView? ActiveSceneView => activeSceneView;

    /// <summary>Occurs after <see cref="State"/> changes.</summary>
    public event EventHandler<SceneViewportStateChangedEventArgs>? StateChanged;

    /// <summary>Occurs when the active source fails to load.</summary>
    public event EventHandler<SceneViewportLoadFailedEventArgs>? LoadFailed;

    /// <summary>Forwards presentation errors raised by the current internal SceneView.</summary>
    public event EventHandler<SurfaceErrorEventArgs>? SurfaceError;

    /// <summary>Cancels, fully unloads and retries when active and loaded.</summary>
    public void Refresh()
    {
        UnloadContent();
        ApplyRequestedLifecycle();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        ApplyRequestedLifecycle();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (!IsActivationEnabled || Activity == SceneViewportActivity.Unloaded)
        {
            UnloadContent();
            return;
        }
        SuspendContent();
    }

    private void RestartActivation()
    {
        UnloadContent();
        ApplyRequestedLifecycle();
    }

    private void ApplyRequestedLifecycle()
    {
        if (!IsActivationEnabled || Activity == SceneViewportActivity.Unloaded)
        {
            UnloadContent();
            return;
        }
        if (!IsLoaded || Activity == SceneViewportActivity.Suspended)
        {
            SuspendContent();
            return;
        }
        if (activeSceneView is not null || activationCancellation is not null)
        {
            return;
        }

        ISceneViewportSource? source = Source;
        SceneViewportBudget? budget = Budget;
        if (source is null || budget is null)
        {
            UnloadContent();
            return;
        }

        CancellationTokenSource cancellation = new();
        activationCancellation = cancellation;
        int generation = activationGeneration;
        _ = ActivateAsync(source, budget, generation, cancellation.Token);
    }

    private async Task ActivateAsync(
        ISceneViewportSource source,
        SceneViewportBudget budget,
        int generation,
        CancellationToken cancellationToken)
    {
        SceneViewportBudgetLease? acquiredViewport = null;
        SceneViewportContent? loadedContent = null;
        try
        {
            if (activeContent is null)
            {
                ChangeState(SceneViewportState.Loading);
                using (await budget.AcquireLoadAsync(cancellationToken))
                {
                    loadedContent = await source.LoadAsync(cancellationToken);
                }
                if (loadedContent is null)
                {
                    throw new InvalidOperationException("A scene viewport source returned null content.");
                }
                if (!IsCurrent(generation, cancellationToken))
                {
                    return;
                }

                activeContent = loadedContent;
                loadedContent = null;
            }

            ChangeState(SceneViewportState.WaitingForViewport);
            acquiredViewport = await budget.AcquireViewportAsync(cancellationToken);
            if (!IsCurrent(generation, cancellationToken))
            {
                return;
            }
            viewportLease = acquiredViewport;
            acquiredViewport = null;
            CreateActiveSceneView(activeContent!);
            ChangeState(SceneViewportState.Loading);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrent(generation, cancellationToken))
            {
                ReleaseViewport();
                SetValue(ErrorPropertyKey, exception);
                ChangeState(SceneViewportState.Failed);
                LoadFailed?.Invoke(this, new SceneViewportLoadFailedEventArgs(exception));
            }
        }
        finally
        {
            if (generation == activationGeneration &&
                activationCancellation is not null &&
                activationCancellation.Token == cancellationToken)
            {
                activationCancellation.Dispose();
                activationCancellation = null;
            }
            loadedContent?.Dispose();
            acquiredViewport?.Dispose();
        }
    }

    private void CancelActivation()
    {
        activationGeneration++;
        activationCancellation?.Cancel();
        activationCancellation?.Dispose();
        activationCancellation = null;
    }

    private void SuspendContent()
    {
        CancelActivation();
        ReleaseViewport();
        SetValue(ErrorPropertyKey, null);
        ChangeState(activeContent is null
            ? SceneViewportState.Unloaded
            : SceneViewportState.Suspended);
    }

    private void UnloadContent()
    {
        CancelActivation();
        ReleaseViewport();
        activeContent?.Dispose();
        activeContent = null;
        SetValue(ErrorPropertyKey, null);
        ChangeState(SceneViewportState.Unloaded);
    }

    private bool IsCurrent(int generation, CancellationToken cancellationToken) =>
        generation == activationGeneration &&
        !cancellationToken.IsCancellationRequested &&
        IsLoaded &&
        IsActivationEnabled &&
        Activity == SceneViewportActivity.Active;

    private void CreateActiveSceneView(SceneViewportContent content)
    {
        Mu3DSceneView sceneView = new()
        {
            ClearColor = ClearColor,
            Scene = content.Scene,
            Camera = content.Camera,
        };
        sceneView.SurfaceError += OnActiveSurfaceError;
        sceneView.FramePresented += OnActiveFramePresented;
        activeSceneView = sceneView;
        Children.Insert(0, sceneView);
        sceneView.InvalidateScene();
        UpdatePlaceholderVisibility();
    }

    private void ReleaseViewport()
    {
        if (activeSceneView is Mu3DSceneView sceneView)
        {
            sceneView.Scene = null;
            sceneView.Camera = null;
            sceneView.SurfaceError -= OnActiveSurfaceError;
            sceneView.FramePresented -= OnActiveFramePresented;
            Children.Remove(sceneView);
            activeSceneView = null;
        }
        viewportLease?.Dispose();
        viewportLease = null;
        UpdatePlaceholderVisibility();
    }

    private void OnPlaceholderChanged(View? oldValue, View? newValue)
    {
        if (oldValue is not null)
        {
            Children.Remove(oldValue);
        }
        if (newValue is not null)
        {
            Children.Add(newValue);
        }
        UpdatePlaceholderVisibility();
    }

    private void UpdatePlaceholderVisibility()
    {
        if (Placeholder is View placeholder)
        {
            placeholder.IsVisible = State != SceneViewportState.Ready;
        }
    }

    private void ApplyClearColor(LinearRgba clearColor)
    {
        if (activeSceneView is not null)
        {
            activeSceneView.ClearColor = clearColor;
        }
    }

    private void ChangeState(SceneViewportState state)
    {
        SceneViewportState previous = State;
        if (previous == state)
        {
            UpdatePlaceholderVisibility();
            return;
        }
        SetValue(StatePropertyKey, state);
        UpdatePlaceholderVisibility();
        StateChanged?.Invoke(this, new SceneViewportStateChangedEventArgs(previous, state));
    }

    private void OnActiveSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        SurfaceError?.Invoke(this, e);
    }

    private void OnActiveFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        _ = sender;
        if (activeContent is not null &&
            State == SceneViewportState.Loading &&
            e.Status is PresentationSurfaceFrameStatus.PresentedOptimal or
                PresentationSurfaceFrameStatus.PresentedSuboptimal)
        {
            ChangeState(SceneViewportState.Ready);
        }
    }
}

/// <summary>Reports one <see cref="VirtualizedSceneView.State"/> transition.</summary>
public sealed class SceneViewportStateChangedEventArgs(
    SceneViewportState PreviousState,
    SceneViewportState State) : EventArgs
{
    /// <summary>Gets the prior state.</summary>
    public SceneViewportState PreviousState { get; } = PreviousState;

    /// <summary>Gets the current state.</summary>
    public SceneViewportState State { get; } = State;
}

/// <summary>Reports a virtualized scene source loading failure.</summary>
public sealed class SceneViewportLoadFailedEventArgs(Exception Exception) : EventArgs
{
    /// <summary>Gets the source exception.</summary>
    public Exception Exception { get; } = Exception ?? throw new ArgumentNullException(nameof(Exception));
}

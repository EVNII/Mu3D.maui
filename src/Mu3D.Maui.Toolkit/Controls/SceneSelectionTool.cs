using System.Numerics;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Selection;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Describes one declarative selection candidate produced by a scene raycast.</summary>
public sealed class SceneSelectionCandidate
{
    internal SceneSelectionCandidate(SceneNode3D node, SceneRaycastHit hit)
    {
        Node = node;
        Hit = hit;
    }

    /// <summary>Gets the declarative node proposed as the selectable owner.</summary>
    public SceneNode3D Node { get; }

    /// <summary>Gets the closest geometric hit that mapped to <see cref="Node"/>.</summary>
    public SceneRaycastHit Hit { get; }
}

/// <summary>Reports a scene selection request before the proposed node is applied.</summary>
/// <remarks>
/// Application code may replace <see cref="SelectedNode"/> or set <see cref="Cancel"/> to retain
/// ownership of selection filtering, ranking, multi-selection, and editor policy.
/// </remarks>
public sealed class SceneSelectionRequestedEventArgs : EventArgs
{
    internal SceneSelectionRequestedEventArgs(
        Vector2 viewportPositionPixels,
        IReadOnlyList<SceneSelectionCandidate> candidates,
        SceneNode3D? selectedNode)
    {
        ViewportPositionPixels = viewportPositionPixels;
        Candidates = candidates;
        SelectedNode = selectedNode;
    }

    /// <summary>Gets the top-left-origin physical-pixel tap position.</summary>
    public Vector2 ViewportPositionPixels { get; }

    /// <summary>Gets geometric candidates ordered by increasing camera distance.</summary>
    public IReadOnlyList<SceneSelectionCandidate> Candidates { get; }

    /// <summary>Gets or sets the node that will become selected when the request is accepted.</summary>
    public SceneNode3D? SelectedNode { get; set; }

    /// <summary>Gets or sets whether the selection request should be ignored.</summary>
    public bool Cancel { get; set; }
}

/// <summary>Reports a committed declarative scene selection change.</summary>
public sealed class SceneSelectionChangedEventArgs : EventArgs
{
    internal SceneSelectionChangedEventArgs(SceneNode3D? oldNode, SceneNode3D? newNode)
    {
        OldNode = oldNode;
        NewNode = newNode;
    }

    /// <summary>Gets the previously selected node.</summary>
    public SceneNode3D? OldNode { get; }

    /// <summary>Gets the newly selected node.</summary>
    public SceneNode3D? NewNode { get; }
}

/// <summary>Reports an input mapping or geometric hit-test failure.</summary>
/// <param name="Exception">The selection input or hit-test exception.</param>
public sealed class SceneSelectionFailedEventArgs(Exception Exception) : EventArgs
{
    /// <summary>Gets the selection input or hit-test exception.</summary>
    public Exception Exception { get; } = Exception;
}

/// <summary>
/// Converts a primary tap into an explicit, bindable declarative scene selection.
/// </summary>
/// <remarks>
/// The tool installs one MAUI tap recognizer only while attached. It raycasts visible base mesh
/// geometry, maps hit descendants to their declarative root owner, and proposes the closest owner.
/// Declaring this tool opts into that default; applications can replace or cancel every result in
/// <see cref="SelectionRequested"/>. The tool does not own a gizmo, camera controller, undo stack,
/// multi-selection model, or native renderer resource.
/// </remarks>
public sealed class SceneSelectionTool : BindableObject, IViewportTool
{
    private readonly SceneRaycaster raycaster = new();
    private ToolAttachment? attachment;

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(SceneSelectionTool),
        true);

    /// <summary>Identifies the <see cref="ClearSelectionOnMiss"/> bindable property.</summary>
    public static readonly BindableProperty ClearSelectionOnMissProperty = BindableProperty.Create(
        nameof(ClearSelectionOnMiss),
        typeof(bool),
        typeof(SceneSelectionTool),
        true);

    /// <summary>Identifies the <see cref="SelectionMask"/> bindable property.</summary>
    public static readonly BindableProperty SelectionMaskProperty = BindableProperty.Create(
        nameof(SelectionMask),
        typeof(uint),
        typeof(SceneSelectionTool),
        uint.MaxValue);

    /// <summary>Identifies the <see cref="SelectedNode"/> bindable property.</summary>
    public static readonly BindableProperty SelectedNodeProperty = BindableProperty.Create(
        nameof(SelectedNode),
        typeof(SceneNode3D),
        typeof(SceneSelectionTool),
        default(SceneNode3D),
        BindingMode.TwoWay,
        validateValue: static (_, value) => value is null or SceneNode3D,
        propertyChanged: static (bindable, oldValue, newValue) =>
            ((SceneSelectionTool)bindable).OnSelectedNodeChanged(
                (SceneNode3D?)oldValue,
                (SceneNode3D?)newValue));

    /// <summary>Occurs before a tap proposal changes <see cref="SelectedNode"/>.</summary>
    public event EventHandler<SceneSelectionRequestedEventArgs>? SelectionRequested;

    /// <summary>Occurs after <see cref="SelectedNode"/> changes from input, binding, or code.</summary>
    public event EventHandler<SceneSelectionChangedEventArgs>? SelectionChanged;

    /// <summary>Occurs when input mapping or CPU raycasting fails.</summary>
    public event EventHandler<SceneSelectionFailedEventArgs>? SelectionFailed;

    /// <summary>Gets or sets whether taps are processed while the tool is attached.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets or sets whether a tap without candidates proposes a null selection.</summary>
    public bool ClearSelectionOnMiss
    {
        get => (bool)GetValue(ClearSelectionOnMissProperty);
        set => SetValue(ClearSelectionOnMissProperty, value);
    }

    /// <summary>Gets or sets the selection-category bits accepted by this tool.</summary>
    /// <remarks>
    /// The default accepts every category. A declarative node is proposed only when this mask
    /// intersects the node's attached <see cref="SceneSelection.MaskProperty"/> value. Changing the
    /// mask does not implicitly clear an existing application selection.
    /// </remarks>
    public uint SelectionMask
    {
        get => (uint)GetValue(SelectionMaskProperty);
        set => SetValue(SelectionMaskProperty, value);
    }

    /// <summary>Gets or sets the selected declarative node.</summary>
    /// <remarks>
    /// Bind this property to <see cref="TransformGizmoTool.Target"/> for automatic gizmo retargeting.
    /// Null is valid and represents no selection.
    /// </remarks>
    public SceneNode3D? SelectedNode
    {
        get => (SceneNode3D?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("A SceneSelectionTool can attach only once at a time.");
        }

        ToolAttachment created = new(this, context);
        attachment = created;
        try
        {
            created.Attach();
        }
        catch
        {
            attachment = null;
            created.Dispose();
            throw;
        }
        return created;
    }

    private void ProcessTap(ViewportToolContext context, TappedEventArgs eventArgs)
    {
        if (!IsEnabled || context.ControlArbiter.CurrentLease is not null ||
            context.Scene is not { } scene ||
            context.Camera is not PerspectiveCamera camera ||
            context.View.SceneContent is not { } declarativeScene)
        {
            return;
        }

        try
        {
            if (!TryMapTapPosition(context.View, eventArgs, out Vector2 viewportPosition))
            {
                return;
            }
            IReadOnlyList<SceneRaycastHit> hits = raycaster.HitTest(
                scene,
                camera,
                context.View.PixelWidth,
                context.View.PixelHeight,
                viewportPosition);
            IReadOnlyList<SceneSelectionCandidate> candidates = MapCandidates(
                declarativeScene,
                hits,
                SelectionMask);
            SceneNode3D? proposed = candidates.Count != 0
                ? candidates[0].Node
                : ClearSelectionOnMiss ? null : SelectedNode;
            SceneSelectionRequestedEventArgs request = new(
                viewportPosition,
                candidates,
                proposed);
            SelectionRequested?.Invoke(this, request);
            if (!request.Cancel)
            {
                SelectedNode = request.SelectedNode;
            }
        }
        catch (Exception exception)
        {
            SelectionFailed?.Invoke(this, new SceneSelectionFailedEventArgs(exception));
        }
    }

    private void OnSelectedNodeChanged(SceneNode3D? oldNode, SceneNode3D? newNode)
    {
        SelectionChanged?.Invoke(this, new SceneSelectionChangedEventArgs(oldNode, newNode));
        attachment?.Context.InvalidateScene();
    }

    private static IReadOnlyList<SceneSelectionCandidate> MapCandidates(
        Scene3D scene,
        IReadOnlyList<SceneRaycastHit> hits,
        uint selectionMask)
    {
        List<SceneSelectionCandidate> candidates = [];
        HashSet<SceneNode3D> accepted = new(ReferenceEqualityComparer.Instance);
        foreach (SceneRaycastHit hit in hits)
        {
            SceneNode3D? node = ResolveDeclarativeOwner(scene, hit.Mesh);
            if (node is not null &&
                SceneSelection.GetIsSelectable(node) &&
                (SceneSelection.GetMask(node) & selectionMask) != 0u &&
                accepted.Add(node))
            {
                candidates.Add(new SceneSelectionCandidate(node, hit));
            }
        }
        return candidates.AsReadOnly();
    }

    private static SceneNode3D? ResolveDeclarativeOwner(Scene3D scene, SceneNode hitNode)
    {
        foreach (SceneNode3D root in scene.Children)
        {
            for (SceneNode? candidate = hitNode; candidate is not null; candidate = candidate.Parent)
            {
                if (ReferenceEquals(candidate, root.CoreNode))
                {
                    return root;
                }
            }
        }
        return null;
    }

    private static bool TryMapTapPosition(
        Mu3DSceneView view,
        TappedEventArgs eventArgs,
        out Vector2 viewportPosition)
    {
        viewportPosition = default;
        Point? logical = eventArgs.GetPosition(view);
        if (logical is not Point position ||
            !double.IsFinite(position.X) || !double.IsFinite(position.Y) ||
            !double.IsFinite(view.Width) || !double.IsFinite(view.Height) ||
            view.Width <= 0d || view.Height <= 0d ||
            view.PixelWidth == 0 || view.PixelHeight == 0 ||
            position.X < 0d || position.Y < 0d ||
            position.X > view.Width || position.Y > view.Height)
        {
            return false;
        }
        viewportPosition = new Vector2(
            (float)(position.X / view.Width * view.PixelWidth),
            (float)(position.Y / view.Height * view.PixelHeight));
        return float.IsFinite(viewportPosition.X) && float.IsFinite(viewportPosition.Y);
    }

    private sealed class ToolAttachment(
        SceneSelectionTool owner,
        ViewportToolContext context) : IDisposable
    {
        private readonly TapGestureRecognizer tapRecognizer = new()
        {
            NumberOfTapsRequired = 1,
        };
        private bool attached;
        private bool disposed;

        internal ViewportToolContext Context => context;

        internal void Attach()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (attached)
            {
                return;
            }
            tapRecognizer.Tapped += OnTapped;
            context.View.GestureRecognizers.Add(tapRecognizer);
            attached = true;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            if (attached)
            {
                tapRecognizer.Tapped -= OnTapped;
                context.View.GestureRecognizers.Remove(tapRecognizer);
                attached = false;
            }
            if (ReferenceEquals(owner.attachment, this))
            {
                owner.attachment = null;
            }
        }

        private void OnTapped(object? sender, TappedEventArgs eventArgs)
        {
            _ = sender;
            owner.ProcessTap(context, eventArgs);
        }
    }
}

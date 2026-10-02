using System.Numerics;
using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Rendering;
using CoreAxesHelper = Mu3D.Toolkit.Helpers.AxesHelper;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Reports a requested cardinal or two-axis 45-degree camera view.</summary>
public sealed class AxesViewRequestedEventArgs : EventArgs
{
    internal AxesViewRequestedEventArgs(AxesHelperHit hit)
    {
        Preset = hit.Preset;
        Axis = hit.Axis;
        CameraDirection = hit.CameraDirection;
    }

    /// <summary>Gets the deterministic view preset selected by the pointer.</summary>
    public AxesViewPreset Preset { get; }

    /// <summary>Gets the cardinal axis or diagonal classification.</summary>
    public AxesHelperAxis Axis { get; }

    /// <summary>Gets or sets the world direction from the orbit target toward the camera.</summary>
    /// <remarks>Applications may replace this value before the default Orbit action executes.</remarks>
    public Vector3 CameraDirection { get; set; }

    /// <summary>Gets or sets whether application code already applied its own camera policy.</summary>
    public bool Handled { get; set; }

    /// <summary>Gets or sets whether this view request should be ignored.</summary>
    public bool Cancel { get; set; }
}

/// <summary>Reports an axes-helper rendering, hit-test, or camera-policy failure.</summary>
/// <param name="Exception">The reported interaction or configuration exception.</param>
public sealed class AxesHelperFailedEventArgs(Exception Exception) : EventArgs
{
    /// <summary>Gets the reported interaction or configuration exception.</summary>
    public Exception Exception { get; } = Exception;
}

/// <summary>
/// Displays a fixed-pixel orientation axes overlay with optional cardinal and 45-degree navigation.
/// </summary>
/// <remarks>
/// Rendering and hit testing use UI-independent Toolkit contracts. This MAUI adapter installs one
/// tap recognizer only while attached and interactive. Assign <see cref="Orbit"/> for the default
/// camera action, or handle <see cref="ViewRequested"/> to supply an application camera policy.
/// It never selects a scene object, owns a camera, starts an animation, or retains a view after
/// detachment.
/// </remarks>
public sealed class AxesHelper : BindableObject, IViewportTool
{
    private readonly CoreAxesHelper helper = new();
    private readonly AxesHelperHitTester hitTester = new();
    private ToolAttachment? attachment;

    /// <summary>Identifies the <see cref="Target"/> bindable property.</summary>
    public static readonly BindableProperty TargetProperty = BindableProperty.Create(
        nameof(Target),
        typeof(SceneNode3D),
        typeof(AxesHelper),
        default(SceneNode3D),
        validateValue: static (_, value) => value is null or SceneNode3D,
        propertyChanged: static (bindable, _, value) =>
            ((AxesHelper)bindable).Apply(helper =>
                helper.OrientationTarget = ((SceneNode3D?)value)?.CoreNode));

    /// <summary>Identifies the <see cref="Orbit"/> bindable property.</summary>
    public static readonly BindableProperty OrbitProperty = BindableProperty.Create(
        nameof(Orbit),
        typeof(OrbitTool),
        typeof(AxesHelper),
        default(OrbitTool),
        validateValue: static (_, value) => value is null or OrbitTool);

    /// <summary>Identifies the <see cref="Space"/> bindable property.</summary>
    public static readonly BindableProperty SpaceProperty = BindableProperty.Create(
        nameof(Space),
        typeof(AxesHelperSpace),
        typeof(AxesHelper),
        AxesHelperSpace.World,
        validateValue: static (_, value) => value is AxesHelperSpace space && Enum.IsDefined(space),
        propertyChanged: static (bindable, _, value) =>
            ((AxesHelper)bindable).Apply(helper => helper.Space = (AxesHelperSpace)value));

    /// <summary>Identifies the <see cref="Placement"/> bindable property.</summary>
    public static readonly BindableProperty PlacementProperty = BindableProperty.Create(
        nameof(Placement),
        typeof(ViewportOverlayPlacement),
        typeof(AxesHelper),
        ViewportOverlayPlacement.TopRight,
        validateValue: static (_, value) =>
            value is ViewportOverlayPlacement placement && Enum.IsDefined(placement),
        propertyChanged: static (bindable, _, value) =>
            ((AxesHelper)bindable).Apply(helper => helper.Placement = (ViewportOverlayPlacement)value));

    /// <summary>Identifies the <see cref="ScreenSize"/> bindable property.</summary>
    public static readonly BindableProperty ScreenSizeProperty = BindableProperty.Create(
        nameof(ScreenSize),
        typeof(float),
        typeof(AxesHelper),
        112f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, value) =>
            ((AxesHelper)bindable).Apply(helper => helper.ScreenSizePixels = (float)value));

    /// <summary>Identifies the <see cref="Margin"/> bindable property.</summary>
    public static readonly BindableProperty MarginProperty = BindableProperty.Create(
        nameof(Margin),
        typeof(float),
        typeof(AxesHelper),
        14f,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number >= 0f,
        propertyChanged: static (bindable, _, value) =>
            ((AxesHelper)bindable).Apply(helper => helper.MarginPixels = (float)value));

    /// <summary>Identifies the <see cref="ShowNegativeAxes"/> bindable property.</summary>
    public static readonly BindableProperty ShowNegativeAxesProperty = CreateBooleanHelperProperty(
        nameof(ShowNegativeAxes),
        true,
        static (helper, value) => helper.ShowNegativeAxes = value);

    /// <summary>Identifies the <see cref="ShowDiagonalViews"/> bindable property.</summary>
    public static readonly BindableProperty ShowDiagonalViewsProperty = CreateBooleanHelperProperty(
        nameof(ShowDiagonalViews),
        true,
        static (helper, value) => helper.ShowDiagonalViews = value);

    /// <summary>Identifies the <see cref="IsVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible),
        typeof(bool),
        typeof(AxesHelper),
        true,
        propertyChanged: static (bindable, _, _) => ((AxesHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="IsInteractive"/> bindable property.</summary>
    public static readonly BindableProperty IsInteractiveProperty = BindableProperty.Create(
        nameof(IsInteractive),
        typeof(bool),
        typeof(AxesHelper),
        false,
        propertyChanged: static (bindable, _, _) => ((AxesHelper)bindable).RefreshInput());

    /// <summary>Identifies the <see cref="HitTolerance"/> bindable property.</summary>
    public static readonly BindableProperty HitToleranceProperty = BindableProperty.Create(
        nameof(HitTolerance),
        typeof(float),
        typeof(AxesHelper),
        12f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, value) =>
            ((AxesHelper)bindable).hitTester.HitTolerancePixels = (float)value);

    /// <summary>Identifies the <see cref="PassOrder"/> bindable property.</summary>
    public static readonly BindableProperty PassOrderProperty = BindableProperty.Create(
        nameof(PassOrder),
        typeof(int),
        typeof(AxesHelper),
        100,
        propertyChanged: static (bindable, _, _) => ((AxesHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="RenderStyle"/> bindable property.</summary>
    public static readonly BindableProperty RenderStyleProperty = BindableProperty.Create(
        nameof(RenderStyle),
        typeof(AxesHelperRenderStyle),
        typeof(AxesHelper),
        AxesHelperRenderStyle.Default,
        validateValue: static (_, value) => value is AxesHelperRenderStyle,
        propertyChanged: static (bindable, _, _) => ((AxesHelper)bindable).RefreshRenderPass());

    /// <summary>Occurs before the default Orbit camera action.</summary>
    public event EventHandler<AxesViewRequestedEventArgs>? ViewRequested;

    /// <summary>Occurs when rendering, hit testing, or the selected camera policy fails.</summary>
    public event EventHandler<AxesHelperFailedEventArgs>? InteractionFailed;

    /// <summary>Gets or sets the optional declarative node defining target-local orientation.</summary>
    public SceneNode3D? Target
    {
        get => (SceneNode3D?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Gets or sets the optional Orbit tool used by the default click action.</summary>
    public OrbitTool? Orbit
    {
        get => (OrbitTool?)GetValue(OrbitProperty);
        set => SetValue(OrbitProperty, value);
    }

    /// <summary>Gets or sets whether world or target-local axes are displayed.</summary>
    public AxesHelperSpace Space
    {
        get => (AxesHelperSpace)GetValue(SpaceProperty);
        set => SetValue(SpaceProperty, value);
    }

    /// <summary>Gets or sets the viewport alignment occupied by the overlay.</summary>
    public ViewportOverlayPlacement Placement
    {
        get => (ViewportOverlayPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>Gets or sets the positive helper diameter in physical pixels.</summary>
    public float ScreenSize
    {
        get => (float)GetValue(ScreenSizeProperty);
        set => SetValue(ScreenSizeProperty, value);
    }

    /// <summary>Gets or sets the non-negative corner margin in physical pixels.</summary>
    public float Margin
    {
        get => (float)GetValue(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>Gets or sets whether negative cardinal directions are displayed.</summary>
    public bool ShowNegativeAxes
    {
        get => (bool)GetValue(ShowNegativeAxesProperty);
        set => SetValue(ShowNegativeAxesProperty, value);
    }

    /// <summary>Gets or sets whether two-axis 45-degree handles are displayed.</summary>
    public bool ShowDiagonalViews
    {
        get => (bool)GetValue(ShowDiagonalViewsProperty);
        set => SetValue(ShowDiagonalViewsProperty, value);
    }

    /// <summary>Gets or sets whether the overlay is rendered.</summary>
    public bool IsVisible
    {
        get => (bool)GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>Gets or sets whether a tap can request a camera view.</summary>
    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <summary>Gets or sets the positive marker hit tolerance in physical pixels.</summary>
    public float HitTolerance
    {
        get => (float)GetValue(HitToleranceProperty);
        set => SetValue(HitToleranceProperty, value);
    }

    /// <summary>Gets or sets ordering among passes registered after the scene.</summary>
    public int PassOrder
    {
        get => (int)GetValue(PassOrderProperty);
        set => SetValue(PassOrderProperty, value);
    }

    /// <summary>Gets or sets the immutable HDR-linear overlay style.</summary>
    public AxesHelperRenderStyle RenderStyle
    {
        get => (AxesHelperRenderStyle)GetValue(RenderStyleProperty);
        set => SetValue(RenderStyleProperty, value);
    }

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("An AxesHelper can attach only once at a time.");
        }
        helper.OrientationTarget = Target?.CoreNode;
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

    private void Apply(Action<CoreAxesHelper> update)
    {
        update(helper);
        attachment?.Context.InvalidateScene();
    }

    private void RefreshRenderPass() => attachment?.RefreshRenderPass();

    private void RefreshInput() => attachment?.RefreshInput();

    private void ProcessTap(ViewportToolContext context, TappedEventArgs eventArgs)
    {
        if (!IsInteractive || context.ControlArbiter.CurrentLease is not null ||
            context.Camera is not PerspectiveCamera camera ||
            !TryMapTapPosition(context.View, eventArgs, out Vector2 viewportPosition))
        {
            return;
        }

        try
        {
            AxesHelperHit? hit = hitTester.HitTest(
                helper,
                camera,
                context.View.PixelWidth,
                context.View.PixelHeight,
                viewportPosition);
            if (hit is null)
            {
                return;
            }
            ViewportControlLease? lease = context.ControlArbiter.TryAcquire(
                "Axes helper view selection",
                ViewportControlPriorities.Gizmo);
            if (lease is null)
            {
                return;
            }
            ToolAttachment? currentAttachment = attachment;
            if (currentAttachment is null)
            {
                lease.Dispose();
                return;
            }
            currentAttachment.HoldLeaseThroughCurrentInput(lease);

            AxesViewRequestedEventArgs request = new(hit);
            ViewRequested?.Invoke(this, request);
            if (request.Cancel)
            {
                return;
            }
            if (!request.Handled)
            {
                OrbitController controller = Orbit?.Controller ??
                    throw new InvalidOperationException(
                        "Interactive AxesHelper requires an attached Orbit tool or a handled ViewRequested event.");
                _ = controller.SetViewDirection(request.CameraDirection);
            }
            context.InvalidateScene();
        }
        catch (Exception exception)
        {
            InteractionFailed?.Invoke(this, new AxesHelperFailedEventArgs(exception));
        }
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

    private static BindableProperty CreateBooleanHelperProperty(
        string name,
        bool defaultValue,
        Action<CoreAxesHelper, bool> update) => BindableProperty.Create(
            name,
            typeof(bool),
            typeof(AxesHelper),
            defaultValue,
            propertyChanged: (bindable, _, value) =>
                ((AxesHelper)bindable).Apply(helper => update(helper, (bool)value)));

    private static bool IsPositiveFinite(object value) =>
        value is float number && float.IsFinite(number) && number > 0f;

    private sealed class ToolAttachment(
        AxesHelper owner,
        ViewportToolContext context) : IDisposable
    {
        private readonly TapGestureRecognizer tapRecognizer = new()
        {
            NumberOfTapsRequired = 1,
        };
        private IDisposable? renderPassRegistration;
        private ViewportControlLease? pendingLease;
        private bool inputAttached;
        private bool disposed;

        internal ViewportToolContext Context => context;

        internal void Attach()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RefreshRenderPass();
            RefreshInput();
        }

        internal void RefreshRenderPass()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            renderPassRegistration?.Dispose();
            renderPassRegistration = null;
            if (!owner.IsVisible)
            {
                context.InvalidateScene();
                return;
            }
            AxesHelperRenderStyle style = owner.RenderStyle;
            renderPassRegistration = context.RegisterRenderPass(
                renderer =>
                {
                    if (renderer.WorkingColorSpace is not StandardRgbColorSpaceReference workingSpace)
                    {
                        throw new InvalidOperationException(
                            "Axes-helper rendering requires a standard linear RGB working space.");
                    }
                    return new AxesHelperRenderPass(
                        owner.helper,
                        style,
                        workingSpace,
                        "SceneView axes helper");
                },
                SceneViewRenderPassPlacement.AfterScene,
                owner.PassOrder);
            context.InvalidateScene();
        }

        internal void RefreshInput()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (owner.IsInteractive && !inputAttached)
            {
                tapRecognizer.Tapped += OnTapped;
                context.View.GestureRecognizers.Add(tapRecognizer);
                inputAttached = true;
            }
            else if (!owner.IsInteractive && inputAttached)
            {
                tapRecognizer.Tapped -= OnTapped;
                context.View.GestureRecognizers.Remove(tapRecognizer);
                inputAttached = false;
                ReleasePendingLease();
            }
        }

        internal void HoldLeaseThroughCurrentInput(ViewportControlLease lease)
        {
            ReleasePendingLease();
            pendingLease = lease;
            if (!context.View.Dispatcher.Dispatch(() =>
                {
                    if (ReferenceEquals(pendingLease, lease))
                    {
                        pendingLease = null;
                        lease.Dispose();
                    }
                }))
            {
                ReleasePendingLease();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            renderPassRegistration?.Dispose();
            renderPassRegistration = null;
            if (inputAttached)
            {
                tapRecognizer.Tapped -= OnTapped;
                context.View.GestureRecognizers.Remove(tapRecognizer);
                inputAttached = false;
            }
            ReleasePendingLease();
            if (ReferenceEquals(owner.attachment, this))
            {
                owner.attachment = null;
            }
        }

        private void ReleasePendingLease()
        {
            pendingLease?.Dispose();
            pendingLease = null;
        }

        private void OnTapped(object? sender, TappedEventArgs eventArgs)
        {
            _ = sender;
            owner.ProcessTap(context, eventArgs);
        }
    }
}

using System.Numerics;
using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.Toolkit.Helpers;
using Mu3D.Toolkit.Rendering;
using CoreGridHelper = Mu3D.Toolkit.Helpers.GridHelper;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Displays a finite, depth-tested scene-space reference grid.</summary>
/// <remarks>
/// This adapter exposes the UI-independent helper through bindable XAML properties. It owns only
/// its render-pass registration and graphics resources for the current attachment. The grid is not
/// a scene node and never participates in bounds, selection, physics, or export.
/// </remarks>
public sealed class GridHelper : BindableObject, IViewportTool
{
    private readonly CoreGridHelper helper = new();
    private ToolAttachment? attachment;

    /// <summary>Identifies the <see cref="Plane"/> bindable property.</summary>
    public static readonly BindableProperty PlaneProperty = BindableProperty.Create(
        nameof(Plane),
        typeof(GridHelperPlane),
        typeof(GridHelper),
        GridHelperPlane.XZ,
        validateValue: static (_, value) => value is GridHelperPlane plane && Enum.IsDefined(plane),
        propertyChanged: static (bindable, _, value) =>
            ((GridHelper)bindable).Apply(helper => helper.Plane = (GridHelperPlane)value));

    /// <summary>Identifies the <see cref="Size"/> bindable property.</summary>
    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size),
        typeof(float),
        typeof(GridHelper),
        10f,
        validateValue: static (_, value) => IsPositiveFinite(value),
        propertyChanged: static (bindable, _, value) =>
            ((GridHelper)bindable).Apply(helper => helper.Size = (float)value));

    /// <summary>Identifies the <see cref="Divisions"/> bindable property.</summary>
    public static readonly BindableProperty DivisionsProperty = BindableProperty.Create(
        nameof(Divisions),
        typeof(int),
        typeof(GridHelper),
        20,
        validateValue: static (_, value) =>
            value is int count and >= 1 and <= CoreGridHelper.MaximumDivisions,
        propertyChanged: static (bindable, _, value) =>
            ((GridHelper)bindable).Apply(helper => helper.Divisions = (int)value));

    /// <summary>Identifies the <see cref="MajorLineEvery"/> bindable property.</summary>
    public static readonly BindableProperty MajorLineEveryProperty = BindableProperty.Create(
        nameof(MajorLineEvery),
        typeof(int),
        typeof(GridHelper),
        5,
        validateValue: static (_, value) => value is int count && count >= 1,
        propertyChanged: static (bindable, _, value) =>
            ((GridHelper)bindable).Apply(helper => helper.MajorLineEvery = (int)value));

    /// <summary>Identifies the <see cref="ShowMinorLines"/> bindable property.</summary>
    public static readonly BindableProperty ShowMinorLinesProperty = CreateBooleanHelperProperty(
        nameof(ShowMinorLines),
        true,
        static (helper, value) => helper.ShowMinorLines = value);

    /// <summary>Identifies the <see cref="ShowMajorLines"/> bindable property.</summary>
    public static readonly BindableProperty ShowMajorLinesProperty = CreateBooleanHelperProperty(
        nameof(ShowMajorLines),
        true,
        static (helper, value) => helper.ShowMajorLines = value);

    /// <summary>Identifies the <see cref="ShowCenterAxes"/> bindable property.</summary>
    public static readonly BindableProperty ShowCenterAxesProperty = CreateBooleanHelperProperty(
        nameof(ShowCenterAxes),
        true,
        static (helper, value) => helper.ShowCenterAxes = value);

    /// <summary>Identifies the <see cref="X"/> bindable property.</summary>
    public static readonly BindableProperty XProperty = CreateOriginProperty(nameof(X));

    /// <summary>Identifies the <see cref="Y"/> bindable property.</summary>
    public static readonly BindableProperty YProperty = CreateOriginProperty(nameof(Y));

    /// <summary>Identifies the <see cref="Z"/> bindable property.</summary>
    public static readonly BindableProperty ZProperty = CreateOriginProperty(nameof(Z));

    /// <summary>Identifies the <see cref="IsVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible),
        typeof(bool),
        typeof(GridHelper),
        true,
        propertyChanged: static (bindable, _, _) => ((GridHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="PassOrder"/> bindable property.</summary>
    public static readonly BindableProperty PassOrderProperty = BindableProperty.Create(
        nameof(PassOrder),
        typeof(int),
        typeof(GridHelper),
        -100,
        propertyChanged: static (bindable, _, _) => ((GridHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="RenderStyle"/> bindable property.</summary>
    public static readonly BindableProperty RenderStyleProperty = BindableProperty.Create(
        nameof(RenderStyle),
        typeof(GridHelperRenderStyle),
        typeof(GridHelper),
        GridHelperRenderStyle.Default,
        validateValue: static (_, value) => value is GridHelperRenderStyle,
        propertyChanged: static (bindable, _, _) => ((GridHelper)bindable).RefreshRenderPass());

    /// <summary>Gets or sets the world plane spanned by the grid.</summary>
    public GridHelperPlane Plane
    {
        get => (GridHelperPlane)GetValue(PlaneProperty);
        set => SetValue(PlaneProperty, value);
    }

    /// <summary>Gets or sets the positive full grid width and height in world units.</summary>
    public float Size
    {
        get => (float)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Gets or sets the positive number of equal intervals along each axis.</summary>
    public int Divisions
    {
        get => (int)GetValue(DivisionsProperty);
        set => SetValue(DivisionsProperty, value);
    }

    /// <summary>Gets or sets the positive interval between emphasized grid lines.</summary>
    public int MajorLineEvery
    {
        get => (int)GetValue(MajorLineEveryProperty);
        set => SetValue(MajorLineEveryProperty, value);
    }

    /// <summary>Gets or sets whether non-major lines are displayed.</summary>
    public bool ShowMinorLines
    {
        get => (bool)GetValue(ShowMinorLinesProperty);
        set => SetValue(ShowMinorLinesProperty, value);
    }

    /// <summary>Gets or sets whether every <see cref="MajorLineEvery"/> line is emphasized.</summary>
    public bool ShowMajorLines
    {
        get => (bool)GetValue(ShowMajorLinesProperty);
        set => SetValue(ShowMajorLinesProperty, value);
    }

    /// <summary>Gets or sets whether colored world axes are displayed through the origin.</summary>
    public bool ShowCenterAxes
    {
        get => (bool)GetValue(ShowCenterAxesProperty);
        set => SetValue(ShowCenterAxesProperty, value);
    }

    /// <summary>Gets or sets the world-space grid-origin X coordinate.</summary>
    public float X
    {
        get => (float)GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    /// <summary>Gets or sets the world-space grid-origin Y coordinate.</summary>
    public float Y
    {
        get => (float)GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    /// <summary>Gets or sets the world-space grid-origin Z coordinate.</summary>
    public float Z
    {
        get => (float)GetValue(ZProperty);
        set => SetValue(ZProperty, value);
    }

    /// <summary>Gets or sets whether the grid is rendered.</summary>
    public bool IsVisible
    {
        get => (bool)GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>Gets or sets ordering among passes registered after the scene.</summary>
    public int PassOrder
    {
        get => (int)GetValue(PassOrderProperty);
        set => SetValue(PassOrderProperty, value);
    }

    /// <summary>Gets or sets the immutable HDR-linear grid style.</summary>
    public GridHelperRenderStyle RenderStyle
    {
        get => (GridHelperRenderStyle)GetValue(RenderStyleProperty);
        set => SetValue(RenderStyleProperty, value);
    }

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("A GridHelper can attach only once at a time.");
        }
        ApplyOrigin();
        ToolAttachment created = new(this, context);
        attachment = created;
        try
        {
            created.RefreshRenderPass();
        }
        catch
        {
            attachment = null;
            created.Dispose();
            throw;
        }
        return created;
    }

    private void Apply(Action<CoreGridHelper> update)
    {
        update(helper);
        attachment?.Context.InvalidateScene();
    }

    private void ApplyOrigin() => Apply(helper => helper.Origin = new Vector3(X, Y, Z));

    private void RefreshRenderPass() => attachment?.RefreshRenderPass();

    private static BindableProperty CreateBooleanHelperProperty(
        string name,
        bool defaultValue,
        Action<CoreGridHelper, bool> update) => BindableProperty.Create(
            name,
            typeof(bool),
            typeof(GridHelper),
            defaultValue,
            propertyChanged: (bindable, _, value) =>
                ((GridHelper)bindable).Apply(helper => update(helper, (bool)value)));

    private static BindableProperty CreateOriginProperty(string name) => BindableProperty.Create(
        name,
        typeof(float),
        typeof(GridHelper),
        0f,
        validateValue: static (_, value) => value is float number && float.IsFinite(number),
        propertyChanged: static (bindable, _, _) => ((GridHelper)bindable).ApplyOrigin());

    private static bool IsPositiveFinite(object value) =>
        value is float number && float.IsFinite(number) && number > 0f;

    private sealed class ToolAttachment(GridHelper owner, ViewportToolContext context) : IDisposable
    {
        private IDisposable? renderPassRegistration;
        private bool disposed;

        internal ViewportToolContext Context => context;

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
            GridHelperRenderStyle style = owner.RenderStyle;
            renderPassRegistration = context.RegisterRenderPass(
                renderer =>
                {
                    if (renderer.WorkingColorSpace is not StandardRgbColorSpaceReference workingSpace)
                    {
                        throw new InvalidOperationException(
                            "Grid-helper rendering requires a standard linear RGB working space.");
                    }
                    return new GridHelperRenderPass(
                        owner.helper,
                        style,
                        workingSpace,
                        "SceneView grid helper");
                },
                SceneViewRenderPassPlacement.AfterScene,
                owner.PassOrder);
            context.InvalidateScene();
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
            if (ReferenceEquals(owner.attachment, this))
            {
                owner.attachment = null;
            }
        }
    }
}

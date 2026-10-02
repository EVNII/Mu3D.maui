using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Rendering;
using MauiColor = Microsoft.Maui.Graphics.Color;
using CoreOutlineHelper = Mu3D.Toolkit.Helpers.OutlineHelper;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Displays a declarative scene node's current screen-space silhouette.</summary>
/// <remarks>
/// This adapter binds a UI-independent helper to one <see cref="SceneNode3D"/>. It owns only its
/// render-pass registration and graphics resources for the current attachment. The outline is not
/// scene geometry and never participates in bounds, selection, physics, or export.
/// </remarks>
public sealed class OutlineHelper : BindableObject, IViewportTool
{
    private readonly CoreOutlineHelper helper = new();
    private ToolAttachment? attachment;

    /// <summary>Identifies the <see cref="Target"/> bindable property.</summary>
    public static readonly BindableProperty TargetProperty = BindableProperty.Create(
        nameof(Target),
        typeof(SceneNode3D),
        typeof(OutlineHelper),
        default(SceneNode3D),
        validateValue: static (_, value) => value is null or SceneNode3D,
        propertyChanged: static (bindable, _, value) =>
            ((OutlineHelper)bindable).Apply(core => core.Target = ((SceneNode3D?)value)?.CoreNode));

    /// <summary>Identifies the <see cref="IncludeDescendants"/> bindable property.</summary>
    public static readonly BindableProperty IncludeDescendantsProperty = CreateBooleanHelperProperty(
        nameof(IncludeDescendants),
        true,
        static (helper, value) => helper.IncludeDescendants = value);

    /// <summary>Identifies the <see cref="IncludeInvisible"/> bindable property.</summary>
    public static readonly BindableProperty IncludeInvisibleProperty = CreateBooleanHelperProperty(
        nameof(IncludeInvisible),
        false,
        static (helper, value) => helper.IncludeInvisible = value);

    /// <summary>Identifies the <see cref="TriangleLimit"/> bindable property.</summary>
    public static readonly BindableProperty TriangleLimitProperty = BindableProperty.Create(
        nameof(TriangleLimit),
        typeof(int),
        typeof(OutlineHelper),
        CoreOutlineHelper.DefaultTriangleLimit,
        validateValue: static (_, value) =>
            value is int count && count is >= 1 and <= CoreOutlineHelper.MaximumTriangleLimit,
        propertyChanged: static (bindable, _, value) =>
            ((OutlineHelper)bindable).Apply(helper => helper.TriangleLimit = (int)value));

    /// <summary>Identifies the <see cref="Color"/> bindable property.</summary>
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color),
        typeof(MauiColor),
        typeof(OutlineHelper),
        MauiColor.FromArgb("#FF38E8FF"),
        validateValue: static (_, value) => value is MauiColor,
        propertyChanged: static (bindable, _, _) => ((OutlineHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="LineWidth"/> bindable property.</summary>
    public static readonly BindableProperty LineWidthProperty = BindableProperty.Create(
        nameof(LineWidth),
        typeof(float),
        typeof(OutlineHelper),
        3f,
        validateValue: static (_, value) =>
            value is float number &&
            float.IsFinite(number) &&
            number > 0f &&
            number <= OutlineHelperRenderStyle.MaximumLineWidthPixels,
        propertyChanged: static (bindable, _, _) => ((OutlineHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="DepthMode"/> bindable property.</summary>
    public static readonly BindableProperty DepthModeProperty = BindableProperty.Create(
        nameof(DepthMode),
        typeof(OutlineHelperDepthMode),
        typeof(OutlineHelper),
        OutlineHelperDepthMode.SceneDepthTested,
        validateValue: static (_, value) =>
            value is OutlineHelperDepthMode mode && Enum.IsDefined(mode),
        propertyChanged: static (bindable, _, _) => ((OutlineHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="IsVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible),
        typeof(bool),
        typeof(OutlineHelper),
        true,
        propertyChanged: static (bindable, _, _) => ((OutlineHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="PassOrder"/> bindable property.</summary>
    public static readonly BindableProperty PassOrderProperty = BindableProperty.Create(
        nameof(PassOrder),
        typeof(int),
        typeof(OutlineHelper),
        -40,
        propertyChanged: static (bindable, _, _) => ((OutlineHelper)bindable).RefreshRenderPass());

    /// <summary>Gets or sets the declarative node whose silhouette is displayed.</summary>
    public SceneNode3D? Target
    {
        get => (SceneNode3D?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Gets or sets whether target descendants contribute to the silhouette.</summary>
    public bool IncludeDescendants
    {
        get => (bool)GetValue(IncludeDescendantsProperty);
        set => SetValue(IncludeDescendantsProperty, value);
    }

    /// <summary>Gets or sets whether invisible target subtrees contribute to the silhouette.</summary>
    public bool IncludeInvisible
    {
        get => (bool)GetValue(IncludeInvisibleProperty);
        set => SetValue(IncludeInvisibleProperty, value);
    }

    /// <summary>Gets or sets the positive per-frame source-triangle safety limit.</summary>
    public int TriangleLimit
    {
        get => (int)GetValue(TriangleLimitProperty);
        set => SetValue(TriangleLimitProperty, value);
    }

    /// <summary>Gets or sets the sRGB outline color converted to linear light for rendering.</summary>
    public MauiColor Color
    {
        get => (MauiColor)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Gets or sets the outer-outline width in physical pixels.</summary>
    public float LineWidth
    {
        get => (float)GetValue(LineWidthProperty);
        set => SetValue(LineWidthProperty, value);
    }

    /// <summary>Gets or sets how the target silhouette interacts with scene depth.</summary>
    public OutlineHelperDepthMode DepthMode
    {
        get => (OutlineHelperDepthMode)GetValue(DepthModeProperty);
        set => SetValue(DepthModeProperty, value);
    }

    /// <summary>Gets or sets whether the outline is rendered.</summary>
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

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("An OutlineHelper can attach only once at a time.");
        }
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

    private void Apply(Action<CoreOutlineHelper> update)
    {
        update(helper);
        attachment?.Context.InvalidateScene();
    }

    private void RefreshRenderPass() => attachment?.RefreshRenderPass();

    private static BindableProperty CreateBooleanHelperProperty(
        string name,
        bool defaultValue,
        Action<CoreOutlineHelper, bool> update) => BindableProperty.Create(
            name,
            typeof(bool),
            typeof(OutlineHelper),
            defaultValue,
            propertyChanged: (bindable, _, value) =>
                ((OutlineHelper)bindable).Apply(helper => update(helper, (bool)value)));

    private OutlineHelperRenderStyle CreateRenderStyle() => new(
        ToLinearSrgb(Color),
        LineWidth,
        DepthMode);

    private static LinearRgba ToLinearSrgb(MauiColor color) => new(
        DecodeSrgb(color.Red),
        DecodeSrgb(color.Green),
        DecodeSrgb(color.Blue),
        color.Alpha,
        StandardColorSpaces.LinearSrgb);

    private static float DecodeSrgb(float encoded) => encoded <= 0.04045f
        ? encoded / 12.92f
        : MathF.Pow((encoded + 0.055f) / 1.055f, 2.4f);

    private sealed class ToolAttachment(OutlineHelper owner, ViewportToolContext context) : IDisposable
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
            OutlineHelperRenderStyle style = owner.CreateRenderStyle();
            renderPassRegistration = context.RegisterRenderPass(
                renderer =>
                {
                    if (renderer.WorkingColorSpace is not StandardRgbColorSpaceReference workingSpace)
                    {
                        throw new InvalidOperationException(
                            "Outline-helper rendering requires a standard linear RGB working space.");
                    }
                    return new OutlineHelperRenderPass(
                        owner.helper,
                        style,
                        workingSpace,
                        "SceneView outline helper");
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

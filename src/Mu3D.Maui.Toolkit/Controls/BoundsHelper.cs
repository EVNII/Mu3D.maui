using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Rendering;
using MauiColor = Microsoft.Maui.Graphics.Color;
using CoreBoundsHelper = Mu3D.Toolkit.Helpers.BoundsHelper;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Displays a declarative scene node's current world-axis-aligned bounds.</summary>
/// <remarks>
/// This adapter binds a UI-independent helper to one <see cref="SceneNode3D"/>. It owns only its
/// render-pass registration and graphics resources for the current attachment. The outline is not
/// a scene node and never participates in bounds, selection, physics, or export.
/// </remarks>
public sealed class BoundsHelper : BindableObject, IViewportTool
{
    private readonly CoreBoundsHelper helper = new();
    private ToolAttachment? attachment;

    /// <summary>Identifies the <see cref="Target"/> bindable property.</summary>
    public static readonly BindableProperty TargetProperty = BindableProperty.Create(
        nameof(Target),
        typeof(SceneNode3D),
        typeof(BoundsHelper),
        default(SceneNode3D),
        validateValue: static (_, value) => value is null or SceneNode3D,
        propertyChanged: static (bindable, _, value) =>
            ((BoundsHelper)bindable).Apply(core => core.Target = ((SceneNode3D?)value)?.CoreNode));

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

    /// <summary>Identifies the <see cref="Precise"/> bindable property.</summary>
    public static readonly BindableProperty PreciseProperty = CreateBooleanHelperProperty(
        nameof(Precise),
        true,
        static (helper, value) => helper.Precise = value);

    /// <summary>Identifies the <see cref="Padding"/> bindable property.</summary>
    public static readonly BindableProperty PaddingProperty = BindableProperty.Create(
        nameof(Padding),
        typeof(float),
        typeof(BoundsHelper),
        0f,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number >= 0f,
        propertyChanged: static (bindable, _, value) =>
            ((BoundsHelper)bindable).Apply(helper => helper.Padding = (float)value));

    /// <summary>Identifies the <see cref="Color"/> bindable property.</summary>
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color),
        typeof(MauiColor),
        typeof(BoundsHelper),
        MauiColor.FromArgb("#FFFF9D24"),
        validateValue: static (_, value) => value is MauiColor,
        propertyChanged: static (bindable, _, _) => ((BoundsHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="LineWidth"/> bindable property.</summary>
    public static readonly BindableProperty LineWidthProperty = BindableProperty.Create(
        nameof(LineWidth),
        typeof(float),
        typeof(BoundsHelper),
        2.4f,
        validateValue: static (_, value) =>
            value is float number && float.IsFinite(number) && number > 0f,
        propertyChanged: static (bindable, _, _) => ((BoundsHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="DepthMode"/> bindable property.</summary>
    public static readonly BindableProperty DepthModeProperty = BindableProperty.Create(
        nameof(DepthMode),
        typeof(BoundsHelperDepthMode),
        typeof(BoundsHelper),
        BoundsHelperDepthMode.SceneDepthTested,
        validateValue: static (_, value) =>
            value is BoundsHelperDepthMode mode && Enum.IsDefined(mode),
        propertyChanged: static (bindable, _, _) => ((BoundsHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="IsVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible),
        typeof(bool),
        typeof(BoundsHelper),
        true,
        propertyChanged: static (bindable, _, _) => ((BoundsHelper)bindable).RefreshRenderPass());

    /// <summary>Identifies the <see cref="PassOrder"/> bindable property.</summary>
    public static readonly BindableProperty PassOrderProperty = BindableProperty.Create(
        nameof(PassOrder),
        typeof(int),
        typeof(BoundsHelper),
        -50,
        propertyChanged: static (bindable, _, _) => ((BoundsHelper)bindable).RefreshRenderPass());

    /// <summary>Gets or sets the declarative node whose bound is displayed.</summary>
    public SceneNode3D? Target
    {
        get => (SceneNode3D?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    /// <summary>Gets or sets whether target descendants contribute to the bound.</summary>
    public bool IncludeDescendants
    {
        get => (bool)GetValue(IncludeDescendantsProperty);
        set => SetValue(IncludeDescendantsProperty, value);
    }

    /// <summary>Gets or sets whether invisible target subtrees contribute to the bound.</summary>
    public bool IncludeInvisible
    {
        get => (bool)GetValue(IncludeInvisibleProperty);
        set => SetValue(IncludeInvisibleProperty, value);
    }

    /// <summary>Gets or sets whether transformed vertices produce a tight world AABB.</summary>
    /// <remarks>
    /// Set false only when a conservative cached local-box transform is preferable for a very
    /// large mesh.
    /// </remarks>
    public bool Precise
    {
        get => (bool)GetValue(PreciseProperty);
        set => SetValue(PreciseProperty, value);
    }

    /// <summary>Gets or sets the non-negative world-space expansion on every side.</summary>
    public float Padding
    {
        get => (float)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>Gets or sets the sRGB line color converted to linear light for rendering.</summary>
    public MauiColor Color
    {
        get => (MauiColor)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>Gets or sets the positive line width in physical pixels.</summary>
    public float LineWidth
    {
        get => (float)GetValue(LineWidthProperty);
        set => SetValue(LineWidthProperty, value);
    }

    /// <summary>Gets or sets how the outline interacts with scene depth.</summary>
    public BoundsHelperDepthMode DepthMode
    {
        get => (BoundsHelperDepthMode)GetValue(DepthModeProperty);
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
            throw new InvalidOperationException("A BoundsHelper can attach only once at a time.");
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

    private void Apply(Action<CoreBoundsHelper> update)
    {
        update(helper);
        attachment?.Context.InvalidateScene();
    }

    private void RefreshRenderPass() => attachment?.RefreshRenderPass();

    private static BindableProperty CreateBooleanHelperProperty(
        string name,
        bool defaultValue,
        Action<CoreBoundsHelper, bool> update) => BindableProperty.Create(
            name,
            typeof(bool),
            typeof(BoundsHelper),
            defaultValue,
            propertyChanged: (bindable, _, value) =>
                ((BoundsHelper)bindable).Apply(helper => update(helper, (bool)value)));

    private BoundsHelperRenderStyle CreateRenderStyle() => new(
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

    private sealed class ToolAttachment(BoundsHelper owner, ViewportToolContext context) : IDisposable
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
            BoundsHelperRenderStyle style = owner.CreateRenderStyle();
            renderPassRegistration = context.RegisterRenderPass(
                renderer =>
                {
                    if (renderer.WorkingColorSpace is not StandardRgbColorSpaceReference workingSpace)
                    {
                        throw new InvalidOperationException(
                            "Bounds-helper rendering requires a standard linear RGB working space.");
                    }
                    return new BoundsHelperRenderPass(
                        owner.helper,
                        style,
                        workingSpace,
                        "SceneView bounds helper");
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

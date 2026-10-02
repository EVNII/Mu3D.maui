using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using Mu3D.Maui.Toolkit.Overlays;
using Mu3D.Rendering;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Helpers;
using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>
/// Displays a compact MAUI viewport toolbar and selects one stable render output for the default
/// <see cref="Mu3D.Maui.Controls.Mu3DSceneView"/> pipeline.
/// </summary>
/// <remarks>
/// The toolbar uses the shared per-viewport <see cref="ViewportOverlay"/> manager, so native layout,
/// scrolling and accessibility move with the viewport while HDR scene rendering remains GPU-native. Output
/// identity stays independent of scene visibility masks and render-pass ordering. Built-in values
/// use <see cref="RenderOutputIds"/>; extensions may add explicitly registered identifiers through
/// <see cref="Items"/>. Disabling or detaching restores the prior view output only while the tool
/// still owns the value, so a later application assignment is never overwritten.
/// </remarks>
[ContentProperty(nameof(Items))]
public class RenderOutputTool : BindableObject, IViewportTool
{
    private static readonly OutputChoice[] DefaultChoices =
    [
        new("Final", RenderOutputIds.Beauty, "Final composed scene-linear image"),
        new("AO", RenderOutputIds.AmbientOcclusion, "Ambient occlusion"),
        new("Direct", RenderOutputIds.DirectLighting, "Direct lighting"),
        new("IBL", RenderOutputIds.ImageBasedLighting, "Image-based lighting"),
        new("Refl", RenderOutputIds.Specular, "Specular reflection"),
        new("Normal", RenderOutputIds.SurfaceNormal, "Surface normal"),
        new("Depth", RenderOutputIds.ViewDepth, "View depth"),
        new("Base", RenderOutputIds.BaseColor, "Material base color"),
    ];

    private readonly OptionCollection items;
    private ToolAttachment? attachment;

    /// <summary>Initializes a render-output tool with the built-in output buttons enabled.</summary>
    public RenderOutputTool() => items = new OptionCollection(this);

    /// <summary>Identifies the <see cref="Output"/> bindable property.</summary>
    public static readonly BindableProperty OutputProperty = BindableProperty.Create(
        nameof(Output),
        typeof(RenderOutputId),
        typeof(RenderOutputTool),
        RenderOutputIds.Beauty,
        validateValue: static (_, value) =>
            value is RenderOutputId output && output.IsValid,
        propertyChanged: static (bindable, _, _) =>
            ((RenderOutputTool)bindable).attachment?.Apply());

    /// <summary>Identifies the <see cref="IsEnabled"/> bindable property.</summary>
    public static readonly BindableProperty IsEnabledProperty = BindableProperty.Create(
        nameof(IsEnabled),
        typeof(bool),
        typeof(RenderOutputTool),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((RenderOutputTool)bindable).attachment?.Apply());

    /// <summary>Identifies the <see cref="IsVisible"/> bindable property.</summary>
    public static readonly BindableProperty IsVisibleProperty = BindableProperty.Create(
        nameof(IsVisible),
        typeof(bool),
        typeof(RenderOutputTool),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((RenderOutputTool)bindable).RefreshOverlay());

    /// <summary>Identifies the <see cref="IsInteractive"/> bindable property.</summary>
    public static readonly BindableProperty IsInteractiveProperty = BindableProperty.Create(
        nameof(IsInteractive),
        typeof(bool),
        typeof(RenderOutputTool),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((RenderOutputTool)bindable).attachment?.UpdateButtons());

    /// <summary>Identifies the <see cref="IncludeDefaultOutputs"/> bindable property.</summary>
    public static readonly BindableProperty IncludeDefaultOutputsProperty = BindableProperty.Create(
        nameof(IncludeDefaultOutputs),
        typeof(bool),
        typeof(RenderOutputTool),
        true,
        propertyChanged: static (bindable, _, _) =>
            ((RenderOutputTool)bindable).RefreshOverlay());

    /// <summary>Identifies the <see cref="Placement"/> bindable property.</summary>
    public static readonly BindableProperty PlacementProperty = BindableProperty.Create(
        nameof(Placement),
        typeof(ViewportOverlayPlacement),
        typeof(RenderOutputTool),
        ViewportOverlayPlacement.TopLeft,
        validateValue: static (_, value) =>
            value is ViewportOverlayPlacement placement && Enum.IsDefined(placement),
        propertyChanged: static (bindable, _, _) =>
            ((RenderOutputTool)bindable).RefreshOverlay());

    /// <summary>Identifies the <see cref="Margin"/> bindable property.</summary>
    public static readonly BindableProperty MarginProperty = BindableProperty.Create(
        nameof(Margin),
        typeof(double),
        typeof(RenderOutputTool),
        12d,
        validateValue: static (_, value) =>
            value is double number && double.IsFinite(number) && number >= 0d,
        propertyChanged: static (bindable, _, _) =>
            ((RenderOutputTool)bindable).RefreshOverlay());

    /// <summary>Identifies the <see cref="MaximumWidth"/> bindable property.</summary>
    public static readonly BindableProperty MaximumWidthProperty = BindableProperty.Create(
        nameof(MaximumWidth),
        typeof(double),
        typeof(RenderOutputTool),
        620d,
        validateValue: static (_, value) =>
            value is double number && double.IsFinite(number) && number > 0d,
        propertyChanged: static (bindable, _, _) =>
            ((RenderOutputTool)bindable).RefreshOverlay());

    /// <summary>Gets the custom output entries appended after the optional built-in entries.</summary>
    public IList<RenderOutputOption> Items => items;

    /// <summary>Gets or sets the stable render-output identifier selected by this tool.</summary>
    /// <remarks>
    /// The default is <see cref="RenderOutputIds.Beauty"/>. Register extension identifiers in the
    /// target view's render-output registry before selecting them.
    /// </remarks>
    public RenderOutputId Output
    {
        get => (RenderOutputId)GetValue(OutputProperty);
        set => SetValue(OutputProperty, value);
    }

    /// <summary>Gets or sets whether the tool currently controls the view's render output.</summary>
    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets or sets whether the attachment-owned toolbar is shown.</summary>
    public bool IsVisible
    {
        get => (bool)GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>Gets or sets whether toolbar buttons accept input.</summary>
    public bool IsInteractive
    {
        get => (bool)GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    /// <summary>Gets or sets whether the standard built-in output buttons are included.</summary>
    public bool IncludeDefaultOutputs
    {
        get => (bool)GetValue(IncludeDefaultOutputsProperty);
        set => SetValue(IncludeDefaultOutputsProperty, value);
    }

    /// <summary>Gets or sets the viewport alignment containing the toolbar.</summary>
    public ViewportOverlayPlacement Placement
    {
        get => (ViewportOverlayPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>Gets or sets the non-negative viewport margin in MAUI device-independent units.</summary>
    public double Margin
    {
        get => (double)GetValue(MarginProperty);
        set => SetValue(MarginProperty, value);
    }

    /// <summary>Gets or sets the positive maximum toolbar width in MAUI device-independent units.</summary>
    /// <remarks>Buttons scroll horizontally when the available viewport width is smaller.</remarks>
    public double MaximumWidth
    {
        get => (double)GetValue(MaximumWidthProperty);
        set => SetValue(MaximumWidthProperty, value);
    }

    /// <inheritdoc />
    public IDisposable Attach(ViewportToolContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (attachment is not null)
        {
            throw new InvalidOperationException("A RenderOutputTool can attach only once at a time.");
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

    private void RefreshOverlay() => attachment?.RefreshOverlay();

    private IReadOnlyList<OutputChoice> GetChoices()
    {
        List<OutputChoice> choices = IncludeDefaultOutputs ? [.. DefaultChoices] : [];
        foreach (RenderOutputOption item in items)
        {
            string label = string.IsNullOrWhiteSpace(item.Label) ? item.Output.Value : item.Label;
            string description = string.IsNullOrWhiteSpace(item.Description)
                ? item.Output.Value
                : item.Description;
            choices.Add(new OutputChoice(label, item.Output, description));
        }
        return choices;
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        RefreshOverlay();
    }

    private sealed class OptionCollection(RenderOutputTool owner) : Collection<RenderOutputOption>
    {
        protected override void InsertItem(int index, RenderOutputOption item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (this.Any(candidate => ReferenceEquals(candidate, item)))
            {
                throw new InvalidOperationException("A render-output option cannot appear twice.");
            }
            base.InsertItem(index, item);
            item.PropertyChanged += owner.OnOptionPropertyChanged;
            owner.RefreshOverlay();
        }

        protected override void SetItem(int index, RenderOutputOption item)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (ReferenceEquals(this[index], item))
            {
                return;
            }
            if (this.Any(candidate => ReferenceEquals(candidate, item)))
            {
                throw new InvalidOperationException("A render-output option cannot appear twice.");
            }
            RenderOutputOption previous = this[index];
            previous.PropertyChanged -= owner.OnOptionPropertyChanged;
            base.SetItem(index, item);
            item.PropertyChanged += owner.OnOptionPropertyChanged;
            owner.RefreshOverlay();
        }

        protected override void RemoveItem(int index)
        {
            RenderOutputOption previous = this[index];
            previous.PropertyChanged -= owner.OnOptionPropertyChanged;
            base.RemoveItem(index);
            owner.RefreshOverlay();
        }

        protected override void ClearItems()
        {
            foreach (RenderOutputOption item in this)
            {
                item.PropertyChanged -= owner.OnOptionPropertyChanged;
            }
            base.ClearItems();
            owner.RefreshOverlay();
        }
    }

    private sealed class ToolAttachment : IDisposable
    {
        private const double ButtonHeight = 34d;
        private const double ToolbarPadding = 4d;
        private const double ToolbarHeight = ButtonHeight + (ToolbarPadding * 2d) + 2d;
        private static readonly MauiColor ToolbarBackground = MauiColor.FromArgb("#E6111827");
        private static readonly MauiColor ToolbarStroke = MauiColor.FromArgb("#FF53647D");
        private static readonly MauiColor SelectedBackground = MauiColor.FromArgb("#FF2563EB");
        private static readonly MauiColor IdleBackground = MauiColor.FromArgb("#00111827");
        private readonly RenderOutputTool owner;
        private readonly ViewportToolContext context;
        private readonly RenderOutputToolState state;
        private readonly List<Button> buttons = [];
        private ViewportOverlay? overlayHost;
        private IDisposable? overlayLease;
        private bool disposed;

        internal ToolAttachment(RenderOutputTool owner, ViewportToolContext context)
        {
            this.owner = owner;
            this.context = context;
            state = new RenderOutputToolState(
                () => context.View.RenderOutput,
                value => context.View.RenderOutput = value);
        }

        internal void Attach()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Apply();
            RefreshOverlay();
        }

        internal void Apply()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            state.Update(owner.IsEnabled, owner.Output);
            UpdateButtons();
        }

        internal void RefreshOverlay()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            RemoveOverlay();
            if (!owner.IsVisible)
            {
                return;
            }

            IReadOnlyList<OutputChoice> choices = owner.GetChoices();
            if (choices.Count == 0)
            {
                return;
            }

            HorizontalStackLayout row = new() { Spacing = 2d };
            foreach (OutputChoice choice in choices)
            {
                Button button = new()
                {
                    AutomationId = $"Mu3D.RenderOutput.{choice.Output.Value}",
                    CommandParameter = choice,
                    CornerRadius = 4,
                    FontSize = 11d,
                    HeightRequest = ButtonHeight,
                    MinimumHeightRequest = ButtonHeight,
                    Padding = new Thickness(9d, 2d),
                    Text = choice.Label,
                    TextColor = Colors.White,
                };
                SemanticProperties.SetDescription(button, choice.Description);
                ToolTipProperties.SetText(button, choice.Description);
                button.Clicked += OnButtonClicked;
                buttons.Add(button);
                row.Children.Add(button);
            }

            ScrollView scroller = new()
            {
                Content = row,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                HeightRequest = ButtonHeight,
                MinimumHeightRequest = ButtonHeight,
                Orientation = ScrollOrientation.Horizontal,
                VerticalScrollBarVisibility = ScrollBarVisibility.Never,
                VerticalOptions = LayoutOptions.Start,
            };
            Border created = new()
            {
                Background = new SolidColorBrush(ToolbarBackground),
                Content = scroller,
                HeightRequest = ToolbarHeight,
                MaximumHeightRequest = ToolbarHeight,
                MinimumHeightRequest = ToolbarHeight,
                Padding = new Thickness(ToolbarPadding),
                Stroke = new SolidColorBrush(ToolbarStroke),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(7d) },
                StrokeThickness = 1d,
            };
            ViewportOverlay createdHost = new()
            {
                Content = created,
                InputMode = ViewportOverlayInputMode.Interactive,
                Margin = new Thickness(owner.Margin),
                MaximumWidthRequest = owner.MaximumWidth,
                Placement = owner.Placement,
            };
            overlayHost = createdHost;
            overlayLease = createdHost.Attach(context);
            UpdateButtons();
        }

        internal void UpdateButtons()
        {
            if (overlayHost is not null)
            {
                overlayHost.InputMode = owner.IsEnabled && owner.IsInteractive
                    ? ViewportOverlayInputMode.Interactive
                    : ViewportOverlayInputMode.PassThrough;
            }
            foreach (Button button in buttons)
            {
                OutputChoice choice = (OutputChoice)button.CommandParameter;
                bool selected = owner.IsEnabled && choice.Output == owner.Output;
                button.BackgroundColor = selected ? SelectedBackground : IdleBackground;
                button.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
                button.IsEnabled = owner.IsEnabled && owner.IsInteractive;
                button.Opacity = button.IsEnabled ? 1d : 0.55d;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            RemoveOverlay();
            state.Release();
            if (ReferenceEquals(owner.attachment, this))
            {
                owner.attachment = null;
            }
        }

        private void RemoveOverlay()
        {
            foreach (Button button in buttons)
            {
                button.Clicked -= OnButtonClicked;
            }
            buttons.Clear();
            overlayLease?.Dispose();
            overlayLease = null;
            if (overlayHost is ViewportOverlay previous)
            {
                previous.Content = null;
                overlayHost = null;
            }
        }

        private void OnButtonClicked(object? sender, EventArgs e)
        {
            _ = e;
            if (owner.IsEnabled && owner.IsInteractive &&
                sender is Button { CommandParameter: OutputChoice choice })
            {
                owner.Output = choice.Output;
            }
        }

    }

    private sealed record OutputChoice(
        string Label,
        RenderOutputId Output,
        string Description);
}

/// <summary>
/// Provides the preferred visual-toolbar name for <see cref="RenderOutputTool"/>.
/// </summary>
/// <remarks>
/// This type has the same output ownership and nested <see cref="RenderOutputOption"/> contract as
/// its base class. It exists so XAML distinguishes this built-in visual control from nonvisual
/// viewport tool state.
/// </remarks>
[ContentProperty(nameof(Items))]
public sealed class RenderOutputToolbar : RenderOutputTool
{
}

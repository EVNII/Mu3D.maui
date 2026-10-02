using Mu3D.Rendering;

namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Defines one explicitly selectable entry in a <see cref="RenderOutputToolbar"/>.</summary>
public sealed class RenderOutputOption : BindableObject
{
    /// <summary>Identifies the <see cref="Label"/> bindable property.</summary>
    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label),
        typeof(string),
        typeof(RenderOutputOption),
        string.Empty,
        validateValue: static (_, value) => value is string);

    /// <summary>Identifies the <see cref="Description"/> bindable property.</summary>
    public static readonly BindableProperty DescriptionProperty = BindableProperty.Create(
        nameof(Description),
        typeof(string),
        typeof(RenderOutputOption),
        string.Empty,
        validateValue: static (_, value) => value is string);

    /// <summary>Identifies the <see cref="Output"/> bindable property.</summary>
    public static readonly BindableProperty OutputProperty = BindableProperty.Create(
        nameof(Output),
        typeof(RenderOutputId),
        typeof(RenderOutputOption),
        RenderOutputIds.Beauty,
        validateValue: static (_, value) =>
            value is RenderOutputId output && output.IsValid);

    /// <summary>Gets or sets the compact label displayed by the overlay button.</summary>
    /// <remarks>An empty label falls back to the stable output identifier.</remarks>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Gets or sets the optional tooltip and accessibility description.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>Gets or sets the stable output selected by this entry.</summary>
    public RenderOutputId Output
    {
        get => (RenderOutputId)GetValue(OutputProperty);
        set => SetValue(OutputProperty, value);
    }
}

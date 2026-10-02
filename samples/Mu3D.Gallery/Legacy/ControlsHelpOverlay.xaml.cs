using System.Collections.ObjectModel;

namespace Mu3D.GalleryApp;

/// <summary>Shows compact camera-control guidance and sensitivity settings over a viewport.</summary>
[ContentProperty(nameof(Items))]
public partial class ControlsHelpOverlay : ContentView
{
    /// <summary>Identifies the <see cref="Title"/> bindable property.</summary>
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title),
        typeof(string),
        typeof(ControlsHelpOverlay),
        "Controls");

    /// <summary>Initializes an initially collapsed control-help overlay.</summary>
    public ControlsHelpOverlay()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    /// <summary>Gets or sets the help-panel heading.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets the icon and instruction rows displayed by the panel.</summary>
    public ObservableCollection<ControlHelpItem> Items { get; } = [];

    /// <summary>Gets the live sensitivity controls displayed by the settings panel.</summary>
    public ObservableCollection<ControlSensitivityItem> Settings { get; } = [];

    private void OnHelpClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        HelpPanel.IsVisible = !HelpPanel.IsVisible;
        SettingsPanel.IsVisible = false;
    }

    private void OnSettingsClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        SettingsPanel.IsVisible = !SettingsPanel.IsVisible;
        HelpPanel.IsVisible = false;
    }

    private void OnCloseHelpClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        HelpPanel.IsVisible = false;
    }

    private void OnCloseSettingsClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        SettingsPanel.IsVisible = false;
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        HelpPanel.IsVisible = false;
        SettingsPanel.IsVisible = false;
    }
}

/// <summary>Defines one icon-led camera-control instruction.</summary>
public sealed class ControlHelpItem
{
    /// <summary>Gets or sets the short platform-rendered icon glyph.</summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>Gets or sets the instruction text.</summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>Defines one live multiplier slider in the camera settings panel.</summary>
public sealed class ControlSensitivityItem : BindableObject
{
    /// <summary>Identifies the <see cref="Label"/> bindable property.</summary>
    public static readonly BindableProperty LabelProperty = BindableProperty.Create(
        nameof(Label),
        typeof(string),
        typeof(ControlSensitivityItem),
        string.Empty);

    /// <summary>Identifies the <see cref="Minimum"/> bindable property.</summary>
    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum),
        typeof(double),
        typeof(ControlSensitivityItem),
        0.1d);

    /// <summary>Identifies the <see cref="Maximum"/> bindable property.</summary>
    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum),
        typeof(double),
        typeof(ControlSensitivityItem),
        3d);

    /// <summary>Identifies the <see cref="Value"/> bindable property.</summary>
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(double),
        typeof(ControlSensitivityItem),
        1d);

    /// <summary>Gets or sets the human-readable sensitivity name.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Gets or sets the slider minimum.</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Gets or sets the slider maximum.</summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Gets or sets the live sensitivity multiplier.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }
}

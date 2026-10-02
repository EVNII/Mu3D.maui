using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Maui.Toolkit.Diagnostics;

namespace Mu3D.GalleryApp.Controls;

/// <summary>
/// Provides one reusable, collapsed-by-default control panel for Gallery scene diagnostics.
/// </summary>
/// <remarks>
/// The panel borrows explicitly declared viewport tools. It does not create, attach, own, or hide
/// them, so every focused example keeps its significant Mu3D declarations visible in XAML.
/// </remarks>
public partial class GalleryDebugSceneControls : ContentView
{
    /// <summary>Identifies the <see cref="Orbit"/> bindable property.</summary>
    public static readonly BindableProperty OrbitProperty = CreateToolProperty<OrbitTool>(
        nameof(Orbit),
        nameof(HasOrbit));

    /// <summary>Identifies the <see cref="Selection"/> bindable property.</summary>
    public static readonly BindableProperty SelectionProperty = CreateToolProperty<SceneSelectionTool>(
        nameof(Selection),
        nameof(HasSelection));

    /// <summary>Identifies the <see cref="Gizmo"/> bindable property.</summary>
    public static readonly BindableProperty GizmoProperty = CreateToolProperty<TransformGizmoTool>(
        nameof(Gizmo),
        nameof(HasGizmo));

    /// <summary>Identifies the <see cref="ReferenceGrid"/> bindable property.</summary>
    public static readonly BindableProperty ReferenceGridProperty = CreateToolProperty<GridHelper>(
        nameof(ReferenceGrid),
        nameof(HasGrid));

    /// <summary>Identifies the <see cref="Axes"/> bindable property.</summary>
    public static readonly BindableProperty AxesProperty = CreateToolProperty<AxesHelper>(
        nameof(Axes),
        nameof(HasAxes));

    /// <summary>Identifies the <see cref="Statistics"/> bindable property.</summary>
    public static readonly BindableProperty StatisticsProperty =
        CreateToolProperty<FrameStatisticsOverlay>(nameof(Statistics), nameof(HasStatistics));

    /// <summary>Identifies the <see cref="IsExpanded"/> bindable property.</summary>
    public static readonly BindableProperty IsExpandedProperty = BindableProperty.Create(
        nameof(IsExpanded),
        typeof(bool),
        typeof(GalleryDebugSceneControls),
        false,
        defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: static (bindable, _, _) =>
            ((GalleryDebugSceneControls)bindable).OnPropertyChanged(nameof(ToggleText)));

    /// <summary>Initializes the reusable debug-scene control panel.</summary>
    public GalleryDebugSceneControls() => InitializeComponent();

    /// <summary>Gets or sets the optional Orbit controller exposed by the panel.</summary>
    public OrbitTool? Orbit
    {
        get => (OrbitTool?)GetValue(OrbitProperty);
        set => SetValue(OrbitProperty, value);
    }

    /// <summary>Gets or sets the optional scene-selection tool exposed by the panel.</summary>
    public SceneSelectionTool? Selection
    {
        get => (SceneSelectionTool?)GetValue(SelectionProperty);
        set => SetValue(SelectionProperty, value);
    }

    /// <summary>Gets or sets the optional Transform Gizmo exposed by the panel.</summary>
    public TransformGizmoTool? Gizmo
    {
        get => (TransformGizmoTool?)GetValue(GizmoProperty);
        set => SetValue(GizmoProperty, value);
    }

    /// <summary>Gets or sets the optional scene grid exposed by the panel.</summary>
    public GridHelper? ReferenceGrid
    {
        get => (GridHelper?)GetValue(ReferenceGridProperty);
        set => SetValue(ReferenceGridProperty, value);
    }

    /// <summary>Gets or sets the optional orientation-axes helper exposed by the panel.</summary>
    public AxesHelper? Axes
    {
        get => (AxesHelper?)GetValue(AxesProperty);
        set => SetValue(AxesProperty, value);
    }

    /// <summary>Gets or sets the optional frame-statistics overlay exposed by the panel.</summary>
    public FrameStatisticsOverlay? Statistics
    {
        get => (FrameStatisticsOverlay?)GetValue(StatisticsProperty);
        set => SetValue(StatisticsProperty, value);
    }

    /// <summary>Gets or sets whether the optional controls are expanded.</summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>Gets whether an Orbit controller was supplied.</summary>
    public bool HasOrbit => Orbit is not null;

    /// <summary>Gets whether a scene-selection tool was supplied.</summary>
    public bool HasSelection => Selection is not null;

    /// <summary>Gets whether a Transform Gizmo was supplied.</summary>
    public bool HasGizmo => Gizmo is not null;

    /// <summary>Gets whether a scene grid was supplied.</summary>
    public bool HasGrid => ReferenceGrid is not null;

    /// <summary>Gets whether an orientation-axes helper was supplied.</summary>
    public bool HasAxes => Axes is not null;

    /// <summary>Gets whether a frame-statistics overlay was supplied.</summary>
    public bool HasStatistics => Statistics is not null;

    /// <summary>Gets the current accessible disclosure-button label.</summary>
    public string ToggleText => IsExpanded ? "Debug controls ▴" : "Debug controls ▾";

    private static BindableProperty CreateToolProperty<T>(string name, string availabilityProperty)
        where T : BindableObject =>
        BindableProperty.Create(
            name,
            typeof(T),
            typeof(GalleryDebugSceneControls),
            default(T),
            propertyChanged: (bindable, _, _) =>
                ((GalleryDebugSceneControls)bindable).OnPropertyChanged(availabilityProperty));

    private void OnToggleExpanded(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        IsExpanded = !IsExpanded;
    }
}

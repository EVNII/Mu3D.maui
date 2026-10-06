using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Maui.Toolkit.Diagnostics;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Diagnostics;
using Mu3D.Toolkit.Helpers;

using MauiColor = Microsoft.Maui.Graphics.Color;

namespace Mu3D.Gallery.Controls;

/// <summary>
/// Uniform chrome for every Mu3D 3D page in this sample: one <see cref="Mu3DSceneView"/> with a
/// shared orbit camera controller, an in-viewport FPS overlay, an explicit display-view picker, an
/// output dynamic-range picker, an EV (exposure stops) slider and a read-only negotiated-output
/// line. Pages supply a <see cref="Scene3D"/> as content and bind their declarative camera to
/// <see cref="Camera"/>; the default view preserves scene-linear HDR without a display transform.
/// </summary>
[ContentProperty(nameof(Scene))]
public partial class SceneHostView : Grid
{
    private static readonly ColorViewPreset[] ViewPresets = Enum.GetValues<ColorViewPreset>();
    private static readonly string[] DynamicRangeNames = ["Automatic", "HDR", "SDR"];

    private readonly Mu3DSceneView sceneView;
    private readonly ViewportTools viewportTools;
    private readonly OrbitTool orbitTool;
    private readonly FrameStatisticsOverlay statisticsOverlay;
    private readonly ColorView3D displayTransform;
    private readonly Picker presetPicker;
    private readonly Picker dynamicRangePicker;
    private readonly Slider evSlider;
    private readonly Label evLabel;
    private readonly Label outputLabel;
    private readonly Label errorLabel;
    private readonly View chromeBar;

    /// <summary>Initializes the host with the uniform scene chrome.</summary>
    public SceneHostView()
    {
        RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)];

        displayTransform = new ColorView3D();
        orbitTool = new OrbitTool
        {
            Input = CreateUniformInput(),
            DampingEnabled = true,
            DampingTime = 0.1f,
            MinimumDistance = 1.5f,
            MaximumDistance = 40,
            PinchDollyMultiplier = 1.6f,
        };
        statisticsOverlay = new FrameStatisticsOverlay
        {
            DisplayMode = FrameStatisticsDisplayMode.Compact,
            IsGraphVisible = true,
            Margin = 12,
            MaximumWidth = 420,
            Placement = ViewportOverlayPlacement.TopRight,
        };
        viewportTools = new ViewportTools();
        viewportTools.Items.Add(statisticsOverlay);

        sceneView = new Mu3DSceneView
        {
            BackgroundColor = Colors.Black,
        };
        sceneView.Features.Add(viewportTools);
        sceneView.SurfaceError += OnSceneSurfaceError;
        sceneView.PresentationSessionChanged += OnPresentationSessionChanged;
        Add(sceneView);

        presetPicker = new Picker { Title = "View" };
        presetPicker.Items.Add("Scene linear · HDR");
        foreach (ColorViewPreset preset in ViewPresets)
        {
            presetPicker.Items.Add(preset.ToString());
        }
        presetPicker.SelectedIndex = 0;
        presetPicker.SelectedIndexChanged += OnPresetChanged;

        dynamicRangePicker = new Picker { Title = "Output" };
        foreach (string name in DynamicRangeNames)
        {
            dynamicRangePicker.Items.Add(name);
        }
        dynamicRangePicker.SelectedIndex = 0;
        dynamicRangePicker.SelectedIndexChanged += OnDynamicRangeChanged;

        evSlider = new Slider(-4, 4, 0) { MinimumWidthRequest = 140 };
        evSlider.ValueChanged += OnExposureChanged;
        evLabel = new Label { Text = "EV +0.00", VerticalTextAlignment = TextAlignment.Center };

        outputLabel = new Label { FontSize = 11, Text = "Waiting for surface…" };
        errorLabel = new Label { FontSize = 11, TextColor = Colors.OrangeRed };

        chromeBar = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = new HorizontalStackLayout
            {
                Padding = new Thickness(12, 6),
                Spacing = 14,
                Children =
                {
                    presetPicker,
                    dynamicRangePicker,
                    new Label { Text = "EV", VerticalTextAlignment = TextAlignment.Center },
                    evSlider,
                    evLabel,
                    outputLabel,
                    errorLabel,
                },
            },
        };
        SetRow((BindableObject)chromeBar, 1);
        Add(chromeBar);
    }

    /// <summary>Identifies the <see cref="Scene"/> bindable property.</summary>
    public static readonly BindableProperty SceneProperty = BindableProperty.Create(
        nameof(Scene),
        typeof(Scene3D),
        typeof(SceneHostView),
        null,
        propertyChanged: static (bindable, _, value) =>
            ((SceneHostView)bindable).sceneView.SceneContent = (Scene3D?)value);

    /// <summary>Identifies the <see cref="Camera"/> bindable property.</summary>
    public static readonly BindableProperty CameraProperty = BindableProperty.Create(
        nameof(Camera),
        typeof(PerspectiveCamera3D),
        typeof(SceneHostView),
        null,
        propertyChanged: static (bindable, _, value) =>
            ((SceneHostView)bindable).ApplyCamera((PerspectiveCamera3D?)value));

    /// <summary>Identifies the <see cref="ShowStatistics"/> bindable property.</summary>
    public static readonly BindableProperty ShowStatisticsProperty = BindableProperty.Create(
        nameof(ShowStatistics),
        typeof(bool),
        typeof(SceneHostView),
        true,
        propertyChanged: static (bindable, _, value) =>
            ((SceneHostView)bindable).statisticsOverlay.IsVisible = (bool)value);

    /// <summary>Identifies the <see cref="ShowChrome"/> bindable property.</summary>
    public static readonly BindableProperty ShowChromeProperty = BindableProperty.Create(
        nameof(ShowChrome),
        typeof(bool),
        typeof(SceneHostView),
        true,
        propertyChanged: static (bindable, _, value) =>
            ((SceneHostView)bindable).chromeBar.IsVisible = (bool)value);

    /// <summary>Identifies the <see cref="SceneBackground"/> bindable property.</summary>
    public static readonly BindableProperty SceneBackgroundProperty = BindableProperty.Create(
        nameof(SceneBackground),
        typeof(MauiColor),
        typeof(SceneHostView),
        Colors.Black,
        propertyChanged: static (bindable, _, value) =>
            ((SceneHostView)bindable).sceneView.BackgroundColor = (MauiColor)value);

    /// <summary>Gets or sets the declarative scene hosted by the view.</summary>
    public Scene3D? Scene
    {
        get => (Scene3D?)GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>
    /// Gets or sets the declarative perspective camera driven by the shared orbit controller.
    /// When null, no camera controller is attached.
    /// </summary>
    public PerspectiveCamera3D? Camera
    {
        get => (PerspectiveCamera3D?)GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    /// <summary>Gets or sets whether the FPS overlay is visible. Defaults to true.</summary>
    public bool ShowStatistics
    {
        get => (bool)GetValue(ShowStatisticsProperty);
        set => SetValue(ShowStatisticsProperty, value);
    }

    /// <summary>Gets or sets whether the bottom chrome bar is visible. Defaults to true.</summary>
    public bool ShowChrome
    {
        get => (bool)GetValue(ShowChromeProperty);
        set => SetValue(ShowChromeProperty, value);
    }

    /// <summary>Gets or sets the hosted scene view's background color.</summary>
    public MauiColor SceneBackground
    {
        get => (MauiColor)GetValue(SceneBackgroundProperty);
        set => SetValue(SceneBackgroundProperty, value);
    }

    /// <summary>Gets the hosted scene view for page-specific feature or event wiring.</summary>
    public Mu3DSceneView SceneView => sceneView;

    /// <summary>
    /// Gets the shared viewport tool container. Page-specific tools (for example a
    /// <see cref="TransformGizmoTool"/>) added to <see cref="ViewportTools.Items"/> attach beside
    /// the host-owned orbit tool and share its <see cref="ViewportControlArbiter"/> lease, which
    /// preserves orbit-versus-gizmo pointer arbitration without a second orbit controller.
    /// </summary>
    public ViewportTools Tools => viewportTools;

    /// <summary>
    /// Gets the host-owned orbit tool for behavior and event wiring. Pages must not add it to
    /// another container or replace its camera.
    /// </summary>
    public OrbitTool OrbitTool => orbitTool;

    /// <summary>Gets the selected display transform, or null for the default scene-linear HDR view.</summary>
    public ColorView3D? DisplayTransform => sceneView.DisplayTransform;

    /// <summary>Forwards the hosted view's presentation failure event.</summary>
    public event EventHandler<SurfaceErrorEventArgs>? SurfaceError;

    private static ViewportInput CreateUniformInput() => new()
    {
        Mouse = new MouseInput
        {
            LeftButtonDragAction = ViewportDragAction.Rotate,
            RightButtonDragAction = ViewportDragAction.Pan,
        },
        Trackpad = new TrackpadInput
        {
            TwoFingerDragAction = ViewportDragAction.Pan,
        },
        Touchscreen = new TouchscreenInput
        {
            OneFingerDragAction = ViewportDragAction.Rotate,
            TwoFingerDragAction = ViewportDragAction.Pan,
        },
        Keyboard = new KeyboardInput { IsEnabled = false },
    };

    private void ApplyCamera(PerspectiveCamera3D? camera)
    {
        orbitTool.Camera = camera;
        if (camera is null)
        {
            viewportTools.Items.Remove(orbitTool);
        }
        else if (!viewportTools.Items.Contains(orbitTool))
        {
            viewportTools.Items.Add(orbitTool);
        }
    }

    private void OnPresetChanged(object? sender, EventArgs e)
    {
        _ = sender;
        if (presetPicker.SelectedIndex < 0)
        {
            return;
        }

        if (presetPicker.SelectedIndex == 0)
        {
            sceneView.DisplayTransform = null;
            return;
        }

        displayTransform.Preset = ViewPresets[presetPicker.SelectedIndex - 1];
        sceneView.DisplayTransform = displayTransform;
        evLabel.Text = $"EV {displayTransform.ExposureStops:+0.00;-0.00;+0.00}";
    }

    private void OnDynamicRangeChanged(object? sender, EventArgs e)
    {
        _ = sender;
        if (dynamicRangePicker.SelectedIndex < 0)
        {
            return;
        }

        OutputDynamicRange range = dynamicRangePicker.SelectedIndex switch
        {
            1 => OutputDynamicRange.Hdr,
            2 => OutputDynamicRange.Sdr,
            _ => OutputDynamicRange.Automatic,
        };
        OutputSettings current = sceneView.OutputSettings ?? OutputSettings.Default;
        sceneView.OutputSettings = current with { DynamicRange = range };
    }

    private void OnExposureChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        float stops = (float)Math.Round(e.NewValue * 4, MidpointRounding.ToEven) / 4f;
        sceneView.SceneLinearExposureStops = stops;
        displayTransform.ExposureStops = stops;
        evLabel.Text = $"EV {stops:+0.00;-0.00;+0.00}";
    }

    private void OnPresentationSessionChanged(object? sender, PresentationSessionChangedEventArgs e)
    {
        _ = sender;
        ActualOutputState? output = e.Session?.OutputPlan.Output;
        outputLabel.Text = output is null
            ? "Waiting for surface…"
            : $"{output.Format} · {output.Encoding} · {output.DynamicRange}" +
              (output.HdrHeadroom is float headroom ? $" · headroom {headroom:0.0}x" : string.Empty) +
              (output.FallbackReason is string reason ? $" · fallback: {reason}" : string.Empty);
    }

    private void OnSceneSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        errorLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
        SurfaceError?.Invoke(this, e);
    }
}

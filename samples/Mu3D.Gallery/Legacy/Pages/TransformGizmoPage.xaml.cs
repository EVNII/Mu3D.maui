using System.Numerics;
using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Controls;
using Mu3D.Toolkit.Gizmos;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates opt-in transform-gizmo rendering and pointer interaction.</summary>
public partial class TransformGizmoPage : ContentPage
{
    private readonly Scene scene = new("Transform gizmo example");
    private readonly SceneNode target = new("Transform target");
    private readonly PerspectiveCamera camera = new(name: "Gizmo camera");
    private readonly TransformGizmo gizmo;
    private readonly OrbitController orbitController;
    private readonly ViewportControlArbiter controlArbiter = new();

    /// <summary>Initializes the interactive transform-gizmo example.</summary>
    public TransformGizmoPage()
    {
        InitializeComponent();

        SceneView.ClearColor = new LinearRgba(
            0.012f,
            0.018f,
            0.035f,
            1f,
            StandardColorSpaces.LinearSrgb);
        CreateTargetVisual();
        scene.Add(target);

        camera.Transform.Position = new Vector3(3.2f, 2.4f, 6.2f);
        orbitController = new OrbitController(camera, Vector3.Zero)
        {
            DampingEnabled = true,
            DampingTime = 0.1f,
            MinimumDistance = 2.5f,
            MaximumDistance = 15f,
        };
        gizmo = new TransformGizmo(target)
        {
            ScreenSizePixels = 110f,
            IsRotateEnabled = true,
            IsScaleEnabled = true,
            TranslationSnap = 0.1f,
            RotationSnapRadians = MathF.PI / 12f,
            ScaleSnap = 0.1f,
        };

        OrbitFeature.Controller = orbitController;
        OrbitFeature.ControlArbiter = controlArbiter;
        GizmoFeature.Gizmo = gizmo;
        GizmoFeature.ControlArbiter = controlArbiter;
        OrbitFeature.Behavior.InteractionFailed += OnOrbitInteractionFailed;
        GizmoFeature.Behavior.InteractionFailed += OnGizmoPointerFailed;
        FeaturesEnabledSwitch.Toggled += OnFeaturesEnabledChanged;

        gizmo.InteractionStarted += OnGizmoInteractionStarted;
        gizmo.InteractionChanged += OnGizmoInteractionChanged;
        gizmo.InteractionCompleted += OnGizmoInteractionCompleted;
        gizmo.InteractionCanceled += OnGizmoInteractionCanceled;
        TranslateSwitch.Toggled += OnEnabledModesChanged;
        RotateSwitch.Toggled += OnEnabledModesChanged;
        ScaleSwitch.Toggled += OnEnabledModesChanged;
        LocalAxesSwitch.Toggled += OnSpaceChanged;
        HandleSizeSlider.ValueChanged += OnHandleSizeChanged;
        TranslationSnapStepper.ValueChanged += OnSnapStepChanged;
        RotationSnapStepper.ValueChanged += OnSnapStepChanged;
        ScaleSnapStepper.ValueChanged += OnSnapStepChanged;

        SceneView.Scene = scene;
        SceneView.Camera = camera;
        UpdateConfigurationLabels();
        UpdatePose();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        StatusLabel.Text = GizmoFeature.IsPointerInputAvailable
            ? "Ready — Orbit and Gizmo are attached through SceneView.Features"
            : "Scene ready, but native pointer input is unavailable on this target";
        SceneView.InvalidateScene();
    }

    private void CreateTargetVisual()
    {
        Examples.ToolkitSceneExamples.AddGizmoVisual(target);
    }

    private void OnEnabledModesChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        _ = e;
        if (gizmo.IsInteracting)
        {
            return;
        }
        gizmo.IsTranslateEnabled = TranslateSwitch.IsToggled;
        gizmo.IsRotateEnabled = RotateSwitch.IsToggled;
        gizmo.IsScaleEnabled = ScaleSwitch.IsToggled;
        StatusLabel.Text = DescribeEnabledModes();
        SceneView.InvalidateScene();
    }

    private void OnFeaturesEnabledChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (gizmo.IsInteracting)
        {
            gizmo.CancelInteraction();
        }

        if (e.Value)
        {
            if (!SceneView.Features.Contains(OrbitFeature))
            {
                SceneView.Features.Add(OrbitFeature);
            }
            if (!SceneView.Features.Contains(GizmoFeature))
            {
                SceneView.Features.Add(GizmoFeature);
            }
            StatusLabel.Text = "Features reattached in XAML order: Orbit, then Gizmo";
        }
        else
        {
            SceneView.Features.Clear();
            StatusLabel.Text = "Features detached in reverse order; scene and target remain owned by the page";
        }
        SceneView.InvalidateScene();
    }

    private void OnHandleSizeChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        if (gizmo.IsInteracting)
        {
            return;
        }
        gizmo.ScreenSizePixels = (float)e.NewValue;
        UpdateConfigurationLabels();
        StatusLabel.Text = $"Handle size: {gizmo.ScreenSizePixels:0} physical px";
        SceneView.InvalidateScene();
    }

    private void OnSnapStepChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (gizmo.IsInteracting)
        {
            return;
        }
        gizmo.TranslationSnap = (float)TranslationSnapStepper.Value;
        gizmo.RotationSnapRadians = (float)(RotationSnapStepper.Value * Math.PI / 180d);
        gizmo.ScaleSnap = (float)ScaleSnapStepper.Value;
        UpdateConfigurationLabels();
        StatusLabel.Text = "Snap steps updated";
    }

    private void OnSpaceChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (gizmo.IsInteracting)
        {
            return;
        }
        gizmo.Space = e.Value ? TransformGizmoSpace.Local : TransformGizmoSpace.World;
        StatusLabel.Text = $"Move/rotate: {gizmo.Space}; scale: Local";
        SceneView.InvalidateScene();
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (gizmo.IsInteracting)
        {
            gizmo.CancelInteraction();
        }
        target.Transform.Position = Vector3.Zero;
        target.Transform.Rotation = Quaternion.Identity;
        target.Transform.Scale = Vector3.One;
        StatusLabel.Text = "Transform reset";
        SetControlsEnabled(true);
        UpdatePose();
        SceneView.InvalidateScene();
    }

    private void OnGizmoInteractionStarted(object? sender, TransformGizmoInteractionEventArgs e)
    {
        _ = sender;
        SetControlsEnabled(false);
        StatusLabel.Text = $"Dragging {e.Axis} ({e.Mode}, {e.Space})";
    }

    private void OnGizmoInteractionChanged(object? sender, TransformGizmoInteractionEventArgs e)
    {
        _ = sender;
        _ = e;
        UpdatePose();
        SceneView.InvalidateScene();
    }

    private void OnGizmoInteractionCompleted(object? sender, TransformGizmoInteractionEventArgs e)
    {
        _ = sender;
        SetControlsEnabled(true);
        StatusLabel.Text = $"Committed {e.Mode} on {e.Axis}";
        UpdatePose();
        SceneView.InvalidateScene();
    }

    private void OnGizmoInteractionCanceled(object? sender, TransformGizmoInteractionEventArgs e)
    {
        _ = sender;
        _ = e;
        SetControlsEnabled(true);
        StatusLabel.Text = "Interaction canceled; initial transform restored";
        UpdatePose();
        SceneView.InvalidateScene();
    }

    private void OnGizmoPointerFailed(object? sender, TransformGizmoPointerFailedEventArgs e)
    {
        _ = sender;
        SetControlsEnabled(!gizmo.IsInteracting);
        StatusLabel.Text = $"Gizmo input failed: {e.Exception.Message}";
    }

    private void OnOrbitInteractionFailed(object? sender, ViewportNavigationFailedEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Orbit input failed: {e.Exception.Message}";
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Presentation {e.Operation} failed: {e.Exception.Message}";
    }

    private void OnFeatureError(object? sender, SceneViewFeatureErrorEventArgs e)
    {
        _ = sender;
        StatusLabel.Text = $"Feature {e.Operation} failed: {e.Exception.Message}";
    }

    private void SetControlsEnabled(bool enabled)
    {
        TranslateSwitch.IsEnabled = enabled;
        RotateSwitch.IsEnabled = enabled;
        ScaleSwitch.IsEnabled = enabled;
        LocalAxesSwitch.IsEnabled = enabled;
        HandleSizeSlider.IsEnabled = enabled;
        TranslationSnapStepper.IsEnabled = enabled;
        RotationSnapStepper.IsEnabled = enabled;
        ScaleSnapStepper.IsEnabled = enabled;
        ResetButton.IsEnabled = enabled;
    }

    private void UpdateConfigurationLabels()
    {
        HandleSizeValueLabel.Text = $"{gizmo.ScreenSizePixels:0} px";
        TranslationSnapValueLabel.Text = $"{gizmo.TranslationSnap:0.00}";
        RotationSnapValueLabel.Text = $"{RotationSnapStepper.Value:0}°";
        ScaleSnapValueLabel.Text = $"{gizmo.ScaleSnap:0.00}";
    }

    private string DescribeEnabledModes()
    {
        List<string> modes = [];
        if (gizmo.IsTranslateEnabled)
        {
            modes.Add("Move");
        }
        if (gizmo.IsRotateEnabled)
        {
            modes.Add("Rotate");
        }
        if (gizmo.IsScaleEnabled)
        {
            modes.Add("Scale");
        }
        return modes.Count == 0
            ? "All gizmo handles hidden"
            : $"Enabled overlay handles: {string.Join(" + ", modes)}";
    }

    private void UpdatePose()
    {
        TransformGizmoPose pose = TransformGizmoPose.Capture(target);
        PoseLabel.Text =
            $"Position  {Format(pose.Position)}\n" +
            $"Rotation  ({pose.Rotation.X,6:0.00}, {pose.Rotation.Y,6:0.00}, " +
            $"{pose.Rotation.Z,6:0.00}, {pose.Rotation.W,6:0.00})\n" +
            $"Scale     {Format(pose.Scale)}";
    }

    private static string Format(Vector3 value) =>
        $"({value.X,6:0.00}, {value.Y,6:0.00}, {value.Z,6:0.00})";
}

using Mu3D.Gallery.Controls;
using System.Numerics;
using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Toolkit.Gizmos;

namespace Mu3D.Gallery.Pages;

/// <summary>Demonstrates opt-in transform-gizmo rendering and pointer interaction.</summary>
/// <remarks>
/// The shared <see cref="Controls.SceneHostView"/> owns the only orbit controller. This page
/// toggles that orbit tool together with its <see cref="TransformGizmoTool"/> in the host's
/// viewport tool container, so both share the container's single control arbiter.
/// </remarks>
public partial class Toolkit3DPage : ContentPage, IGalleryPageActivation
{
    private readonly TransformGizmoTool gizmoTool;
    private TransformGizmo? subscribedGizmo;

    /// <summary>Initializes the interactive transform-gizmo example.</summary>
    public Toolkit3DPage()
    {
        InitializeComponent();

        // The wrapper's Color property accepts an sRGB UI color. Preserve the original example's
        // explicitly tagged linear HDR value through its owned Core material.
        TargetMaterial.UnlitMaterial.Color = new LinearRgba(
            .18f, .55f, 1.35f, 1f, StandardColorSpaces.LinearSrgb);

        gizmoTool = new TransformGizmoTool
        {
            Target = TargetCone,
            IsRotateEnabled = true,
            IsScaleEnabled = true,
            TranslationStep = 0.1f,
            RotationStep = 15f,
            ScaleStep = 0.1f,
            ScreenSize = 110f,
        };

        gizmoTool.Behavior.InteractionFailed += OnGizmoPointerFailed;
        Host.OrbitTool.Behavior.InteractionFailed += OnOrbitInteractionFailed;
        Host.SceneView.FeatureError += OnFeatureError;
        FeaturesEnabledSwitch.Toggled += OnFeaturesEnabledChanged;
        TranslateSwitch.Toggled += OnEnabledModesChanged;
        RotateSwitch.Toggled += OnEnabledModesChanged;
        ScaleSwitch.Toggled += OnEnabledModesChanged;
        LocalAxesSwitch.Toggled += OnSpaceChanged;
        HandleSizeSlider.ValueChanged += OnHandleSizeChanged;
        TranslationSnapStepper.ValueChanged += OnSnapStepChanged;
        RotationSnapStepper.ValueChanged += OnSnapStepChanged;
        ScaleSnapStepper.ValueChanged += OnSnapStepChanged;

        UpdateConfigurationLabels();
        UpdatePose();
        DetachTools();
    }

    private bool navigationActive;

    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (navigationActive == active) return;
        navigationActive = active;
        if (active && FeaturesEnabledSwitch.IsToggled)
        {
            AttachTools();
            Host.SceneView.InvalidateScene();
        }
        else
        {
            DetachTools();
        }
    }

    private void AttachTools()
    {
        if (!Host.Tools.Items.Contains(Host.OrbitTool))
        {
            Host.Tools.Items.Add(Host.OrbitTool);
        }
        if (!Host.Tools.Items.Contains(gizmoTool))
        {
            Host.Tools.Items.Add(gizmoTool);
        }
        SubscribeGizmo();
        StatusLabel.Text = gizmoTool.IsPointerInputAvailable
            ? "Ready — the host's orbit tool and the page's gizmo tool share one control arbiter"
            : "Scene ready, but native pointer input is unavailable on this target";
    }

    private void DetachTools()
    {
        if (gizmoTool.Gizmo?.IsInteracting == true) gizmoTool.Gizmo.CancelInteraction();
        Host.Tools.Items.Remove(gizmoTool);
        Host.Tools.Items.Remove(Host.OrbitTool);
        UnsubscribeGizmo();
        SetControlsEnabled(true);
    }

    private void SubscribeGizmo()
    {
        TransformGizmo? gizmo = gizmoTool.Gizmo;
        if (ReferenceEquals(gizmo, subscribedGizmo))
        {
            return;
        }
        UnsubscribeGizmo();
        subscribedGizmo = gizmo;
        if (gizmo is null)
        {
            return;
        }
        gizmo.InteractionStarted += OnGizmoInteractionStarted;
        gizmo.InteractionChanged += OnGizmoInteractionChanged;
        gizmo.InteractionCompleted += OnGizmoInteractionCompleted;
        gizmo.InteractionCanceled += OnGizmoInteractionCanceled;
    }

    private void UnsubscribeGizmo()
    {
        if (subscribedGizmo is null)
        {
            return;
        }
        subscribedGizmo.InteractionStarted -= OnGizmoInteractionStarted;
        subscribedGizmo.InteractionChanged -= OnGizmoInteractionChanged;
        subscribedGizmo.InteractionCompleted -= OnGizmoInteractionCompleted;
        subscribedGizmo.InteractionCanceled -= OnGizmoInteractionCanceled;
        subscribedGizmo = null;
    }

    private void OnEnabledModesChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        _ = e;
        if (gizmoTool.Gizmo?.IsInteracting == true)
        {
            return;
        }
        gizmoTool.IsTranslateEnabled = TranslateSwitch.IsToggled;
        gizmoTool.IsRotateEnabled = RotateSwitch.IsToggled;
        gizmoTool.IsScaleEnabled = ScaleSwitch.IsToggled;
        StatusLabel.Text = DescribeEnabledModes();
        Host.SceneView.InvalidateScene();
    }

    private void OnFeaturesEnabledChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (e.Value && navigationActive)
        {
            AttachTools();
            StatusLabel.Text = "Tools reattached in order: Orbit, then Gizmo";
        }
        else
        {
            DetachTools();
            StatusLabel.Text = "Orbit and Gizmo detached; the scene and target remain owned by the page";
        }
        Host.SceneView.InvalidateScene();
    }

    private void OnHandleSizeChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        if (gizmoTool.Gizmo?.IsInteracting == true)
        {
            return;
        }
        gizmoTool.ScreenSize = (float)e.NewValue;
        UpdateConfigurationLabels();
        StatusLabel.Text = $"Handle size: {gizmoTool.ScreenSize:0} physical px";
        Host.SceneView.InvalidateScene();
    }

    private void OnSnapStepChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (gizmoTool.Gizmo?.IsInteracting == true)
        {
            return;
        }
        gizmoTool.TranslationStep = (float)TranslationSnapStepper.Value;
        gizmoTool.RotationStep = (float)RotationSnapStepper.Value;
        gizmoTool.ScaleStep = (float)ScaleSnapStepper.Value;
        UpdateConfigurationLabels();
        StatusLabel.Text = "Snap steps updated";
    }

    private void OnSpaceChanged(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (gizmoTool.Gizmo?.IsInteracting == true)
        {
            return;
        }
        gizmoTool.Space = e.Value ? TransformGizmoSpace.Local : TransformGizmoSpace.World;
        StatusLabel.Text = $"Move/rotate: {gizmoTool.Space}; scale: Local";
        Host.SceneView.InvalidateScene();
    }

    private void OnResetClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (gizmoTool.Gizmo?.IsInteracting == true)
        {
            gizmoTool.Gizmo.CancelInteraction();
        }
        TargetCone.CoreNode.Transform.Position = Vector3.Zero;
        TargetCone.CoreNode.Transform.Rotation = Quaternion.Identity;
        TargetCone.CoreNode.Transform.Scale = Vector3.One;
        StatusLabel.Text = "Transform reset";
        SetControlsEnabled(true);
        UpdatePose();
        Host.SceneView.InvalidateScene();
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
        Host.SceneView.InvalidateScene();
    }

    private void OnGizmoInteractionCompleted(object? sender, TransformGizmoInteractionEventArgs e)
    {
        _ = sender;
        SetControlsEnabled(true);
        StatusLabel.Text = $"Committed {e.Mode} on {e.Axis}";
        UpdatePose();
        Host.SceneView.InvalidateScene();
    }

    private void OnGizmoInteractionCanceled(object? sender, TransformGizmoInteractionEventArgs e)
    {
        _ = sender;
        _ = e;
        SetControlsEnabled(true);
        StatusLabel.Text = "Interaction canceled; initial transform restored";
        UpdatePose();
        Host.SceneView.InvalidateScene();
    }

    private void OnGizmoPointerFailed(object? sender, TransformGizmoPointerFailedEventArgs e)
    {
        _ = sender;
        SetControlsEnabled(gizmoTool.Gizmo?.IsInteracting != true);
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
        HandleSizeValueLabel.Text = $"{gizmoTool.ScreenSize:0} px";
        TranslationSnapValueLabel.Text = $"{gizmoTool.TranslationStep:0.00}";
        RotationSnapValueLabel.Text = $"{gizmoTool.RotationStep:0}°";
        ScaleSnapValueLabel.Text = $"{gizmoTool.ScaleStep:0.00}";
    }

    private string DescribeEnabledModes()
    {
        List<string> modes = [];
        if (gizmoTool.IsTranslateEnabled)
        {
            modes.Add("Move");
        }
        if (gizmoTool.IsRotateEnabled)
        {
            modes.Add("Rotate");
        }
        if (gizmoTool.IsScaleEnabled)
        {
            modes.Add("Scale");
        }
        return modes.Count == 0
            ? "All gizmo handles hidden"
            : $"Enabled overlay handles: {string.Join(" + ", modes)}";
    }

    private void UpdatePose()
    {
        TransformGizmoPose pose = TransformGizmoPose.Capture(TargetCone.CoreNode);
        PoseLabel.Text =
            $"Position  {Format(pose.Position)}\n" +
            $"Rotation  ({pose.Rotation.X,6:0.00}, {pose.Rotation.Y,6:0.00}, " +
            $"{pose.Rotation.Z,6:0.00}, {pose.Rotation.W,6:0.00})\n" +
            $"Scale     {Format(pose.Scale)}";
    }

    private static string Format(Vector3 value) =>
        $"({value.X,6:0.00}, {value.Y,6:0.00}, {value.Z,6:0.00})";
}

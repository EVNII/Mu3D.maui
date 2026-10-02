using System.Diagnostics;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Selection;

namespace Mu3D.GalleryApp.Pages;

public partial class PointerPenInputPage
{
    private static readonly long HoverRaycastIntervalTicks = Stopwatch.Frequency / 60;
    private readonly IPenTipShadowPreview pencilTipPreview;
    private bool pencilHoverCapabilityDetected;
    private long lastHoverRaycastTimestamp;

    private void InitializePencilTipPreview()
    {
        ApplyPencilShadowSettings();
        UpdatePencilTipPreviewLabel();
    }

    private void ApplyPencilShadowSettings()
    {
        pencilTipPreview.ShadowCoefficient = (float)PencilShadowCoefficientSlider.Value;
        pencilTipPreview.BlurDecayCoefficient = (float)PencilBlurDecaySlider.Value;
        pencilTipPreview.ShadowDecayCoefficient = (float)PencilShadowDecaySlider.Value;
        UpdatePencilShadowSettingLabels();
    }

    private void UpdatePencilShadowSettingLabels()
    {
        PencilShadowCoefficientLabel.Text =
            $"Shadow coefficient · {PencilShadowCoefficientSlider.Value:F2}";
        PencilBlurDecayLabel.Text =
            $"Blur falloff coefficient · {PencilBlurDecaySlider.Value:F2} px/mm";
        PencilShadowDecayLabel.Text =
            $"Shadow falloff coefficient · {PencilShadowDecaySlider.Value:F3} /mm";
    }

    private void ObservePencilHoverCapability(ApplicationPointerSample sample)
    {
        if (pencilHoverCapabilityDetected ||
            sample.DeviceKind != ApplicationPointerDeviceKind.Pen ||
            sample.HoverDistanceNormalized is null)
        {
            return;
        }

        pencilHoverCapabilityDetected = true;
        PencilTipPreviewSwitch.IsEnabled = true;
        PencilTipPreviewSwitch.IsToggled = sample.IsHoverToolPreviewPreferred ?? true;
        UpdatePencilTipPreviewLabel();
    }

    private void UpdatePencilTipPreview(ApplicationPointerSample sample)
    {
        bool isHover =
            sample.Phase == ApplicationPointerPhase.Hovered &&
            sample.HoverDistanceNormalized is not null;
        bool isContact = sample.Phase is
            ApplicationPointerPhase.Pressed or
            ApplicationPointerPhase.Moved or
            ApplicationPointerPhase.Released;
        if (sample.DeviceKind != ApplicationPointerDeviceKind.Pen ||
            !pencilHoverCapabilityDetected ||
            (!isHover && !isContact) ||
            !PencilTipPreviewSwitch.IsToggled)
        {
            HidePencilTipPreview();
            return;
        }

        long timestamp = Stopwatch.GetTimestamp();
        if ((sample.Phase is
                ApplicationPointerPhase.Hovered or ApplicationPointerPhase.Moved) &&
            timestamp - lastHoverRaycastTimestamp < HoverRaycastIntervalTicks)
        {
            return;
        }
        lastHoverRaycastTimestamp = timestamp;

        Scene? scene = SceneView.Scene;
        if (scene is null ||
            SceneView.Camera is not PerspectiveCamera camera ||
            SceneView.PixelWidth == 0 ||
            SceneView.PixelHeight == 0)
        {
            HidePencilTipPreview();
            return;
        }
        bool hasHit = raycaster.TryHitClosest(
            scene,
            camera,
            SceneView.PixelWidth,
            SceneView.PixelHeight,
            sample.PositionPixels,
            out SceneRaycastIntersection hit,
            raycastMeshFilter);
        SceneRaycastIntersection? projectionReference = hasHit ? hit : null;

        pencilTipPreview.Update(
            new PenShadowInput(sample.PositionPixels, sample.TiltXDegrees, sample.TiltYDegrees,
                sample.HoverDistanceNormalized, sample.Phase == ApplicationPointerPhase.Hovered),
            projectionReference,
            camera,
            SceneView.PixelWidth,
            SceneView.PixelHeight,
            SceneView.Height);
        SceneView.InvalidateScene();
    }

    private void HidePencilTipPreview()
    {
        if (!pencilTipPreview.IsVisible)
        {
            return;
        }
        pencilTipPreview.Hide();
        SceneView.InvalidateScene();
    }

    private void OnPencilTipPreviewToggled(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (pencilTipPreview is null)
        {
            return;
        }
        if (!e.Value)
        {
            HidePencilTipPreview();
        }
        UpdatePencilTipPreviewLabel();
    }

    private void OnPencilShadowCoefficientChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        if (pencilTipPreview is null)
        {
            return;
        }
        pencilTipPreview.ShadowCoefficient = (float)e.NewValue;
        PencilShadowCoefficientLabel.Text = $"Shadow coefficient · {e.NewValue:F2}";
        SceneView.InvalidateScene();
    }

    private void OnPencilBlurDecayChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        if (pencilTipPreview is null)
        {
            return;
        }
        pencilTipPreview.BlurDecayCoefficient = (float)e.NewValue;
        PencilBlurDecayLabel.Text = $"Blur falloff coefficient · {e.NewValue:F2} px/mm";
        SceneView.InvalidateScene();
    }

    private void OnPencilShadowDecayChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        if (pencilTipPreview is null)
        {
            return;
        }
        pencilTipPreview.ShadowDecayCoefficient = (float)e.NewValue;
        PencilShadowDecayLabel.Text = $"Shadow falloff coefficient · {e.NewValue:F3} /mm";
        SceneView.InvalidateScene();
    }

    private void UpdatePencilTipPreviewLabel()
    {
        PencilTipPreviewLabel.Text = !pencilHoverCapabilityDetected
            ? "Physical Apple Pencil shadow · awaiting supported iPad hover"
            : PencilTipPreviewSwitch.IsToggled
                ? "Physical Apple Pencil shadow · display-normal surface"
                : "Physical Apple Pencil shadow · off";
    }
}

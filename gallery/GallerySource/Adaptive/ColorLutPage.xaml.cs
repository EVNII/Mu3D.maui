using Mu3D.Gallery.Controls;
using Mu3D.GalleryApp.Examples;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;

namespace Mu3D.Gallery.Pages;

/// <summary>Demonstrates explicit GPU color-table transforms through the automatic draw boundary.</summary>
public partial class ColorLutPage : ContentPage, IGalleryPageActivation
{
    private readonly ColorLutExample example = new();
    private bool initialized;
    private bool navigationActive;

    /// <summary>Initializes the focused GPU color-table example.</summary>
    public ColorLutPage()
    {
        InitializeComponent();
        initialized = true;
    }

    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (navigationActive == active) return;
        navigationActive = active;
        if (active) OnPageLoaded(this, EventArgs.Empty);
        else OnPageUnloaded(this, EventArgs.Empty);
    }

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
        if (!navigationActive) return;
        if (e.Target.Descriptor.Format != GraphicsTextureFormat.Rgba16Float ||
            e.OutputPlan.Output.Encoding != ColorEncoding.ExtendedSrgbLinear ||
            e.OutputPlan.Output.DynamicRange != OutputDynamicRange.Hdr)
        {
            DisposeResources();
            using GraphicsCommandEncoder encoder = e.Device.CreateCommandEncoder("unsupported color LUT clear");
            using (encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(new GraphicsRenderPassColorAttachment(
                e.Target, clearColor: new GraphicsClearColor(0, 0, 0, 1))))) { }
            using GraphicsCommandBuffer commands = encoder.Finish();
            e.Device.Queue.Submit(commands);
            StatusLabel.Text = $"Unsupported output: {e.OutputPlan.Output.Format}, {e.OutputPlan.Output.Encoding}. " +
                "This example requires negotiated FP16 extended-linear HDR; no SDR conversion is applied.";
            return;
        }

        example.Enabled = LutEnabled.IsToggled;
        example.ExposureStops = (float)ExposureSlider.Value;
        StatusLabel.Text = example.Draw(e.Device, e.Target);
        ExposureLabel.Text = $"LUT exposure: {ExposureSlider.Value:+0.0;-0.0;0.0} stops";
    }

    private void OnLutToggled(object? sender, ToggledEventArgs e)
    {
        if (initialized) SurfaceView.InvalidateSurface();
    }

    private void OnExposureChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!initialized) return;
        example.ExposureStops = (float)e.NewValue;
        SurfaceView.InvalidateSurface();
    }

    private void OnPresentationSessionChanged(object? sender, PresentationSessionChangedEventArgs e) => DisposeResources();

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        StatusLabel.Text = $"Color LUT {e.Operation} failed: {e.Exception.Message}";
        DisposeResources();
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        SurfaceView.InvalidateSurface();
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        DisposeResources();
    }

    private void DisposeResources() => example.Dispose();
}

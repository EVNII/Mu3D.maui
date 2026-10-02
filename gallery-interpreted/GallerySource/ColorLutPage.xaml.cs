using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.GalleryApp.Examples;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Demonstrates explicit GPU color-table transforms through the automatic draw boundary.</summary>
public partial class ColorLutPage : ContentPage
{
    private readonly ColorLutExample example = new();
    private bool initialized;

    /// <summary>Initializes the focused GPU color-table example.</summary>
    public ColorLutPage()
    {
        InitializeComponent();
        initialized = true;
    }

    private void OnSurfaceDraw(object? sender, SurfaceDrawEventArgs e)
    {
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

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        SurfaceView.InvalidateSurface();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        DisposeResources();
        base.OnDisappearing();
    }

    private void DisposeResources() => example.Dispose();
}

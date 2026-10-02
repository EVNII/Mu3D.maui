using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Rendering;

namespace Mu3D.Gallery.Pages;

/// <summary>Demonstrates explicit GPU color-table transforms through the automatic draw boundary.</summary>
public partial class ColorLutPage : ContentPage
{
    private GraphicsDevice? resourceDevice;
    private GraphicsTexture? source;
    private GraphicsShaderModule? patternShader;
    private GraphicsRenderPipeline? patternPipeline;
    private LinearRgbLutGpuTransform? identity;
    private LinearRgbLutGpuTransform? adjusted;
    private bool initialized;

    /// <summary>Initializes the focused GPU color-table example.</summary>
    public ColorLutPage()
    {
        InitializeComponent();
        initialized = true;
        // AdaptiveShell.Maui 0.1.x hosts content pages without raising Appearing/Disappearing;
        // Loaded/Unloaded fire as the hosted page enters and leaves the window's visual tree.
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
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

        EnsureResources(e.Device, e.Width, e.Height);
        EnsureAdjustedTransform();
        using (GraphicsCommandEncoder encoder = e.Device.CreateCommandEncoder("HDR LUT test-pattern encoder"))
        {
            using (GraphicsRenderPassEncoder pass = encoder.BeginRenderPass(new GraphicsRenderPassDescriptor(
                new GraphicsRenderPassColorAttachment(source!), "HDR LUT source pattern")))
            {
                pass.SetPipeline(patternPipeline!);
                pass.Draw(3);
            }
            using GraphicsCommandBuffer commands = encoder.Finish();
            e.Device.Queue.Submit(commands);
        }
        (LutEnabled.IsToggled ? adjusted! : identity!).Apply(source!, StandardColorSpaces.LinearSrgb,
            e.Target, StandardColorSpaces.LinearSrgb);
        ExposureLabel.Text = $"LUT exposure: {ExposureSlider.Value:+0.0;-0.0;0.0} stops";
        StatusLabel.Text = $"{e.Width} × {e.Height} · FP32 17³ table → FP16 extended-linear sRGB\n" +
            (LutEnabled.IsToggled ? "Color mix and exposure enabled." : "Identity table selected.") +
            " Input domain −1 to 4; Clamp policy explicitly selected.";
    }

    private void EnsureResources(GraphicsDevice device, uint width, uint height)
    {
        if (ReferenceEquals(resourceDevice, device) && source?.Descriptor.Size == new GraphicsExtent3D(width, height))
        {
            return;
        }
        DisposeResources();
        // Bound this sample's one additional FP16 image; no CPU pixel copy or size polling is used.
        if ((ulong)width * height * 8 > 128UL * 1024 * 1024)
        {
            throw new InvalidOperationException("The example's HDR source texture exceeds its 128 MiB budget.");
        }
        resourceDevice = device;
        try
        {
            source = device.CreateTexture(new GraphicsTextureDescriptor(new GraphicsExtent3D(width, height),
                GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.TextureBinding | GraphicsTextureUsage.RenderAttachment,
                label: "Gallery linear-sRGB HDR LUT input"));
            patternShader = device.CreateShaderModule(new GraphicsShaderModuleDescriptor(PatternShader, "Gallery HDR LUT pattern"));
            patternPipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescriptor(
                patternShader, "vs_main", patternShader, "fs_main", GraphicsTextureFormat.Rgba16Float,
                label: "Gallery HDR LUT source pattern"));
            identity = new LinearRgbLutGpuTransform(device, BakeTable(0f, colorMix: false), PixelPrecision.Float16);
        }
        catch
        {
            DisposeResources();
            throw;
        }
    }

    private void EnsureAdjustedTransform()
    {
        adjusted ??= new LinearRgbLutGpuTransform(resourceDevice!,
            BakeTable((float)ExposureSlider.Value, colorMix: true), PixelPrecision.Float16);
    }

    private static LinearRgbLut3D BakeTable(float exposureStops, bool colorMix)
    {
        float exposure = MathF.Pow(2f, exposureStops);
        return LinearRgbLut3D.Bake(17, new Vector3(-1f), new Vector3(4f),
            StandardColorSpaces.LinearSrgb, StandardColorSpaces.LinearSrgb,
            color => new LinearRgba(
                (colorMix ? color.Red * 1.05f + color.Green * 0.05f : color.Red) * exposure,
                (colorMix ? color.Red * 0.08f + color.Green * 0.95f : color.Green) * exposure,
                (colorMix ? color.Blue * 0.9f + color.Green * 0.03f : color.Blue) * exposure,
                color.Alpha, StandardColorSpaces.LinearSrgb),
            ColorLutRangePolicy.Clamp);
    }

    private void OnLutToggled(object? sender, ToggledEventArgs e)
    {
        if (initialized) SurfaceView.InvalidateSurface();
    }

    private void OnExposureChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!initialized) return;
        adjusted?.Dispose();
        adjusted = null;
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

    private void DisposeResources()
    {
        adjusted?.Dispose(); adjusted = null;
        identity?.Dispose(); identity = null;
        patternPipeline?.Dispose(); patternPipeline = null;
        patternShader?.Dispose(); patternShader = null;
        source?.Dispose(); source = null;
        resourceDevice = null;
    }

    private const string PatternShader = """
        struct VertexOutput {
            @builtin(position) position: vec4f,
            @location(0) uv: vec2f,
        }
        @vertex fn vs_main(@builtin(vertex_index) index: u32) -> VertexOutput {
            var positions = array(vec2f(-1.0, -1.0), vec2f(3.0, -1.0), vec2f(-1.0, 3.0));
            let p = positions[index];
            var output: VertexOutput;
            output.position = vec4f(p, 0.0, 1.0);
            output.uv = vec2f((p.x + 1.0) * 0.5, (1.0 - p.y) * 0.5);
            return output;
        }
        @fragment fn fs_main(input: VertexOutput) -> @location(0) vec4f {
            let value = input.uv.x * 4.0;
            var rgb = vec3f(value);
            if (input.uv.y > 0.5 && input.uv.y <= 0.6667) { rgb = vec3f(value, 0.0, 0.0); }
            if (input.uv.y > 0.6667 && input.uv.y <= 0.8333) { rgb = vec3f(0.0, value, 0.0); }
            if (input.uv.y > 0.8333) { rgb = vec3f(0.0, 0.0, value); }
            return vec4f(rgb, 1.0);
        }
        """;
}

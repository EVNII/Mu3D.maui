using Mu3D.GalleryApp.Examples;
using Mu3D.Samples;
using Mu3D.Formats.MaterialX;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Compares one XAML OpenPBR scene in raster, hybrid, split-sum and two path-tracing modes.</summary>
public partial class OpenPbrPage : ContentPage
{
    private OpenPbrGalleryExample? example;
    private OpenPbrRenderPass? renderPass => example?.Transport;
    private VsyncFrameSource? vsyncLoop;
    private bool isPageVisible;
    private int presentedFrames;
    private OpenPbrParameterEditor? parameterEditor;

    /// <summary>Initializes the focused OpenPBR example.</summary>
    public OpenPbrPage()
    {
        InitializeComponent();
        SceneView.PrepareGraphicsAsync = PrepareGraphicsAsync;
        ParameterHost.Content = parameterEditor = new OpenPbrParameterEditor(Surface, ApplySurface);
        EnsureRenderPass();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        EnsureRenderPass();
        SceneView.SceneContent = DeclaredScene;
        SceneView.InvalidateScene();
        RefreshRendering();
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        isPageVisible = false;
        StopRendering();
        // Detach the declared scene before returning to the default pipeline, which deliberately
        // rejects OpenPBR materials. The application owns the pass; the control owns its surface.
        SceneView.SceneContent = null;
        SceneView.RenderPipeline = null;
        example?.Dispose();
        example = null;
        base.OnDisappearing();
    }

    private async Task PrepareGraphicsAsync(GraphicsDevice device, GraphicsTextureFormat format, CancellationToken cancellationToken)
    {
        EnsureRenderPass();
        OpenPbrRenderPass preparing = renderPass!;
        ModePicker.IsEnabled = false;
        StatusLabel.Text = "Preparing OpenPBR shaders… You can leave this page while preparation runs.";
        try
        {
            await preparing.PrepareAsync(device,
                SceneView.DisplayTransform is null ? format : GraphicsTextureFormat.Rgba16Float, cancellationToken);
            if (ReferenceEquals(renderPass, preparing) && isPageVisible)
                StatusLabel.Text = "OpenPBR ready.";
        }
        finally
        {
            if (ReferenceEquals(renderPass, preparing) && !cancellationToken.IsCancellationRequested)
                ModePicker.IsEnabled = true;
        }
    }

    private void EnsureRenderPass()
    {
        example ??= new OpenPbrGalleryExample(DeclaredScene.Scene,
            (PerspectiveCamera)DeclaredScene.Camera!.Camera, Material.OpenPbrMaterial);
        example.Transport.Mode = SelectedMode;
        SceneView.RenderPipeline = example.Transport.Pipeline;
        ApplyShadowSettings();
    }

    private void OnShadowSettingsChanged(object? sender, EventArgs e)
    {
        if (renderPass is null || ShadowNormalBiasSlider is null) return;
        ApplyShadowSettings();
        SceneView.InvalidateScene();
        UpdateSamples();
    }

    private void ApplyShadowSettings()
    {
        if (renderPass is null) return;
        renderPass.DirectionalShadowsEnabled = ShadowSwitch.IsToggled;
        renderPass.DirectionalShadowMapSize = (uint)(512 << Math.Clamp(ShadowSizePicker.SelectedIndex, 0, 3));
        renderPass.DirectionalShadowPcfRadius = Math.Clamp(ShadowFilterPicker.SelectedIndex, 0, 2);
        renderPass.DirectionalShadowDepthBias = (float)ShadowBiasSlider.Value;
        renderPass.DirectionalShadowNormalBias = (float)ShadowNormalBiasSlider.Value;
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        if (renderPass is null) return;
        // Leave the native Picker selection transaction before rejecting a mode.
        Dispatcher.Dispatch(() =>
        {
            if (!isPageVisible || renderPass is null) return;
            string? error = ModeError(Surface.ToSurface(), SelectedMode);
            if (error is not null)
            {
                ModePicker.SelectedIndex = (int)renderPass.Mode;
                StatusLabel.Text = error;
                return;
            }
            if (renderPass.Mode == SelectedMode) return;
            renderPass.Mode = SelectedMode;
            renderPass.ResetAccumulation();
            SceneView.InvalidateScene();
            UpdateSamples();
        });
    }

    private static string? ModeError(OpenPbrSurface surface, OpenPbrRenderMode mode) =>
        OpenPbrGalleryExample.ModeError(surface, mode);

    private OpenPbrRenderMode SelectedMode => ModePicker.SelectedIndex is >= 0 and <= 4
        ? (OpenPbrRenderMode)ModePicker.SelectedIndex : OpenPbrRenderMode.Raster;

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        if (SceneView is null || DisplayPicker.SelectedIndex < 0) return;
        if (DisplayPicker.SelectedIndex == 0)
        {
            SceneView.DisplayTransform = null;
            return;
        }
        ColorView3D view = (ColorView3D)Resources["DisplayView"];
        view.Preset = OpenPbrGalleryExample.ViewPreset(DisplayPicker.SelectedIndex);
        SceneView.DisplayTransform = view;
    }

    private void OnRunChanged(object? sender, ToggledEventArgs e)
    {
        RefreshRendering();
        if (SamplesLabel is not null) UpdateSamples();
    }

    private void RefreshRendering()
    {
        if (!isPageVisible || RunSwitch is null || !RunSwitch.IsToggled || SceneView.Renderer is null)
        {
            StopRendering();
            return;
        }
        if (vsyncLoop is not null) return;
        // The Toolkit frame source follows the display: on Apple platforms an owned CADisplayLink
        // tracks the screen's real ceiling (ProMotion 120 Hz), elsewhere the MAUI VSync animation
        // service already follows the display.
        vsyncLoop = new VsyncFrameSource(() => SceneView.InvalidateScene());
        if (!vsyncLoop.Start(SceneView))
        {
            vsyncLoop.Dispose();
            vsyncLoop = null;
            StatusLabel.Text = "VSync frame source unavailable.";
        }
    }

    private void StopRendering()
    {
        vsyncLoop?.Dispose();
        vsyncLoop = null;
    }

    private void OnRendererChanged(object? sender, SceneRendererChangedEventArgs e) => RefreshRendering();

    private void OnPresentationSessionChanged(object? sender, PresentationSessionChangedEventArgs e)
    {
        if (e.Session is not null) return;
        StopRendering();
        example?.Dispose();
        example = null;
        if (isPageVisible) EnsureRenderPass();
    }

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        if (isPageVisible && e.Status is PresentationSurfaceFrameStatus.PresentedOptimal or PresentationSurfaceFrameStatus.PresentedSuboptimal &&
            (presentedFrames++ % 16 == 0 || RunSwitch is { IsToggled: false })) UpdateSamples();
    }

    private void UpdateSamples()
    {
        SamplesLabel.Text = renderPass is null ? "Waiting for renderer." : OpenPbrGalleryExample.SamplingSummary(renderPass);
#if IOS || MACCATALYST
        if (vsyncLoop is { IsRunning: true } loop)
        {
            SamplesLabel.Text +=
                $" · vsync {loop.MeasuredTicksPerSecond:F0}/s on {loop.ScreenMaximumFramesPerSecond} Hz screen";
#if IOS
            // The ProMotion clamp key only exists for iPhone; Mac Catalyst is never clamped.
            SamplesLabel.Text +=
                $" · ProMotion key {(ProMotionOptInDeployed ? "deployed" : "MISSING — stale bundle, rebuild")}";
#endif
        }
#endif
    }

#if IOS
    // Reads the deployed bundle at runtime, which also catches stale incremental deployments.
    private static bool ProMotionOptInDeployed =>
        Foundation.NSBundle.MainBundle.ObjectForInfoDictionary("CADisableMinimumFrameDurationOnPhone")
            is Foundation.NSNumber flag && flag.BoolValue;
#endif

    private async void OnLoadClicked(object? sender, EventArgs e)
    {
        try
        {
            using Stream stream = await FileSystem.OpenAppPackageFileAsync("MaterialX/blue-metal.mtlx");
            MaterialXOpenPbrDocument document = await MaterialXOpenPbrSerializer.ImportAsync(stream);
            ApplySurface(document.Surfaces[0].Surface);
            Report("Loaded blue-metal.mtlx");
        }
        catch (Exception error) { StatusLabel.Text = error.Message; }
    }

    private void OnTextureClicked(object? sender, EventArgs e)
    {
        ApplySurface(new OpenPbrSurface { Graph = OpenPbrTextureExample.Create(.3f),
            SpecularRoughness = .3f, BaseMetalness = .25f, CoatWeight = .4f, CoatRoughness = .2f });
        Report("Color, roughness and tangent-normal textures loaded");
    }

    private void OnRoundTripClicked(object? sender, EventArgs e)
    {
        try
        {
            EnsureRenderPass();
            long bytes = example!.RoundTrip();
            ApplySurface(example.Surface);
            Report($"Exported and reimported {bytes:N0} XML bytes");
        }
        catch (Exception error) { StatusLabel.Text = error.Message; }
    }

    private void ApplySurface(OpenPbrSurface surface)
    {
        if (ModeError(surface, SelectedMode) is { } error) throw new NotSupportedException(error);
        Surface.LoadSurface(surface);
        parameterEditor?.Refresh();
        renderPass?.ResetAccumulation();
        SceneView.InvalidateScene();
        Report("Material updated");
    }
    private void Report(string action) => StatusLabel.Text = $"{action}. XAML and .mtlx retain the same OpenPBR inputs.";

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        StopRendering();
        RunSwitch.IsToggled = false;
        StatusLabel.Text = e.Exception.Message;
    }
}

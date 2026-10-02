using Mu3D.Color;
using Mu3D.GalleryApp.Pages;
using Mu3D.Samples;
using Mu3D.Formats.MaterialX;
using Mu3D.Gallery.Controls;
using Mu3D.Maui.Toolkit.Controls;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

namespace Mu3D.Gallery.Pages;

/// <summary>Compares one XAML OpenPBR scene in raster, hybrid and two path-tracing modes.</summary>
public partial class OpenPbrPage : ContentPage, IGalleryPageActivation
{
    private OpenPbrRenderPass? renderPass;
    private VsyncFrameSource? vsyncLoop;
    private bool isPageVisible;
    private int presentedFrames;
    private OpenPbrParameterEditor? parameterEditor;

    /// <summary>Initializes the focused OpenPBR example.</summary>
    public OpenPbrPage()
    {
        InitializeComponent();
        Host.SceneView.PrepareGraphicsAsync = PrepareGraphicsAsync;
        ParameterHost.Content = parameterEditor = new OpenPbrParameterEditor(Surface, ApplySurface);
        Host.SceneView.RendererChanged += OnRendererChanged;
        Host.SceneView.PresentationSessionChanged += OnPresentationSessionChanged;
        Host.SceneView.FramePresented += OnFramePresented;
        EnsureRenderPass();
    }

    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (isPageVisible == active) return;
        if (active) OnPageLoaded(this, EventArgs.Empty);
        else OnPageUnloaded(this, EventArgs.Empty);
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        _ = sender;
        isPageVisible = true;
        EnsureRenderPass();
        Host.SceneView.SceneContent = DeclaredScene;
        Host.SceneView.InvalidateScene();
        RefreshRendering();
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        _ = sender;
        isPageVisible = false;
        StopRendering();
        // Detach the declared scene before returning to the default pipeline, which deliberately
        // rejects OpenPBR materials. The application owns the pass; the control owns its surface.
        Host.SceneView.SceneContent = null;
        Host.SceneView.RenderPipeline = null;
        renderPass?.Dispose();
        renderPass = null;
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
                Host.SceneView.DisplayTransform is null ? format : GraphicsTextureFormat.Rgba16Float, cancellationToken);
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
        renderPass ??= new OpenPbrRenderPass
        {
            Mode = SelectedMode,
            EnvironmentRadiance = new LinearRgba(0.08f, 0.08f, 0.08f, 1f, StandardColorSpaces.AcesCg),
            BackgroundAlpha = 1f,
        };
        Host.SceneView.RenderPipeline = renderPass.Pipeline;
        ApplyShadowSettings();
    }

    private void OnShadowSettingsChanged(object? sender, EventArgs e)
    {
        if (renderPass is null || ShadowNormalBiasSlider is null) return;
        ApplyShadowSettings();
        Host.SceneView.InvalidateScene();
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
            Host.SceneView.InvalidateScene();
            UpdateSamples();
        });
    }

    private static string? ModeError(OpenPbrSurface surface, OpenPbrRenderMode mode)
    {
        if (mode is not (OpenPbrRenderMode.Raster or OpenPbrRenderMode.Fast)) return null;
        float transmission = surface.Graph?.Maximum(OpenPbrInput.TransmissionWeight, surface.TransmissionWeight) ?? surface.TransmissionWeight;
        float opacity = surface.Graph?.Minimum(OpenPbrInput.GeometryOpacity, surface.GeometryOpacity) ?? surface.GeometryOpacity;
        float subsurface = surface.Graph?.Maximum(OpenPbrInput.SubsurfaceWeight, surface.SubsurfaceWeight) ?? surface.SubsurfaceWeight;
        return transmission > 0 || opacity < 1 || (mode == OpenPbrRenderMode.Raster && subsurface > 0)
            ? $"{mode} cannot render this transmission, opacity or subsurface setting. Select Hybrid, Interactive or Reference first."
            : null;
    }
    private OpenPbrRenderMode SelectedMode => ModePicker.SelectedIndex is >= 0 and <= 4
        ? (OpenPbrRenderMode)ModePicker.SelectedIndex : OpenPbrRenderMode.Raster;

    private void OnRunChanged(object? sender, ToggledEventArgs e)
    {
        RefreshRendering();
        if (SamplesLabel is not null) UpdateSamples();
    }

    private void RefreshRendering()
    {
        if (!isPageVisible || RunSwitch is null || !RunSwitch.IsToggled || Host.SceneView.Renderer is null)
        {
            StopRendering();
            return;
        }
        if (vsyncLoop is not null) return;
        vsyncLoop = new VsyncFrameSource(() => Host.SceneView.InvalidateScene());
        if (!vsyncLoop.Start(Host.SceneView))
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
        renderPass?.Dispose();
        renderPass = null;
        if (isPageVisible) EnsureRenderPass();
    }

    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        if (isPageVisible && e.Status is PresentationSurfaceFrameStatus.PresentedOptimal or PresentationSurfaceFrameStatus.PresentedSuboptimal &&
            (presentedFrames++ % 16 == 0 || RunSwitch is { IsToggled: false })) UpdateSamples();
    }

    private void UpdateSamples()
    {
        SamplesLabel.Text = renderPass?.Mode switch
        {
            OpenPbrRenderMode.Reference => $"Reference: {renderPass.AccumulatedSamples:N0} samples per pixel, up to 32 path events.",
            OpenPbrRenderMode.Hybrid => $"Hybrid: {renderPass.AccumulatedSamples:N0} samples, four path events; shadows, reflection and indirect light. " +
                (renderPass.UsesPrimaryRayFallback ? "Primary rays preserve near-plane / medium boundaries." : "Raster primary visibility; secondary sampling can be noisy."),
            OpenPbrRenderMode.Raster => $"Raster: direct light + environment; directional shadows {(renderPass.DirectionalShadowsEnabled ? "on" : "off")}; no indirect transport.",
            OpenPbrRenderMode.Fast => FastSummary(renderPass),
            _ => "Interactive: half resolution, up to four path events per frame.",
        };
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

    private static string FastSummary(OpenPbrRenderPass pass)
    {
        if (pass.FastApproximations.Count == 0)
            return $"Fast: directional shadows {(pass.DirectionalShadowsEnabled ? "on" : "off")}; no indirect transport; no compiled surfaces yet.";
        OpenPbrFastApproximationKinds kinds = pass.FastApproximations.Aggregate(
            OpenPbrFastApproximationKinds.None, (all, a) => all | a.Kinds);
        int textures = pass.FastApproximations.Sum(a => a.BakedTextureCount);
        long bytes = pass.FastApproximations.Sum(a => a.BakedBytes);
        List<string> approximations = [];
        if (kinds.HasFlag(OpenPbrFastApproximationKinds.FuzzEnvironmentCharlieChain)) approximations.Add("fuzz→Charlie chain");
        if (kinds.HasFlag(OpenPbrFastApproximationKinds.CoatEnvironmentBaseGgxChain)) approximations.Add("coat→GGX chain");
        if (kinds.HasFlag(OpenPbrFastApproximationKinds.SubsurfaceDropped)) approximations.Add("subsurface dropped");
        if (kinds.HasFlag(OpenPbrFastApproximationKinds.ThinFilmDropped)) approximations.Add("thin film dropped");
        string baked = kinds.HasFlag(OpenPbrFastApproximationKinds.BakedGraphTextures)
            ? $"{textures} baked graph textures ({bytes / 1024.0:F0} KiB)" : "no baked graph textures";
        return $"Fast: directional shadows {(pass.DirectionalShadowsEnabled ? "on" : "off")}; no indirect transport; split-sum IBL; {baked}; " +
            $"approximations: {(approximations.Count > 0 ? string.Join(", ", approximations) : "none beyond split-sum")}.";
    }

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
            MaterialXOpenPbrDocument document = new(
                [new MaterialXOpenPbrSurface("surface", Surface.ToSurface())],
                [new MaterialXSurfaceMaterial("material", "surface")]);
            using MemoryStream stream = new();
            MaterialXOpenPbrSerializer.Export(stream, document);
            stream.Position = 0;
            MaterialXOpenPbrDocument imported = MaterialXOpenPbrSerializer.Import(stream, new() { TextureResolver = OpenPbrTextureExample.Resolve });
            ApplySurface(imported.Surfaces[0].Surface);
            Report($"Exported and reimported {stream.Length:N0} XML bytes");
        }
        catch (Exception error) { StatusLabel.Text = error.Message; }
    }

    private void ApplySurface(OpenPbrSurface surface)
    {
        if (ModeError(surface, SelectedMode) is { } error) throw new NotSupportedException(error);
        Surface.LoadSurface(surface);
        parameterEditor?.Refresh();
        renderPass?.ResetAccumulation();
        Host.SceneView.InvalidateScene();
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

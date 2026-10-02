using System.Diagnostics;
using System.Numerics;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Formats.Gltf;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Native.Ktx;
using Mu3D.Native.UltraHdr;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Physically exercises official Khronos glTF sample assets.</summary>
public partial class ModelLabPage : ContentPage
{
    private static readonly ModelSpec[] Models = ModelLabCatalog.Models;
    private static readonly IEncodedImageDecoder ImageDecoder = new JpegImageDecoder();
    private IPresentationSurfaceSession? session;
    private ModelLabPresenter? presenter;
    private ModelSpec? activeModel;
    private ModelResourceCounts activeResourceCounts;
    private ModelLoadMetrics activeLoadMetrics;
    private CancellationTokenSource lifetime = new();
    private Task? configurationTask;
    private int configurationGeneration;
    private float[] animationDurations = [];
    private bool applyAllAnimations;
    private bool isPlaying;
    private bool updatingTimeline;
    private double playbackTimeSeconds;
    private long playbackTimestamp = Stopwatch.GetTimestamp();
    private long timelineUiTimestamp;

    /// <summary>Initializes the Khronos model laboratory.</summary>
    public ModelLabPage()
    {
        InitializeComponent();
        ModelPicker.ItemsSource = Models.Select(static model => model.DisplayName).ToArray();
        ModelPicker.SelectedIndex = 0;
        MaterialVariantPicker.ItemsSource = new[] { "Default material" };
        MaterialVariantPicker.SelectedIndex = 0;
        MaterialVariantPicker.IsEnabled = false;
        AnimationPicker.ItemsSource = new[] { "No animation" };
        AnimationPicker.SelectedIndex = 0;
        AnimationPicker.IsEnabled = false;
        LayerPicker.ItemsSource = Enum.GetNames<SceneRenderLayer>();
        LayerPicker.SelectedIndex = (int)SceneRenderLayer.Beauty;
        UpdateLayerHint(SceneRenderLayer.Beauty);
        BloomModePicker.ItemsSource = Enum.GetNames<BloomCompositeMode>();
        BloomModePicker.SelectedIndex = (int)BloomCompositeMode.EnergyPreserving;
        SurfaceView.PresentationSessionChanged += OnPresentationSessionChanged;
        SurfaceView.SurfaceError += OnSurfaceError;
    }

    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        _ = sender;
        LoadingOverlay.IsVisible = false;
        StatusLabel.Text = $"Presentation {e.Operation} failed";
        DetailsLabel.Text = e.Exception.ToString();
    }

    private void OnPresentationSessionChanged(
        object? sender,
        PresentationSessionChangedEventArgs e)
    {
        _ = sender;
        ResetLifetime();
        DisposeRendering();
        session = e.Session;
        if (session is null)
        {
            StatusLabel.Text = "Presentation surface unavailable";
            return;
        }
        StartConfiguration(session);
    }

    private async Task ConfigureAndRunAsync(
        IPresentationSurfaceSession activeSession,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            ModelSpec model = Models[Math.Max(0, ModelPicker.SelectedIndex)];
            LoadingLabel.Text = $"Loading {model.DisplayName}…";
            LoadingOverlay.IsVisible = true;
            GateLabel.Text = model.GateDescription;
            StatusLabel.Text = $"Loading official Khronos {Path.GetFileName(model.PackagePath)}…";
            long resourceLoadStarted = Stopwatch.GetTimestamp();
            byte[] asset = await ReadPackageResourceAsync(model.PackagePath, cancellationToken);
            Dictionary<string, ReadOnlyMemory<byte>> externalResources =
                new(StringComparer.Ordinal);
            string packageDirectory = model.PackagePath[..(model.PackagePath.LastIndexOf('/') + 1)];
            foreach (string resource in model.ExternalResources ?? [])
            {
                externalResources.Add(
                    resource,
                    await ReadPackageResourceAsync(packageDirectory + resource, cancellationToken));
            }
            double resourceLoadMilliseconds = Stopwatch.GetElapsedTime(
                resourceLoadStarted).TotalMilliseconds;
            LoadingLabel.Text = $"Creating {model.DisplayName} GPU session…";
            cancellationToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentConfiguration(activeSession, generation, cancellationToken))
            {
                return;
            }
            LoadingLabel.Text = $"Decoding {model.DisplayName} textures…";
            long importStarted = Stopwatch.GetTimestamp();
            (GltfAsset imported, ModelResourceCounts resourceCounts) = await Task.Run(() =>
            {
                GltfImportOptions importOptions = new()
                {
                    ExternalBufferResolver = ResolveExternalResource,
                    ExternalImageResolver = ResolveExternalResource,
                    Name = model.SceneName,
                    ImageDecoder = model.UseKtx
                        ? new Ktx2ImageDecoder(
                            ImageDecoder,
                            allowBt709PrimariesForLinearData: true)
                        : ImageDecoder,
                    TextureTranscoder = model.UseKtx
                        ? new Ktx2TextureTranscoder(
                            allowBt709PrimariesForLinearData: true)
                        : null,
                    GraphicsCapabilities = model.UseKtx
                        ? activeSession.Device.Capabilities
                        : null,
                };
                GltfAsset loaded = GltfImporter.ImportAssetWithOptions(asset, importOptions);
                if (model.AddAmbientOcclusionProbe)
                {
                    ModelLabAssets.AddAmbientOcclusionProbe(loaded, model.CameraTarget);
                }
                if (model.AddDiagnosticGround)
                {
                    ModelLabAssets.AddDiagnosticGround(loaded);
                }
                return (loaded, ModelLabAssets.CountResources(loaded));

                ReadOnlyMemory<byte> ResolveExternalResource(string uri) =>
                    externalResources.TryGetValue(uri, out ReadOnlyMemory<byte> resource)
                        ? resource
                        : throw new InvalidDataException(
                            $"Packaged Model Lab resource '{uri}' was not declared by the model gate.");
            }, cancellationToken);
            ModelLoadMetrics loadMetrics = new(
                resourceLoadMilliseconds,
                Stopwatch.GetElapsedTime(importStarted).TotalMilliseconds);
            EquirectangularHdrEnvironment? referenceEnvironment = null;
            if (model.EnvironmentPath is not null)
            {
                LoadingLabel.Text = $"Preparing {model.DisplayName} reference HDRI…";
                byte[] hdrBytes = await ReadPackageResourceAsync(
                    model.EnvironmentPath,
                    cancellationToken);
                referenceEnvironment = await Task.Run(() =>
                {
                    using MemoryStream hdrStream = new(hdrBytes, writable: false);
                    return RadianceHdrReader.Read(
                        hdrStream,
                        StandardColorSpaces.LinearSrgb,
                        "Artist Workshop (Poly Haven, CC0, 1K HDR)");
                }, cancellationToken);
                await SceneRenderer.PrepareImageBasedLightingAsync(
                    referenceEnvironment,
                    cancellationToken);
            }
            LoadingLabel.Text = $"Preparing {model.DisplayName} GPU textures, mipmaps and pipelines…";
            if (!IsCurrentConfiguration(activeSession, generation, cancellationToken))
            {
                return;
            }
            AnimationPicker.ItemsSource = imported.Animations.Count == 0
                ? new[] { "No animation" }
                : model.ApplyAllAnimations
                    ? new[] { $"All {imported.Animations.Count} clips synchronized" }
                    : imported.Animations.Select((clip, index) =>
                        string.IsNullOrWhiteSpace(clip.Name) ? $"Clip {index + 1}" : clip.Name).ToArray();
            AnimationPicker.SelectedIndex = 0;
            AnimationPicker.IsEnabled = imported.Animations.Count != 0 && !model.ApplyAllAnimations;
            ConfigurePlayback(imported.Animations, model.ApplyAllAnimations);
            MaterialVariantPicker.ItemsSource = new[] { "Default material" }
                .Concat(imported.MaterialVariants.Select(static variant => variant.Name))
                .ToArray();
            MaterialVariantPicker.SelectedIndex = 0;
            MaterialVariantPicker.IsEnabled = imported.MaterialVariants.Count > 0;
            if (!IsCurrentConfiguration(activeSession, generation, cancellationToken))
            {
                return;
            }
            presenter = new ModelLabPresenter(
                activeSession,
                imported,
                model.CameraPosition,
                model.CameraTarget,
                model.ApplyAllAnimations,
                referenceEnvironment,
                model.DirectionalLightEnabled);
            presenter.RenderLayer = (SceneRenderLayer)Math.Max(0, LayerPicker.SelectedIndex);
            presenter.ShadowsEnabled = ShadowSwitch.IsToggled;
            presenter.AmbientOcclusionEnabled = AoSwitch.IsToggled;
            presenter.CameraDistanceScale = (float)CameraDistanceSlider.Value;
            presenter.CameraAzimuthDegrees = (float)CameraAzimuthSlider.Value;
            presenter.CameraElevationDegrees = (float)CameraElevationSlider.Value;
            presenter.AmbientOcclusionRadius = (float)AoRadiusSlider.Value;
            presenter.AmbientOcclusionStrength = (float)AoStrengthSlider.Value;
            presenter.BloomEnabled = BloomSwitch.IsToggled;
            presenter.BloomThreshold = (float)BloomThresholdSlider.Value;
            presenter.BloomSoftKnee = (float)BloomSoftKneeSlider.Value;
            presenter.BloomIntensity = (float)BloomIntensitySlider.Value;
            presenter.BloomRadiusPixels = (float)BloomRadiusSlider.Value;
            presenter.ClearcoatFactorScale = (float)ClearcoatSlider.Value;
            presenter.AnisotropyStrengthScale = (float)AnisotropySlider.Value;
            presenter.AnisotropyRotationOffset = (float)(
                AnisotropyRotationSlider.Value * Math.PI / 180.0);
            presenter.TransmissionFactorScale = (float)TransmissionSlider.Value;
            presenter.TransmissionIndexOfRefractionOffset = (float)IorSlider.Value;
            presenter.VolumeThicknessScale = (float)ThicknessSlider.Value;
            presenter.DispersionScale = (float)DispersionSlider.Value;
            presenter.VolumeAbsorptionStrength = (float)AbsorptionSlider.Value;
            presenter.IridescenceFactorScale = (float)IridescenceSlider.Value;
            presenter.IridescenceIndexOfRefractionOffset = (float)IridescenceIorSlider.Value;
            presenter.IridescenceThicknessScale = (float)IridescenceThicknessSlider.Value;
            presenter.SpecularFactorScale = (float)SpecularFactorSlider.Value;
            presenter.SpecularColorScale = (float)SpecularColorSlider.Value;
            presenter.BloomCompositeMode = (BloomCompositeMode)Math.Max(
                0,
                BloomModePicker.SelectedIndex);
            presenter.LightAzimuthDegrees = (float)LightAzimuthSlider.Value;
            presenter.LightElevationDegrees = (float)LightElevationSlider.Value;
            presenter.LightAngularDiameter = (float)LightSizeSlider.Value;
            activeModel = model;
            activeResourceCounts = resourceCounts;
            activeLoadMetrics = loadMetrics;
            await RunFramesAsync(
                activeSession,
                presenter,
                model,
                resourceCounts,
                loadMetrics,
                generation,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception) when (!IsCurrentConfiguration(
            activeSession,
            generation,
            cancellationToken))
        {
            // An obsolete load/render task must not tear down the replacement session.
        }
        catch (Exception exception)
        {
            LoadingOverlay.IsVisible = false;
            StatusLabel.Text = "Model Lab failed";
            DetailsLabel.Text = exception.ToString();
            DisposeRendering();
        }
    }

    private void OnLayerChanged(object? sender, EventArgs e)
    {
        _ = sender;
        if (LayerPicker.SelectedIndex < 0)
        {
            return;
        }
        SceneRenderLayer layer = (SceneRenderLayer)LayerPicker.SelectedIndex;
        UpdateLayerHint(layer);
        ModelLabPresenter? activePresenter = presenter;
        if (activePresenter is not null)
        {
            activePresenter.RenderLayer = layer;
        }
    }

    private void OnShadowToggled(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.ShadowsEnabled = e.Value;
        }
    }

    private void OnAoToggled(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.AmbientOcclusionEnabled = e.Value;
        }
    }

    private void OnCameraDistanceChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        CameraDistanceLabel.Text = $"Camera distance: {e.NewValue:0.00}x";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.CameraDistanceScale = (float)e.NewValue;
        }
    }

    private void OnCameraAzimuthChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        CameraAzimuthLabel.Text = $"Camera orbit: {e.NewValue:+0;-0;0}°";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.CameraAzimuthDegrees = (float)e.NewValue;
        }
    }

    private void OnCameraElevationChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        CameraElevationLabel.Text = $"Camera elevation: {e.NewValue:+0;-0;0}°";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.CameraElevationDegrees = (float)e.NewValue;
        }
    }

    private void OnAoRadiusChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        AoRadiusLabel.Text = $"AO radius: {e.NewValue:0.00}";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.AmbientOcclusionRadius = (float)e.NewValue;
        }
    }

    private void OnAoStrengthChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        AoStrengthLabel.Text = $"AO strength: {e.NewValue:0.00}";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.AmbientOcclusionStrength = (float)e.NewValue;
        }
    }

    private void OnBloomToggled(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.BloomEnabled = e.Value;
        }
    }

    private void OnBloomModeChanged(object? sender, EventArgs e)
    {
        _ = sender;
        if (BloomModePicker.SelectedIndex >= 0 && presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.BloomCompositeMode =
                (BloomCompositeMode)BloomModePicker.SelectedIndex;
        }
    }

    private void OnBloomThresholdChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        BloomThresholdLabel.Text = $"Bloom threshold: {e.NewValue:0.00}";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.BloomThreshold = (float)e.NewValue;
        }
    }

    private void OnBloomSoftKneeChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        BloomSoftKneeLabel.Text = $"Bloom soft knee: {e.NewValue:0.00}";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.BloomSoftKnee = (float)e.NewValue;
        }
    }

    private void OnBloomIntensityChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        BloomIntensityLabel.Text = $"Bloom intensity: {e.NewValue:0.00}";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.BloomIntensity = (float)e.NewValue;
        }
    }

    private void OnBloomRadiusChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        BloomRadiusLabel.Text = $"Bloom radius: {e.NewValue:0} px";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.BloomRadiusPixels = (float)e.NewValue;
        }
    }

    private void OnClearcoatChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        ClearcoatLabel.Text = $"Clearcoat factor: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.ClearcoatFactorScale = (float)e.NewValue;
        }
    }

    private void OnAnisotropyChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        AnisotropyLabel.Text = $"Anisotropy strength: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.AnisotropyStrengthScale = (float)e.NewValue;
        }
    }

    private void OnAnisotropyRotationChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        AnisotropyRotationLabel.Text = $"Anisotropy rotation: {e.NewValue:+0;-0;0}°";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.AnisotropyRotationOffset = (float)(e.NewValue * Math.PI / 180.0);
        }
    }

    private void OnTransmissionChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        TransmissionLabel.Text = $"Transmission: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.TransmissionFactorScale = (float)e.NewValue;
        }
    }

    private void OnIorChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        IorLabel.Text = $"IOR offset: {e.NewValue:+0.00;-0.00;0.00}";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.TransmissionIndexOfRefractionOffset = (float)e.NewValue;
        }
    }

    private void OnThicknessChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        ThicknessLabel.Text = $"Volume thickness: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.VolumeThicknessScale = (float)e.NewValue;
        }
    }

    private void OnDispersionChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        DispersionLabel.Text = $"Dispersion: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.DispersionScale = (float)e.NewValue;
        }
    }

    private void OnAbsorptionChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        AbsorptionLabel.Text = $"Volume absorption: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.VolumeAbsorptionStrength = (float)e.NewValue;
        }
    }

    private void OnIridescenceChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        IridescenceLabel.Text = $"Iridescence: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.IridescenceFactorScale = (float)e.NewValue;
        }
    }

    private void OnIridescenceIorChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        IridescenceIorLabel.Text = $"Iridescence IOR offset: {e.NewValue:+0.00;-0.00;0.00}";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.IridescenceIndexOfRefractionOffset = (float)e.NewValue;
        }
    }

    private void OnIridescenceThicknessChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        IridescenceThicknessLabel.Text = $"Film thickness: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.IridescenceThicknessScale = (float)e.NewValue;
        }
    }

    private void OnSpecularFactorChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        SpecularFactorLabel.Text = $"Specular factor: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.SpecularFactorScale = (float)e.NewValue;
        }
    }

    private void OnSpecularColorChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        SpecularColorLabel.Text = $"Specular color: {e.NewValue:0.00}× authored";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.SpecularColorScale = (float)e.NewValue;
        }
    }

    private void OnLightAzimuthChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        LightAzimuthLabel.Text = $"Light azimuth: {e.NewValue:0}°";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.LightAzimuthDegrees = (float)e.NewValue;
        }
    }

    private void OnLightElevationChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        LightElevationLabel.Text = $"Light elevation: {e.NewValue:0}°";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.LightElevationDegrees = (float)e.NewValue;
        }
    }

    private void OnLightSizeChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        LightSizeLabel.Text = $"Light angular diameter: {e.NewValue:0.000} rad";
        if (presenter is ModelLabPresenter activePresenter)
        {
            activePresenter.LightAngularDiameter = (float)e.NewValue;
        }
    }

    private async Task RunFramesAsync(
        IPresentationSurfaceSession activeSession,
        ModelLabPresenter activePresenter,
        ModelSpec model,
        ModelResourceCounts resourceCounts,
        ModelLoadMetrics loadMetrics,
        int generation,
        CancellationToken cancellationToken)
    {
        using Process process = Process.GetCurrentProcess();
        playbackTimestamp = Stopwatch.GetTimestamp();
        long sampleStart = Stopwatch.GetTimestamp();
        double submitMilliseconds = 0;
        double acquireMilliseconds = 0;
        double renderMilliseconds = 0;
        double presentMilliseconds = 0;
        double prepareMilliseconds = 0;
        double encodeMilliseconds = 0;
        double queueSubmitMilliseconds = 0;
        double cacheTrimMilliseconds = 0;
        int presentedFrames = 0;
        int totalPresentedFrames = 0;
        while (!cancellationToken.IsCancellationRequested &&
            generation == configurationGeneration &&
            ReferenceEquals(session, activeSession) &&
            ReferenceEquals(presenter, activePresenter))
        {
            float animationTime = AdvancePlaybackTime();
            AnimationWrapMode wrapMode = LoopSwitch.IsToggled
                ? AnimationWrapMode.Loop
                : AnimationWrapMode.Clamp;
            long submitStart = Stopwatch.GetTimestamp();
            PresentationSurfaceFrameStatus status = await Task.Run(
                () => activePresenter.Present(animationTime, wrapMode),
                cancellationToken);
            double currentSubmitMilliseconds = Stopwatch.GetElapsedTime(submitStart).TotalMilliseconds;
            submitMilliseconds += currentSubmitMilliseconds;
            PresentationSurfaceFrameTimings frameTimings = activeSession.LastFrameTimings;
            acquireMilliseconds += frameTimings.AcquireMilliseconds;
            renderMilliseconds += frameTimings.RenderMilliseconds;
            presentMilliseconds += frameTimings.PresentMilliseconds;
            SceneRendererFrameTimings rendererTimings = activePresenter.LastRendererFrameTimings;
            prepareMilliseconds += rendererTimings.PrepareMilliseconds;
            encodeMilliseconds += rendererTimings.EncodeMilliseconds;
            queueSubmitMilliseconds += rendererTimings.SubmitMilliseconds;
            cacheTrimMilliseconds += rendererTimings.CacheTrimMilliseconds;
            if (status is PresentationSurfaceFrameStatus.Outdated or
                PresentationSurfaceFrameStatus.Lost)
            {
                activeSession.Resize(activeSession.Width, activeSession.Height);
            }
            else if (status == PresentationSurfaceFrameStatus.Error)
            {
                throw new InvalidOperationException("wgpu reported a Model Lab frame error.");
            }
            if (status is PresentationSurfaceFrameStatus.PresentedOptimal or
                PresentationSurfaceFrameStatus.PresentedSuboptimal)
            {
                presentedFrames++;
                totalPresentedFrames++;
            }
            if (totalPresentedFrames == 1)
            {
                LoadingOverlay.IsVisible = false;
                StatusLabel.Text = $"Official {model.DisplayName} rendered — {status}";
                DetailsLabel.Text =
                    $"Asset: Khronos {Path.GetFileName(model.PackagePath)}\n" +
                    model.Details + "\n" +
                    $"Ignored optional extensions: " +
                    $"{(activePresenter.IgnoredOptionalExtensions.Count == 0 ? "none" : string.Join(", ", activePresenter.IgnoredOptionalExtensions))}\n" +
                    $"Compressed material formats: {resourceCounts.CompressedFormats}\n" +
                    $"KTX2 data DFD: " +
                    $"{(model.UseKtx ? "BT709+linear compatibility enabled for this known asset" : "strict")}\n" +
                    $"Package resource load: {loadMetrics.ResourceLoadMilliseconds:0.0} ms\n" +
                    $"Import + decode/transcode: {loadMetrics.ImportMilliseconds:0.0} ms\n" +
                    $"First GPU prepare + submit: {currentSubmitMilliseconds:0.0} ms\n" +
                    "Material color: linear sRGB\n" +
                    "Output: Rgba16Float preferred";
            }
            TimeSpan sampleDuration = Stopwatch.GetElapsedTime(sampleStart);
            if (sampleDuration.TotalSeconds >= 0.5)
            {
                UpdatePerformanceLabel(
                    activeSession,
                    process,
                    resourceCounts,
                    presentedFrames / sampleDuration.TotalSeconds,
                    presentedFrames == 0 ? 0 : submitMilliseconds / presentedFrames,
                    presentedFrames == 0 ? default : new PresentationSurfaceFrameTimingsAverage(
                        acquireMilliseconds / presentedFrames,
                        renderMilliseconds / presentedFrames,
                        presentMilliseconds / presentedFrames),
                    presentedFrames == 0 ? default : new SceneRendererFrameTimingsAverage(
                        prepareMilliseconds / presentedFrames,
                        encodeMilliseconds / presentedFrames,
                        queueSubmitMilliseconds / presentedFrames,
                        cacheTrimMilliseconds / presentedFrames));
                sampleStart = Stopwatch.GetTimestamp();
                submitMilliseconds = 0;
                acquireMilliseconds = 0;
                renderMilliseconds = 0;
                presentMilliseconds = 0;
                prepareMilliseconds = 0;
                encodeMilliseconds = 0;
                queueSubmitMilliseconds = 0;
                cacheTrimMilliseconds = 0;
                presentedFrames = 0;
            }
            // The loop observes cancellation at the next frame boundary. Avoid throwing for the
            // ordinary page-disappearance path while keeping shutdown latency below one frame.
            await Task.Delay(8);
        }
    }

    private void UpdatePerformanceLabel(
        IPresentationSurfaceSession activeSession,
        Process process,
        ModelResourceCounts resourceCounts,
        double framesPerSecond,
        double averageSubmitMilliseconds,
        PresentationSurfaceFrameTimingsAverage frameTimings,
        SceneRendererFrameTimingsAverage rendererTimings)
    {
        long workingSet = 0;
        try
        {
            process.Refresh();
            workingSet = process.WorkingSet64;
        }
        catch (InvalidOperationException)
        {
        }
        catch (PlatformNotSupportedException)
        {
        }
        ulong attachmentBytes = EstimateAttachmentBytes(
            activeSession,
            BloomSwitch.IsToggled,
            (float)BloomRadiusSlider.Value);
        PerformanceLabel.Text =
            $"FPS: {framesPerSecond:0.0}\n" +
#if DEBUG
            "Runtime mode: Debug (MAUI Mac Catalyst uses Mono interpreter by default)\n" +
#else
            "Runtime mode: Release/AOT\n" +
#endif
            $"Frame wall avg: {averageSubmitMilliseconds:0.00} ms\n" +
            $"  acquire: {frameTimings.AcquireMilliseconds:0.00} ms\n" +
            $"  render/encode/submit: {frameTimings.RenderMilliseconds:0.00} ms\n" +
            $"  present/poll: {frameTimings.PresentMilliseconds:0.00} ms\n" +
            $"Renderer prepare: {rendererTimings.PrepareMilliseconds:0.00} ms\n" +
            $"Renderer encode: {rendererTimings.EncodeMilliseconds:0.00} ms\n" +
            $"Queue submit: {rendererTimings.SubmitMilliseconds:0.00} ms\n" +
            $"Cache trim: {rendererTimings.CacheTrimMilliseconds:0.00} ms\n" +
            $"Surface: {activeSession.Width} x {activeSession.Height} " +
            $"{activeSession.OutputPlan.Output.Format}\n" +
            $"Managed heap: {FormatBytes(GC.GetTotalMemory(false))}\n" +
            $"Process working set: {(workingSet == 0 ? "unavailable" : FormatBytes(workingSet))}\n" +
            $"GC collections: {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}\n" +
            $"Scene CPU: {resourceCounts.Meshes} meshes, {resourceCounts.Vertices} vertices, " +
            $"{resourceCounts.Triangles} triangles\n" +
            $"Geometry channel payload: {FormatBytes(resourceCounts.GeometryBytes)}\n" +
            $"Material texture CPU payload: {FormatBytes(resourceCounts.TextureCpuBytes)}\n" +
            $"Texture GPU mip estimate: {FormatBytes(resourceCounts.TextureGpuBytes)} " +
            $"({resourceCounts.ColorTextures} color, {resourceCounts.DataTextures} data)\n" +
            $"Material shader variants: {presenter?.ObservedMaterialShaderVariantCount ?? 0}\n" +
            $"Prepared material variants: {presenter?.CachedMaterialShaderVariantCount ?? 0} " +
            $"(hits {presenter?.MaterialShaderVariantCacheHits ?? 0}, " +
            $"misses {presenter?.MaterialShaderVariantCacheMisses ?? 0})\n" +
            $"Max material sampled textures: " +
            $"{presenter?.MaximumObservedMaterialSampledTextureCount ?? 0} " +
            "(+ 6 shared IBL/shadow/AO/background budget)\n" +
            $"Deformation: {resourceCounts.MorphTargets} morph targets, " +
            $"{resourceCounts.Joints} skin joints\n" +
            $"Color+depth attachments{(BloomSwitch.IsToggled ? " + Bloom FP16 intermediates" : " (minimum)")}: " +
            $"{FormatBytes(attachmentBytes)}\n" +
            "GPU total memory: unavailable through portable wgpu v29";
    }

    private readonly record struct PresentationSurfaceFrameTimingsAverage(
        double AcquireMilliseconds,
        double RenderMilliseconds,
        double PresentMilliseconds);

    private readonly record struct SceneRendererFrameTimingsAverage(
        double PrepareMilliseconds,
        double EncodeMilliseconds,
        double SubmitMilliseconds,
        double CacheTrimMilliseconds);



    private static async Task<byte[]> ReadPackageResourceAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using Stream stream = await FileSystem.OpenAppPackageFileAsync(path);
        using MemoryStream memory = new();
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }





    private void UpdateLayerHint(SceneRenderLayer layer)
    {
        LayerHintLabel.Text = layer switch
        {
            SceneRenderLayer.Beauty => "Beauty: direct + IBL; AO modulates indirect diffuse. Optional HDR Bloom is applied only here.",
            SceneRenderLayer.AmbientOcclusion => "AO: white is open; gray/black marks nearby occlusion. The diagnostic ground provides contact evidence.",
            SceneRenderLayer.DirectLighting => "Direct lighting only; shadows are included in this contribution.",
            SceneRenderLayer.ImageBasedLighting => "IBL only; the neutral diagnostic environment is intentionally subtle.",
            SceneRenderLayer.ViewDepth => "View depth: normalized camera distance, not material brightness.",
            SceneRenderLayer.DirectionalShadow => "Shadow visibility: white is lit and gray/black is shadowed. InterpolationTest includes a diagnostic receiver.",
            SceneRenderLayer.BentNormal => "Bent normal encoded as RGB; vivid colors are expected.",
            SceneRenderLayer.BaseColor => "Linear working-space base color without lighting.",
            SceneRenderLayer.TextureCoordinates => "Repeated UV encoded as red/green; a flat result is valid on meshes without useful UV variation.",
            SceneRenderLayer.SurfaceNormal => "World-space shading normal encoded as RGB.",
            SceneRenderLayer.OcclusionRoughnessMetallic => "ORM encoded as RGB; InterpolationTest uses mostly scalar defaults, so a flat field is expected.",
            SceneRenderLayer.Emissive => "Emissive: linear working-space texture multiplied by emissiveFactor, without direct or environment lighting.",
            SceneRenderLayer.Sheen => "Sheen: Charlie direct lobe plus the current approximate prefiltered-environment contribution; base and clearcoat lighting are hidden.",
            SceneRenderLayer.Transmission => "Transmission: refracted opaque HDR background after volume absorption; non-transmissive surfaces are black.",
            SceneRenderLayer.VolumeAttenuation => "Volume attenuation: white means no absorption; colored/darker values show Beer-Lambert transmittance through the current thickness.",
            SceneRenderLayer.Specular => "Specular: base PBR direct + IBL reflection only; diffuse, sheen, clearcoat, emissive and transmission are hidden.",
            SceneRenderLayer.DiffuseTransmission => "Diffuse transmission: only opposite-hemisphere thin-surface direct and IBL light; reflection and emission are hidden.",
            _ => layer.ToString(),
        };
    }





    private static ulong EstimateAttachmentBytes(
        IPresentationSurfaceSession activeSession,
        bool bloomEnabled,
        float bloomRadiusPixels)
    {
        uint colorBytesPerPixel = activeSession.OutputPlan.Output.Format == PresentationFormat.Rgba16Float
            ? 8u
            : 4u;
        ulong pixels = (ulong)activeSession.Width * activeSession.Height;
        ulong bytes = checked(pixels * (colorBytesPerPixel + 4u));
        if (bloomEnabled)
        {
            uint downsampleFactor = bloomRadiusPixels switch
            {
                <= 32f => 2u,
                <= 64f => 4u,
                _ => 8u,
            };
            ulong halfResolutionPixels =
                (ulong)Math.Max(1u, activeSession.Width / downsampleFactor) *
                Math.Max(1u, activeSession.Height / downsampleFactor);
            bytes = checked(bytes + (pixels * colorBytesPerPixel) + (halfResolutionPixels * 16u));
        }
        return bytes;
    }

    private static string FormatBytes(long bytes) => FormatBytes(checked((ulong)Math.Max(0, bytes)));

    private static string FormatBytes(ulong bytes) => $"{bytes / (1024d * 1024d):0.00} MiB";

    private void OnModelChanged(object? sender, EventArgs e)
    {
        _ = sender;
        if (ModelPicker.SelectedIndex < 0)
        {
            return;
        }
        GateLabel.Text = Models[ModelPicker.SelectedIndex].GateDescription;
        ConfigurePlayback([], playAllAnimations: false);
        if (SurfaceView.PresentationSession is not IPresentationSurfaceSession current)
        {
            return;
        }
        ResetLifetime();
        DisposeRendering();
        session = current;
        StartConfiguration(current);
    }

    private void OnAnimationChanged(object? sender, EventArgs e)
    {
        _ = sender;
        if (presenter is not null && AnimationPicker.IsEnabled && AnimationPicker.SelectedIndex >= 0)
        {
            presenter.AnimationIndex = AnimationPicker.SelectedIndex;
            playbackTimeSeconds = 0;
            isPlaying = true;
            playbackTimestamp = Stopwatch.GetTimestamp();
            UpdatePlaybackControls(forceTimelineUpdate: true);
        }
    }

    private void OnMaterialVariantChanged(object? sender, EventArgs e)
    {
        _ = sender;
        if (presenter is ModelLabPresenter activePresenter &&
            MaterialVariantPicker.IsEnabled && MaterialVariantPicker.SelectedIndex >= 0)
        {
            activePresenter.ApplyMaterialVariant(
                MaterialVariantPicker.SelectedIndex == 0
                    ? null
                    : MaterialVariantPicker.SelectedIndex - 1);
        }
    }

    private void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        float duration = CurrentAnimationDuration;
        if (duration <= 0f)
        {
            return;
        }
        if (!isPlaying && !LoopSwitch.IsToggled && playbackTimeSeconds >= duration)
        {
            playbackTimeSeconds = 0;
        }
        isPlaying = !isPlaying;
        playbackTimestamp = Stopwatch.GetTimestamp();
        UpdatePlaybackControls(forceTimelineUpdate: true);
    }

    private void OnReplayClicked(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        if (CurrentAnimationDuration <= 0f)
        {
            return;
        }
        playbackTimeSeconds = 0;
        isPlaying = true;
        playbackTimestamp = Stopwatch.GetTimestamp();
        UpdatePlaybackControls(forceTimelineUpdate: true);
    }

    private void OnLoopToggled(object? sender, ToggledEventArgs e)
    {
        _ = sender;
        if (e.Value && CurrentAnimationDuration > 0f &&
            playbackTimeSeconds >= CurrentAnimationDuration)
        {
            playbackTimeSeconds = 0;
        }
        playbackTimestamp = Stopwatch.GetTimestamp();
        UpdatePlaybackControls(forceTimelineUpdate: true);
    }

    private void OnTimelineChanged(object? sender, ValueChangedEventArgs e)
    {
        _ = sender;
        if (updatingTimeline || CurrentAnimationDuration <= 0f)
        {
            return;
        }
        playbackTimeSeconds = Math.Clamp(e.NewValue, 0, CurrentAnimationDuration);
        playbackTimestamp = Stopwatch.GetTimestamp();
        UpdatePlaybackControls(forceTimelineUpdate: true);
    }

    private void ConfigurePlayback(
        IReadOnlyList<AnimationClip> animations,
        bool playAllAnimations)
    {
        animationDurations = animations.Select(static animation => animation.Duration).ToArray();
        applyAllAnimations = playAllAnimations;
        playbackTimeSeconds = 0;
        isPlaying = animationDurations.Length != 0;
        playbackTimestamp = Stopwatch.GetTimestamp();
        timelineUiTimestamp = 0;
        bool enabled = animationDurations.Length != 0 && CurrentAnimationDuration > 0f;
        PlayPauseButton.IsEnabled = enabled;
        ReplayButton.IsEnabled = enabled;
        LoopSwitch.IsEnabled = enabled;
        TimelineSlider.IsEnabled = enabled;
        UpdatePlaybackControls(forceTimelineUpdate: true);
    }

    private float AdvancePlaybackTime()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedSeconds = Stopwatch.GetElapsedTime(playbackTimestamp, now).TotalSeconds;
        playbackTimestamp = now;
        float duration = CurrentAnimationDuration;
        if (isPlaying && duration > 0f)
        {
            playbackTimeSeconds += elapsedSeconds;
            if (LoopSwitch.IsToggled)
            {
                playbackTimeSeconds %= duration;
            }
            else if (playbackTimeSeconds >= duration)
            {
                playbackTimeSeconds = duration;
                isPlaying = false;
            }
        }
        if (timelineUiTimestamp == 0 || Stopwatch.GetElapsedTime(timelineUiTimestamp, now).TotalMilliseconds >= 50)
        {
            timelineUiTimestamp = now;
            UpdatePlaybackControls(forceTimelineUpdate: true);
        }
        return (float)playbackTimeSeconds;
    }

    private float CurrentAnimationDuration
    {
        get
        {
            if (animationDurations.Length == 0)
            {
                return 0f;
            }
            if (applyAllAnimations)
            {
                return animationDurations.Max();
            }
            int index = Math.Clamp(AnimationPicker.SelectedIndex, 0, animationDurations.Length - 1);
            return animationDurations[index];
        }
    }

    private void UpdatePlaybackControls(bool forceTimelineUpdate)
    {
        PlayPauseButton.Text = isPlaying ? "Pause" : "Play";
        if (!forceTimelineUpdate)
        {
            return;
        }
        float duration = CurrentAnimationDuration;
        updatingTimeline = true;
        try
        {
            TimelineSlider.Maximum = Math.Max(duration, 0.001f);
            TimelineSlider.Value = Math.Clamp(playbackTimeSeconds, 0, duration);
            TimelineLabel.Text = $"Timeline: {playbackTimeSeconds:0.00} / {duration:0.00} s";
        }
        finally
        {
            updatingTimeline = false;
        }
    }

    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        lifetime.Cancel();
        base.OnDisappearing();
    }

    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (lifetime.IsCancellationRequested)
        {
            ResetLifetime();
        }
        IPresentationSurfaceSession? current = SurfaceView.PresentationSession;
        if (session is null && current is not null &&
            (configurationTask is null || configurationTask.IsCompleted))
        {
            session = current;
            StartConfiguration(current);
        }
        else if (current is not null &&
            ReferenceEquals(session, current) &&
            presenter is ModelLabPresenter activePresenter &&
            activeModel is ModelSpec model &&
            (configurationTask is null || configurationTask.IsCompleted))
        {
            configurationTask = RunFramesAsync(
                current,
                activePresenter,
                model,
                activeResourceCounts,
                activeLoadMetrics,
                configurationGeneration,
                lifetime.Token);
        }
    }

    private void StartConfiguration(IPresentationSurfaceSession activeSession) =>
        configurationTask = ConfigureAndRunAsync(
            activeSession,
            configurationGeneration,
            lifetime.Token);

    private void ResetLifetime()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        lifetime = new CancellationTokenSource();
        configurationGeneration++;
        configurationTask = null;
    }

    private bool IsCurrentConfiguration(
        IPresentationSurfaceSession activeSession,
        int generation,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        generation == configurationGeneration &&
        ReferenceEquals(session, activeSession) &&
        ReferenceEquals(SurfaceView.PresentationSession, activeSession);

    private void DisposeRendering()
    {
        presenter?.Dispose();
        presenter = null;
        activeModel = null;
        activeResourceCounts = default;
        activeLoadMetrics = default;
        session = null;
    }





    private readonly record struct ModelLoadMetrics(
        double ResourceLoadMilliseconds,
        double ImportMilliseconds);
}

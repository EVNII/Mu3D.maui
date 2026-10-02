using Microsoft.Maui.Animations;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using Mu3D.Rendering.OpenPbr;
using MauiAnimation = Microsoft.Maui.Animations.Animation;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Tests a single OpenPBR sphere in a uniform white environment without a display view.</summary>
public partial class OpenPbrFurnacePage : ContentPage
{
    private OpenPbrFurnaceRenderer? furnace;
    private IAnimationManager? manager;
    private MauiAnimation? animation;
    private readonly FurnaceSettingsQueue settingsUpdates;
    private bool ready, visible, updating;
    private int revision, frames;

    /// <summary>Creates the white-furnace controls and declarative sphere scene.</summary>
    public OpenPbrFurnacePage()
    {
        InitializeComponent();
        settingsUpdates = new(Dispatcher, () => { if (visible) ApplySettings(); });
        ready = true;
        EnsureRenderer(); ApplySettings();
    }
    /// <inheritdoc />
    protected override void OnAppearing()
    {
        base.OnAppearing(); visible = true;
        EnsureRenderer(); ApplySettings(); SceneView.SceneContent = DeclaredScene;
        SceneView.InvalidateScene(); RefreshSampling();
    }
    /// <inheritdoc />
    protected override void OnDisappearing()
    {
        visible = false; settingsUpdates.Cancel(); revision++; StopSampling();
        SceneView.SceneContent = null; SceneView.RenderPipeline = null;
        furnace?.Dispose(); furnace = null;
        base.OnDisappearing();
    }
    private void EnsureRenderer()
    {
        furnace ??= new(); SceneView.RenderPipeline = furnace.Pipeline;
    }
    private void OnSettingsChanged(object? sender, EventArgs e)
    { if (ready && !updating) settingsUpdates.Request(); }
    private void OnValueChanged(object? sender, ValueChangedEventArgs e) => OnSettingsChanged(sender, e);
    private void ApplySettings()
    {
        if (!ready || updating || furnace is null) return;
        updating = true;
        try
        {
            int preset = Math.Max(0, PresetPicker.SelectedIndex);
            int modeIndex = ModePicker.SelectedIndex;
            bool unsupported = modeIndex == 3 && preset is 4 or 5 || modeIndex == 4 && preset == 4;
            if (unsupported)
            {
                ModePicker.SelectedIndex = 0;
                StatusLabel.Text = modeIndex == 4
                    ? "Fast 模式与光栅一样拒绝透射（玻璃），已选择 Reference。"
                    : "光栅模式不支持玻璃/次表面，已选择 Reference。";
            }
            else if (modeIndex == 4 && preset == 5)
                StatusLabel.Text = "Fast 丢弃次表面 lobe 并计入近似报告；扩散/高光项仍参与。数值不自动判定通过或失败。";
            else StatusLabel.Text = "有限采样、反弹截断和光栅近似都会影响结果；数值不自动判定通过或失败。";
            furnace.Transport.Mode = modeIndex switch
            { 1 => OpenPbrRenderMode.Hybrid, 2 => OpenPbrRenderMode.Interactive, 3 => OpenPbrRenderMode.Raster,
                4 => OpenPbrRenderMode.Fast, _ => OpenPbrRenderMode.Reference };
            int events = BouncePicker.SelectedIndex switch { 0 => 4, 1 => 8, 3 => 64, 4 => 128, _ => 32 };
            furnace.Transport.ReferenceMaxBounces = events; furnace.Transport.HybridMaxBounces = events;
            furnace.Transport.InteractiveMaxBounces = events;
            furnace.EnvironmentLevel = MathF.Pow(2, (float)EnvironmentEv.Value);
            furnace.ExpectedRatio = preset == 6 ? .5f : 1;
            furnace.DisplayMode = ViewPicker.SelectedIndex;
            Surface.LoadSurface(FurnaceMaterials.Create(preset, (float)Roughness.Value, (float)Anisotropy.Value));
            ExpectationLabel.Text = $"Lenv = {furnace.EnvironmentLevel:F3}；目标 Lout/Lenv ≈ {furnace.ExpectedRatio:F2}。" +
                (preset == 6 ? "灰色对照固定为 50% Lambert 吸收。" : "玻璃/次表面需要更多事件与采样才能收敛。");
            ResetHistory(); RefreshSampling();
        }
        finally { updating = false; }
    }
    private void OnViewChanged(object? sender, EventArgs e)
    {
        if (!ready || furnace is null) return;
        furnace.DisplayMode = ViewPicker.SelectedIndex; SceneView.InvalidateScene();
    }
    private void ResetHistory()
    {
        revision++; furnace?.Transport.ResetAccumulation();
        MeasurementLabel.Text = "结果已更新，请重新测量。";
        SceneView.InvalidateScene(); UpdateSampling();
    }
    private void OnResetClicked(object? sender, EventArgs e) => ResetHistory();
    private void OnRunChanged(object? sender, ToggledEventArgs e) { if (ready) RefreshSampling(); }
    private void RefreshSampling()
    {
        if (!visible || !RunSwitch.IsToggled || SceneView.Renderer is null ||
            furnace?.Transport.Mode is OpenPbrRenderMode.Raster or OpenPbrRenderMode.Fast)
        { StopSampling(); return; }
        if (animation is not null) return;
        manager = SceneView.Handler?.MauiContext?.Services.GetService(typeof(IAnimationManager)) as IAnimationManager;
        if (manager is null) { StatusLabel.Text = "MAUI VSync 服务不可用。"; return; }
        animation = new MauiAnimation(_ => SceneView.InvalidateScene(), 0, 1, Easing.Linear, null)
            { Name = "White furnace sampling", Repeats = true };
        animation.Commit(manager);
    }
    private void StopSampling()
    {
        if (animation is not null) { manager?.Remove(animation); animation.Dispose(); animation = null; }
        manager = null;
    }
    private void OnRendererChanged(object? sender, SceneRendererChangedEventArgs e) { if (ready) RefreshSampling(); }
    private void OnPresentationSessionChanged(object? sender, PresentationSessionChangedEventArgs e)
    {
        if (e.Session is not null) return;
        StopSampling(); revision++; furnace?.Dispose(); furnace = null;
        if (visible) { EnsureRenderer(); ApplySettings(); }
    }
    private void OnFramePresented(object? sender, SurfaceFramePresentedEventArgs e)
    {
        if (visible && e.Status is PresentationSurfaceFrameStatus.PresentedOptimal or PresentationSurfaceFrameStatus.PresentedSuboptimal &&
            (frames++ % 8 == 0 || !RunSwitch.IsToggled || furnace?.Transport.Mode is OpenPbrRenderMode.Raster or OpenPbrRenderMode.Fast)) UpdateSampling();
    }
    private void UpdateSampling()
    {
        if (furnace is null) return;
        var pass = furnace.Transport;
        SamplingLabel.Text = pass.Mode switch
        {
            OpenPbrRenderMode.Raster => "Raster：64 个固定环境样本，不累积；存在积分近似误差。",
            OpenPbrRenderMode.Fast => "Fast：split-sum 环境与烘焙纹理，单帧确定结果，不累积；近似项见 OpenPBR 页报告。",
            OpenPbrRenderMode.Interactive => "Interactive：每帧独立采样，不累积。",
            _ => $"{pass.Mode}：已累积 {pass.AccumulatedSamples:N0} spp；" +
                (pass.UsesPrimaryRayFallback ? "主光线求交回退。" : "修改材质/环境会重置。"),
        };
    }
    private async void OnMeasureClicked(object? sender, EventArgs e)
    {
        if (furnace is null || SceneView.Renderer is null) { StatusLabel.Text = "等待渲染表面就绪。"; return; }
        var active = furnace; int version = revision;
        MeasureButton.IsEnabled = false;
        try
        {
            Task<FurnaceMeasurement> pending = active.MeasureNextFrameAsync(); SceneView.InvalidateScene();
            FurnaceMeasurement result = await pending;
            if (version != revision || active != furnace || !visible) return;
            MeasurementLabel.Text = $"{result.Mode} · {(result.Samples > 0 ? $"{result.Samples:N0} spp" : "单帧")} · {result.Pixels:N0} 个球内像素\n" +
                $"平均 RGB 比值 {result.Mean.X:F4} / {result.Mean.Y:F4} / {result.Mean.Z:F4}；范围 {result.Minimum:F4}–{result.Maximum:F4}；目标 RMS 误差 {result.Rms:P2}";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (version == revision && visible) MeasurementLabel.Text = error.Message; }
        finally { MeasureButton.IsEnabled = true; }
    }
    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    {
        StopSampling(); RunSwitch.IsToggled = false; StatusLabel.Text = e.Exception.Message;
    }
}

using Mu3D.Gallery.Controls;
using Mu3D.GalleryApp.Pages;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;

namespace Mu3D.Gallery.Pages;

/// <summary>Compares Standard, AgX, ACES 2.0 and SDR Filmic using saturated color ramps.</summary>
public partial class ColorRampsPage : ContentPage, IGalleryPageActivation
{
    private readonly Dictionary<Mu3DView, (GraphicsDevice Device, ColorComparisonRenderer Renderer)> renderers = [];
    private bool initialized, active;
    private (int Count, int Columns) comparisonLayout;
    /// <summary>Initializes RGB bands and the existing XAML highlight scene.</summary>
    public ColorRampsPage()
    {
        InitializeComponent();
        initialized = true;
        UpdateSettings();
    }

    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (this.active == active) return;
        if (active) OnPageLoaded(this, EventArgs.Empty);
        else OnPageUnloaded(this, EventArgs.Empty);
    }

    private ColorViewPreset SelectedPreset(bool aces) => RangePicker.SelectedIndex == 0
        ? aces ? ColorViewPreset.Aces2Hdr1000P3 : ColorViewPreset.AgXHdr1000
        : aces ? ColorViewPreset.Aces2Sdr : ColorViewPreset.AgXSdr;

    private void OnSettingsChanged(object? sender, EventArgs e) => UpdateSettings();
    private void OnExposureChanged(object? sender, ValueChangedEventArgs e) => UpdateSettings();
    private void OnSaturationChanged(object? sender, ValueChangedEventArgs e) => UpdateSettings();
    private void UpdateSettings()
    {
        if (!initialized) return;
        bool scene = ScenePicker.SelectedIndex == 2;
        ComparisonPanel.IsVisible = SaturationControls.IsVisible = ComparisonDescription.IsVisible = !scene;
        ScenePanel.IsVisible = scene;
        if (scene) DisposeComparisons();
        FilmicPanel.IsVisible = RangePicker.SelectedIndex == 1;
        if (!FilmicPanel.IsVisible) DisposeComparison(FilmicView);
        OnComparisonSizeChanged(this, EventArgs.Empty);
        DisplayView.Preset = SelectedPreset(ViewPicker.SelectedIndex == 1);
        string output = RangePicker.SelectedIndex == 0 ? "HDR 1000 nit · P3 D65" : "SDR 100 nit · Rec.709";
        AgxTitle.Text = $"AgX · {output}"; AcesTitle.Text = $"ACES 2.0 · {output}";
        StandardTitle.Text = RangePicker.SelectedIndex == 0 ? "Standard · HDR 直出" : "Standard · SDR 硬裁切";
        ExposureLabel.Text = $"曝光 {ExposureSlider.Value:+0.0;-0.0;0.0} EV（共用）";
        SaturationLabel.Text = $"色带饱和度 {SaturationSlider.Value:P0}";
        BandLegend.Text = ScenePicker.SelectedIndex == 1
            ? "从上到下连续改变色相，左暗右亮。观察高亮色相带是否连续。"
            : "从上到下：红 R、绿 G、蓝 B、青 C、品红 M、黄 Y、白。每行通道峰值相同，亮度 Y 不相同。";
        if (active && !scene)
        {
            StandardView.InvalidateSurface(); AgxView.InvalidateSurface(); AcesView.InvalidateSurface();
            if (FilmicPanel.IsVisible) FilmicView.InvalidateSurface();
        }
    }
    private void OnComparisonSizeChanged(object? sender, EventArgs e)
    {
        if (!initialized || OutputGrid.Width <= 0) return;
        var panels = OutputGrid.Children.OfType<View>().Where(v => v.IsVisible).ToArray();
        int columns = OutputGrid.Width < 900 ? 1 : panels.Length == 4 ? 2 : 3;
        if (comparisonLayout == (panels.Length, columns)) return;
        comparisonLayout = (panels.Length, columns);
        OutputGrid.ColumnDefinitions = new ColumnDefinitionCollection();
        OutputGrid.RowDefinitions = new RowDefinitionCollection();
        for (int i = 0; i < columns; i++) OutputGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (int i = 0; i < (panels.Length + columns - 1) / columns; i++) OutputGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int i = 0; i < panels.Length; i++)
        {
            Grid.SetColumn(panels[i], i % columns);
            Grid.SetRow(panels[i], i / columns);
        }
    }
    private Label ComparisonStatus(object? surface) => ReferenceEquals(surface, StandardView)
        ? StandardStatus : ReferenceEquals(surface, AcesView) ? AcesStatus : ReferenceEquals(surface, FilmicView) ? FilmicStatus : AgxStatus;
    private void OnComparisonDraw(object? sender, SurfaceDrawEventArgs e)
    {
        if (!active || ScenePicker.SelectedIndex == 2 || sender is not Mu3DView surface) return;
        bool filmic = ReferenceEquals(surface, FilmicView);
        if (filmic && !FilmicPanel.IsVisible) return;
        bool aces = ReferenceEquals(surface, AcesView);
        bool raw = ReferenceEquals(surface, StandardView);
        Label status = ComparisonStatus(surface);
        bool hdr = RangePicker.SelectedIndex == 0;
        if (hdr && (e.Target.Descriptor.Format != GraphicsTextureFormat.Rgba16Float ||
            e.OutputPlan.Output.Encoding != ColorEncoding.ExtendedSrgbLinear || e.OutputPlan.Output.DynamicRange != OutputDynamicRange.Hdr))
        {
            DisposeComparison(surface);
            using var encoder = e.Device.CreateCommandEncoder("Unsupported HDR comparison");
            using (encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(e.Target,
                clearColor: new GraphicsClearColor(0, 0, 0, 1))))) { }
            using var command = encoder.Finish(); e.Device.Queue.Submit(command);
            status.Text = "HDR 表面不可用；请选择 SDR 比较。"; return;
        }
        if (!renderers.TryGetValue(surface, out var entry) || !ReferenceEquals(entry.Device, e.Device))
        {
            DisposeComparison(surface);
            entry = (e.Device, new ColorComparisonRenderer(e.Device)); renderers.Add(surface, entry);
        }
        if (raw)
            entry.Renderer.DrawStandard(e.Target, (float)ExposureSlider.Value, (float)SaturationSlider.Value,
                ScenePicker.SelectedIndex == 1, hdr, e.OutputPlan.Output.Encoding);
        else
            entry.Renderer.Draw(e.Target, filmic ? ColorViewPreset.FilmicSdr : SelectedPreset(aces), (float)ExposureSlider.Value,
                (float)SaturationSlider.Value, ScenePicker.SelectedIndex == 1, e.OutputPlan.Output.Encoding);
        status.Text = raw ? hdr
            ? "原始线性值；显示受屏幕余量限制。极端曝光超出 ±65504 时截断。"
            : "直接裁切到 0–1，无高光压缩。"
            : $"{e.Target.Descriptor.Format} · 参考白 100 nit";
    }
    private void OnComparisonSessionChanged(object? sender, PresentationSessionChangedEventArgs e)
    { if (sender is Mu3DView surface) DisposeComparison(surface); }
    private void OnComparisonError(object? sender, SurfaceErrorEventArgs e)
    {
        if (sender is Mu3DView surface) DisposeComparison(surface);
        ComparisonStatus(sender).Text = e.Exception.Message;
    }
    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e) => StatusLabel.Text = e.Exception.Message;
    private void DisposeComparison(Mu3DView surface)
    { if (renderers.Remove(surface, out var entry)) entry.Renderer.Dispose(); }
    private void DisposeComparisons()
    { foreach (var entry in renderers.Values) entry.Renderer.Dispose(); renderers.Clear(); }
    private void OnPageLoaded(object? sender, EventArgs e) { _ = sender; active = true; UpdateSettings(); }
    private void OnPageUnloaded(object? sender, EventArgs e) { _ = sender; active = false; DisposeComparisons(); }
}

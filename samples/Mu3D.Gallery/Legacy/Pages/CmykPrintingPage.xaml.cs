using Mu3D.Color;
using Mu3D.Color.Printing;
using Mu3D.GalleryApp.Examples;
using Mu3D.Graphics;
using Mu3D.Maui.Controls;
using MauiColor = Microsoft.Maui.Graphics.Color;
namespace Mu3D.GalleryApp.Pages;

/// <summary>Optional print-profile example, excluded together with its package when printing is disabled.</summary>
public partial class CmykPrintingPage : ContentPage
{
    private const int Columns = CmykPrintingPattern.Columns, Rows = CmykPrintingPattern.Rows;
    private CmykPrintingHdrRenderer? hdrRenderer;
    private GraphicsDevice? hdrDevice;
    private readonly List<CmykProfile> profiles = [];
    private readonly LinearRgba[] source = CmykPrintingPattern.Create();
    private CmykImage? separation;
    private MauiColor[]? proofPixels;
    private int revision;
    private bool loaded;
    private bool active;

    /// <summary>Creates HDR source and explicitly mapped SDR print comparisons.</summary>
    public CmykPrintingPage()
    {
        InitializeComponent();
        var initial = new PrintRgbTransform(StandardColorSpaces.LinearAdobeRgb, StandardColorSpaces.LinearAdobeRgb);
        OriginalView.Drawable = new Chart(source.Select(c => DisplayColor(initial.Transform(c))).ToArray());
    }
    /// <inheritdoc />
    protected override async void OnAppearing()
    {
        base.OnAppearing(); active = true; HdrView.InvalidateSurface();
        if (!loaded)
        {
            loaded = true;
            // Optional local build inputs, never downloaded or embedded by default.
            foreach (string name in new[] { "JapanColor2011Coated.icc", "GRACoL2013_CRPC6.icc", "SWOP2013C3_CRPC5.icc", "PSOcoated_v3.icc", "PSOuncoated_v3_FOGRA52.icc" })
            {
                try
                {
                    string path = "PrintProfiles/" + name;
                    if (!await FileSystem.Current.AppPackageFileExistsAsync(path)) continue;
                    using var stream = await FileSystem.Current.OpenAppPackageFileAsync(path);
                    using var memory = new MemoryStream(); await stream.CopyToAsync(memory);
                    profiles.Add(new CmykProfile(memory.ToArray())); ProfilePicker.Items.Add(name);
                }
                catch (Exception error) { StatusLabel.Text = error.Message; }
            }
            if (profiles.Count > 0) ProfilePicker.SelectedIndex = 0;
            else await Refresh();
        }
        else await Refresh();
    }
    /// <inheritdoc />
    protected override void OnDisappearing() { active = false; revision++; DisposeHdr(); base.OnDisappearing(); }

    private async void OnLoadProfile(object? sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "选择 CMYK 印刷 ICC 配置" });
            if (result is null) return;
            using var stream = await result.OpenReadAsync(); using var memory = new MemoryStream();
            if (stream.CanSeek && stream.Length > 128 * 1024 * 1024) throw new InvalidDataException("ICC 文件过大。");
            byte[] buffer = new byte[65536]; int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                if (memory.Length + read > 128 * 1024 * 1024) throw new InvalidDataException("ICC 文件过大。");
                await memory.WriteAsync(buffer.AsMemory(0, read));
            }
            profiles.Add(new CmykProfile(memory.ToArray())); ProfilePicker.Items.Add(result.FileName);
            ProfilePicker.SelectedIndex = profiles.Count - 1;
        }
        catch (Exception error) { StatusLabel.Text = error.Message; }
    }
    private async void OnSettingsChanged(object? sender, EventArgs e) => await Refresh();
    private async void OnPaperChanged(object? sender, ToggledEventArgs e) => await Refresh();
    private async void OnExposureChanged(object? sender, ValueChangedEventArgs e) => await Refresh();
    private void OnInkChanged(object? sender, ValueChangedEventArgs e) => UpdateInk();
    private void UpdateInk()
    {
        if (InkLabel is null || InkSlider is null) return;
        if (separation is null || proofPixels is null) { InkLabel.Text = "等待分色"; return; }
        var indices = separation.FindExcessInk((float)InkSlider.Value);
        var mask = new bool[source.Length];
        foreach (int i in indices) mask[i] = true;
        ProofView.Drawable = new Chart(proofPixels, mask); ProofView.Invalidate();
        InkLabel.Text = $"警戒值 {InkSlider.Value:F0}% · 超限 {indices.Count} / {source.Length} 色块（黑白斜线）"
            + (indices.Count == 0 ? " · 当前没有超限，可降低阈值观察" : "")
            + " · 仅诊断，不修改墨量或黑版";
    }
    private async Task Refresh()
    {
        if (!active || ProfilePicker is null || IntentPicker is null || PaperSwitch is null || ExposureSlider is null || MappingPicker is null || TargetPicker is null || MappedTitle is null) return;
        int version = ++revision;
        float exposure = (float)ExposureSlider.Value;
        var transform = CmykPrintingExample.CreateTransform(TargetPicker.SelectedIndex, MappingPicker.SelectedIndex, exposure);
        ExposureLabel.Text = $"HDR → {TargetPicker.SelectedItem} · 曝光 {exposure:+0.0;-0.0;0.0} EV（0 EV = 原值）";
        MappedTitle.Text = $"{TargetPicker.SelectedItem}（sRGB 屏幕预览）";
        var mapped = source.Select(transform.Transform).ToArray();
        HdrView.InvalidateSurface();
        MappingValuesLabel.Text = CmykPrintingExample.GrayValues(transform);
        OriginalView.Drawable = new Chart(mapped.Select(DisplayColor).ToArray()); OriginalView.Invalidate();
        if (ProfilePicker.SelectedIndex < 0) return;
        var profile = profiles[ProfilePicker.SelectedIndex];
        int intent = IntentPicker.SelectedIndex;
        bool paper = PaperSwitch.IsToggled, mark = GamutSwitch.IsToggled;
        StatusLabel.Text = "正在分色并计算软打样…";
        try
        {
            var result = await Task.Run(() => CmykPrintingExample.SeparateAsync(mapped, profile, intent, paper, mark,
                () => active && version == revision));
            if (!active || version != revision || result is null) return;
            separation = result.Image; proofPixels = result.Preview.Select(DisplayColor).ToArray();
            StatusLabel.Text = $"{ProfilePicker.SelectedItem} · 平均 ΔE76 {result.TotalDeltaE / source.Length:F2} · ΔE76 > 2：{result.Changed}/{source.Length} · 最大总墨量 {result.MaximumInk:F1}% · 显示裁切 {result.Clipped} 色块";
            UpdateInk();
        }
        catch (Exception e)
        {
            if (active && version == revision)
            {
                separation = null; proofPixels = null;
                ProofView.Drawable = null; ProofView.Invalidate(); UpdateInk();
                StatusLabel.Text = e.Message;
            }
        }
    }
    private static MauiColor DisplayColor(LinearRgba color)
    {
        var c = StandardRgbEncodingConverter.Encode(color, StandardRgbEncoding.Srgb, RgbEncodingRangePolicy.Clip);
        return new MauiColor(c.Red, c.Green, c.Blue, 1);
    }
    private void OnHdrDraw(object? sender, SurfaceDrawEventArgs e)
    {
        if (!active) return;
        if (e.Target.Descriptor.Format != GraphicsTextureFormat.Rgba16Float ||
            e.OutputPlan.Output.Encoding != ColorEncoding.ExtendedSrgbLinear ||
            e.OutputPlan.Output.DynamicRange != OutputDynamicRange.Hdr)
        {
            DisposeHdr();
            using var encoder = e.Device.CreateCommandEncoder("Unavailable HDR source");
            using (encoder.BeginRenderPass(new(new GraphicsRenderPassColorAttachment(e.Target,
                clearColor: new GraphicsClearColor(0, 0, 0, 1))))) { }
            using var command = encoder.Finish(); e.Device.Queue.Submit(command);
            HdrStatusLabel.Text = "当前表面未提供 FP16 extended-linear HDR；原始 HDR 区域留黑，下方映射预览仍可用。";
            return;
        }
        if (!ReferenceEquals(hdrDevice, e.Device))
        {
            DisposeHdr(); hdrRenderer = new(e.Device); hdrDevice = e.Device;
        }
        hdrRenderer!.Draw(e.Target, source, (float)ExposureSlider.Value);
        HdrStatusLabel.Text = "FP16 extended-linear HDR · 原始值 × 曝光，无色调映射。实际高光亮度取决于屏幕 HDR 余量。";
    }
    private void OnHdrSessionChanged(object? sender, PresentationSessionChangedEventArgs e) => DisposeHdr();
    private void OnHdrError(object? sender, SurfaceErrorEventArgs e)
    {
        DisposeHdr(); HdrStatusLabel.Text = $"HDR 显示失败：{e.Exception.Message}";
    }
    private void DisposeHdr() { hdrRenderer?.Dispose(); hdrRenderer = null; hdrDevice = null; }

    private sealed class Chart(MauiColor[] pixels, bool[]? excess = null) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF bounds)
        {
            float w = bounds.Width / Columns, h = bounds.Height / Rows;
            for (int y = 0; y < Rows; y++) for (int x = 0; x < Columns; x++)
            {
                float left = bounds.X + x * w, top = bounds.Y + y * h;
                canvas.FillColor = pixels[y * Columns + x]; canvas.FillRectangle(left, top, w + .5f, h + .5f);
                if (excess?[y * Columns + x] == true)
                {
                    canvas.StrokeColor = Colors.Black; canvas.StrokeSize = 3;
                    canvas.DrawLine(left + 1, top + h - 1, left + w - 1, top + 1);
                    canvas.StrokeColor = Colors.White; canvas.StrokeSize = 1;
                    canvas.DrawLine(left + 1, top + h - 1, left + w - 1, top + 1);
                }
            }
        }
    }
}

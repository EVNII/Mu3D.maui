using System.Globalization;
using Mu3D.Gallery.Controls;
using Mu3D.GalleryApp.Examples;
using Mu3D.Maui.Controls;
using Vector2 = System.Numerics.Vector2;

namespace Mu3D.Gallery.Pages;

/// <summary>Demonstrates application-owned coordinate editing over standard, batched color conversions.</summary>
public partial class PaintingColorSpacesPage : ContentPage, IGalleryPageActivation
{
    private readonly PaintingColorSpacesExample example = new();
    private readonly Entry[] entries;
    private readonly Slider[] sliders;
    private readonly Label[] labels, trackLabels;
    private bool initialized, updating, active, pointerDown, panning;
    private int selectionRevision, dragRegion;
    private Vector2 panOrigin;
    private PaintingColorPickerLayout PickerLayout => new((float)SurfaceView.Width, (float)SurfaceView.Height);
    private const string BoundaryMessage = "已到可选颜色边界；灰区不能选取，合法 HDR 和模型负坐标仍可选择。";
    /// <summary>Initializes the shared color-space editor and ordinary native controls.</summary>
    public PaintingColorSpacesPage()
    {
        InitializeComponent();
        entries = [FirstEntry, SecondEntry, ThirdEntry];
        sliders = [FirstSlider, SecondSlider, ThirdSlider];
        labels = [FirstLabel, SecondLabel, ThirdLabel];
        trackLabels = [FirstTrackLabel, SecondTrackLabel, ThirdTrackLabel];
        ModelPicker.ItemsSource = PaintingColorSpacesExample.Models;
        ModelPicker.SelectedIndex = example.Model;
        initialized = true; Refresh(); PositionSliders();
    }
    void IGalleryPageActivation.SetNavigationActive(bool active)
    {
        if (this.active == active) return;
        this.active = active;
        if (active) Refresh();
        else { selectionRevision++; CancelDrag(); example.Dispose(); }
    }
    private void OnModelChanged(object? sender, EventArgs e)
    {
        if (!initialized || updating || ModelPicker.SelectedIndex < 0) return;
        CancelDrag();
        try { example.SelectModel(ModelPicker.SelectedIndex); StatusLabel.Text = ""; }
        catch (InvalidOperationException error) { StatusLabel.Text = error.Message; }
        // Picker corrections run after Apple's current native selection transaction completes.
        int revision = ++selectionRevision;
        Dispatcher.Dispatch(() => { if (active && revision == selectionRevision) Refresh(); });
    }
    private void OnReset(object? sender, EventArgs e)
    { CancelDrag(); example.Reset(); StatusLabel.Text = ""; Refresh(); }
    private void OnValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!initialized || updating || !active || sender is not Slider slider) return;
        int channel = Array.IndexOf(sliders, slider);
        if (channel >= 0) { CancelDrag(); SetValue(channel, (float)e.NewValue); }
    }
    private void OnEntryCompleted(object? sender, EventArgs e)
    {
        if (!initialized || updating || sender is not Entry entry) return;
        int channel = Array.IndexOf(entries, entry);
        if (channel < 0) return;
        if ((!float.TryParse(entry.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
            !float.TryParse(entry.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)) || !float.IsFinite(value))
        { StatusLabel.Text = "请输入有限数值。"; Refresh(); return; }
        CancelDrag(); SetValue(channel, value);
    }
    private void SetValue(int channel, float value)
    {
        try { StatusLabel.Text = example.PickValue(channel, value) ? "" : BoundaryMessage; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { StatusLabel.Text = error.Message; }
        Refresh();
    }
    private bool TryPosition(Point? point, out Vector2 position)
    {
        position = default;
        if (point is not Point p || !double.IsFinite(p.X) || !double.IsFinite(p.Y) ||
            !double.IsFinite(SurfaceView.Width) || !double.IsFinite(SurfaceView.Height) ||
            SurfaceView.Width <= 0 || SurfaceView.Height <= 0) return false;
        position = new((float)(p.X / SurfaceView.Width), (float)(p.Y / SurfaceView.Height));
        return float.IsFinite(position.X) && float.IsFinite(position.Y);
    }
    private void ApplyAt(int region, Vector2 position)
    {
        if (!active || region == 0) return;
        try { StatusLabel.Text = PickerLayout.Apply(example, region, position.X, position.Y) ? "" : BoundaryMessage; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        { StatusLabel.Text = error.Message; }
        Refresh();
    }
    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (active && TryPosition(e.GetPosition(SurfaceView), out var p)) ApplyAt(PickerLayout.RegionAt(p.X, p.Y), p);
    }
    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        CancelDrag();
        if (!active || !TryPosition(e.GetPosition(SurfaceView), out var p)) return;
        pointerDown = true;
        int region = PickerLayout.RegionAt(p.X, p.Y);
        dragRegion = region == 3 ? 0 : region;
        ApplyAt(dragRegion, p); // Harmony is applied once by Tap, rather than on both press and release.
    }
    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (pointerDown && TryPosition(e.GetPosition(SurfaceView), out var p)) ApplyAt(dragRegion, p);
    }
    private void OnPointerReleased(object? sender, PointerEventArgs e)
    { OnPointerMoved(sender, e); CancelDrag(); }
    private void OnPointerExited(object? sender, PointerEventArgs e) => CancelDrag();
    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (e.StatusType == GestureStatus.Started)
        {
            // Ordinary MAUI Pan supplies total translation, not an absolute starting touch position.
            panning = active && !pointerDown && TryPosition(new Point(0, 0), out _);
            if (!panning) return;
            var rect = PickerLayout.Plane; var marker = example.PlanePosition;
            panOrigin = new(rect.X + marker.X * rect.Z, rect.Y + marker.Y * rect.W);
        }
        else if (e.StatusType == GestureStatus.Running && panning && !pointerDown)
            ApplyAt(1, panOrigin + new Vector2((float)(e.TotalX / SurfaceView.Width), (float)(e.TotalY / SurfaceView.Height)));
        else if (e.StatusType is GestureStatus.Completed or GestureStatus.Canceled) panning = false;
    }
    private void CancelDrag() { pointerDown = panning = false; dragRegion = 0; }
    private void OnPickerSizeChanged(object? sender, EventArgs e) => PositionSliders();
    private void PositionSliders()
    {
        if (!initialized || !double.IsFinite(PickerHost.Width) || !double.IsFinite(PickerHost.Height) ||
            PickerHost.Width <= 0 || PickerHost.Height <= 0) return;
        double width = PickerHost.Width, height = PickerHost.Height;
        for (int i = 0; i < 3; i++)
        {
            var rect = PaintingColorPickerLayout.Track(i);
            // Keep a native 44-unit hit target; native thumb insets remain platform-owned.
            AbsoluteLayout.SetLayoutBounds(sliders[i], new Rect(rect.X * width,
                (rect.Y + rect.W * .5) * height - 22, rect.Z * width, 44));
            AbsoluteLayout.SetLayoutBounds(trackLabels[i], new Rect(rect.X * width,
                (.63 + i * .105) * height, rect.Z * width, 20));
        }
    }
    private void Refresh()
    {
        updating = true;
        try
        {
            ModelPicker.SelectedIndex = example.Model;
            NotesLabel.Text = example.Notes;
            for (int i = 0; i < 3; i++)
            {
                var range = example.GetChannel(i);
                labels[i].Text = $"{range.Name}（{range.Minimum:0.###} – {range.Maximum:0.###}）";
                entries[i].Text = example.GetValue(i).ToString("G7", CultureInfo.InvariantCulture);
                trackLabels[i].Text = $"{range.Name}: {example.GetValue(i):0.000}（{range.Minimum:0.###} – {range.Maximum:0.###}）";
                SemanticProperties.SetDescription(sliders[i], $"{PaintingColorSpacesExample.Models[example.Model]} 的 {range.Name} 通道");
                sliders[i].Maximum = Math.Max(sliders[i].Minimum, range.Maximum);
                sliders[i].Minimum = range.Minimum; sliders[i].Maximum = range.Maximum;
                sliders[i].Value = Math.Clamp(example.GetValue(i), range.Minimum, range.Maximum);
            }
            PlaneLabel.Text = example.Shape == 1 ?
                $"圆盘 · 角度 {example.GetChannel(example.PlaneX).Name}，半径 {example.GetChannel(example.PlaneY).Name}" :
                $"二维选区 · 横向 {example.GetChannel(example.PlaneX).Name}，向上 {example.GetChannel(example.PlaneY).Name}";
            ReadoutLabel.Text = example.Readout;
        }
        finally { updating = false; }
        if (active) SurfaceView.InvalidateSurface();
    }
    private void OnDraw(object? sender, SurfaceDrawEventArgs e)
    {
        if (!active) return;
        example.Draw(e.Device, e.Target, e.OutputPlan.Output.Encoding);
    }
    private void OnSessionChanged(object? sender, PresentationSessionChangedEventArgs e)
    { CancelDrag(); example.Dispose(); }
    private void OnSurfaceError(object? sender, SurfaceErrorEventArgs e)
    { CancelDrag(); example.Dispose(); StatusLabel.Text = e.Exception.Message; }
}

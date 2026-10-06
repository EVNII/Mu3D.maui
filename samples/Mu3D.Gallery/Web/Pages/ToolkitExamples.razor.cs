using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Mu3D.GalleryApp.Web.Infrastructure;
using Mu3D.Toolkit.Gizmos;
using Mu3D.Toolkit.Diagnostics;

namespace Mu3D.GalleryApp.Web.Pages;

public partial class ToolkitExamples
{
    private string id = "", status = "正在连接 GPU…";
    private ToolkitGalleryExample? example;
    private RenderSession? session;
    private IJSObjectReference? module, host;
    private DotNetObjectReference<ToolkitExamples>? reference;
    private ElementReference canvas, container, cyanLabel, magentaLabel;
    private Task? startup, disposal;
    private bool disposed, hdr = true;
    private ulong frame;
    private uint selectionMask = 3;
    private string[] mapKeys = ["A", "D", "W", "S", "E", "Q"];
    private static readonly string[] MapKeyNames = ["Rotate left", "Rotate right", "Rotate up", "Rotate down", "Dolly in", "Dolly out"];
    private static readonly string[] AllowedKeys = ["None", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "PageUp", "PageDown", "+", "-",
        .. Enumerable.Range('A', 26).Select(value => ((char)value).ToString())];
    private string Title => id switch { "ui-anchors" => "UI Anchors", "declarative-tools" => "Declarative Tools", "declarative-scene" => "Declarative Scene",
        "bounds-helper" => "Bounds + Outline Helpers", _ => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' ')) };
    private string Instructions => id switch {
        "map-controls" => "左键拖动平移；右键拖动或双指拖动旋转；滚轮/捏合缩放。按住多个配置键组合导航。",
        "fly-controls" => "左键拖动环顾；右键或双指拖动平移；W/A/S/D 移动，E/Q 升降，方向键环顾；滚轮/捏合前进后退。",
        "axes-helper" => "点击轴端点切换正交方向，点击菱形切换 45° 方向。",
        "bounds-helper" => "橙色是世界 AABB，青色是实际模型轮廓；前景球会遮挡深度测试模式的辅助线。",
        "transform-gizmo" => "拖动手柄移动、旋转或缩放锥体；拖动空白处 Orbit。缩放始终沿目标局部轴。",
        "declarative-tools" => "点击真实锥体或球体重新选择 Gizmo 目标，空白处拖动 Orbit；共用输入边界。",
        "declarative-scene" => "读取原生示例的同一 Scene3D 定义，复用 Mu3D 场景和渲染器。",
        "frame-statistics" => "显示真实提交帧的滚动记录。连续渲染由同一个 Canvas 帧调度器负责。",
        "ui-anchors" => "两个 HTML 标签借用同一场景节点，由 Toolkit 用提交帧相机投影定位；点击透传到画布。",
        _ => "左键/单指拖动旋转，右键/双指拖动平移，滚轮或捏合缩放。" };
    private string[] NavigationActions => id == "fly-controls"
        ? ["Forward", "Backward", "Strafe left", "Strafe right", "Lift", "Lower", "Look left", "Look right", "Look up", "Look down"]
        : ["Rotate left", "Rotate right", "Rotate up", "Rotate down", "Pan left", "Pan right", "Pan up", "Pan down", "Dolly in", "Dolly out"];
    private bool ControllerEnabled { get => example?.Orbit?.IsEnabled ?? example?.Fly?.IsEnabled ?? false; set { if (example?.Orbit is { } orbit) orbit.IsEnabled = value; if (example?.Fly is { } fly) fly.IsEnabled = value; } }
    private bool DampingEnabled { get => example?.Orbit?.DampingEnabled ?? example?.Fly?.DampingEnabled ?? false; set { if (example?.Orbit is { } orbit) orbit.DampingEnabled = value; if (example?.Fly is { } fly) fly.DampingEnabled = value; } }
    private float RotationSensitivity { get => example?.Orbit?.RotationSensitivity ?? example?.Fly?.RotationSensitivity ?? 1; set { if (example?.Orbit is { } orbit) orbit.RotationSensitivity = value; if (example?.Fly is { } fly) fly.RotationSensitivity = value; } }
    private float PanSensitivity { get => example?.Orbit?.PanSensitivity ?? example?.Fly?.PanSensitivity ?? 1; set { if (example?.Orbit is { } orbit) orbit.PanSensitivity = value; if (example?.Fly is { } fly) fly.PanSensitivity = value; } }
    private float DollySensitivity { get => example?.Orbit?.DollySensitivity ?? example?.Fly?.DollySensitivity ?? 1; set { if (example?.Orbit is { } orbit) orbit.DollySensitivity = value; if (example?.Fly is { } fly) fly.DollySensitivity = value; } }
    private bool TargetVisible { get => example?.Bounds?.Target?.IsVisible ?? false; set { if (example?.Bounds?.Target is { } target) target.IsVisible = value; } }
    private bool IncludeInvisible { get => example?.Bounds?.IncludeInvisible ?? false; set { if (example?.Bounds is { } bounds) bounds.IncludeInvisible = value; if (example?.Outline is { } outline) outline.IncludeInvisible = value; } }
    private bool LocalAxes { get => example?.Gizmo?.Space == TransformGizmoSpace.Local; set { if (example?.Gizmo is { } gizmo) gizmo.Space = value ? TransformGizmoSpace.Local : TransformGizmoSpace.World; } }
    private float MoveStep { get => example?.Gizmo?.TranslationSnap ?? 0; set { if (example?.Gizmo is { } gizmo) gizmo.TranslationSnap = Math.Clamp(value, 0, 2); } }
    private float RotationStep { get => (example?.Gizmo?.RotationSnapRadians ?? 0) * 180 / MathF.PI; set { if (example?.Gizmo is { } gizmo) gizmo.RotationSnapRadians = Math.Clamp(value, 0, 90) * MathF.PI / 180; } }
    private float ScaleStep { get => example?.Gizmo?.ScaleSnap ?? 0; set { if (example?.Gizmo is { } gizmo) gizmo.ScaleSnap = Math.Clamp(value, 0, 1); } }
    private double SnapshotInterval { get => example?.SnapshotInterval ?? 100; set { if (example is not null) example.SnapshotInterval = Math.Clamp(value, 50, 1000); } }
    private string StatisticsMode
    {
        get => (example?.StatisticsDisplayMode ?? FrameStatisticsDisplayMode.Compact).ToString().ToLowerInvariant();
        set { if (example is not null) example.StatisticsDisplayMode = ParseStatisticsMode(value); }
    }
    private static FrameStatisticsDisplayMode ParseStatisticsMode(string mode) => mode switch
    {
        "compact" => FrameStatisticsDisplayMode.Compact,
        "normal" => FrameStatisticsDisplayMode.Normal,
        "detail" => FrameStatisticsDisplayMode.Detail,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    protected override async Task OnParametersSetAsync()
    {
        string next = RouteId;
        if (disposed || next == id) return;
        await StopAsync();
        if (disposed) return;
        id = next; frame = 0; status = "正在连接 GPU…"; example = new(id);
    }
    protected override async Task OnAfterRenderAsync(bool firstRender)
    { if (startup is null && !disposed) { startup = StartAsync(); await startup; } }
    private async Task StartAsync()
    {
        try {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./toolkit-viewport.mjs"); if (disposed) return;
            session = await RenderSession.OpenAsync(); if (disposed) return;
            reference = DotNetObjectReference.Create(this);
            host = await module.InvokeAsync<IJSObjectReference>("create", canvas, container, reference, session.MaximumDimension, id, cyanLabel, magentaLabel);
            await SettingsChanged();
        } catch (Exception error) { if (!disposed) { status = error.ToString(); await InvokeAsync(StateHasChanged); } }
    }
    private async Task SettingsChanged()
    {
        if (example is null || host is null || disposed) return;
        if (!example.FeaturesEnabled || !example.GizmoEnabled) example.EndContact(true);
        await host.InvokeVoidAsync("configure", ControllerEnabled && example.FeaturesEnabled, hdr, example.Continuous,
            example.StatisticsVisible, StatisticsMode, example.SnapshotInterval, mapKeys,
            example.GizmoEnabled && example.FeaturesEnabled);
    }
    private async Task NavigateButton(string action)
    {
        if (example is null) return;
        if (id == "fly-controls") example.Navigate(0, 0, 0, 0, 0, 1 << Array.IndexOf(NavigationActions, action), 1f / 12);
        else {
            int index = Array.IndexOf(NavigationActions, action);
            example.Navigate(index == 0 ? -MathF.PI / 36 : index == 1 ? MathF.PI / 36 : 0,
                index == 2 ? -MathF.PI / 36 : index == 3 ? MathF.PI / 36 : 0,
                index == 4 ? -.05f : index == 5 ? .05f : 0, index == 6 ? .05f : index == 7 ? -.05f : 0,
                index == 8 ? .12f : index == 9 ? -.12f : 0, 0, 1f / 12);
        }
        await SettingsChanged();
    }
    private async Task ResetPose() { example?.ResetPose(); await SettingsChanged(); }
    private async Task ResetStatistics() { example?.ResetStatistics(); if (host is not null) await host.InvokeVoidAsync("resetStatistics"); await SettingsChanged(); }
    private async Task ToggleMotion() { if (example is not null) example.Continuous = !example.Continuous; await SettingsChanged(); }
    [JSInvokable] public async Task<string> DrawFrame(int width, int height, double delta, double interval, double ratio, double logicalWidth, double logicalHeight,
        float rotateX, float rotateY, float panX, float panY, float dolly, int keys, bool outputHdr, bool refresh)
    {
        if (disposed || session is null || example is null) return "{\"continuous\":false}";
        float elapsed = (float)Math.Clamp(delta, 0, .1);
        example.Navigate(rotateX, rotateY, panX, panY, dolly, keys, elapsed);
        await session.DrawExampleAsync(width, height, 0, outputHdr, refresh, (device, target, depth) => example.Render(device, target, depth, elapsed));
        if (disposed) return "{\"continuous\":false}";
        string report = example.CompleteFrame(++frame, logicalWidth, logicalHeight, interval <= 0 || refresh);
        status = example.Status;
        if (example.Gizmo is not null || frame == 1) await InvokeAsync(StateHasChanged);
        return report;
    }
    [JSInvokable] public int BeginContact(double u, double v) => example?.BeginContact(u, v) ?? 0;
    [JSInvokable] public void UpdateContact(double u, double v) => example?.UpdateContact(u, v);
    [JSInvokable] public void EndContact(bool cancel) => example?.EndContact(cancel);
    [JSInvokable] public void SelectAt(double u, double v) => example?.SelectAt(u, v, selectionMask);
    [JSInvokable] public string AnchorSourceId() => example?.Anchors?.SourceId ?? throw new InvalidOperationException("No anchor source.");
    [JSInvokable] public string ConfigureAnchor(string registration, string target, double x, double y, double z) => example!.ConfigureAnchor(registration, target, x, y, z);
    [JSInvokable] public long RemoveAnchor(string registration) => example!.RemoveAnchor(registration);
    [JSInvokable] public Task RenderFailed(string message) { if (disposed) return Task.CompletedTask; status = message; return InvokeAsync(StateHasChanged); }
    [JSInvokable] public Task StatisticsDisplayModeChanged(string mode)
    {
        if (disposed || example is null) return Task.CompletedTask;
        example.StatisticsDisplayMode = ParseStatisticsMode(mode);
        return InvokeAsync(StateHasChanged);
    }
    [JSInvokable] public void Disconnect() { session?.Dispose(); session = null; }
    private async Task StopAsync()
    {
        if (startup is not null) await startup;
        try { if (host is not null) { await host.InvokeVoidAsync("dispose"); await host.DisposeAsync(); } }
        finally { host = null; Disconnect(); reference?.Dispose(); reference = null; example?.Dispose(); example = null; if (module is not null) await module.DisposeAsync(); module = null; startup = null; }
    }
    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());
    private async Task DisposeCoreAsync() { disposed = true; await StopAsync(); }
}

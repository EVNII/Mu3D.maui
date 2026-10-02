using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Mu3D.Color;
using Mu3D.GalleryApp.Examples;
using Mu3D.GalleryApp.Pages;
using Mu3D.GalleryApp.Web.Infrastructure;
using Mu3D.Rendering;
using Mu3D.SceneGraph;
using Mu3D.Toolkit.Selection;

namespace Mu3D.GalleryApp.Web.Pages;

public partial class PointerPen
{
    private readonly NativeGalleryScene definition = NativeGalleryScene.Read("PointerPenInputPage");
    private readonly SceneRaycaster raycaster = new();
    private RenderSession? session;
    private SceneRenderer? renderer;
    private ApplePencilTipShadowPreview? shadow;
    private PenTipDisplayNormalShadowPass? shadowPass;
    private IReadOnlyList<Mesh>? casters;
    private PenTipDisplayNormalShadowState? shadowState;
    private PenShadowInput? lastContactShadow;
    private SceneRaycastIntersection? lastContactHit;
    private IJSObjectReference? module, host;
    private DotNetObjectReference<PointerPen>? reference;
    private ElementReference canvas, cursor, tiltLine;
    private Task? startup, disposal;
    private bool disposed, contactActive, shadowEnabled = true;
    private float shadowCoefficient = .78f, blurDecay = .55f, shadowDecay = 1f / 42, pointsPerInch = 96;
    private uint width, height;
    private double logicalWidth, logicalHeight;
    private long lastRaycast;
    private int sampleCount;
    private string status = "正在连接 GPU…";
    private Mesh Marker => (Mesh)definition.NamedNodes["HitMarker"];

    protected override async Task OnAfterRenderAsync(bool firstRender)
    { if (firstRender) { startup = StartAsync(); await startup; } }
    private async Task StartAsync()
    {
        try {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./pointer-pen-viewport.mjs"); if (disposed) return;
            session = await RenderSession.OpenAsync(); if (disposed) return;
            reference = DotNetObjectReference.Create(this);
            host = await module.InvokeAsync<IJSObjectReference>("create", canvas, cursor, tiltLine, reference, session.MaximumDimension);
            CreateShadow(); await SettingsChanged();
        } catch (Exception error) { if (!disposed) { status = error.ToString(); await InvokeAsync(StateHasChanged); } }
    }
    private void CreateShadow()
    {
        shadowPass?.Dispose(); shadowPass = null;
        shadow = new((meshes, state) => { casters = meshes; shadowState = state; }, pointsPerInch);
    }
    private async Task CalibrationChanged() { if (disposed) return; pointsPerInch = Math.Clamp(pointsPerInch, 50, 300); CreateShadow(); await SettingsChanged(); }
    private async Task SettingsChanged()
    {
        if (shadow is not null) {
            shadow.ShadowCoefficient = shadowCoefficient; shadow.BlurDecayCoefficient = blurDecay; shadow.ShadowDecayCoefficient = shadowDecay;
            if (shadowEnabled && lastContactShadow is PenShadowInput contact && width > 0 && height > 0 && logicalHeight > 0)
                shadow.Update(contact, lastContactHit, definition.Camera, width, height, logicalHeight);
            else shadow.Hide();
        }
        if (host is not null && !disposed) await host.InvokeVoidAsync("invalidate");
    }
    private async Task Clear() { Marker.IsVisible = false; status = "Marker cleared; application owns input and raycast policy."; await SettingsChanged(); }
    [JSInvokable] public async Task DrawFrame(int w, int h, double logicalW, double logicalH, bool refresh)
    {
        if (disposed || session is null) return;
        width = (uint)w; height = (uint)h; logicalWidth = logicalW; logicalHeight = logicalH;
        definition.Camera.AspectRatio = (float)w / h;
        await session.DrawExampleAsync(w, h, 0, true, refresh, (device, target, depth) => {
            renderer ??= new SceneRenderer(device, target.Descriptor.Format);
            renderer.Render(definition.Scene, definition.Camera, target, depth!);
            if (shadowState?.IsVisible == true) {
                shadowPass ??= new(renderer, casters!, shadowState, StandardColorSpaces.LinearSrgb);
                shadowPass.Execute(new(definition.Scene, definition.Camera, target, depth, StandardColorSpaces.LinearSrgb,
                    colorTargetInitialized: true, depthTargetInitialized: true));
            }
        });
    }
    [JSInvokable] public string PointerSamples(string json)
    {
        if (disposed) return "{\"visible\":false,\"x\":0,\"y\":0,\"rotation\":0,\"red\":0,\"green\":0,\"blue\":0}";
        BrowserPenSample[] samples = JsonSerializer.Deserialize(json, PointerPenJsonContext.Default.BrowserPenSampleArray) ?? [];
        PointerCursorReport cursorReport = new(false, 0, 0, 0, 53 / 255f, 242 / 255f, 208 / 255f);
        foreach (BrowserPenSample sample in samples) {
            sampleCount++;
            bool pen = sample.PointerType == "pen", hover = sample.Phase == "hover", terminal = sample.Phase is "cancel" or "exit";
            int palette = PenTipPalette.Index(pen, sample.TiltX, sample.TiltY);
            Vector3 rgb = PenTipPalette.EncodedColor(palette);
            cursorReport = new(pen && !terminal, Math.Clamp(sample.U * logicalWidth - 21, 0, Math.Max(0, logicalWidth - 42)),
                Math.Clamp(sample.V * logicalHeight - 21, 0, Math.Max(0, logicalHeight - 42)), PenTipPalette.Rotation(sample.TiltX, sample.TiltY), rgb.X, rgb.Y, rgb.Z);
            if (terminal) { contactActive = false; lastContactShadow = null; lastContactHit = null; shadow?.Hide(); status = $"#{sampleCount} contact canceled/left viewport"; continue; }
            if (hover) { lastContactShadow = null; lastContactHit = null; shadow?.Hide(); status = $"#{sampleCount} {sample.PointerType}/hover · pressure {Optional(sample.Pressure)} · tilt {Optional(sample.TiltX)}°/{Optional(sample.TiltY)}° · hover distance unknown"; continue; }
            if (sample.Phase == "down") contactActive = true;
            if (!contactActive) continue;
            long now = Stopwatch.GetTimestamp();
            if (sample.Phase == "move" && Stopwatch.GetElapsedTime(lastRaycast, now).TotalSeconds < 1d / 60) continue;
            lastRaycast = now;
            Vector2 point = new((float)sample.U * width, (float)sample.V * height);
            if (width == 0 || height == 0) { status = "Waiting for a renderable viewport"; continue; }
            bool hit = raycaster.TryHitClosest(definition.Scene, definition.Camera, width, height, point, out SceneRaycastIntersection intersection,
                mesh => !ReferenceEquals(mesh, Marker));
            if (hit) {
                Marker.Transform.Position = intersection.WorldPosition;
                Marker.Transform.Scale = new Vector3(.65f + (sample.Pressure ?? .5f) * 1.75f);
                Marker.IsVisible = true;
                static float Decode(float encoded) => encoded <= .04045f ? encoded / 12.92f : MathF.Pow((encoded + .055f) / 1.055f, 2.4f);
                ((UnlitMaterial)Marker.Material).Color = new(Decode(rgb.X), Decode(rgb.Y), Decode(rgb.Z), 1, StandardColorSpaces.LinearSrgb);
            } else Marker.IsVisible = false;
            if (pen && sample.Phase != "up") {
                lastContactShadow = new(point, sample.TiltX, sample.TiltY, null, false); lastContactHit = hit ? intersection : null;
                if (shadowEnabled) shadow?.Update(lastContactShadow.Value, lastContactHit, definition.Camera, width, height, logicalHeight);
                else shadow?.Hide();
            } else { lastContactShadow = null; lastContactHit = null; shadow?.Hide(); }
            status = $"#{sampleCount} {sample.PointerType}/{sample.Phase} · px {point.X:0},{point.Y:0} · pressure {Optional(sample.Pressure)} · tilt {Optional(sample.TiltX)}°/{Optional(sample.TiltY)}° · {(sample.Eraser ? "eraser" : sample.Barrel ? "barrel" : "tip")} · " +
                (hit ? $"hit ({intersection.WorldPosition.X:0.00}, {intersection.WorldPosition.Y:0.00}, {intersection.WorldPosition.Z:0.00})" : "no visible triangle hit");
            if (sample.Phase == "up") contactActive = false;
        }
        _ = InvokeAsync(StateHasChanged);
        return JsonSerializer.Serialize(cursorReport, PointerPenJsonContext.Default.PointerCursorReport);
    }
    private static string Optional(float? number) => number?.ToString("0.00", CultureInfo.InvariantCulture) ?? "unknown";
    [JSInvokable] public Task RenderFailed(string message) { if (disposed) return Task.CompletedTask; status = message; return InvokeAsync(StateHasChanged); }
    [JSInvokable] public void Disconnect() { shadowPass?.Dispose(); shadowPass = null; renderer?.Dispose(); renderer = null; session?.Dispose(); session = null; }
    public ValueTask DisposeAsync() => new(disposal ??= DisposeCoreAsync());
    private async Task DisposeCoreAsync()
    {
        disposed = true; if (startup is not null) await startup;
        try { if (host is not null) { await host.InvokeVoidAsync("dispose"); await host.DisposeAsync(); } }
        finally { Disconnect(); reference?.Dispose(); if (module is not null) await module.DisposeAsync(); }
    }
}

internal sealed record BrowserPenSample(string PointerType, string Phase, double U, double V, float? Pressure, float? TiltX, float? TiltY, bool Eraser, bool Barrel);
internal sealed record PointerCursorReport(bool Visible, double X, double Y, float Rotation, float Red, float Green, float Blue);
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BrowserPenSample[]))]
[JsonSerializable(typeof(PointerCursorReport))]
internal partial class PointerPenJsonContext : JsonSerializerContext;

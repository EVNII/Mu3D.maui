using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Maui.Controls.Xaml;
using Microsoft.Maui.Dispatching;
using Mu3D.Color;
using Mu3D.Maui.Controls;
using Mu3D.SceneGraph;

DispatcherProvider.SetCurrent(new ImmediateTestDispatcherProvider());
FurnacePickerChecks.Run();
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
OpenPbrEditorChecks.Run();
foreach (var (name, encoding) in new[] { ("display-p3", StandardRgbEncoding.DisplayP3), ("rec2020", StandardRgbEncoding.Rec2020), ("a98-rgb", StandardRgbEncoding.AdobeRgb), ("prophoto-rgb", StandardRgbEncoding.ProPhotoRgb) })
{
    OpenPbrSurface3D wide = new();
    wide.LoadFromXaml($"""
        <local:OpenPbrSurface3D xmlns:local="clr-namespace:Mu3D.Maui.Controls;assembly=Mu3D.Maui.Authoring.Tests" BaseColor="0.2,0.4,0.8;{name}" />
        """);
    var expected = StandardRgbEncodingConverter.Decode(new(.2f,.4f,.8f,1,encoding));
    Near(expected.Red, wide.BaseColor.Red); Near(expected.Green, wide.BaseColor.Green);
    Near(expected.Blue, wide.BaseColor.Blue); Near(1, wide.BaseColor.Alpha);
    if (wide.BaseColor.ColorSpace != expected.ColorSpace) throw new Exception("XAML lost wide-gamut space.");
}
OpenPbrSurface3D surface = new();
surface.LoadFromXaml("""
    <local:OpenPbrSurface3D xmlns:local="clr-namespace:Mu3D.Maui.Controls;assembly=Mu3D.Maui.Authoring.Tests"
        BaseColor="0.2,0.4,0.8;acescg" BaseMetalness="0.75" SpecularRoughness="0.22"
        GeometryTangent="1,0,0" />
    """);
Near(0.2f, surface.BaseColor.Red);
if (surface.BaseColor.ColorSpace != StandardColorSpaces.AcesCg || surface.GeometryTangent != Vector3.UnitX)
    throw new Exception("XAML color/vector metadata failed.");
surface.GeometryTangent = null;
OpenPbrPreviewMaterial3D preview = new() { Surface = surface };
if (!preview.IsPreviewAvailable) throw new Exception(preview.PreviewError);
Material stable = preview.CoreMaterial;
int changes = 0;
preview.Changed += (_, _) => changes++;
surface.BaseMetalness = 0.25f;
Near(0.25f, ((PbrMaterial)stable).Metallic);
if (changes == 0 || !ReferenceEquals(stable, preview.CoreMaterial)) throw new Exception("Live preview did not update in place.");
surface.SubsurfaceWeight = 0.5f;
if (preview.IsPreviewAvailable || preview.PreviewError is null || ((PbrMaterial)stable).BaseColor.Alpha != 0)
    throw new Exception("Unsupported state must hide stale preview and expose diagnostics.");
preview.PreviewPolicy = OpenPbrPreviewPolicy.AllowLossyApproximation;
if (!preview.IsPreviewAvailable || preview.PreviewResult?.HasLossyMappings != true)
    throw new Exception("Explicit approximation was not diagnosed.");
OpenPbrSurface imported = new() { BaseMetalness = 0.9f, EmissionLuminance = 800f };
surface.LoadSurface(imported);
if (preview.IsPreviewAvailable) throw new Exception("Physical emission needs a scale.");
preview.NitsPerSceneUnit = 100f;
if (!preview.IsPreviewAvailable) throw new Exception(preview.PreviewError);
Near(8f, ((PbrMaterial)stable).EmissiveStrength);
OpenPbrSurface snapshot = surface.ToSurface();
surface.BaseMetalness = 0.1f;
Near(0.9f, snapshot.BaseMetalness);
surface.SetBinding(OpenPbrSurface3D.BaseMetalnessProperty, new Binding("Value"));
surface.BindingContext = new { Value = 0.65f };
Near(0.65f, ((PbrMaterial)stable).Metallic);
OpenPbrSurface3D oldSurface = surface;
preview.Surface = new OpenPbrSurface3D();
preview.Surface.SetBinding(OpenPbrSurface3D.BaseMetalnessProperty, new Binding("Value"));
preview.BindingContext = new { Value = 0.45f };
Near(0.45f, ((PbrMaterial)stable).Metallic);
preview.Surface.BindingContext = new { Value = 0.35f };
preview.BindingContext = new { Value = 0.55f };
Near(0.35f, ((PbrMaterial)stable).Metallic);
preview.Surface.GeometryThinWalled = true;
if (!((PbrMaterial)stable).IsDoubleSided) throw new Exception("Thin-wall preview sidedness was lost.");
preview.IsDoubleSided = true;
preview.IsDoubleSided = false;
if (!((PbrMaterial)stable).IsDoubleSided) throw new Exception("Wrapper update erased thin-wall preview sidedness.");
changes = 0;
oldSurface.BaseWeight = 0.4f;
if (changes != 0) throw new Exception("Replaced surface retained preview subscription.");
preview.ClearValue(OpenPbrPreviewMaterial3D.SurfaceProperty);
preview.Surface.BaseMetalness = 0.8f;
Near(0.8f, ((PbrMaterial)stable).Metallic);
OpenPbrSurface3D sharedSurface = new();
WeakReference discardedPreview = CreateDiscardedPreview(sharedSurface);
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
if (discardedPreview.IsAlive) throw new Exception("A shared authoring surface retained a discarded preview wrapper.");
sharedSurface.BaseMetalness = 0.7f;
GC.KeepAlive(sharedSurface);
// Retain the import-then-slider binding regression for application-owned editors.
Slider importSlider = new() { Minimum = 0.045, Maximum = 1, Value = 0.3 };
OpenPbrSurface3D importedAuthoring = new();
importedAuthoring.SetBinding(OpenPbrSurface3D.SpecularRoughnessProperty,
    new Binding(nameof(Slider.Value), source: importSlider));
OpenPbrPreviewMaterial3D importedPreview = new() { Surface = importedAuthoring };
importedAuthoring.LoadSurface(new OpenPbrSurface { SpecularRoughness = 0.22f });
importSlider.Value = importedAuthoring.SpecularRoughness;
importedAuthoring.SetBinding(OpenPbrSurface3D.SpecularRoughnessProperty,
    new Binding(nameof(Slider.Value), source: importSlider));
importSlider.Value = 0.8;
Near(0.8f, importedAuthoring.SpecularRoughness);
Near(0.8f, ((PbrMaterial)importedPreview.CoreMaterial).Roughness);
if (!importedPreview.IsPreviewAvailable) throw new Exception(importedPreview.PreviewError);
UnlitMaterial3D canvasMaterial = new();
LinearRgbaImage image = new(1, 1, [new Vector4(4, -0.25f, 1, 1)], StandardColorSpaces.AcesCg);
canvasMaterial.Texture = image;
if (!ReferenceEquals(canvasMaterial.UnlitMaterial.BaseColorTexture, image)) throw new Exception("HDR texture binding failed.");
ColorView3D displayView = new();
displayView.LoadFromXaml("""
    <local:ColorView3D xmlns:local="clr-namespace:Mu3D.Maui.Controls;assembly=Mu3D.Maui.Authoring.Tests"
        Preset="Aces2Hdr1000" ExposureStops="1.5" ReferenceWhiteNits="200" />
    """);
ColorViewTransform displayTransform = displayView.ToTransform(StandardColorSpaces.LinearSrgb);
if (displayTransform.Preset != ColorViewPreset.Aces2Hdr1000 || !displayTransform.IsHdr)
    throw new Exception("XAML display preset did not survive construction.");
Near(1.5f, displayTransform.ExposureStops); Near(200, displayTransform.ReferenceWhiteNits);
if (!ReferenceEquals(displayTransform, displayView.ToTransform(StandardColorSpaces.LinearSrgb)))
    throw new Exception("Unchanged display facade did not reuse its transform.");
int displayChanges = 0; displayView.Changed += (_, _) => displayChanges++;
displayView.SetBinding(ColorView3D.ExposureStopsProperty, new Binding("Value"));
displayView.BindingContext = new { Value = -2.5f };
Near(-2.5f, displayView.ToTransform(StandardColorSpaces.LinearSrgb).ExposureStops);
if (displayChanges < 1 || ReferenceEquals(displayTransform, displayView.ToTransform(StandardColorSpaces.LinearSrgb)))
    throw new Exception("Display binding failed to invalidate the immutable transform.");
LinearRgba displayHighlight = displayView.ToTransform(StandardColorSpaces.AcesCg).Transform(
    new LinearRgba(100,100,100,0.5f,StandardColorSpaces.AcesCg));
if (displayHighlight.Red <= 1 || displayHighlight.Alpha != 0.5f)
    throw new Exception("XAML display view lost HDR or alpha.");
{
    // VsyncFrameSource (linked from Mu3D.Maui.Toolkit) without a handler/host: explicit decline, never a fake loop.
    int ticks = 0;
    Mu3D.Maui.Toolkit.Controls.VsyncFrameSource frameSource = new(() => ticks++);
    if (frameSource.IsRunning || frameSource.MeasuredTicksPerSecond != 0 || frameSource.ScreenMaximumFramesPerSecond != 0)
        throw new Exception("Frame source must start idle.");
    if (frameSource.Start(new Label()) || ticks != 0) throw new Exception("A handler-less element must not start a frame source.");
    frameSource.Stop(); frameSource.Stop();
    frameSource.Dispose(); frameSource.Dispose();
    try { frameSource.Start(new Label()); throw new Exception("Disposed frame source accepted Start."); }
    catch (ObjectDisposedException) { }
    try { _ = new Mu3D.Maui.Toolkit.Controls.VsyncFrameSource(null!); throw new Exception("Frame source accepted a null callback."); }
    catch (ArgumentNullException) { }
    try { new Mu3D.Maui.Toolkit.Controls.VsyncFrameSource(() => { }).Start(null!); throw new Exception("Frame source accepted a null anchor."); }
    catch (ArgumentNullException) { }
}
Console.WriteLine("Color view XAML parsing, culture invariance, binding, cache invalidation, HDR and alpha passed.");
Console.WriteLine($"OpenPBR MAUI authoring: {OpenPbrMaterialAuthoringChecks.Run()} checks passed.");
Console.WriteLine("MAUI authoring regressions passed: real XAML parsing, explicit color/vector conversion, binding, preview updates/failure/recovery, detached and weak subscriptions, import-then-slider binding, HDR texture.");

static void Near(float expected, float actual)
{
    if (MathF.Abs(expected - actual) > 0.00001f) throw new Exception($"Expected {expected}, got {actual}.");
}

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference CreateDiscardedPreview(OpenPbrSurface3D sharedSurface) =>
    new(new OpenPbrPreviewMaterial3D { Surface = sharedSurface });

sealed class ImmediateTestDispatcherProvider : IDispatcherProvider
{
    private readonly IDispatcher dispatcher = new ImmediateTestDispatcher();
    public IDispatcher GetForCurrentThread() => dispatcher;
}

sealed class ImmediateTestDispatcher : IDispatcher
{
    public bool IsDispatchRequired => false;
    public bool Dispatch(Action action) { action(); return true; }
    public bool DispatchDelayed(TimeSpan delay, Action action) => throw new NotSupportedException();
    public IDispatcherTimer CreateTimer() => throw new NotSupportedException();
}

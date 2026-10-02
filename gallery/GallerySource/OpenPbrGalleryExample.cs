using System.Numerics;
using Mu3D.Color;
using Mu3D.Formats.MaterialX;
using Mu3D.GalleryApp.Pages;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.Rendering.OpenPbr;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Examples;

// Portable example state. MAUI may supply its XAML-authored Core objects; Web creates the
// corresponding scene here. Neither host converts OpenPBR to the glTF PBR material model.
internal sealed class OpenPbrGalleryExample : IDisposable
{
    internal Scene Scene { get; }
    internal PerspectiveCamera Camera { get; }
    internal OpenPbrMaterial Material { get; }
    internal OpenPbrSurface Surface => Material.Surface;
    internal OpenPbrRenderPass Transport { get; } = new()
    {
        Mode = OpenPbrRenderMode.Raster,
        EnvironmentRadiance = new(.08f, .08f, .08f, 1, StandardColorSpaces.AcesCg),
        BackgroundAlpha = 1,
    };
    internal bool UsingTextures { get; private set; }
    private GraphicsDevice? displayDevice;
    private GraphicsTexture? displaySource;
    private ColorViewGpuTransform? display;
    private int displayIndex;
    private bool disposed;

    internal OpenPbrGalleryExample() : this(CreateScene()) { }
    private OpenPbrGalleryExample((Scene Scene, PerspectiveCamera Camera, OpenPbrMaterial Material) objects)
        : this(objects.Scene, objects.Camera, objects.Material) { }
    internal OpenPbrGalleryExample(Scene scene, PerspectiveCamera camera, OpenPbrMaterial material)
    {
        Scene = scene; Camera = camera; Material = material;
        UsingTextures = material.Surface.Graph is not null;
    }

    private static (Scene, PerspectiveCamera, OpenPbrMaterial) CreateScene()
    {
        var material = new OpenPbrMaterial(new()
        {
            BaseColor = new(.08f, .35f, .8f, 1, StandardColorSpaces.AcesCg),
            BaseMetalness = .25f, CoatWeight = .4f, CoatRoughness = .2f,
            SpecularRoughness = .3f, Graph = OpenPbrTextureExample.Create(.3f),
        }) { NitsPerSceneUnit = 100 };
        var scene = new Scene();
        scene.Add(new Mesh(MeshPrimitives.CreateUvSphere(), material));
        var pedestal = new Mesh(MeshPrimitives.CreateUvSphere(), new OpenPbrMaterial(new()
        { BaseColor = new(.5f, .5f, .5f, 1, StandardColorSpaces.AcesCg), SpecularRoughness = .55f }));
        pedestal.Transform.Position = new(0, -1.12f, 0);
        pedestal.Transform.Scale = new(1.8f, .12f, 1.4f);
        scene.Add(pedestal);
        var light = new DirectionalLight(new(1, 1, 1, 1, StandardColorSpaces.LinearSrgb), 4) { CastsShadows = true };
        light.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-30 * MathF.PI / 180, -25 * MathF.PI / 180, 0);
        scene.Add(light);
        return (scene, CameraAt(5, 50), material);
    }

    private static PerspectiveCamera CameraAt(float z, float fieldOfView) => new(fieldOfView * MathF.PI / 180)
        { Transform = { Position = new(0, 0, z) } };

    internal static string? ModeError(OpenPbrSurface surface, OpenPbrRenderMode mode)
    {
        if (mode is not (OpenPbrRenderMode.Raster or OpenPbrRenderMode.Fast)) return null;
        float transmission = surface.Graph?.Maximum(OpenPbrInput.TransmissionWeight, surface.TransmissionWeight) ?? surface.TransmissionWeight;
        float opacity = surface.Graph?.Minimum(OpenPbrInput.GeometryOpacity, surface.GeometryOpacity) ?? surface.GeometryOpacity;
        float subsurface = surface.Graph?.Maximum(OpenPbrInput.SubsurfaceWeight, surface.SubsurfaceWeight) ?? surface.SubsurfaceWeight;
        return transmission > 0 || opacity < 1 || (mode == OpenPbrRenderMode.Raster && subsurface > 0)
            ? $"{mode} cannot render this transmission, opacity or subsurface setting. Select Hybrid, Interactive or Reference first."
            : null;
    }

    internal void ApplySurface(OpenPbrSurface surface)
    {
        if (ModeError(surface, Transport.Mode) is { } error) throw new NotSupportedException(error);
        Material.Surface = surface;
        UsingTextures = surface.Graph is not null;
        Transport.ResetAccumulation();
    }

    internal void SetRoughness(float roughness)
    {
        Surface.SpecularRoughness = roughness;
        if (UsingTextures) Surface.Graph = OpenPbrTextureExample.Create(roughness);
        Transport.ResetAccumulation();
    }

    internal void UseTextureMaterial(float roughness)
    {
        ApplySurface(new() { Graph = OpenPbrTextureExample.Create(roughness), SpecularRoughness = roughness,
            BaseMetalness = .25f, CoatWeight = .4f, CoatRoughness = .2f });
    }

    internal async Task LoadAsync(Stream stream)
    {
        MaterialXOpenPbrDocument document = await MaterialXOpenPbrSerializer.ImportAsync(stream);
        if (disposed) return;
        ApplySurface(document.Surfaces[0].Surface);
    }

    internal long RoundTrip()
    {
        MaterialXOpenPbrDocument document = new([new MaterialXOpenPbrSurface("surface", Surface)],
            [new MaterialXSurfaceMaterial("material", "surface")]);
        using MemoryStream stream = new();
        MaterialXOpenPbrSerializer.Export(stream, document); stream.Position = 0;
        ApplySurface(MaterialXOpenPbrSerializer.Import(stream, new()
            { TextureResolver = OpenPbrTextureExample.Resolve }).Surfaces[0].Surface);
        return stream.Length;
    }

    internal void Draw(GraphicsDevice device, GraphicsTexture target, GraphicsTexture? depth, int view)
    {
        Camera.AspectRatio = (float)target.Descriptor.Size.Width / target.Descriptor.Size.Height;
        if (view == 0)
        {
            ReleaseDisplay();
            Transport.Execute(new(Scene, Camera, target, depth, StandardColorSpaces.LinearSrgb));
            return;
        }
        ColorViewPreset preset = ViewPreset(view);
        if (displayDevice != device || displayIndex != view)
        {
            ReleaseDisplay(); displayDevice = device; displayIndex = view;
            display = new(device, new(preset, StandardColorSpaces.LinearSrgb), GraphicsTextureFormat.Rgba16Float);
        }
        if (displaySource?.Descriptor.Size != target.Descriptor.Size)
        {
            displaySource?.Dispose(); displaySource = null;
            displaySource = device.CreateTexture(new(target.Descriptor.Size, GraphicsTextureFormat.Rgba16Float,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding));
        }
        Transport.Execute(new(Scene, Camera, displaySource, depth, StandardColorSpaces.LinearSrgb));
        display!.Apply(displaySource, StandardColorSpaces.LinearSrgb, target, StandardColorSpaces.LinearSrgb);
    }

    internal static ColorViewPreset ViewPreset(int index) => index switch
    {
        1 => ColorViewPreset.AgXHdr1000, 2 => ColorViewPreset.Aces2Hdr1000,
        3 => ColorViewPreset.AgXSdr, 4 => ColorViewPreset.Aces2Sdr,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    internal static string SamplingSummary(OpenPbrRenderPass pass) => pass.Mode switch
    {
        OpenPbrRenderMode.Reference => $"Reference: {pass.AccumulatedSamples:N0} samples per pixel, up to 32 path events.",
        OpenPbrRenderMode.Hybrid => $"Hybrid: {pass.AccumulatedSamples:N0} samples, four path events; shadows, reflection and indirect light. " +
            (pass.UsesPrimaryRayFallback ? "Primary rays preserve near-plane / medium boundaries." : "Raster primary visibility; secondary sampling can be noisy."),
        OpenPbrRenderMode.Raster => $"Raster: direct light + environment; directional shadows {(pass.DirectionalShadowsEnabled ? "on" : "off")}; no indirect transport.",
        OpenPbrRenderMode.Fast => FastSummary(pass),
        _ => "Interactive: half resolution, up to four path events per frame.",
    };

    private static string FastSummary(OpenPbrRenderPass pass)
    {
        if (pass.FastApproximations.Count == 0) return $"Fast: directional shadows {(pass.DirectionalShadowsEnabled ? "on" : "off")}; no indirect transport; no compiled surfaces yet.";
        OpenPbrFastApproximationKinds kinds = pass.FastApproximations.Aggregate(
            OpenPbrFastApproximationKinds.None, (all, item) => all | item.Kinds);
        List<string> approximations = [];
        if (kinds.HasFlag(OpenPbrFastApproximationKinds.FuzzEnvironmentCharlieChain)) approximations.Add("fuzz→Charlie chain");
        if (kinds.HasFlag(OpenPbrFastApproximationKinds.CoatEnvironmentBaseGgxChain)) approximations.Add("coat→GGX chain");
        if (kinds.HasFlag(OpenPbrFastApproximationKinds.SubsurfaceDropped)) approximations.Add("subsurface dropped");
        if (kinds.HasFlag(OpenPbrFastApproximationKinds.ThinFilmDropped)) approximations.Add("thin film dropped");
        string baked = kinds.HasFlag(OpenPbrFastApproximationKinds.BakedGraphTextures)
            ? $"{pass.FastApproximations.Sum(item => item.BakedTextureCount)} baked graph textures ({pass.FastApproximations.Sum(item => item.BakedBytes) / 1024.0:F0} KiB)"
            : "no baked graph textures";
        return $"Fast: directional shadows {(pass.DirectionalShadowsEnabled ? "on" : "off")}; no indirect transport; split-sum IBL; {baked}; approximations: " +
            $"{(approximations.Count > 0 ? string.Join(", ", approximations) : "none beyond split-sum")}.";
    }

    private void ReleaseDisplay()
    {
        displaySource?.Dispose(); displaySource = null; display?.Dispose(); display = null; displayDevice = null;
    }
    public void Dispose() { if (disposed) return; disposed = true; ReleaseDisplay(); Transport.Dispose(); }
}

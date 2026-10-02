using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.Toolkit.Controls;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Small Canvas adapter over the same native scene data and public renderer/display-view APIs.
internal sealed class FocusedGalleryScene : IDisposable
{
    internal NativeGalleryScene Definition { get; }
    internal ColorViewPreset? View { get; set; }
    internal float Exposure { get; set; }
    internal bool Hdr { get; set; } = true;
    internal bool HasPendingMotion => orbit.HasPendingMotion;
    private readonly OrbitController orbit;
    private readonly LinearRgba clear;
    private SceneRenderer? renderer;
    private GraphicsTexture? sceneTarget;
    private ColorViewGpuTransform? display;

    internal FocusedGalleryScene(string nativePage, bool transparent = false)
    {
        Definition = NativeGalleryScene.Read(nativePage);
        clear = new(0, 0, 0, transparent ? 0 : 1, StandardColorSpaces.LinearSrgb);
        orbit = new(Definition.Camera, Vector3.Zero)
        { DampingEnabled = true, DampingTime = .1f, MinimumDistance = 1.5f, MaximumDistance = 40 };
    }

    internal void ApplyInput(float rotateX, float rotateY, float dolly)
    {
        orbit.Rotate(new Vector2(rotateX, rotateY));
        orbit.Dolly(dolly);
    }

    internal void Draw(GraphicsDevice device, GraphicsTexture target, GraphicsTexture? depth, double delta)
    {
        orbit.Update((float)delta);
        Definition.Camera.AspectRatio = (float)target.Descriptor.Size.Width / target.Descriptor.Size.Height;
        renderer ??= new(device, target.Descriptor.Format);
        if (View is not ColorViewPreset preset)
        {
            renderer.Render(Definition.Scene, Definition.Camera, target, depth!, clear);
            return;
        }

        ColorViewTransform transform = new(preset, StandardColorSpaces.LinearSrgb, Exposure, 100);
        if (transform.IsHdr && !Hdr)
            throw new NotSupportedException("The selected HDR view requires HDR output. Select an SDR view explicitly.");
        if (sceneTarget?.Descriptor.Size != target.Descriptor.Size)
        {
            sceneTarget?.Dispose();
            sceneTarget = device.CreateTexture(new(target.Descriptor.Size, target.Descriptor.Format,
                GraphicsTextureUsage.RenderAttachment | GraphicsTextureUsage.TextureBinding,
                label: "Gallery scene-linear display input"));
        }
        renderer.Render(Definition.Scene, Definition.Camera, sceneTarget, depth!, clear);
        if (display is null || display.Transform.Preset != preset)
        {
            display?.Dispose();
            display = new(device, transform, target.Descriptor.Format, ColorEncoding.ExtendedSrgbLinear,
                inputPremultiplied: true, outputPremultiplied: true);
        }
        else display.UpdateTransform(transform);
        display.Apply(sceneTarget, StandardColorSpaces.LinearSrgb, target, StandardColorSpaces.LinearSrgb);
    }

    internal void ReleaseGraphics()
    {
        display?.Dispose(); display = null;
        sceneTarget?.Dispose(); sceneTarget = null;
        renderer?.Dispose(); renderer = null;
    }

    public void Dispose() { ReleaseGraphics(); orbit.Dispose(); }
}

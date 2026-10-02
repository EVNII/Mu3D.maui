using System.Numerics;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.Rendering;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

internal sealed record KtxTextureLabTextures(
    CompressedMaterialTexture? CompressedColor,
    LinearRgbaImage DecodedColor,
    CompressedMaterialTexture? CompressedNormal,
    NormalizedRgbaDataImage DecodedNormal);

internal sealed class KtxTextureLabPresenter : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly Scene scene = new("KTX2 GPU sampling");
    private readonly PerspectiveCamera camera = new(name: "KTX camera");
    private SceneRenderer? renderer;
    private GraphicsTexture? depthTexture;
    private GraphicsExtent3D depthExtent;

    internal KtxTextureLabPresenter(GraphicsDevice device, KtxTextureLabTextures textures)
    {
        this.device = device ?? throw new ArgumentNullException(nameof(device));
        camera.Transform.Position = new Vector3(0f, 0f, 6f);

        DirectionalLight light = new(
            new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb),
            2.2f,
            "KTX neutral light");
        light.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-0.65f, -0.45f, 0f);
        scene.Add(light);

        MeshGeometry card = CreateCard();
        AddCard(card, CreateMaterial(textures, false, false), -1.05f, 1f);
        AddCard(card, CreateMaterial(textures, true, false), 1.05f, 1f);
        AddCard(card, CreateMaterial(textures, false, true), -1.05f, -1f);
        AddCard(card, CreateMaterial(textures, true, true), 1.05f, -1f);
    }

    internal void Render(GraphicsTexture target, uint width, uint height)
    {
        camera.AspectRatio = (float)width / height;
        renderer ??= new SceneRenderer(device, target.Descriptor.Format);
        renderer.Render(scene, camera, target, EnsureDepthTexture(width, height));
    }

    public void Dispose()
    {
        depthTexture?.Dispose();
        depthTexture = null;
        renderer?.Dispose();
        renderer = null;
    }

    private static PbrMaterial CreateMaterial(
        KtxTextureLabTextures textures,
        bool compressed,
        bool normal)
    {
        PbrMaterial material = new(
            new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb),
            0f,
            0.62f,
            compressed ? "GPU compressed" : "Decoded RGBA8")
        {
            TextureSampling = MaterialTextureSampling.RepeatingLinear,
        };
        if (compressed && textures.CompressedColor is not null)
        {
            material.CompressedBaseColorTexture = textures.CompressedColor;
        }
        else
        {
            material.BaseColorTexture = textures.DecodedColor;
        }
        if (normal && compressed && textures.CompressedNormal is not null)
        {
            material.CompressedNormalTexture = textures.CompressedNormal;
        }
        else if (normal)
        {
            material.NormalTexture = textures.DecodedNormal;
        }
        return material;
    }

    private void AddCard(MeshGeometry geometry, Material material, float x, float y)
    {
        Mesh card = new(geometry, material);
        card.Transform.Position = new Vector3(x, y, 0f);
        scene.Add(card);
    }

    private static MeshGeometry CreateCard()
    {
        const float size = 0.82f;
        return new MeshGeometry(
            [
                new Vector3(-size, -size, 0f),
                new Vector3(size, -size, 0f),
                new Vector3(size, size, 0f),
                new Vector3(-size, size, 0f),
            ],
            [0u, 1u, 2u, 0u, 2u, 3u],
            [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
            [
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f),
                new Vector2(0f, 0f),
            ],
            tangents:
            [
                new Vector4(1f, 0f, 0f, -1f),
                new Vector4(1f, 0f, 0f, -1f),
                new Vector4(1f, 0f, 0f, -1f),
                new Vector4(1f, 0f, 0f, -1f),
            ]);
    }

    private GraphicsTexture EnsureDepthTexture(uint width, uint height)
    {
        GraphicsExtent3D extent = new(width, height);
        if (depthTexture is not null && depthExtent == extent)
        {
            return depthTexture;
        }
        depthTexture?.Dispose();
        depthTexture = device.CreateTexture(new GraphicsTextureDescriptor(
            extent,
            GraphicsTextureFormat.Depth32Float,
            GraphicsTextureUsage.RenderAttachment,
            label: "KTX depth"));
        depthExtent = extent;
        return depthTexture;
    }
}

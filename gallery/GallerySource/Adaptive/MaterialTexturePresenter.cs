using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Gallery.Pages;

/// <summary>
/// Populates a scene with the metallic/roughness example content and exposes the editable
/// material as simple setters. Unlike the low-level original, this presenter owns no renderer or
/// depth resources: the hosting <see cref="Mu3D.Maui.Controls.Mu3DSceneView"/> renders the scene
/// through its control-owned pipeline, so mutations only require an invalidation.
/// </summary>
internal sealed class MaterialTexturePresenter
{
    private static readonly LinearRgbaImage ColorTexture = CreateColorTexture();
    private static readonly NormalizedRgbaDataImage NormalTexture = new(
        2,
        2,
        Enumerable.Repeat(new Vector4(0.65f, 0.35f, 0.95f, 1f), 4).ToArray(),
        "Normal texture");
    private static readonly NormalizedRgbaDataImage OrmTexture = new(
        2,
        2,
        [
            new Vector4(1f, 0.2f, 0f, 1f),
            new Vector4(1f, 0.8f, 1f, 1f),
            new Vector4(0.45f, 0.4f, 0f, 1f),
            new Vector4(0.45f, 0.65f, 1f, 1f),
        ],
        "ORM texture");

    private readonly PbrMaterial material;

    /// <summary>Adds the example light, environment and textured sphere to the supplied scene.</summary>
    internal MaterialTexturePresenter(
        Scene scene,
        EquirectangularHdrEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(scene);
        DirectionalLight light = new(
            new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb),
            1.5f,
            "Material light");
        light.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(-0.5f, -0.55f, 0f);
        scene.Add(light);
        scene.Add(new ImageBasedLight(environment, 0.7f, "Studio environment"));

        material = new PbrMaterial(
            new LinearRgba(1f, 1f, 1f, 1f, StandardColorSpaces.LinearSrgb),
            0f,
            0.55f,
            "Editable material");
        scene.Add(new Mesh(MeshPrimitives.CreateUvSphere(1.1f, 64, 32), material));
    }

    internal float Metallic { set => material.Metallic = value; }

    internal float Roughness { set => material.Roughness = value; }

    internal bool UseColorTexture { set => material.BaseColorTexture = value ? ColorTexture : null; }

    internal bool UseNormalTexture { set => material.NormalTexture = value ? NormalTexture : null; }

    internal bool UseOrmTexture
    {
        set => material.OcclusionRoughnessMetallicTexture = value ? OrmTexture : null;
    }

    internal float TextureScale { set => material.TextureCoordinateScale = new Vector2(value); }

    internal MaterialTextureSampling TextureSampling { set => material.TextureSampling = value; }

    private static LinearRgbaImage CreateColorTexture() => new(
        2,
        2,
        [
            new Vector4(1.2f, 0.04f, 0.03f, 1f),
            new Vector4(0.03f, 0.8f, 0.06f, 1f),
            new Vector4(0.04f, 0.08f, 1.5f, 1f),
            new Vector4(0.18f, 0.18f, 0.18f, 1f),
        ],
        StandardColorSpaces.LinearDisplayP3,
        "Linear Display P3 color texture");
}

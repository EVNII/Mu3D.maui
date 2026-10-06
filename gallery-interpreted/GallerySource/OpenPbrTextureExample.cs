using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Pages;

/// <summary>Creates small in-memory material maps with explicit color/data interpretation.</summary>
public sealed class OpenPbrTextureExample
{
    private const int Size = 64;
    private static readonly OpenPbrTexture ColorMap = CreateColor();
    private static readonly OpenPbrTexture DataMap = CreateData();
    private static readonly OpenPbrTexture GlossMap = CreateGloss();
    private static readonly OpenPbrTexture CoatAlphaMap = CreateCoatAlpha();

    /// <summary>Gets the initial graph for the XAML resource binding.</summary>
    public OpenPbrGraph Graph { get; } = Create(.3f);

    /// <summary>Builds color, roughness and tangent-normal connections for the example sphere.</summary>
    public static OpenPbrGraph Create(float roughness)
    {
        var color = OpenPbrNode.Image(ColorMap, OpenPbrNodeType.Color3);
        var data = OpenPbrNode.Image(DataMap, OpenPbrNodeType.Vector3);
        // A separate raw gloss map keeps normal-map channels independent. Its magnitude is
        // inverted and explicitly bounded before applying the authored roughness multiplier.
        var gloss = OpenPbrNode.Image(GlossMap, OpenPbrNodeType.Float);
        var roughnessMap = OpenPbrNode.Max(OpenPbrNode.Min(
            OpenPbrNode.Subtract(OpenPbrNode.Float(1), OpenPbrNode.Abs(gloss)), OpenPbrNode.Float(1)),
            OpenPbrNode.Float(0));
        // This asset encodes GGX microfacet alpha on an authored 16-bit full-scale range.
        // Normalize the raw value explicitly, then map alpha to perceptual roughness.
        var coatAlpha = OpenPbrNode.Divide(OpenPbrNode.Image(CoatAlphaMap, OpenPbrNodeType.Float),
            OpenPbrNode.Float(65535));
        return new(new Dictionary<OpenPbrInput, OpenPbrNode>
        {
            [OpenPbrInput.BaseColor] = color,
            [OpenPbrInput.SpecularRoughness] = OpenPbrNode.Multiply(roughnessMap, OpenPbrNode.Float(roughness)),
            [OpenPbrInput.GeometryNormal] = OpenPbrNode.NormalMap(data, .35f),
            [OpenPbrInput.CoatRoughness] = OpenPbrNode.Sqrt(coatAlpha),
        });
    }
    /// <summary>Resolves only the four known in-memory assets; never opens an XML-specified path.</summary>
    public static OpenPbrTexture Resolve(string source, string? space, OpenPbrNodeType type) => source switch
    {
        "gallery-color.exr" when space == "acescg" && type == OpenPbrNodeType.Color3 => ColorMap,
        "gallery-data.exr" when space == "raw" && type == OpenPbrNodeType.Vector3 => DataMap,
        "gallery-gloss.exr" when space == "raw" && type == OpenPbrNodeType.Float => GlossMap,
        "gallery-coat-alpha.exr" when space == "raw" && type == OpenPbrNodeType.Float => CoatAlphaMap,
        _ => throw new NotSupportedException($"Unknown Gallery texture '{source}'."),
    };
    private static OpenPbrTexture CreateColor()
    {
        Vector4[] pixels = new Vector4[Size * Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            pixels[y * Size + x] = ((x / 8 + y / 8) & 1) == 0 ? new(.65f,.08f,.015f,1) : new(.02f,.22f,.65f,1);
        return OpenPbrTexture.FromColor(new(Size, Size, pixels, StandardColorSpaces.AcesCg), "gallery-color.exr");
    }
    private static OpenPbrTexture CreateData()
    {
        Vector4[] pixels = new Vector4[Size * Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            pixels[y * Size + x] = new(.5f + .12f * MathF.Sin(x * MathF.Tau / 8),
                .5f + .12f * MathF.Sin(y * MathF.Tau / 8), ((x / 8 + y / 8) & 1) == 0 ? .7f : 1, 1);
        return OpenPbrTexture.FromData(Size, Size, pixels, "gallery-data.exr");
    }
    private static OpenPbrTexture CreateGloss()
    {
        Vector4[] pixels = new Vector4[Size * Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            pixels[y * Size + x] = new(((x / 8 + y / 8) & 1) == 0 ? -.3f : 0);
        return OpenPbrTexture.FromData(Size, Size, pixels, "gallery-gloss.exr");
    }
    private static OpenPbrTexture CreateCoatAlpha()
    {
        Vector4[] pixels = new Vector4[Size * Size];
        for (int y = 0; y < Size; y++) for (int x = 0; x < Size; x++)
            pixels[y * Size + x] = new((((x / 8 + y / 8) & 1) == 0 ? .04f : .09f) * 65535);
        return OpenPbrTexture.FromData(Size, Size, pixels, "gallery-coat-alpha.exr");
    }
}

using System.Numerics;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Gallery.Pages;

/// <summary>Creates small in-memory material maps with explicit color/data interpretation.</summary>
public sealed class OpenPbrTextureExample
{
    private const int Size = 64;
    private static readonly OpenPbrTexture ColorMap = CreateColor();
    private static readonly OpenPbrTexture DataMap = CreateData();

    /// <summary>Gets the initial graph for the XAML resource binding.</summary>
    public OpenPbrGraph Graph { get; } = Create(.3f);

    /// <summary>Builds color, roughness and tangent-normal connections for the example sphere.</summary>
    public static OpenPbrGraph Create(float roughness)
    {
        var color = OpenPbrNode.Image(ColorMap, OpenPbrNodeType.Color3);
        var data = OpenPbrNode.Image(DataMap, OpenPbrNodeType.Vector3);
        return new(new Dictionary<OpenPbrInput, OpenPbrNode>
        {
            [OpenPbrInput.BaseColor] = color,
            [OpenPbrInput.SpecularRoughness] = OpenPbrNode.Multiply(OpenPbrNode.Extract(data, 2), OpenPbrNode.Float(roughness)),
            [OpenPbrInput.GeometryNormal] = OpenPbrNode.NormalMap(data, .35f),
        });
    }

    /// <summary>Resolves only the two known in-memory assets; never opens an XML-specified path.</summary>
    public static OpenPbrTexture Resolve(string source, string? space, OpenPbrNodeType type) => source switch
    {
        "gallery-color.exr" when space == "acescg" && type == OpenPbrNodeType.Color3 => ColorMap,
        "gallery-data.exr" when space == "raw" && type == OpenPbrNodeType.Vector3 => DataMap,
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
}

using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.GalleryApp.Web.Infrastructure;

// Bounded sample adapter, not a MAUI XAML runtime: the native scene is the authoritative data.
// All construction is explicit so trimming/AOT never discovers components through reflection.
internal sealed class NativeGalleryScene
{
    internal Scene Scene { get; }
    internal PerspectiveCamera Camera { get; }
    internal XDocument Source { get; }
    internal Dictionary<string, SceneNode> NamedNodes { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, Material> NamedMaterials { get; } = new(StringComparer.Ordinal);
    internal Dictionary<Mesh, uint> SelectionMasks { get; } = new();
    internal XElement? Find(string name) => Source.Descendants().FirstOrDefault(element => element.Name.LocalName == name);

    private NativeGalleryScene(XDocument source)
    {
        Source = source;
        XElement definition = Find("Scene3D") ?? throw new NotSupportedException("This native page has no declarative Scene3D.");
        Scene = new((string?)definition.Attribute("Name"));
        XElement cameraDefinition = definition.Elements().First(element => element.Name.LocalName == "Scene3D.Camera").Elements().Single();
        if (cameraDefinition.Name.LocalName != "PerspectiveCamera3D") throw new NotSupportedException("Unsupported Gallery camera.");
        Camera = new(Number(cameraDefinition, "FieldOfView", 60) * MathF.PI / 180, nearClip: Number(cameraDefinition, "NearClip", .1f),
            farClip: Number(cameraDefinition, "FarClip", 1000), name: (string?)cameraDefinition.Attribute("Name"));
        ApplyTransform(Camera, cameraDefinition);
        RegisterNode(Camera, cameraDefinition);
        foreach (XElement node in definition.Elements())
        {
            if (node.Name.LocalName == "Scene3D.Camera") continue;
            Scene.Add(ReadNode(node));
        }
    }

    internal static NativeGalleryScene Read(string pageName)
    {
        using Stream stream = typeof(NativeGalleryScene).Assembly.GetManifestResourceStream($"Mu3D.GalleryApp.NativePages.{pageName}.xaml")
            ?? throw new InvalidOperationException($"Native Gallery source is missing: {pageName}.");
        return new(XDocument.Load(stream));
    }

    private SceneNode ReadNode(XElement element)
    {
        string? name = (string?)element.Attribute("Name");
        SceneNode node;
        switch (element.Name.LocalName)
        {
            case "Sphere3D": node = new Mesh(MeshPrimitives.CreateUvSphere(Number(element, "Radius", 1),
                (int)Number(element, "LongitudeSegments", 32), (int)Number(element, "LatitudeSegments", 16)), ReadMaterial(element), name); break;
            case "Cone3D": node = new Mesh(MeshPrimitives.CreateCone(Number(element, "Radius", 1), Number(element, "Height", 2),
                (int)Number(element, "RadialSegments", 32)), ReadMaterial(element), name); break;
            case "DirectionalLight3D": node = new DirectionalLight(Color((string?)element.Attribute("Color") ?? "#FFFFFF"),
                Number(element, "Intensity", 1), name) { CastsShadows = Boolean(element, "CastsShadows", false) }; break;
            case "Group3D":
                node = new SceneNode(name);
                foreach (XElement child in element.Elements()) node.AddChild(ReadNode(child));
                break;
            default: throw new NotSupportedException($"Unsupported native Gallery scene node: {element.Name.LocalName}.");
        }
        ApplyTransform(node, element);
        node.IsVisible = Boolean(element, "IsVisible", true);
        RegisterNode(node, element);
        if (node is Mesh mesh)
        {
            mesh.ShadowCastingMode = (string?)element.Attribute("ShadowCastingMode") == "Off" ? MeshShadowCastingMode.Off : MeshShadowCastingMode.On;
            XAttribute? selectable = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "SceneSelection.IsSelectable");
            XAttribute? mask = element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "SceneSelection.Mask");
            SelectionMasks.Add(mesh, selectable?.Value == "False" ? 0 : mask is null ? 1 : uint.Parse(mask.Value, CultureInfo.InvariantCulture));
        }
        return node;
    }

    private Material ReadMaterial(XElement mesh)
    {
        XElement? definition = mesh.Elements().SingleOrDefault();
        if (definition?.Name.LocalName.EndsWith(".Material", StringComparison.Ordinal) == true) definition = definition.Elements().Single();
        LinearRgba color = Color((string?)definition?.Attribute("Color") ?? "#FFFFFF");
        string? name = (string?)definition?.Attribute("Name");
        Material material = definition?.Name.LocalName switch
        {
            null or "PbrMaterial3D" => new PbrMaterial(color, Number(definition, "Metallic", 0), Number(definition, "Roughness", .5f), name),
            "UnlitMaterial3D" => new UnlitMaterial(color, name),
            _ => throw new NotSupportedException($"Unsupported native Gallery material: {definition.Name.LocalName}."),
        };
        material.IsDoubleSided = Boolean(definition, "IsDoubleSided", false);
        string? key = definition?.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name" && attribute.Name.NamespaceName.Contains("winfx", StringComparison.Ordinal))?.Value;
        if (key is not null) NamedMaterials.Add(key, material);
        return material;
    }

    private void RegisterNode(SceneNode node, XElement definition)
    {
        string? key = definition.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "Name" && attribute.Name.NamespaceName.Contains("winfx", StringComparison.Ordinal))?.Value;
        if (key is not null) NamedNodes.Add(key, node);
    }

    internal static float Number(XElement? element, string name, float fallback) =>
        element?.Attribute(name) is XAttribute value ? float.Parse(value.Value, CultureInfo.InvariantCulture) : fallback;
    internal static bool Boolean(XElement? element, string name, bool fallback) =>
        element?.Attribute(name) is XAttribute value ? bool.Parse(value.Value) : fallback;
    internal static void ApplyTransform(SceneNode node, XElement definition)
    {
        node.Transform.Position = new(Number(definition, "X", 0), Number(definition, "Y", 0), Number(definition, "Z", 0));
        node.Transform.Rotation = Quaternion.CreateFromYawPitchRoll(Number(definition, "RotationY", 0) * MathF.PI / 180,
            Number(definition, "RotationX", 0) * MathF.PI / 180, Number(definition, "RotationZ", 0) * MathF.PI / 180);
        node.Transform.Scale = new(Number(definition, "ScaleX", 1), Number(definition, "ScaleY", 1), Number(definition, "ScaleZ", 1));
    }
    internal static LinearRgba Color(string hexadecimal)
    {
        hexadecimal = hexadecimal == "White" ? "#FFFFFF" : hexadecimal;
        string hex = hexadecimal.TrimStart('#');
        if (hex.Length is not (6 or 8)) throw new NotSupportedException($"Unsupported Gallery color: {hexadecimal}.");
        uint encoded = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static float Decode(uint value) { float v = value / 255f; return v <= .04045f ? v / 12.92f : MathF.Pow((v + .055f) / 1.055f, 2.4f); }
        return new(Decode((encoded >> 16) & 255), Decode((encoded >> 8) & 255), Decode(encoded & 255),
            hex.Length == 8 ? (encoded >> 24) / 255f : 1, StandardColorSpaces.LinearSrgb);
    }
}

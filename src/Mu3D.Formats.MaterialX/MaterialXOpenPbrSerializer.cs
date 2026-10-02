using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Formats.MaterialX;

/// <summary>
/// Reads and writes OpenPBR 1.1.1 constants and bounded typed input graphs in the supported subset of MaterialX 1.39 XML without native libraries.
/// External images require an explicit resolver. Includes, DTDs, namespaces and unknown semantics fail closed.
/// </summary>
public static partial class MaterialXOpenPbrSerializer
{
    /// <summary>Gets the supported MaterialX document version.</summary>
    public const string DocumentVersion = "1.39";

    /// <summary>Gets the pinned official OpenPBR reference source.</summary>
    public const string ReferenceDefinitionUrl = "https://raw.githubusercontent.com/AcademySoftwareFoundation/OpenPBR/v1.1.1/reference/open_pbr_surface.mtlx";

    private const string NodeDefinition = "ND_open_pbr_surface_surfaceshader";

    /// <summary>
    /// Imports from the stream's current position, leaving the caller-owned stream open.
    /// Invalid XML/data raises <see cref="InvalidDataException"/>; unsupported semantics raise
    /// <see cref="NotSupportedException"/>. Untagged authored colors require an explicit import assignment.
    /// </summary>
    public static MaterialXOpenPbrDocument Import(Stream source, MaterialXImportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new MaterialXImportOptions();
        options.Validate();
        using var buffer = new MemoryStream();
        Span<byte> chunk = stackalloc byte[8192];
        while (true)
        {
            var count = source.Read(chunk[..Math.Min(chunk.Length, options.MaximumDocumentBytes - (int)buffer.Length + 1)]);
            if (count == 0) break;
            if (buffer.Length + count > options.MaximumDocumentBytes)
                throw new InvalidDataException("MaterialX document exceeds the encoded byte limit.");
            buffer.Write(chunk[..count]);
        }
        return Parse(buffer, options);
    }

    /// <summary>Imports bounded XML asynchronously and leaves the caller-owned stream open.</summary>
    public static async Task<MaterialXOpenPbrDocument> ImportAsync(
        Stream source, MaterialXImportOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new MaterialXImportOptions();
        options.Validate();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = await source.ReadAsync(chunk.AsMemory(0,
                Math.Min(chunk.Length, options.MaximumDocumentBytes - (int)buffer.Length + 1)), cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (buffer.Length + count > options.MaximumDocumentBytes)
                throw new InvalidDataException("MaterialX document exceeds the encoded byte limit.");
            buffer.Write(chunk, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Parse(buffer, options);
    }

    /// <summary>
    /// Writes deterministic UTF-8 XML with explicit per-color spaces and leaves the destination open.
    /// All model semantics are validated before writing. Scene length units currently must be metres.
    /// No shader compilation, texture access or rendering occurs during serialization.
    /// </summary>
    public static void Export(Stream destination, MaterialXOpenPbrDocument document)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(document);
        var root = new XElement("materialx", new XAttribute("version", DocumentVersion));
        HashSet<string> occupied = document.Surfaces.Select(s => s.Name).Concat(document.Materials.Select(m => m.Name)).ToHashSet(StringComparer.Ordinal);
        int graphIndex = 0;
        foreach (var entry in document.Surfaces)
        {
            if (entry.Surface.MetersPerUnit != 1f)
                throw new NotSupportedException("This MaterialX subset requires MetersPerUnit=1; convert lengths explicitly before exporting.");
            var node = new XElement("open_pbr_surface", new XAttribute("name", entry.Name),
                new XAttribute("type", "surfaceshader"), new XAttribute("version", OpenPbrSurface.SpecificationVersion));
            if (entry.Surface.Graph is { Bindings.Count: > 0 } graph)
            {
                string graphName;
                do { graphName = "mu3d_graph_" + graphIndex++; } while (!occupied.Add(graphName));
                WriteGraph(root, node, graphName, graph);
            }
            WriteParameters(node, entry.Surface);
            root.Add(node);
        }
        foreach (var material in document.Materials)
            root.Add(new XElement("surfacematerial", new XAttribute("name", material.Name), new XAttribute("type", "material"),
                new XElement("input", new XAttribute("name", "surfaceshader"), new XAttribute("type", "surfaceshader"),
                    new XAttribute("nodename", material.SurfaceNodeName))));

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false), Indent = true, NewLineChars = "\n",
            NewLineHandling = NewLineHandling.None, CloseOutput = false,
        };
        using var writer = XmlWriter.Create(destination, settings);
        new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(writer);
    }

    private static MaterialXOpenPbrDocument Parse(MemoryStream buffer, MaterialXImportOptions options)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, CloseInput = false,
            MaxCharactersInDocument = options.MaximumDocumentBytes,
            MaxCharactersFromEntities = 1,
        };
        try
        {
            // Bound nesting before constructing the tree: root, optional graph, node, input.
            // Byte, inventory and per-surface expression limits bound allocation and evaluation.
            buffer.Position = 0;
            var scannedSurfaces = 0;
            var scannedMaterials = 0;
            var scannedInputs = 0;
            using (var reader = XmlReader.Create(buffer, settings))
            {
                while (reader.Read())
                {
                    if (reader.Depth > 3)
                        throw new NotSupportedException("Nested graphs or content beyond graph/node/input depth are not supported.");
                    if (reader.NodeType is XmlNodeType.ProcessingInstruction or XmlNodeType.EntityReference)
                        throw new NotSupportedException("Processing instructions and entities are not supported.");
                    if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA)
                        throw new InvalidDataException("MaterialX values must use attributes, not text content.");
                    if (reader.NodeType == XmlNodeType.Element && reader.Depth == 1)
                    {
                        scannedInputs = 0;
                        if (reader.Name == "open_pbr_surface")
                        {
                            if (++scannedSurfaces > options.MaximumSurfaceCount)
                                throw new InvalidDataException("MaterialX surface count exceeds the configured limit.");
                        }
                        else if (reader.Name == "surfacematerial")
                        {
                            if (++scannedMaterials > options.MaximumMaterialCount)
                                throw new InvalidDataException("MaterialX material count exceeds the configured limit.");
                        }
                        else if (reader.Name != "nodegraph" && !GraphCategories.Contains(reader.Name))
                            throw new NotSupportedException($"Unsupported MaterialX element '{reader.Name}'.");
                    }
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "input" && reader.Depth == 2 && ++scannedInputs > 4096)
                        throw new InvalidDataException("A surface cannot contain more than the 41 pinned OpenPBR inputs.");
                }
            }
            buffer.Position = 0;
            using var treeReader = XmlReader.Create(buffer, settings);
            var root = XDocument.Load(treeReader).Root ?? throw new InvalidDataException("Missing materialx root.");
            RequireElement(root, "materialx");
            CheckAttributes(root, "version", "colorspace");
            RequireValue(root, "version", DocumentVersion);
            var rootColorSpace = ResolveColorSpace(root.Attribute("colorspace")?.Value) ?? options.DefaultColorSpace;
            var graphs = new GraphReader(root, rootColorSpace, options);
            var surfaces = new List<MaterialXOpenPbrSurface>();
            var materials = new List<MaterialXSurfaceMaterial>();
            foreach (var child in root.Elements())
            {
                if (child.Name == "open_pbr_surface")
                {
                    if (surfaces.Count == options.MaximumSurfaceCount)
                        throw new InvalidDataException("MaterialX surface count exceeds the configured limit.");
                    surfaces.Add(ParseSurface(child, rootColorSpace, graphs));
                }
                else if (child.Name == "surfacematerial")
                {
                    if (materials.Count == options.MaximumMaterialCount)
                        throw new InvalidDataException("MaterialX material count exceeds the configured limit.");
                    materials.Add(ParseMaterial(child));
                }
                else if (child.Name != "nodegraph" && !GraphCategories.Contains(child.Name.LocalName))
                    throw new NotSupportedException($"Unsupported MaterialX element '{child.Name}'.");
            }
            graphs.ValidateReachability();
            return new MaterialXOpenPbrDocument(surfaces, materials);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("Malformed or prohibited MaterialX XML.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("Invalid MaterialX identifier, parameter or reference.", exception);
        }
    }

    private static MaterialXOpenPbrSurface ParseSurface(XElement node, StandardRgbColorSpaceReference? inheritedColorSpace, GraphReader graphs)
    {
        CheckAttributes(node, "name", "type", "version", "nodedef", "colorspace");
        RequireValue(node, "type", "surfaceshader");
        if (node.Attribute("version") is { } version && version.Value != OpenPbrSurface.SpecificationVersion)
            throw new NotSupportedException($"OpenPBR version '{version.Value}' is not the pinned {OpenPbrSurface.SpecificationVersion}.");
        if (node.Attribute("nodedef") is { } definition && definition.Value != NodeDefinition)
            throw new NotSupportedException($"OpenPBR node definition '{definition.Value}' is unsupported.");

        var name = Required(node, "name");
        var surface = new OpenPbrSurface { Name = name };
        var space = ResolveColorSpace(node.Attribute("colorspace")?.Value) ?? inheritedColorSpace;
        if (space is not null) SetDefaultColorSpace(surface, space);
        var names = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<OpenPbrInput, OpenPbrNode> bindings = [];
        foreach (var input in node.Elements())
        {
            RequireElement(input, "input");
            CheckAttributes(input, "name", "type", "value", "colorspace", "nodename", "nodegraph", "output");
            var inputName = Required(input, "name");
            if (!names.Add(inputName))
                throw new InvalidDataException($"Duplicate input '{inputName}' on '{name}'.");
            if (names.Count > 41 || input.HasElements) throw new InvalidDataException("OpenPBR inputs must be flat and bounded to 41.");
            if (input.Attribute("nodename") is not null || input.Attribute("nodegraph") is not null)
            {
                OpenPbrInput id = InputId(inputName);
                if (!Enum.IsDefined(id)) throw new NotSupportedException($"Unknown OpenPBR input '{inputName}'.");
                string expected = id == OpenPbrInput.SubsurfaceRadiusScale ? "color3" : TypeName(OpenPbrInputInfo.Get(id).Type);
                RequireValue(input, "type", expected);
                bindings.Add(id, graphs.Connection(input, "", id == OpenPbrInput.SubsurfaceRadiusScale));
                continue;
            }
            if (input.Attribute("output") is not null) throw new InvalidDataException("Output requires a nodegraph connection.");
            SetParameter(surface, inputName, Required(input, "type"), Required(input, "value"),
                input.Attribute("colorspace")?.Value, space);
        }
        if (bindings.Count > 0) surface.Graph = new OpenPbrGraph(bindings);
        return new MaterialXOpenPbrSurface(name, surface);
    }

    private static MaterialXSurfaceMaterial ParseMaterial(XElement node)
    {
        CheckAttributes(node, "name", "type");
        RequireValue(node, "type", "material");
        var inputs = node.Elements().ToArray();
        if (inputs.Length != 1)
            throw new NotSupportedException("A surface material must contain exactly one surfaceshader input.");
        var input = inputs[0];
        RequireElement(input, "input");
        CheckAttributes(input, "name", "type", "nodename");
        RequireValue(input, "name", "surfaceshader");
        RequireValue(input, "type", "surfaceshader");
        return new MaterialXSurfaceMaterial(Required(node, "name"), Required(input, "nodename"));
    }

    private static void CheckAttributes(XElement element, params ReadOnlySpan<string> allowed)
    {
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.NamespaceName.Length > 0 || !allowed.Contains(attribute.Name.LocalName))
                throw new NotSupportedException($"Attribute '{attribute.Name}' on '{element.Name}' is unsupported; no external resource or connection is evaluated.");
        }
    }

    private static void RequireElement(XElement element, string name)
    {
        if (element.Name != name)
            throw new NotSupportedException($"Expected '{name}', found unsupported element '{element.Name}'.");
    }

    private static string Required(XElement element, string attribute) =>
        element.Attribute(attribute)?.Value ?? throw new InvalidDataException($"Missing '{attribute}' on '{element.Name}'.");

    private static void RequireValue(XElement element, string attribute, string expected)
    {
        if (Required(element, attribute) != expected)
            throw new NotSupportedException($"'{element.Name}' requires {attribute}='{expected}'.");
    }

    private static StandardRgbColorSpaceReference? ResolveColorSpace(string? name) => name switch
    {
        null => null,
        "lin_rec709" => StandardColorSpaces.LinearSrgb,
        "acescg" or "lin_ap1" => StandardColorSpaces.AcesCg,
        _ => throw new NotSupportedException($"MaterialX color space '{name}' is unsupported. Use explicit linear sRGB (lin_rec709) or ACEScg (acescg)."),
    };

    internal static string GetColorSpaceName(ColorSpaceReference space) => space switch
    {
        StandardRgbColorSpaceReference { Space: StandardRgbColorSpace.LinearSrgb } => "lin_rec709",
        StandardRgbColorSpaceReference { Space: StandardRgbColorSpace.AcesCg } => "acescg",
        _ => throw new NotSupportedException($"Color space '{space.Name}' has no supported MaterialX name in this subset."),
    };

    private static float ParseFloat(string value)
    {
        if (value.Length > 128 || !float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !float.IsFinite(result))
            throw new InvalidDataException($"'{value}' is not a finite invariant-culture float.");
        return result;
    }

    private static Vector3 ParseVector(string value)
    {
        if (value.Length > 392)
            throw new InvalidDataException("Vector constants exceed the bounded numeric representation length.");
        var components = value.Split(',');
        if (components.Length != 3)
            throw new InvalidDataException("Expected exactly three comma-separated components.");
        return new Vector3(ParseFloat(components[0]), ParseFloat(components[1]), ParseFloat(components[2]));
    }

    private static LinearRgba ParseColor(string value, string? explicitColorSpace, StandardRgbColorSpaceReference? inheritedColorSpace)
    {
        var space = ResolveColorSpace(explicitColorSpace) ?? inheritedColorSpace ??
            throw new InvalidDataException("An authored color3 requires colorspace metadata or an explicit DefaultColorSpace assignment.");
        var rgb = ParseVector(value);
        return new LinearRgba(rgb.X, rgb.Y, rgb.Z, 1f, space);
    }

    private static void CheckType(string name, string actual, string expected, string? colorSpace, bool isColor = false)
    {
        if (actual != expected)
            throw new InvalidDataException($"Input '{name}' has type '{actual}', expected '{expected}'.");
        if (!isColor && colorSpace is not null)
            throw new InvalidDataException($"Numeric input '{name}' cannot carry a colorspace attribute.");
    }

    private static string Format(float value)
    {
        if (!float.IsFinite(value)) throw new InvalidDataException("Cannot export non-finite components.");
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string Format(Vector3 value) => $"{Format(value.X)}, {Format(value.Y)}, {Format(value.Z)}";

    private static void Add(XElement node, string name, string type, string value, string? colorSpace = null)
    {
        if (node.Elements("input").Any(i => i.Attribute("name")?.Value == name)) return;
        var input = new XElement("input", new XAttribute("name", name), new XAttribute("type", type), new XAttribute("value", value));
        if (colorSpace is not null) input.Add(new XAttribute("colorspace", colorSpace));
        node.Add(input);
    }

    private static void AddColor(XElement node, string name, LinearRgba value)
    {
        if (node.Elements("input").Any(i => i.Attribute("name")?.Value == name)) return;
        if (value.ColorSpace is null || value.Alpha != 1f)
            throw new InvalidDataException($"OpenPBR color '{name}' requires a color space and alpha 1.");
        Add(node, name, "color3", Format(new Vector3(value.Red, value.Green, value.Blue)), GetColorSpaceName(value.ColorSpace));
    }
}

using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using Mu3D.Color;
using Mu3D.SceneGraph;

namespace Mu3D.Formats.MaterialX;

public static partial class MaterialXOpenPbrSerializer
{
    private static readonly string[] GraphCategories = ["constant", "texcoord", "image", "add", "multiply",
        "subtract", "min", "max", "absval", "divide", "sqrt", "mix", "clamp", "normalmap", "extract", "convert"];
    private static string TypeName(OpenPbrNodeType type) => type switch
    {
        OpenPbrNodeType.Float => "float", OpenPbrNodeType.Color3 => "color3", OpenPbrNodeType.Vector2 => "vector2",
        OpenPbrNodeType.Vector3 => "vector3", OpenPbrNodeType.Vector4 => "vector4", OpenPbrNodeType.Boolean => "boolean", _ => throw new NotSupportedException(),
    };
    private static OpenPbrNodeType NodeType(string type) => type switch
    {
        "float" => OpenPbrNodeType.Float, "color3" => OpenPbrNodeType.Color3, "vector2" => OpenPbrNodeType.Vector2,
        "vector3" => OpenPbrNodeType.Vector3, "vector4" => OpenPbrNodeType.Vector4, "boolean" => OpenPbrNodeType.Boolean,
        _ => throw new NotSupportedException($"Unsupported material graph type '{type}'."),
    };
    private static OpenPbrInput InputId(string name) => Enum.GetValues<OpenPbrInput>().FirstOrDefault(
        p => OpenPbrInputInfo.Get(p).Name == name, (OpenPbrInput)(-1));

    private sealed class GraphReader
    {
        private readonly Dictionary<string, XElement> nodes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> outputs = new(StringComparer.Ordinal);
        private readonly Dictionary<(string, bool), OpenPbrNode> built = [];
        private readonly HashSet<(string, bool)> active = [];
        private readonly StandardRgbColorSpaceReference? rootSpace;
        private readonly MaterialXImportOptions options;
        internal GraphReader(XElement root, StandardRgbColorSpaceReference? space, MaterialXImportOptions importOptions)
        {
            rootSpace = space; options = importOptions;
            HashSet<string> names = new(StringComparer.Ordinal);
            int count = 0;
            foreach (var child in root.Elements())
            {
                string name = Required(child, "name"); MaterialXNames.Validate(name, "name");
                if (!names.Add(name)) throw new InvalidDataException($"Duplicate MaterialX name '{name}'.");
                if (child.Name == "open_pbr_surface" || child.Name == "surfacematerial") continue;
                if (child.Name == "nodegraph")
                {
                    CheckAttributes(child, "name"); HashSet<string> local = new(StringComparer.Ordinal);
                    foreach (var node in child.Elements())
                    {
                        string n = Required(node, "name"); MaterialXNames.Validate(n, "name");
                        if (!local.Add(n)) throw new InvalidDataException($"Duplicate nodegraph name '{n}'.");
                        if (node.Name == "output")
                        {
                            CheckAttributes(node, "name", "type", "nodename");
                            if (node.HasElements) throw new InvalidDataException("Graph outputs must be empty elements.");
                            outputs.Add(name + "/" + n, node);
                        }
                        else Add(name + "/" + n, node);
                    }
                }
                else Add(name, child);
            }
            void Add(string name, XElement node)
            {
                if (++count > options.MaximumGraphNodes) throw new InvalidDataException("MaterialX graph node count exceeds its budget.");
                if (!GraphCategories.Contains(node.Name.LocalName) || node.Name.NamespaceName.Length != 0)
                    throw new NotSupportedException($"Unsupported MaterialX graph node '{node.Name}'.");
                CheckAttributes(node, "name", "type", "colorspace");
                nodes.Add(name, node);
            }
        }
        internal OpenPbrNode Connection(XElement input, string scope, bool channelData = false)
        {
            if (input.HasElements || input.Attribute("value") is not null || input.Attribute("colorspace") is not null)
                throw new InvalidDataException("A connected port cannot also contain a constant, color assignment or child elements.");
            string name;
            if (input.Attribute("nodegraph") is { } graph)
            {
                if (input.Attribute("nodename") is not null) throw new InvalidDataException("Ambiguous MaterialX connection.");
                string key = graph.Value + "/" + Required(input, "output");
                if (!outputs.TryGetValue(key, out var output)) throw new InvalidDataException($"Missing graph output '{key}'.");
                if (Required(output, "type") != Required(input, "type")) throw new InvalidDataException("Graph output type mismatch.");
                name = graph.Value + "/" + Required(output, "nodename");
            }
            else
            {
                if (input.Attribute("output") is not null) throw new NotSupportedException("Only single-output node connections are supported.");
                name = scope + Required(input, "nodename");
            }
            if (!nodes.TryGetValue(name, out var source)) throw new InvalidDataException($"Missing graph node '{name}'.");
            if (Required(source, "type") != Required(input, "type")) throw new InvalidDataException("Connected MaterialX types differ.");
            return Build(name, channelData);
        }
        internal void ValidateReachability()
        {
            if (nodes.Keys.Any(name => !built.Keys.Any(key => key.Item1 == name)))
                throw new NotSupportedException("Unconnected graph nodes are not retained by the OpenPBR document model; remove them before importing.");
        }
        private OpenPbrNode Build(string name, bool channelData)
        {
            channelData = channelData && Required(nodes[name], "type") == "color3";
            if (built.TryGetValue((name, channelData), out var existing)) return existing;
            if (active.Count >= OpenPbrGraph.MaximumNodes || !active.Add((name, channelData)))
                throw new InvalidDataException("MaterialX connections contain a cycle or exceed the graph depth budget.");
            var node = nodes[name]; string kind = node.Name.LocalName;
            string scope = name.Contains('/') ? name[..(name.LastIndexOf('/') + 1)] : "";
            OpenPbrNodeType type = NodeType(Required(node, "type"));
            if (kind == "sqrt" && type == OpenPbrNodeType.Color3)
                throw new NotSupportedException("MaterialX sqrt supports float and vector types, not color3.");
            if (channelData && type == OpenPbrNodeType.Color3) type = OpenPbrNodeType.Vector3;
            var space = node.Attribute("colorspace")?.Value;
            if (channelData && space is not null && space != "raw") throw new NotSupportedException("Numeric channel distances cannot carry a color transform.");
            Dictionary<string, XElement> inputs = new(StringComparer.Ordinal);
            foreach (var i in node.Elements())
            {
                RequireElement(i, "input"); CheckAttributes(i, "name", "type", "value", "colorspace", "nodename", "nodegraph", "output");
                if (i.HasElements || !inputs.TryAdd(Required(i, "name"), i)) throw new InvalidDataException("Duplicate or nested graph input.");
            }
            XElement Take(string key) => inputs.Remove(key, out var input) ? input : throw new InvalidDataException($"Missing '{key}' on '{name}'.");
            OpenPbrNode Operand(string key, OpenPbrNode? fallback = null, bool data = false)
            {
                if (!inputs.ContainsKey(key) && fallback is not null) return fallback;
                var i = Take(key);
                return i.Attribute("value") is null ? Connection(i, scope, data || channelData) : Constant(i, data || channelData,
                    Required(i, "type") != "color3" ? null : space is null ? rootSpace : space == "raw" ? null : ResolveColorSpace(space));
            }
            string Literal(string key, string expected, string fallback)
            {
                if (!inputs.ContainsKey(key)) return fallback;
                var i = Take(key); CheckAttributes(i, "name", "type", "value"); RequireValue(i, "type", expected);
                return Required(i, "value");
            }
            OpenPbrNode result;
            switch (kind)
            {
                case "constant": result = Operand("value"); break;
                case "texcoord": result = OpenPbrNode.Texcoord(ParseInteger(Literal("index", "integer", "0"))); break;
                case "image":
                    string file = Literal("file", "filename", "");
                    if (string.IsNullOrWhiteSpace(file) || file.Length > 4096) throw new InvalidDataException("An image requires a bounded filename identity.");
                    var uv = Operand("texcoord", OpenPbrNode.Texcoord());
                    var u = Address(Literal("uaddressmode", "string", "periodic"));
                    var v = Address(Literal("vaddressmode", "string", "periodic"));
                    var filter = Literal("filtertype", "string", "linear") switch
                    {
                        "linear" => OpenPbrTextureFilter.Linear, "closest" => OpenPbrTextureFilter.Closest,
                        _ => throw new NotSupportedException("Only closest and linear image filtering are supported."),
                    };
                    if (Literal("layer", "string", "").Length != 0) throw new NotSupportedException("Multilayer image selection requires application decoding.");
                    if (options.TextureResolver is null) throw new NotSupportedException("Image connections require an explicit TextureResolver; no file is opened automatically.");
                    string? sourceSpace = space ?? (type == OpenPbrNodeType.Color3 && rootSpace is not null ? GetColorSpaceName(rootSpace) : null);
                    var image = options.TextureResolver(file, sourceSpace, type) ?? throw new InvalidDataException($"TextureResolver did not resolve '{file}'.");
                    if (image.Source != file || sourceSpace is not null && image.SourceColorSpace != sourceSpace) throw new InvalidDataException("The resolved texture must retain the requested filename identity for export.");
                    result = OpenPbrNode.Image(image, type, uv, u, v, filter); break;
                case "add": result = OpenPbrNode.Add(Operand("in1"), Operand("in2")); break;
                case "multiply": result = OpenPbrNode.Multiply(Operand("in1"), Operand("in2")); break;
                case "subtract": case "min": case "max":
                    // MaterialX 1.39.4 defines zero defaults for both like-type and scalar-RHS ports.
                    var zero = DefaultZero(type);
                    var first = Operand("in1", zero); var second = Operand("in2", zero);
                    result = kind switch {
                        "subtract" => OpenPbrNode.Subtract(first, second),
                        "min" => OpenPbrNode.Min(first, second),
                        _ => OpenPbrNode.Max(first, second),
                    };
                    break;
                case "absval": result = OpenPbrNode.Abs(Operand("in", DefaultZero(type))); break;
                case "divide": result = OpenPbrNode.Divide(Operand("in1", DefaultZero(type)), Operand("in2", DefaultOne(type))); break;
                case "sqrt": result = OpenPbrNode.Sqrt(Operand("in", DefaultZero(type))); break;
                case "mix":
                    var fg = Operand("fg"); var bg = Operand("bg");
                    result = OpenPbrNode.Mix(bg, fg, Operand("mix", OpenPbrNode.Float(.5f))); break;
                case "clamp":
                    var value = Operand("in"); var low = Operand("low", OpenPbrNode.Float(0)); var high = Operand("high", OpenPbrNode.Float(1));
                    if (low.Operation != OpenPbrNodeOperation.Constant || high.Operation != OpenPbrNodeOperation.Constant ||
                        low.Type != OpenPbrNodeType.Float || high.Type != OpenPbrNodeType.Float)
                        throw new NotSupportedException("Clamp requires constant scalar bounds.");
                    result = OpenPbrNode.Clamp(value, low.Value.X, high.Value.X); break;
                case "normalmap":
                    var data = Operand("in", data: true); var scale = Operand("scale", OpenPbrNode.Float(1));
                    if (scale.Operation != OpenPbrNodeOperation.Constant || scale.Type != OpenPbrNodeType.Float)
                        throw new NotSupportedException("Normal scale must be a scalar constant.");
                    if (Literal("space", "string", "tangent") != "tangent") throw new NotSupportedException("Only tangent-space normal maps are supported.");
                    result = OpenPbrNode.NormalMap(data, scale.Value.X); break;
                case "extract": result = OpenPbrNode.Extract(Operand("in"), ParseInteger(Literal("index", "integer", "0"))); break;
                case "convert":
                    result = Operand("in", data: channelData);
                    if (!channelData || type != OpenPbrNodeType.Vector3 || result.Type != OpenPbrNodeType.Vector3)
                        throw new NotSupportedException("This subset supports convert(vector3 to color3) only for raw subsurface channel distances.");
                    break;
                default: throw new NotSupportedException();
            }
            if (inputs.Count > 0) throw new NotSupportedException($"Unsupported inputs on '{name}': {string.Join(", ", inputs.Keys)}.");
            if (result.Type != type) throw new InvalidDataException($"Graph node '{name}' output type is inconsistent with its operands.");
            active.Remove((name, channelData)); built.Add((name, channelData), result); return result;
        }
        private static OpenPbrNode DefaultZero(OpenPbrNodeType type) => type switch
        {
            OpenPbrNodeType.Float => OpenPbrNode.Float(0),
            OpenPbrNodeType.Color3 => OpenPbrNode.Color(new LinearRgba(0, 0, 0, 1, StandardColorSpaces.AcesCg)),
            OpenPbrNodeType.Vector2 => OpenPbrNode.Vector2(Vector2.Zero),
            OpenPbrNodeType.Vector3 => OpenPbrNode.Vector3(Vector3.Zero),
            OpenPbrNodeType.Vector4 => OpenPbrNode.Vector4(Vector4.Zero),
            _ => throw new NotSupportedException($"Arithmetic graph nodes do not support '{TypeName(type)}'."),
        };
        private static OpenPbrNode DefaultOne(OpenPbrNodeType type) => type switch
        {
            OpenPbrNodeType.Float => OpenPbrNode.Float(1),
            OpenPbrNodeType.Color3 => OpenPbrNode.Color(new LinearRgba(1, 1, 1, 1, StandardColorSpaces.AcesCg)),
            OpenPbrNodeType.Vector2 => OpenPbrNode.Vector2(Vector2.One),
            OpenPbrNodeType.Vector3 => OpenPbrNode.Vector3(Vector3.One),
            OpenPbrNodeType.Vector4 => OpenPbrNode.Vector4(Vector4.One),
            _ => throw new NotSupportedException($"Arithmetic graph nodes do not support '{TypeName(type)}'."),
        };
        private static int ParseInteger(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value : throw new InvalidDataException("Expected an integer graph literal.");
        private static OpenPbrAddressMode Address(string value) => value switch
        {
            "periodic" => OpenPbrAddressMode.Periodic, "clamp" => OpenPbrAddressMode.Clamp, "mirror" => OpenPbrAddressMode.Mirror,
            _ => throw new NotSupportedException($"Unsupported image addressing '{value}'."),
        };
        private static OpenPbrNode Constant(XElement input, bool data, StandardRgbColorSpaceReference? inherited)
        {
            CheckAttributes(input, "name", "type", "value", "colorspace");
            string type = Required(input, "type"), value = Required(input, "value");
            string? space = input.Attribute("colorspace")?.Value;
            if (type != "color3" && space is not null || data && space is not null && space != "raw")
                throw new NotSupportedException("Numeric graph data cannot carry a color transform.");
            return type switch
            {
                "float" => OpenPbrNode.Float(ParseFloat(value)),
                "boolean" => OpenPbrNode.Boolean(value == "true" ? true : value == "false" ? false : throw new InvalidDataException("Invalid boolean.")),
                "vector2" => OpenPbrNode.Vector2(ParseVector2(value)),
                "vector3" => OpenPbrNode.Vector3(ParseVector(value)),
                "vector4" => OpenPbrNode.Vector4(ParseVector4(value)),
                "color3" when data => OpenPbrNode.Vector3(ParseVector(value)),
                "color3" => OpenPbrNode.Color(ParseColor(value, space, inherited)),
                _ => throw new NotSupportedException($"Unsupported constant type '{type}'."),
            };
        }
        private static Vector4 ParseVector4(string text)
        {
            if (text.Length > 520) throw new InvalidDataException("Vector literal is too long.");
            string[] parts = text.Split(','); if (parts.Length != 4) throw new InvalidDataException("Expected four vector components.");
            return new(ParseFloat(parts[0]), ParseFloat(parts[1]), ParseFloat(parts[2]), ParseFloat(parts[3]));
        }
        private static Vector2 ParseVector2(string text)
        {
            if (text.Length > 260) throw new InvalidDataException("Vector literal is too long.");
            string[] parts = text.Split(','); if (parts.Length != 2) throw new InvalidDataException("Expected two vector components.");
            return new(ParseFloat(parts[0]), ParseFloat(parts[1]));
        }
    }

    private static void WriteGraph(XElement root, XElement surface, string graphName, OpenPbrGraph graph)
    {
        var container = new XElement("nodegraph", new XAttribute("name", graphName));
        Dictionary<OpenPbrNode, string> ids = graph.Nodes.Select((n, i) => (n, id: "n" + i)).ToDictionary(p => p.n, p => p.id);
        XElement Input(string name, OpenPbrNode n) => new("input", new XAttribute("name", name), new XAttribute("type", TypeName(n.Type)), new XAttribute("nodename", ids[n]));
        XElement Literal(string name, string type, string value) => new("input", new XAttribute("name", name), new XAttribute("type", type), new XAttribute("value", value));
        foreach (var n in graph.Nodes)
        {
            string kind = n.Operation switch { OpenPbrNodeOperation.NormalMap => "normalmap", OpenPbrNodeOperation.Abs => "absval",
                _ => n.Operation.ToString().ToLowerInvariant() };
            var node = new XElement(kind, new XAttribute("name", ids[n]), new XAttribute("type", TypeName(n.Type)));
            switch (n.Operation)
            {
                case OpenPbrNodeOperation.Constant:
                    string value = n.Type switch
                    {
                        OpenPbrNodeType.Float => Format(n.Value.X), OpenPbrNodeType.Boolean => n.Value.X == 0 ? "false" : "true",
                        OpenPbrNodeType.Vector4 => Format(n.Value.X) + ", " + Format(n.Value.Y) + ", " + Format(n.Value.Z) + ", " + Format(n.Value.W),
                        OpenPbrNodeType.Vector2 => Format(n.Value.X) + ", " + Format(n.Value.Y),
                        _ => Format(n.Value.X) + ", " + Format(n.Value.Y) + ", " + Format(n.Value.Z),
                    };
                    var literal = Literal("value", TypeName(n.Type), value);
                    if (n.Type == OpenPbrNodeType.Color3) literal.Add(new XAttribute("colorspace", "acescg"));
                    node.Add(literal); break;
                case OpenPbrNodeOperation.Texcoord: node.Add(Literal("index", "integer", Format(n.Value.X))); break;
                case OpenPbrNodeOperation.Image:
                    if (string.IsNullOrWhiteSpace(n.Texture!.Source)) throw new NotSupportedException("Exported image nodes require a Source filename; write the resource through the application.");
                    // Resources returned by a resolver retain their original filename. Explicit ACEScg
                    // here describes already-converted resources supplied directly by the application.
                    node.Add(new XAttribute("colorspace", n.Texture.SourceColorSpace));
                    node.Add(Literal("file", "filename", n.Texture.Source), Input("texcoord", n.A!),
                        Literal("uaddressmode", "string", n.AddressU.ToString().ToLowerInvariant()),
                        Literal("vaddressmode", "string", n.AddressV.ToString().ToLowerInvariant()),
                        Literal("filtertype", "string", n.Filter.ToString().ToLowerInvariant())); break;
                case OpenPbrNodeOperation.Add: case OpenPbrNodeOperation.Multiply:
                case OpenPbrNodeOperation.Subtract: case OpenPbrNodeOperation.Min: case OpenPbrNodeOperation.Max:
                case OpenPbrNodeOperation.Divide:
                    node.Add(Input("in1", n.A!), Input("in2", n.B!)); break;
                case OpenPbrNodeOperation.Abs: node.Add(Input("in", n.A!)); break;
                case OpenPbrNodeOperation.Sqrt: node.Add(Input("in", n.A!)); break;
                case OpenPbrNodeOperation.Mix: node.Add(Input("bg", n.A!), Input("fg", n.B!), Input("mix", n.C!)); break;
                case OpenPbrNodeOperation.Clamp:
                    node.Add(Input("in", n.A!), Literal("low", "float", Format(n.Value.X)), Literal("high", "float", Format(n.Value.Y))); break;
                case OpenPbrNodeOperation.NormalMap: node.Add(Input("in", n.A!), Literal("scale", "float", Format(n.Value.X))); break;
                case OpenPbrNodeOperation.Extract: node.Add(Input("in", n.A!), Literal("index", "integer", Format(n.Value.X))); break;
            }
            container.Add(node);
        }
        foreach (var binding in graph.Bindings)
        {
            string name = OpenPbrInputInfo.Get(binding.Key).Name, type = TypeName(binding.Value.Type), source = ids[binding.Value];
            if (binding.Key == OpenPbrInput.SubsurfaceRadiusScale)
            {
                type = "color3"; source = "radius_channels";
                container.Add(new XElement("convert", new XAttribute("name", source), new XAttribute("type", type), Input("in", binding.Value)));
            }
            container.Add(new XElement("output", new XAttribute("name", name), new XAttribute("type", type), new XAttribute("nodename", source)));
            surface.Elements("input").FirstOrDefault(e => e.Attribute("name")?.Value == name)?.Remove();
            surface.Add(new XElement("input", new XAttribute("name", name), new XAttribute("type", type),
                new XAttribute("nodegraph", graphName), new XAttribute("output", name)));
        }
        root.Add(container);
    }
}

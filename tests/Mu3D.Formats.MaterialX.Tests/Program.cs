using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Mu3D.Color;
using Mu3D.Formats.MaterialX;
using Mu3D.SceneGraph;

var count = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    count++;
}
void Reject<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T) { count++; return; }
    throw new InvalidOperationException(message);
}
MaterialXOpenPbrDocument Read(string xml, MaterialXImportOptions? options = null)
{
    using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
    return MaterialXOpenPbrSerializer.Import(stream, options);
}
string Write(MaterialXOpenPbrDocument document)
{
    using var stream = new MemoryStream();
    MaterialXOpenPbrSerializer.Export(stream, document);
    Check(stream.CanWrite, "Export must leave caller stream open.");
    return Encoding.UTF8.GetString(stream.ToArray());
}
string SurfaceXml(string inputs, string attributes = "") =>
    $"<materialx version='1.39'><open_pbr_surface name='Surface' type='surfaceshader' {attributes}>{inputs}</open_pbr_surface></materialx>";
string Input(string name, string type, string value, string extra = "") =>
    $"<input name='{name}' type='{type}' value='{value}' {extra}/>";

var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "open_pbr_surface_1_1_1.mtlx");
var fixtureBytes = File.ReadAllBytes(fixturePath);
var fixtureHash = Convert.ToHexString(SHA256.HashData(fixtureBytes));
Check(fixtureHash == "C15674AAA82EF4BF0A27388B3B3A8F0F171982F53DF6EBBCEED679CE63699F51", "Pinned upstream reference fixture hash mismatch.");
var definition = XDocument.Load(fixturePath).Root!.Element("nodedef")!;
Check(definition.Attribute("version")!.Value == OpenPbrSurface.SpecificationVersion, "Core and fixture version mismatch.");
var referenceInputs = definition.Elements("input").ToArray();
Check(referenceInputs.Length == 41, "Pinned OpenPBR definition must contain 41 inputs.");

var defaults = new MaterialXOpenPbrDocument([new MaterialXOpenPbrSurface("DefaultSurface", new OpenPbrSurface())],
    [new MaterialXSurfaceMaterial("DefaultMaterial", "DefaultSurface")]);
var defaultXml = Write(defaults);
var defaultInputs = XDocument.Parse(defaultXml).Root!.Element("open_pbr_surface")!.Elements("input").ToDictionary(e => e.Attribute("name")!.Value);
Check(defaultInputs.Count == 37, "Default export must omit the four inherited geometry vectors.");
foreach (var reference in referenceInputs)
{
    var name = reference.Attribute("name")!.Value;
    if (reference.Attribute("value") is not { } value) continue;
    Check(defaultInputs[name].Attribute("type")!.Value == reference.Attribute("type")!.Value, $"Wrong type for {name}.");
    if (reference.Attribute("type")!.Value == "boolean")
        Check(defaultInputs[name].Attribute("value")!.Value == value.Value, $"Wrong default for {name}.");
    else
    {
        var expected = value.Value.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture));
        var actual = defaultInputs[name].Attribute("value")!.Value.Split(',').Select(v => float.Parse(v, CultureInfo.InvariantCulture));
        Check(expected.SequenceEqual(actual), $"Wrong default for {name}.");
    }
}
Check(Write(Read(defaultXml)) == defaultXml, "Default document roundtrip must be byte deterministic.");

// Construct a constant instance from every input of the unmodified upstream definition. This catches
// schema drift independently of the hand-written serializer's parameter dispatch.
var authoredNode = new XElement("open_pbr_surface", new XAttribute("name", "FullSurface"), new XAttribute("type", "surfaceshader"),
    new XAttribute("version", "1.1.1"), new XAttribute("colorspace", "acescg"));
foreach (var reference in referenceInputs)
{
    var name = reference.Attribute("name")!.Value;
    var type = reference.Attribute("type")!.Value;
    var value = type switch
    {
        "float" => name == "emission_luminance" ? "1550.25" : "0.375",
        "boolean" => "true",
        "color3" => name == "subsurface_radius_scale" ? "1.5, 0.5, 0.25" : "-0.125, 0.75, 2.5",
        "vector3" => "-2.25, 1.5, 0.75",
        _ => throw new InvalidOperationException("Unexpected pinned type."),
    };
    authoredNode.Add(new XElement("input", new XAttribute("name", name), new XAttribute("type", type), new XAttribute("value", value)));
}
var authoredXml = new XElement("materialx", new XAttribute("version", "1.39"), authoredNode,
    new XElement("surfacematerial", new XAttribute("name", "FullMaterial"), new XAttribute("type", "material"),
        new XElement("input", new XAttribute("name", "surfaceshader"), new XAttribute("type", "surfaceshader"), new XAttribute("nodename", "FullSurface")))).ToString();
var originalCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    var authored = Read(authoredXml);
    var serialized = Write(authored);
    var reread = Read(serialized);
    Check(Write(reread) == serialized, "All 41 parameters must roundtrip under a comma-decimal culture.");
    var writtenInputs = XDocument.Parse(serialized).Root!.Element("open_pbr_surface")!.Elements("input").ToDictionary(e => e.Attribute("name")!.Value);
    Check(writtenInputs.Count == 41, "All authored parameters must be exported.");
    foreach (var input in authoredNode.Elements("input"))
    {
        var name = input.Attribute("name")!.Value;
        Check(writtenInputs[name].Attribute("value")!.Value == input.Attribute("value")!.Value, $"Authored value changed: {name}.");
    }
    Check(reread.Surfaces[0].Surface.BaseColor.Red == -0.125f && reread.Surfaces[0].Surface.BaseColor.Blue == 2.5f,
        "Signed and extended-range color must survive without clipping.");
    Check(reread.Surfaces[0].Surface.EmissionLuminance == 1550.25f, "Nits must survive without normalization.");
    Check(reread.Surfaces[0].Surface.ThinFilmThickness == 0.375f, "Thin-film micrometres must not become nanometres.");
    Check(reread.Surfaces[0].Surface.GeometryNormal == new Vector3(-2.25f, 1.5f, 0.75f), "Geometry vectors must not be normalized.");
    Check(reread.Materials[0].SurfaceNodeName == "FullSurface", "Material surface reference must survive.");
}
finally { CultureInfo.CurrentCulture = originalCulture; }

var forwardReference = "<materialx version='1.39'><surfacematerial name='M' type='material'><input name='surfaceshader' type='surfaceshader' nodename='S'/></surfacematerial><open_pbr_surface name='S' type='surfaceshader'/></materialx>";
Check(Read(forwardReference).Materials[0].SurfaceNodeName == "S", "Forward references must resolve after parsing.");
var assigned = Read(SurfaceXml(Input("base_color", "color3", "0.2, 0.3, 0.4")),
    new MaterialXImportOptions { DefaultColorSpace = StandardColorSpaces.LinearSrgb });
Check(assigned.Surfaces[0].Surface.BaseColor.ColorSpace == StandardColorSpaces.LinearSrgb, "Caller color assignment must be honored.");
Check(assigned.Surfaces[0].Surface.CoatColor.ColorSpace == StandardColorSpaces.LinearSrgb, "Inherited default colors must follow explicit assignment.");
var mixed = Read(SurfaceXml(Input("base_color", "color3", "0.2, 0.3, 0.4", "colorspace='lin_rec709'"), "colorspace='acescg'"));
Check(mixed.Surfaces[0].Surface.BaseColor.ColorSpace == StandardColorSpaces.LinearSrgb && mixed.Surfaces[0].Surface.CoatColor.ColorSpace == StandardColorSpaces.AcesCg,
    "Per-input color identity must override node identity without converting values.");
Check(Read(SurfaceXml(Input("base_color", "color3", "1,1,1"), "colorspace='lin_ap1'")).Surfaces[0].Surface.BaseColor.ColorSpace == StandardColorSpaces.AcesCg,
    "MaterialX lin_ap1 alias must resolve to ACEScg.");

Reject<InvalidDataException>(() => Read(SurfaceXml(Input("base_color", "color3", "1,1,1"))), "Untagged authored color must fail.");
Reject<NotSupportedException>(() => Read(SurfaceXml(Input("base_color", "color3", "1,1,1", "colorspace='srgb_texture'"))), "Encoded colors require an explicit transform, not relabeling.");
Reject<NotSupportedException>(() => Read(SurfaceXml("", "colorspace='unrecognized'")), "Unknown inherited color metadata must fail.");
Reject<NotSupportedException>(() => Read(defaultXml.Replace("version=\"1.39\"", "version=\"1.38\"")), "MaterialX version mismatch must fail.");
Reject<NotSupportedException>(() => Read(SurfaceXml("", "version='1.0'")), "OpenPBR version mismatch must fail.");
Reject<NotSupportedException>(() => Read(SurfaceXml("", "nodedef='other'")), "Unknown nodedef must fail.");
Reject<InvalidDataException>(() => Read(SurfaceXml(Input("base_color", "color3", "1,1,1", "nodename='Texture'"))), "Texture connections must fail even when fallback value exists.");
foreach (var attr in new[] { "interfacename='Input'", "channels='rgb'", "unit='centimeter'", "unittype='distance'" })
    Reject<NotSupportedException>(() => Read(SurfaceXml(Input("base_weight", "float", "0.5", attr))), $"Unsupported input semantic {attr} must fail.");
Reject<NotSupportedException>(() => Read(SurfaceXml(Input("unknown_parameter", "float", "0.5"))), "Unknown parameters must fail.");
Reject<InvalidDataException>(() => Read(SurfaceXml(Input("base_weight", "color3", "0.5,0.5,0.5"))), "Wrong parameter type must fail.");
Reject<InvalidDataException>(() => Read(SurfaceXml(Input("base_weight", "float", "0.5", "colorspace='acescg'"))), "Numeric colorspace tags must fail.");
Reject<InvalidDataException>(() => Read(SurfaceXml(Input("subsurface_radius_scale", "color3", "1,1,1", "colorspace='acescg'"))), "Length scales must not be treated as colors.");
foreach (var value in new[] { "NaN", "Infinity", "-Infinity", "1e100", "1,000", "2.0", "-0.1", "" })
    Reject<InvalidDataException>(() => Read(SurfaceXml(Input("base_weight", "float", value))), $"Invalid bounded float {value} must fail.");
foreach (var value in new[] { "1,2", "1,2,3,4", "1;2;3", "1,2,NaN", "0,0,0" })
    Reject<InvalidDataException>(() => Read(SurfaceXml(Input("geometry_normal", "vector3", value))), $"Invalid direction {value} must fail.");
Reject<InvalidDataException>(() => Read(SurfaceXml(Input("geometry_thin_walled", "boolean", "1"))), "Non-MaterialX bool spelling must fail.");
Reject<InvalidDataException>(() => Read(SurfaceXml(Input("base_weight", "float", "0.5") + Input("base_weight", "float", "0.4"))), "Duplicate inputs must fail.");
Reject<InvalidDataException>(() => Read(forwardReference.Replace("nodename='S'", "nodename='Missing'")), "Dangling reference must fail.");
Reject<InvalidDataException>(() => Read(forwardReference.Replace("name='M'", "name='S'")), "Duplicate sibling names must fail.");
Reject<InvalidDataException>(() => Read(SurfaceXml("").Replace("name='Surface'", "name='bad/name'")), "Unsupported identifier must fail.");
Reject<InvalidDataException>(() => Read("<materialx version='1.39'/>"), "Empty document must fail.");
Reject<InvalidDataException>(() => Read("<materialx version='1.39'><open_pbr_surface"), "Malformed XML must fail.");
Reject<InvalidDataException>(() => Read("<!DOCTYPE materialx [<!ENTITY bomb 'large'>]><materialx version='1.39'>&bomb;</materialx>"), "Internal entities must be prohibited.");
Reject<InvalidDataException>(() => Read("<!DOCTYPE materialx SYSTEM 'file:///etc/passwd'><materialx version='1.39'/>"), "External DTDs must be prohibited without resolution.");
Reject<NotSupportedException>(() => Read("<materialx version='1.39' xmlns:xi='http://www.w3.org/2001/XInclude'><xi:include href='https://example.invalid/payload'/></materialx>"), "XInclude must fail without fetching.");
Reject<InvalidDataException>(() => Read("<materialx version='1.39'><nodegraph name='Graph'/></materialx>"), "A graph alone does not define an OpenPBR surface.");
Reject<NotSupportedException>(() => Read("<?evaluate file='evil'?><materialx version='1.39'/>"), "Processing instructions must fail.");
Reject<InvalidDataException>(() => Read(SurfaceXml("text")), "Unexpected element text must fail.");
Reject<NotSupportedException>(() => Read(SurfaceXml("<input name='base_weight' type='float'><nested><deeper/></nested></input>")), "Nesting must fail before tree construction.");
Reject<InvalidDataException>(() => Read(defaultXml, new MaterialXImportOptions { MaximumDocumentBytes = 64 }), "Encoded byte limit must fail.");
Reject<InvalidDataException>(() => Read(defaultXml, new MaterialXImportOptions { MaximumMaterialCount = 0 }), "Material count limit must fail.");
Reject<InvalidDataException>(() => Read("<materialx version='1.39'><open_pbr_surface name='S1' type='surfaceshader'/><open_pbr_surface name='S2' type='surfaceshader'/></materialx>",
    new MaterialXImportOptions { MaximumSurfaceCount = 1 }), "Surface count limit must fail.");
Reject<ArgumentOutOfRangeException>(() => Read(defaultXml, new MaterialXImportOptions { MaximumDocumentBytes = 0 }), "Zero byte limit must fail as API misuse.");
Reject<NotSupportedException>(() => Write(new MaterialXOpenPbrDocument([new MaterialXOpenPbrSurface("S", new OpenPbrSurface { MetersPerUnit = 0.01f })])),
    "Unsupported scene unit scale must never be silently discarded.");
Reject<NotSupportedException>(() => Write(new MaterialXOpenPbrDocument([new MaterialXOpenPbrSurface("S", new OpenPbrSurface { BaseColor = new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearDisplayP3) })])),
    "Unsupported export color identity must fail without relabeling.");

using var sourceStream = new MemoryStream(Encoding.UTF8.GetBytes(defaultXml));
_ = MaterialXOpenPbrSerializer.Import(sourceStream);
Check(sourceStream.CanRead, "Import must leave caller stream open.");
sourceStream.Position = 0;
_ = await MaterialXOpenPbrSerializer.ImportAsync(sourceStream);
Check(sourceStream.CanRead, "Async import must leave caller stream open.");
sourceStream.Position = 0;
using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    _ = await MaterialXOpenPbrSerializer.ImportAsync(sourceStream, cancellationToken: cancelled.Token);
    throw new InvalidOperationException("Cancellation must interrupt asynchronous import.");
}
catch (OperationCanceledException) { count++; }
Check(sourceStream.CanRead, "Cancelled import must leave caller stream open.");

using var failedSource = new MemoryStream(Encoding.UTF8.GetBytes("<!DOCTYPE materialx><materialx/>"));
Reject<InvalidDataException>(() => MaterialXOpenPbrSerializer.Import(failedSource), "DTD-bearing source must fail.");
Check(failedSource.CanRead, "Rejected import must leave caller stream open.");
using var untouchedDestination = new MemoryStream();
var unsupportedDocument = new MaterialXOpenPbrDocument([
    new MaterialXOpenPbrSurface("Valid", new OpenPbrSurface()),
    new MaterialXOpenPbrSurface("Unsupported", new OpenPbrSurface { MetersPerUnit = 0.01f }),
]);
Reject<NotSupportedException>(() => MaterialXOpenPbrSerializer.Export(untouchedDestination, unsupportedDocument), "Late export validation must fail.");
Check(untouchedDestination.Length == 0 && untouchedDestination.CanWrite, "Semantic rejection must leave the caller destination untouched and open.");

var boundaryBytes = Encoding.UTF8.GetBytes(SurfaceXml(""));
using var boundarySource = new MemoryStream(boundaryBytes);
Check(MaterialXOpenPbrSerializer.Import(boundarySource, new MaterialXImportOptions { MaximumDocumentBytes = boundaryBytes.Length }).Surfaces.Count == 1,
    "An exact byte-limit document must succeed.");
using var utf16Source = new MemoryStream(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(SurfaceXml(""))).ToArray());
Check(MaterialXOpenPbrSerializer.Import(utf16Source).Surfaces.Count == 1, "XML reader must honor UTF-16 BOM without relabeling bytes.");

// Keep one actual exported document in ignored build output for optional upstream-SDK validation.
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ExportedOpenPbr.mtlx"), Write(Read(authoredXml)), new UTF8Encoding(false));

Console.WriteLine($"MaterialX OpenPBR tests passed ({count} checks; 41-input pinned reference, roundtrip, color semantics, bounds and hostile XML).");

Console.WriteLine($"MaterialX graph checks passed: {GraphChecks.Run()}.");

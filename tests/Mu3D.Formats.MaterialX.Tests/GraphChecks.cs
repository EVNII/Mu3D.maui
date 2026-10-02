using System.Numerics;
using System.Text;
using Mu3D.Color;
using Mu3D.Formats.MaterialX;
using Mu3D.SceneGraph;

internal static class GraphChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Expect(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
        void Reject(Action action) { checks++; try { action(); } catch (Exception e) when (e is InvalidDataException or NotSupportedException or ArgumentException) { return; } throw new InvalidOperationException("Invalid graph was accepted."); }
        OpenPbrTexture colors = OpenPbrTexture.FromColor(new(2, 2,
            [new(1,0,0,1), new(0,1,0,1), new(0,0,1,1), new(1,1,1,1)], StandardColorSpaces.LinearSrgb), "colors.png", "srgb_texture");
        OpenPbrTexture data = OpenPbrTexture.FromData(2, 2,
            [new(.5f,.5f,1,1), new(.8f,.5f,.9f,1), new(.2f,.5f,.9f,1), new(.5f,.7f,.9f,1)], "data.exr");
        var color = OpenPbrNode.Image(colors, OpenPbrNodeType.Color3, OpenPbrNode.Texcoord(1), OpenPbrAddressMode.Mirror, OpenPbrAddressMode.Clamp);
        var sample = OpenPbrNode.Image(data, OpenPbrNodeType.Vector3);
        var normal = OpenPbrNode.NormalMap(sample, .75f);
        Dictionary<OpenPbrInput, OpenPbrNode> bindings = new()
        {
            [OpenPbrInput.BaseColor] = OpenPbrNode.Mix(OpenPbrNode.Color(new(.2f,.2f,.2f,1,StandardColorSpaces.AcesCg)), color, OpenPbrNode.Float(.7f)),
            [OpenPbrInput.SpecularRoughness] = OpenPbrNode.Clamp(OpenPbrNode.Add(OpenPbrNode.Extract(sample, 0), OpenPbrNode.Float(.2f))),
            [OpenPbrInput.GeometryNormal] = normal,
            [OpenPbrInput.SubsurfaceRadiusScale] = OpenPbrNode.Vector3(new(1,.5f,.25f)),
            [OpenPbrInput.GeometryOpacity] = OpenPbrNode.Extract(OpenPbrNode.Vector4(new(.1f,.2f,.3f,.4f)), 3),
            [OpenPbrInput.GeometryThinWalled] = OpenPbrNode.Boolean(true),
            [OpenPbrInput.EmissionColor] = OpenPbrNode.Multiply(color, OpenPbrNode.Float(8)),
        };
        var surface = new OpenPbrSurface { Graph = new(bindings), BaseColor = new(-1,2,3,1,StandardColorSpaces.LinearDisplayP3) };
        var document = new MaterialXOpenPbrDocument([new("Surface", surface)], [new("Material", "Surface")]);
        MaterialXImportOptions options = new()
        {
            TextureResolver = (file, space, type) =>
            {
                Expect(file == "colors.png" ? space == "srgb_texture" && type == OpenPbrNodeType.Color3 : space == "raw", "Resource color identity survives XML.");
                return file == "colors.png" ? colors : data;
            },
        };
        string Write(MaterialXOpenPbrDocument doc) { using MemoryStream s = new(); MaterialXOpenPbrSerializer.Export(s, doc); return Encoding.UTF8.GetString(s.ToArray()); }
        MaterialXOpenPbrDocument Read(string xml, MaterialXImportOptions? o = null)
        { using MemoryStream s = new(Encoding.UTF8.GetBytes(xml)); return MaterialXOpenPbrSerializer.Import(s, o); }
        string xml = Write(document);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ExportedGraph.mtlx"), xml);
        var imported = Read(xml, options).Surfaces[0].Surface.Graph!;
        for (int y = 0; y < 5; y++) for (int x = 0; x < 5; x++)
        foreach (var input in bindings.Keys)
        {
            Vector2 uv = new(x * .41f - .4f, y * .41f - .4f);
            Vector4 a = OpenPbrGraphEvaluator.Evaluate(surface.Graph!, input, uv, uv, Vector3.UnitZ, new(1,0,0,1));
            Vector4 b = OpenPbrGraphEvaluator.Evaluate(imported, input, uv, uv, Vector3.UnitZ, new(1,0,0,1));
            Expect(Vector4.Distance(a,b) < 1e-6f, "Graph XML round trip preserves evaluated results.");
        }
        Expect(Write(Read(xml, options)) == xml, "Graph serialization is deterministic.");
        Expect(ReferenceEquals(surface.Clone().Graph, surface.Graph), "Clones safely share immutable graphs.");
        Reject(() => Read(xml));
        Reject(() => Read(xml, new() { MaximumGraphNodes = 1, TextureResolver = options.TextureResolver }));
        Reject(() => new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode> { [OpenPbrInput.BaseColor] = OpenPbrNode.Float(1) }));
        Reject(() => new OpenPbrGraph(new Dictionary<OpenPbrInput, OpenPbrNode> { [OpenPbrInput.GeometryOpacity] = OpenPbrNode.Float(2) }));
        Reject(() => OpenPbrNode.NormalMap(OpenPbrNode.Vector3(new(float.MaxValue))));
        Reject(() => surface.ToPbrPreview(new() { Policy = OpenPbrPreviewPolicy.AllowLossyApproximation }));
        string Wrap(string nodes, string port) => $"<materialx version='1.39'>{nodes}<open_pbr_surface name='S' type='surfaceshader'>{port}</open_pbr_surface></materialx>";
        const string Port = "<input name='base_weight' type='float' nodename='a'/>";
        Reject(() => Read(Wrap("<add name='a' type='float'><input name='in1' type='float' nodename='a'/><input name='in2' type='float' value='0'/></add>", Port)));
        Reject(() => Read(Wrap("", Port)));
        Reject(() => Read(Wrap("<noise3d name='a' type='float'/>", Port)));
        Reject(() => Read(Wrap("<constant name='a' type='float'><input name='value' type='float' value='1'/></constant>", Port.Replace("nodename='a'", "nodename='a' value='1'"))));
        // Independent hand-authored standard nodegraph, including forward references.
        var hand = Read("""
            <materialx version="1.39"><open_pbr_surface name="S" type="surfaceshader">
              <input name="specular_roughness" type="float" nodegraph="G" output="out"/></open_pbr_surface>
              <nodegraph name="G"><output name="out" type="float" nodename="scale"/>
                <multiply name="scale" type="float"><input name="in1" type="float" nodename="r"/><input name="in2" type="float" value="0.5"/></multiply>
                <constant name="r" type="float"><input name="value" type="float" value="0.8"/></constant>
              </nodegraph></materialx>
            """);
        Expect(Math.Abs(OpenPbrGraphEvaluator.Evaluate(hand.Surfaces[0].Surface.Graph!, OpenPbrInput.SpecularRoughness, default, default, Vector3.UnitZ, new(1,0,0,1)).X - .4f) < 1e-6f, "Hand-authored MaterialX standard operations work.");
        return checks;
    }
}

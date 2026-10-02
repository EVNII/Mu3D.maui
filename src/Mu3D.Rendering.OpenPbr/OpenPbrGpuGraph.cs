using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

// Four float4 words per instruction, then one word per output binding. Integer addresses
// remain below 2^24 so their FP32 representation is exact on every backend.
internal sealed class OpenPbrGpuGraph
{
    internal OpenPbrGraph?[] Graphs { get; }
    internal Vector4[] Programs { get; }
    internal Vector4[] Pixels { get; }
    private readonly Vector3[] headers;
    private readonly float meters;
    private readonly long budget;

    internal bool IsCurrent(IReadOnlyList<OpenPbrMaterial> materials, float units, long maximumBytes) =>
        meters == units && budget == maximumBytes && Graphs.Length == materials.Count &&
        materials.Select((m, i) => ReferenceEquals(m.Surface.Graph, Graphs[i])).All(x => x);

    internal OpenPbrGpuGraph(IReadOnlyList<OpenPbrMaterial> materials, float units, long maximumBytes)
    {
        meters = units; budget = maximumBytes;
        Graphs = materials.Select(m => m.Surface.Graph).ToArray(); headers = new Vector3[materials.Count];
        List<Vector4> programs = [], pixels = [];
        Dictionary<OpenPbrTexture, int> images = [];
        for (int m = 0; m < materials.Count; m++)
        {
            var graph = Graphs[m]; if (graph is null || graph.Bindings.Count == 0) continue;
            headers[m] = new(programs.Count + 1, graph.Nodes.Count, graph.Bindings.Count);
            Dictionary<OpenPbrNode, int> indices = graph.Nodes.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);
            foreach (var n in graph.Nodes)
            {
                int offset = 0;
                if (n.Texture is { } image && !images.TryGetValue(image, out offset))
                {
                    offset = pixels.Count;
                    CheckBudget((long)programs.Count + graph.Nodes.Count * 4 + graph.Bindings.Count,
                        (long)pixels.Count + image.Pixels.Count);
                    pixels.AddRange(image.Pixels); images.Add(image, offset);
                }
                programs.Add(new((int)n.Operation, n.A is null ? 0 : indices[n.A], n.B is null ? 0 : indices[n.B], n.C is null ? 0 : indices[n.C]));
                programs.Add(n.Value);
                programs.Add(new(offset, n.Texture?.Width ?? 0, n.Texture?.Height ?? 0, (int)n.Type));
                programs.Add(new((int)n.AddressU, (int)n.AddressV, (int)n.Filter, 0));
            }
            foreach (var binding in graph.Bindings)
            {
                var (slot, channel) = Slot(binding.Key);
                programs.Add(new(slot, channel, indices[binding.Value], binding.Key is OpenPbrInput.TransmissionDepth or OpenPbrInput.SubsurfaceRadius ? 1 : 0));
            }
            CheckBudget(programs.Count, pixels.Count);
        }
        Programs = programs.ToArray(); Pixels = pixels.ToArray();
        void CheckBudget(long words, long texels)
        {
            if (words >= 8_388_608 || texels >= 8_388_608 || (words + texels) * 16 > maximumBytes)
                throw new InvalidOperationException("OpenPBR material graphs exceed MaximumTextureBytes or exact-address limits.");
        }
    }
    internal void SetHeaders(Vector4[] packed)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            int p = i * OpenPbrGpuMaterial.Stride + 18;
            packed[p] = new(packed[p].X, headers[i].X, headers[i].Y, headers[i].Z);
        }
    }
    internal static (int, int) Slot(OpenPbrInput input) => input switch
    {
        OpenPbrInput.BaseColor => (0, -1), OpenPbrInput.BaseWeight => (0, 3),
        OpenPbrInput.SpecularColor => (1, -1), OpenPbrInput.BaseMetalness => (1, 3),
        OpenPbrInput.BaseDiffuseRoughness => (2, 0), OpenPbrInput.SpecularWeight => (2, 1),
        OpenPbrInput.SpecularRoughness => (2, 2), OpenPbrInput.SpecularIor => (2, 3),
        OpenPbrInput.SpecularRoughnessAnisotropy => (3, 0), OpenPbrInput.TransmissionWeight => (3, 1),
        OpenPbrInput.TransmissionDepth => (3, 2), OpenPbrInput.TransmissionScatterAnisotropy => (3, 3),
        OpenPbrInput.TransmissionColor => (4, -1), OpenPbrInput.TransmissionDispersionScale => (4, 3),
        OpenPbrInput.TransmissionScatter => (5, -1), OpenPbrInput.TransmissionDispersionAbbeNumber => (5, 3),
        OpenPbrInput.SubsurfaceColor => (6, -1), OpenPbrInput.SubsurfaceWeight => (6, 3),
        OpenPbrInput.SubsurfaceRadiusScale => (7, -1), OpenPbrInput.SubsurfaceRadius => (7, 3),
        OpenPbrInput.SubsurfaceScatterAnisotropy => (8, 0), OpenPbrInput.FuzzWeight => (8, 1),
        OpenPbrInput.FuzzRoughness => (8, 2), OpenPbrInput.CoatWeight => (8, 3),
        OpenPbrInput.FuzzColor => (9, -1), OpenPbrInput.CoatRoughness => (9, 3),
        OpenPbrInput.CoatColor => (10, -1), OpenPbrInput.CoatIor => (10, 3),
        OpenPbrInput.CoatRoughnessAnisotropy => (11, 0), OpenPbrInput.CoatDarkening => (11, 1),
        OpenPbrInput.ThinFilmWeight => (11, 2), OpenPbrInput.ThinFilmThickness => (11, 3),
        OpenPbrInput.ThinFilmIor => (12, 0), OpenPbrInput.EmissionLuminance => (12, 1),
        OpenPbrInput.GeometryOpacity => (12, 2), OpenPbrInput.GeometryThinWalled => (12, 3),
        OpenPbrInput.EmissionColor => (13, -1), OpenPbrInput.GeometryNormal => (14, -1),
        OpenPbrInput.GeometryTangent => (15, -1), OpenPbrInput.GeometryCoatNormal => (16, -1),
        OpenPbrInput.GeometryCoatTangent => (17, -1),
        _ => throw new ArgumentOutOfRangeException(nameof(input)),
    };
}

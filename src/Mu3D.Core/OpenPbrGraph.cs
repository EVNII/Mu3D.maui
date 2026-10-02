using System.Collections.ObjectModel;
using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Owns immutable OpenPBR input connections and a bounded topological node inventory.</summary>
/// <remarks>Unconnected inputs retain the surface's constants. Graphs are shared safely by cloned
/// surfaces; replacing a graph resets render accumulation. A graph contains at most 64 unique nodes.
/// Physical bounds are conservative: add an explicit Clamp when an expression may leave a parameter's domain.</remarks>
public sealed class OpenPbrGraph
{
    /// <summary>Maximum distinct operations evaluated at one surface hit.</summary>
    public const int MaximumNodes = 64;

    /// <summary>Creates validated, immutable connections; duplicate input keys are rejected.</summary>
    public OpenPbrGraph(IEnumerable<KeyValuePair<OpenPbrInput, OpenPbrNode>> bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Dictionary<OpenPbrInput, OpenPbrNode> copy = [];
        List<OpenPbrNode> nodes = [];
        HashSet<OpenPbrNode> seen = [];
        foreach (var binding in bindings)
        {
            var info = OpenPbrInputInfo.Get(binding.Key);
            ArgumentNullException.ThrowIfNull(binding.Value);
            if (!copy.TryAdd(binding.Key, binding.Value)) throw new ArgumentException("An input can have only one connection.", nameof(bindings));
            if (binding.Value.Type != info.Type) throw new ArgumentException($"{info.Name} requires {info.Type}.", nameof(bindings));
            if (binding.Key is OpenPbrInput.GeometryNormal or OpenPbrInput.GeometryCoatNormal or
                OpenPbrInput.GeometryTangent or OpenPbrInput.GeometryCoatTangent &&
                binding.Value.IsUniform && binding.Value.Minimum == Vector4.Zero && binding.Value.Maximum == Vector4.Zero)
                throw new ArgumentException("A uniform geometry direction cannot be zero.", nameof(bindings));
            int channels = info.Type is OpenPbrNodeType.Float or OpenPbrNodeType.Boolean ? 1 : 3;
            for (int c = 0; c < channels; c++)
                if (binding.Value.Minimum[c] < info.Minimum || binding.Value.Maximum[c] > info.Maximum)
                    throw new ArgumentOutOfRangeException(nameof(bindings), $"{info.Name} exceeds its physical input domain; use an explicit Clamp.");
            Visit(binding.Value);
        }
        Bindings = new ReadOnlyDictionary<OpenPbrInput, OpenPbrNode>(copy);
        Nodes = new ReadOnlyCollection<OpenPbrNode>(nodes);
        void Visit(OpenPbrNode node)
        {
            if (!seen.Add(node)) return;
            if (seen.Count > MaximumNodes) throw new ArgumentException("Material graph exceeds 64 unique nodes.", nameof(bindings));
            if (node.A is not null) Visit(node.A);
            if (node.B is not null) Visit(node.B);
            if (node.C is not null) Visit(node.C);
            nodes.Add(node);
        }
    }
    /// <summary>Gets parameter-to-expression connections.</summary>
    public IReadOnlyDictionary<OpenPbrInput, OpenPbrNode> Bindings { get; }
    /// <summary>Gets unique nodes in dependency-first order.</summary>
    public IReadOnlyList<OpenPbrNode> Nodes { get; }
    /// <summary>Gets an input's conservative lower bound, or the supplied constant fallback.</summary>
    public float Minimum(OpenPbrInput input, float fallback) => Bindings.TryGetValue(input, out var node) ? node.Minimum.X : fallback;
    /// <summary>Gets an input's conservative upper bound, or the supplied constant fallback.</summary>
    public float Maximum(OpenPbrInput input, float fallback) => Bindings.TryGetValue(input, out var node) ? node.Maximum.X : fallback;
}

using System.Numerics;
using Mu3D.SceneGraph;

namespace Mu3D.Rendering.OpenPbr;

// ADR 0030: at scene-compile time Fast bakes every connected, non-uniform input into a real
// mipmapped texture sampled with hardware filtering, and folds uniform subtrees back into the
// material constants. Reference modes keep the interpreted per-texel evaluation untouched.
internal sealed class OpenPbrFastBake
{
    internal const int MaximumBakedTextures = 8;
    private const int MaximumBakeDimension = 1024;
    private const int DefaultBakeSize = 256;

    internal sealed class BakedTexture
    {
        internal required int Width { get; init; }
        internal required int Height { get; init; }
        // Texel (x, y) lives at Texels[y * Width + x] with y = 0 at graph UV v = 0.
        internal required Vector4[] Texels { get; init; }
    }

    internal readonly record struct Entry(int MaterialIndex, int Slot, int Channel, int TextureIndex, int UvSet, bool IsNormalMap, float NormalScale);
    internal readonly record struct Fold(int MaterialIndex, int Slot, int Channel, Vector4 Value);

    internal List<BakedTexture> Textures { get; } = [];
    internal List<Entry> Entries { get; } = [];
    internal List<Fold> Folds { get; } = [];
    internal List<OpenPbrFastApproximation> Approximations { get; private set; } = [];
    private int[] bakedCounts = [];
    private long[] bakedSizes = [];

    // FP16 bytes including the full mip chain.
    internal long BakedBytes { get; private set; }

    private static OpenPbrFastApproximationKinds LobeKinds(OpenPbrMaterial material)
    {
        OpenPbrSurface surface = material.Surface;
        OpenPbrGraph? graph = surface.Graph;
        OpenPbrFastApproximationKinds kinds = OpenPbrFastApproximationKinds.None;
        if ((graph?.Maximum(OpenPbrInput.FuzzWeight, surface.FuzzWeight) ?? surface.FuzzWeight) > 0)
            kinds |= OpenPbrFastApproximationKinds.FuzzEnvironmentCharlieChain;
        if ((graph?.Maximum(OpenPbrInput.CoatWeight, surface.CoatWeight) ?? surface.CoatWeight) > 0)
            kinds |= OpenPbrFastApproximationKinds.CoatEnvironmentBaseGgxChain;
        if ((graph?.Maximum(OpenPbrInput.SubsurfaceWeight, surface.SubsurfaceWeight) ?? surface.SubsurfaceWeight) > 0)
            kinds |= OpenPbrFastApproximationKinds.SubsurfaceDropped;
        if ((graph?.Maximum(OpenPbrInput.ThinFilmWeight, surface.ThinFilmWeight) ?? surface.ThinFilmWeight) > 0)
            kinds |= OpenPbrFastApproximationKinds.ThinFilmDropped;
        return kinds;
    }

    // Live constant edits (for example fuzz weight) do not rebuild baked textures, but the
    // per-surface lobe report must still track them.
    internal void RefreshReports(IReadOnlyList<OpenPbrMaterial> materials)
    {
        var reports = new List<OpenPbrFastApproximation>(materials.Count);
        for (int m = 0; m < materials.Count; m++)
        {
            var kinds = LobeKinds(materials[m]);
            if (Entries.Any(e => e.MaterialIndex == m)) kinds |= OpenPbrFastApproximationKinds.BakedGraphTextures;
            reports.Add(new(m, materials[m].Name, kinds, bakedCounts[m], bakedSizes[m]));
        }
        Approximations = reports;
    }

    private static bool IsConsumed(OpenPbrInput input) => input switch
    {
        // Transmission is rejected before baking; dropped lobes never read their inputs.
        OpenPbrInput.TransmissionWeight or OpenPbrInput.TransmissionColor or OpenPbrInput.TransmissionDepth or
        OpenPbrInput.TransmissionScatter or OpenPbrInput.TransmissionScatterAnisotropy or
        OpenPbrInput.TransmissionDispersionScale or OpenPbrInput.TransmissionDispersionAbbeNumber or
        OpenPbrInput.SubsurfaceWeight or OpenPbrInput.SubsurfaceColor or OpenPbrInput.SubsurfaceRadius or
        OpenPbrInput.SubsurfaceRadiusScale or OpenPbrInput.SubsurfaceScatterAnisotropy or
        OpenPbrInput.ThinFilmWeight or OpenPbrInput.ThinFilmThickness or OpenPbrInput.ThinFilmIor => false,
        // Opacity is validated to be the constant one; thin-walled only affects volume transport.
        OpenPbrInput.GeometryOpacity or OpenPbrInput.GeometryThinWalled => false,
        _ => true,
    };

    internal static OpenPbrFastBake Bake(IReadOnlyList<OpenPbrMaterial> materials, long maximumBytes)
    {
        OpenPbrFastBake bake = new();
        bake.bakedCounts = new int[materials.Count];
        bake.bakedSizes = new long[materials.Count];
        int[] bakedCounts = bake.bakedCounts;
        long[] bakedBytes = bake.bakedSizes;
        for (int m = 0; m < materials.Count; m++)
        {
            OpenPbrMaterial material = materials[m];
            OpenPbrSurface surface = material.Surface;
            OpenPbrGraph? graph = surface.Graph;
            if (graph is not null)
            {
                foreach (var (input, root) in graph.Bindings)
                {
                    if (!IsConsumed(input)) continue;
                    HashSet<OpenPbrNode> subtree = Collect(root);
                    bool isNormalMap = false;
                    float normalScale = 0;
                    OpenPbrNode target = root;
                    if (subtree.Any(n => n.Operation == OpenPbrNodeOperation.NormalMap))
                    {
                        // Only a root normal map feeding a shading-normal input can move its tangent-frame
                        // decode to shade time; every other placement needs the hit frame mid-graph.
                        if (root.Operation != OpenPbrNodeOperation.NormalMap ||
                            input is not (OpenPbrInput.GeometryNormal or OpenPbrInput.GeometryCoatNormal))
                            throw new NotSupportedException(
                                $"OpenPBR Fast cannot bake '{input}': a tangent normal map is only supported as the root of a shading-normal connection.");
                        if (Collect(root.A!).Any(n => n.Operation == OpenPbrNodeOperation.NormalMap))
                            throw new NotSupportedException($"OpenPBR Fast cannot bake '{input}': nested tangent normal maps.");
                        isNormalMap = true; normalScale = root.Value.X; target = root.A!;
                        subtree = Collect(target);
                    }
                    var (slot, channel) = OpenPbrGpuGraph.Slot(input);
                    if (target.IsUniform)
                    {
                        Vector4 value = EvaluateSubgraph(graph, target, Vector2.Zero, Vector2.Zero);
                        bake.Folds.Add(new(m, slot, channel, value));
                        continue;
                    }
                    int uvSet = -1;
                    int width = 0, height = 0;
                    foreach (OpenPbrNode node in subtree)
                    {
                        if (node.Operation == OpenPbrNodeOperation.Texcoord)
                        {
                            int set = (int)node.Value.X;
                            if (uvSet >= 0 && uvSet != set)
                                throw new NotSupportedException(
                                    $"OpenPBR Fast cannot bake '{input}': one connection mixes UV0 and UV1.");
                            uvSet = set;
                        }
                        if (node.Texture is { } image)
                        {
                            width = Math.Max(width, image.Width);
                            height = Math.Max(height, image.Height);
                        }
                    }
                    if (uvSet < 0) uvSet = 0;
                    if (width == 0) { width = DefaultBakeSize; height = DefaultBakeSize; }
                    width = Math.Min(width, MaximumBakeDimension);
                    height = Math.Min(height, MaximumBakeDimension);
                    if (bake.Textures.Count >= MaximumBakedTextures)
                        throw new NotSupportedException(
                            $"OpenPBR Fast bakes at most {MaximumBakedTextures} distinct graph inputs per scene; '{input}' exceeds the limit.");
                    Vector4[] texels = new Vector4[width * height];
                    Parallel.For(0, height, y =>
                    {
                        for (int x = 0; x < width; x++)
                        {
                            Vector2 uv = new((x + 0.5f) / width, (y + 0.5f) / height);
                            texels[y * width + x] = EvaluateSubgraph(graph, target, uvSet == 0 ? uv : Vector2.Zero, uvSet == 1 ? uv : Vector2.Zero);
                        }
                    });
                    long bytes = MipChainBytes(width, height);
                    bakedBytes[m] += bytes; bakedCounts[m]++;
                    bake.BakedBytes += bytes;
                    if (bake.BakedBytes > maximumBytes)
                        throw new InvalidOperationException("OpenPBR Fast baked textures exceed MaximumTextureBytes.");
                    bake.Textures.Add(new BakedTexture { Width = width, Height = height, Texels = texels });
                    bake.Entries.Add(new(m, slot, channel, bake.Textures.Count - 1, uvSet, isNormalMap, normalScale));
                }
            }
        }
        bake.RefreshReports(materials);
        return bake;
    }

    // Applies folded uniform constants to the packed material words, matching the reference
    // interpreter's write-back: a connected value always dominates its stored authoring fallback.
    internal void ApplyFolds(Vector4[] packed)
    {
        foreach (var (materialIndex, slot, channel, value) in Folds)
        {
            int at = materialIndex * OpenPbrGpuMaterial.Stride + slot;
            if (channel < 0)
            {
                packed[at] = new(value.X, value.Y, value.Z,
                    slot >= 14 ? (value.X * value.X + value.Y * value.Y + value.Z * value.Z > 1e-20f ? 1 : 0) : packed[at].W);
            }
            else
            {
                Vector4 word = packed[at]; word[channel] = value.X; packed[at] = word;
            }
        }
    }

    // Per-material headers, then two words per entry: (slot, channel, texture, flags), (scale, 0, 0, 0).
    // flags: 1 = UV set one, 2 = tangent normal map whose data texture is sampled instead.
    internal Vector4[] PackBakeInfo(int materialCount)
    {
        Vector4[] result = new Vector4[Math.Max(1, materialCount + Entries.Count * 2)];
        var groups = Entries.GroupBy(e => e.MaterialIndex).ToDictionary(g => g.Key, g => g.ToList());
        int cursor = materialCount;
        for (int m = 0; m < materialCount; m++)
        {
            if (!groups.TryGetValue(m, out var entries)) { result[m] = Vector4.Zero; continue; }
            result[m] = new(cursor, entries.Count, 0, 0);
            foreach (var entry in entries)
            {
                int flags = (entry.UvSet == 1 ? 1 : 0) | (entry.IsNormalMap ? 2 : 0);
                result[cursor++] = new(entry.Slot, entry.Channel, entry.TextureIndex, flags);
                result[cursor++] = new(entry.NormalScale, 0, 0, 0);
            }
        }
        return result;
    }

    private static long MipChainBytes(int width, int height)
    {
        long bytes = 0;
        while (true)
        {
            bytes += (long)width * height * 8;
            if (width == 1 && height == 1) return bytes;
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
    }

    private static HashSet<OpenPbrNode> Collect(OpenPbrNode root)
    {
        HashSet<OpenPbrNode> set = [];
        Stack<OpenPbrNode> pending = new([root]);
        while (pending.Count > 0)
        {
            OpenPbrNode node = pending.Pop();
            if (!set.Add(node)) continue;
            if (node.A is not null) pending.Push(node.A);
            if (node.B is not null) pending.Push(node.B);
            if (node.C is not null) pending.Push(node.C);
        }
        return set;
    }

    // Mirrors OpenPbrGraphEvaluator for one subtree; normal maps are validated away before baking.
    private static Vector4 EvaluateSubgraph(OpenPbrGraph graph, OpenPbrNode target, Vector2 uv0, Vector2 uv1)
    {
        HashSet<OpenPbrNode> subtree = Collect(target);
        Dictionary<OpenPbrNode, Vector4> values = [];
        foreach (var n in graph.Nodes)
        {
            if (!subtree.Contains(n)) continue;
            Vector4 a = n.A is null ? default : values[n.A], b = n.B is null ? default : values[n.B], c = n.C is null ? default : values[n.C];
            Vector4 value = n.Operation switch
            {
                OpenPbrNodeOperation.Constant => n.Value,
                OpenPbrNodeOperation.Texcoord => new(n.Value.X == 0 ? uv0 : uv1, 0, 0),
                OpenPbrNodeOperation.Image => Sample(n, new(a.X, a.Y)),
                OpenPbrNodeOperation.Add => a + b,
                OpenPbrNodeOperation.Subtract => a - b,
                OpenPbrNodeOperation.Min => Vector4.Min(a, b),
                OpenPbrNodeOperation.Max => Vector4.Max(a, b),
                OpenPbrNodeOperation.Abs => Vector4.Abs(a),
                OpenPbrNodeOperation.Divide or OpenPbrNodeOperation.Sqrt => EvaluateDomainOperation(n, a, b),
                OpenPbrNodeOperation.Multiply => a * b,
                OpenPbrNodeOperation.Mix => Vector4.Lerp(a, b, c.X),
                OpenPbrNodeOperation.Clamp => Vector4.Clamp(a, new(n.Value.X), new(n.Value.Y)),
                OpenPbrNodeOperation.Extract => new(a[(int)n.Value.X]),
                _ => throw new InvalidOperationException("A tangent normal map cannot be baked."),
            };
            values.Add(n, value);
        }
        return values[target];

        static Vector4 EvaluateDomainOperation(OpenPbrNode node, Vector4 a, Vector4 b)
        {
            int channels = node.Type == OpenPbrNodeType.Float ? 1 : node.Type == OpenPbrNodeType.Vector2 ? 2 :
                node.Type == OpenPbrNodeType.Vector4 ? 4 : 3;
            Vector4 value = default;
            for (int channel = 0; channel < channels; channel++)
                value[channel] = node.Operation == OpenPbrNodeOperation.Sqrt ? MathF.Sqrt(a[channel]) :
                    a[channel] / b[node.B!.Type == OpenPbrNodeType.Float ? 0 : channel];
            return node.Type == OpenPbrNodeType.Float ? new(value.X) : value;
        }

        static Vector4 Sample(OpenPbrNode node, Vector2 uv)
        {
            var image = node.Texture!;
            float x = Address(uv.X, node.AddressU) * image.Width, y = Address(uv.Y, node.AddressV) * image.Height;
            Vector4 value;
            if (node.Filter == OpenPbrTextureFilter.Closest) value = Pixel((int)MathF.Floor(x), (int)MathF.Floor(y));
            else
            {
                x -= .5f; y -= .5f;
                int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y); float fx = x - ix, fy = y - iy;
                value = Vector4.Lerp(Vector4.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), fx), Vector4.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), fx), fy);
            }
            return node.Type == OpenPbrNodeType.Float ? new(value.X) : value;
            Vector4 Pixel(int px, int py)
            {
                px = Index(px, image.Width, node.AddressU); py = Index(py, image.Height, node.AddressV);
                return image.Pixels[(image.Height - 1 - py) * image.Width + px];
            }
        }
        static float Address(float value, OpenPbrAddressMode mode) => mode switch
        {
            OpenPbrAddressMode.Clamp => Math.Clamp(value, 0, 1),
            OpenPbrAddressMode.Mirror => Mirror(value),
            _ => value - MathF.Floor(value),
        };
        static float Mirror(float value) { float x = value - MathF.Floor(value * .5f) * 2; return x <= 1 ? x : 2 - x; }
        static int Index(int value, int size, OpenPbrAddressMode mode) => mode switch
        {
            OpenPbrAddressMode.Clamp => Math.Clamp(value, 0, size - 1),
            OpenPbrAddressMode.Mirror => MirrorIndex(value, size),
            _ => (value % size + size) % size,
        };
        static int MirrorIndex(int value, int size) { int x = (value % (size * 2) + size * 2) % (size * 2); return x < size ? x : size * 2 - x - 1; }
    }

    // Box filter used for the baked mip chain; matches the Core material-texture convention.
    internal static Vector4[] DownsampleBox(Vector4[] source, int width, int height, out int nextWidth, out int nextHeight)
    {
        int nw = Math.Max(1, width / 2), nh = Math.Max(1, height / 2);
        nextWidth = nw; nextHeight = nh;
        Vector4[] result = new Vector4[nw * nh];
        Parallel.For(0, nh, y =>
        {
            for (int x = 0; x < nw; x++)
            {
                Vector4 sum = Vector4.Zero; int count = 0;
                for (int dy = 0; dy < 2; dy++)
                    for (int dx = 0; dx < 2; dx++)
                    {
                        int sx = Math.Min(x * 2 + dx, width - 1), sy = Math.Min(y * 2 + dy, height - 1);
                        sum += source[sy * width + sx]; count++;
                    }
                result[y * nw + x] = sum / count;
            }
        });
        return result;
    }
}

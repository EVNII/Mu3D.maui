using System.Numerics;

namespace Mu3D.SceneGraph;

/// <summary>Evaluates the portable material graph on the CPU for inspection and numerical comparison.</summary>
public static class OpenPbrGraphEvaluator
{
    /// <summary>Evaluates one connected input using bottom-left UVs and an inherited world-space shading frame.</summary>
    /// <remarks>Use mesh UV sets zero/one and the tangent's W handedness. This does not evaluate the BSDF.</remarks>
    public static Vector4 Evaluate(OpenPbrGraph graph, OpenPbrInput input, Vector2 uv0, Vector2 uv1,
        Vector3 normal, Vector4 tangent)
    {
        ArgumentNullException.ThrowIfNull(graph);
        OpenPbrTexture.RequireFinite(new(uv0, uv1.X, uv1.Y));
        OpenPbrTexture.RequireFinite(new(normal, 0)); OpenPbrTexture.RequireFinite(tangent);
        if (normal.LengthSquared() < 1e-20f || !float.IsFinite(normal.LengthSquared())) throw new ArgumentOutOfRangeException(nameof(normal));
        if (!graph.Bindings.TryGetValue(input, out var output)) throw new ArgumentException("The input has no graph connection.", nameof(input));
        normal = Vector3.Normalize(normal);
        Dictionary<OpenPbrNode, Vector4> values = [];
        foreach (var n in graph.Nodes)
        {
            Vector4 a = n.A is null ? default : values[n.A], b = n.B is null ? default : values[n.B], c = n.C is null ? default : values[n.C];
            Vector4 value = n.Operation switch
            {
                OpenPbrNodeOperation.Constant => n.Value,
                OpenPbrNodeOperation.Texcoord => new(n.Value.X == 0 ? uv0 : uv1, 0, 0),
                OpenPbrNodeOperation.Image => Sample(n, new(a.X, a.Y)),
                OpenPbrNodeOperation.Add => a + b,
                OpenPbrNodeOperation.Multiply => a * b,
                OpenPbrNodeOperation.Mix => Vector4.Lerp(a, b, c.X),
                OpenPbrNodeOperation.Clamp => Vector4.Clamp(a, new(n.Value.X), new(n.Value.Y)),
                OpenPbrNodeOperation.NormalMap => MapNormal(a, n.Value.X, normal, tangent),
                OpenPbrNodeOperation.Extract => new(a[(int)n.Value.X]),
                _ => throw new InvalidOperationException(),
            };
            OpenPbrTexture.RequireFinite(value); values.Add(n, value);
        }
        return values[output];
    }
    private static Vector4 MapNormal(Vector4 data, float scale, Vector3 normal, Vector4 tangent)
    {
        Vector3 t = new(tangent.X, tangent.Y, tangent.Z); t -= normal * Vector3.Dot(t, normal);
        Vector3 bitangent;
        if (t.LengthSquared() < 1e-15f)
        {
            float sign = normal.Z >= 0 ? 1 : -1;
            float a = -1 / (sign + normal.Z), b = normal.X * normal.Y * a;
            t = new(b, sign + normal.Y * normal.Y * a, -normal.Y);
            bitangent = new(1 + sign * normal.X * normal.X * a, sign * b, -sign * normal.X);
        }
        else
        {
            t = Vector3.Normalize(t);
            bitangent = Vector3.Cross(normal, t) * (tangent.W < 0 ? -1 : 1);
        }
        Vector3 result = t * ((data.X * 2 - 1) * scale) + bitangent * ((data.Y * 2 - 1) * scale) + normal * (data.Z * 2 - 1);
        return new(result.LengthSquared() > 1e-20f ? Vector3.Normalize(result) : normal, 0);
    }
    private static Vector4 Sample(OpenPbrNode node, Vector2 uv)
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
    private static float Address(float value, OpenPbrAddressMode mode)
    {
        if (mode == OpenPbrAddressMode.Clamp) return Math.Clamp(value, 0, 1);
        if (mode == OpenPbrAddressMode.Mirror) { float x = value - MathF.Floor(value * .5f) * 2; return x <= 1 ? x : 2 - x; }
        return value - MathF.Floor(value);
    }
    private static int Index(int value, int size, OpenPbrAddressMode mode)
    {
        if (mode == OpenPbrAddressMode.Clamp) return Math.Clamp(value, 0, size - 1);
        if (mode == OpenPbrAddressMode.Mirror) { int x = (value % (size * 2) + size * 2) % (size * 2); return x < size ? x : size * 2 - x - 1; }
        return (value % size + size) % size;
    }
}

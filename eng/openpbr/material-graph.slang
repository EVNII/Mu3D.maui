// Bounded typed material expressions. CPU validates the DAG, bounds and all addresses.
[[vk::binding(6, 0)]] StructuredBuffer<float4> graphProgram;
[[vk::binding(7, 0)]] StructuredBuffer<float4> graphPixels;
float address_uv(float x, int mode)
{
    if (mode == 1) return clamp(x, 0.0, 1.0);
    if (mode == 2) { float a = x - floor(x * .5) * 2; return a <= 1 ? a : 2 - a; }
    return x - floor(x);
}
int address_texel(int x, int size, int mode)
{
    if (mode == 1) return clamp(x, 0, size - 1);
    if (mode == 2) { int a = (x % (size * 2) + size * 2) % (size * 2); return a < size ? a : size * 2 - a - 1; }
    return (x % size + size) % size;
}
float4 graph_texel(float4 image, int2 xy, int2 modes)
{
    int x = address_texel(xy.x, int(image.y), modes.x);
    int y = address_texel(xy.y, int(image.z), modes.y);
    // MaterialX uses bottom-left UVs; resources have top-to-bottom rows.
    return graphPixels[int(image.x) + (int(image.z) - 1 - y) * int(image.y) + x];
}
float4 sample_graph_image(float4 image, float4 options, float2 uv)
{
    int2 modes = int2(options.xy);
    uv = float2(address_uv(uv.x, modes.x), address_uv(uv.y, modes.y));
    float2 p = uv * image.yz;
    float4 value;
    if (options.z == 0) value = graph_texel(image, int2(floor(p)), modes);
    else
    {
        p -= .5; int2 a = int2(floor(p)); float2 f = p - float2(a);
        value = lerp(lerp(graph_texel(image, a, modes), graph_texel(image, a + int2(1, 0), modes), f.x),
                     lerp(graph_texel(image, a + int2(0, 1), modes), graph_texel(image, a + int2(1, 1), modes), f.x), f.y);
    }
    if (image.w == 0) value = float4(value.x);
    return value;
}
void load_material(int index, float4 uv, float3 inheritedNormal, float4 inheritedTangent, out float4 p[19])
{
    for (int i = 0; i < 19; ++i) p[i] = materials[index * 19 + i];
    if (p[18].y == 0) return;
    int start = int(p[18].y) - 1, count = int(p[18].z);
    float4 values[64]; for (int zero = 0; zero < 64; ++zero) values[zero] = float4(0);
    for (int i = 0; i < count; ++i)
    {
        int at = start + i * 4;
        float4 op = graphProgram[at], parameter = graphProgram[at + 1];
        int code = int(op.x); float4 value = parameter;
        if (code == 1) value = float4(parameter.x == 0 ? uv.xy : uv.zw, 0, 0);
        else if (code == 2) value = sample_graph_image(graphProgram[at + 2], graphProgram[at + 3], values[int(op.y)].xy);
        else if (code == 3) value = values[int(op.y)] + values[int(op.z)];
        else if (code == 4) value = values[int(op.y)] * values[int(op.z)];
        else if (code == 5) value = lerp(values[int(op.y)], values[int(op.z)], values[int(op.w)].x);
        else if (code == 6) value = clamp(values[int(op.y)], parameter.x, parameter.y);
        else if (code == 7)
        {
            float3 n = values[int(op.y)].xyz * 2 - 1; n.xy *= parameter.x;
            OpenPBR_Basis basis = make_basis(inheritedNormal, inheritedTangent.xyz, inheritedTangent.w);
            float3 world = basis.t * n.x + basis.b * n.y + basis.n * n.z;
            value = float4(dot(world, world) > 1e-20 ? normalize(world) : inheritedNormal, 0);
        }
        else if (code == 8) value = float4(values[int(op.y)][int(parameter.x)]);
        values[i] = value;
    }
    for (int b = 0; b < int(p[18].w); ++b)
    {
        float4 binding = graphProgram[start + count * 4 + b];
        int slot = int(binding.x), channel = int(binding.y);
        float4 value = values[int(binding.z)];
        if (channel < 0)
        {
            p[slot].xyz = value.xyz;
            if (slot >= 14) p[slot].w = dot(value.xyz, value.xyz) > 1e-20 ? 1 : 0;
        }
        else p[slot][channel] = value.x * (binding.w > 0 ? p[13].w / frame[14].x : 1.0);
    }
}

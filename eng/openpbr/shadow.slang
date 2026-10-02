// Single directional shadow map. Only the selected light's direct radiance is attenuated.
[[vk::binding(0, 3)]] cbuffer Shadow { float4 shadowFrame[6]; }
[[vk::binding(1, 3)]] DepthTexture2D directionalDepth;
[[vk::binding(2, 3)]] SamplerComparisonState shadowSampler;

float directional_visibility(int light, float3 position, float3 normal, float3 lightDirection)
{
    if (shadowFrame[4].x < 0 || light != int(shadowFrame[4].x)) return 1;
    float3 n = dot(normal, lightDirection) < 0 ? -normal : normal;
    float slope = 1 - saturate(dot(n, lightDirection));
    float3 p = position + n * shadowFrame[4].z * slope;
    float4 clip = shadowFrame[0] * p.x + shadowFrame[1] * p.y + shadowFrame[2] * p.z + shadowFrame[3];
    float3 ndc = clip.xyz / clip.w;
    float2 uv = float2(ndc.x * .5 + .5, .5 - ndc.y * .5);
    if (any(uv < 0) || any(uv > 1) || ndc.z < 0 || ndc.z > 1) return 1;
    // Project the geometric receiver plane. Comparing every PCF tap against the centre depth
    // would self-shadow sloping surfaces, even with no occluder. No screen derivatives needed.
    float3 tangent = normalize(cross(n, abs(n.z) < .99 ? float3(0, 0, 1) : float3(0, 1, 0)));
    float3 bitangent = cross(n, tangent);
    float3 tx = shadowFrame[0].xyz * tangent.x + shadowFrame[1].xyz * tangent.y + shadowFrame[2].xyz * tangent.z;
    float3 ty = shadowFrame[0].xyz * bitangent.x + shadowFrame[1].xyz * bitangent.y + shadowFrame[2].xyz * bitangent.z;
    float3 plane = cross(tx, ty);
    float2 gradient = abs(plane.z) > 1e-12 ? -plane.xy / plane.z : float2(0);
    int radius = int(shadowFrame[4].w);
    float visibility = 0;
    for (int y = -radius; y <= radius; ++y)
        for (int x = -radius; x <= radius; ++x)
        {
            float2 tap = (floor(uv * shadowFrame[5].x) + .5 + float2(x, y)) / shadowFrame[5].x;
            float compareDepth = ndc.z + dot(gradient, (tap - uv) * float2(2, -2)) - shadowFrame[4].y;
            visibility += any(tap < 0) || any(tap > 1) ? 1 :
                directionalDepth.SampleCmpLevelZero(shadowSampler, tap, compareDepth);
        }
    float width = radius * 2 + 1;
    return lerp(1.0, visibility / (width * width), shadowFrame[5].y);
}

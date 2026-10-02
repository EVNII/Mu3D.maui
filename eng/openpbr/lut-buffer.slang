// Mu3D adapter for the pinned Adobe OpenPBR texture lookup hooks.
// One readonly FP32 buffer; values are exact FP32 normalized energy entries followed by LTC RGB.
[[vk::binding(0, 1)]] StructuredBuffer<float> openPbrLookup;
uint openPbrOffset(int id)
{
    switch(id) {
      case 0:return 0; case 1:return 32768; case 2:return 33792; case 3:return 34816;
      case 4:return 67584; case 5:return 68608; case 6:return 69632; default:return 69664;
    }
}
float3 openPbrFetch2(int id, int2 index)
{
    index = clamp(index, int2(0), int2(31, id == 6 ? 0 : 31));
    uint address = openPbrOffset(id) + uint(index.y * 32 + index.x) * (id == 7 ? 3u : 1u);
    if(id == 7) return float3(openPbrLookup[address], openPbrLookup[address+1], openPbrLookup[address+2]);
    return float3(openPbrLookup[address], 0, 0);
}
float4 openPbrSample2(int id, float2 uv)
{
    float2 p = uv * float2(32, id == 6 ? 1 : 32) - .5;
    int2 a = int2(floor(p)); float2 t = p-float2(a);
    return float4(lerp(lerp(openPbrFetch2(id,a),openPbrFetch2(id,a+int2(1,0)),t.x),
                      lerp(openPbrFetch2(id,a+int2(0,1)),openPbrFetch2(id,a+int2(1,1)),t.x),t.y),0);
}
float openPbrFetch3(int id, int3 index)
{
    index=clamp(index,int3(0),int3(31));
    return openPbrLookup[openPbrOffset(id)+uint((index.z*32+index.y)*32+index.x)];
}
float4 openPbrSample3(int id, float3 uv)
{
    float3 p=uv*32-.5; int3 a=int3(floor(p)); float3 t=p-float3(a);
    float v=lerp(
        lerp(lerp(openPbrFetch3(id,a),openPbrFetch3(id,a+int3(1,0,0)),t.x),lerp(openPbrFetch3(id,a+int3(0,1,0)),openPbrFetch3(id,a+int3(1,1,0)),t.x),t.y),
        lerp(lerp(openPbrFetch3(id,a+int3(0,0,1)),openPbrFetch3(id,a+int3(1,0,1)),t.x),lerp(openPbrFetch3(id,a+int3(0,1,1)),openPbrFetch3(id,a+int3(1,1,1)),t.x),t.y),t.z);
    return float4(v,0,0,0);
}
#define OPENPBR_USE_TEXTURE_LUTS 1
#define OPENPBR_SAMPLE_2D_TEXTURE(id,uv) openPbrSample2(id,uv)
#define OPENPBR_SAMPLE_3D_TEXTURE(id,uvw) openPbrSample3(id,uvw)

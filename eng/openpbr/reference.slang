#define OPENPBR_LANGUAGE_TARGET_SLANG 1
#define OPENPBR_ENERGY_TABLES_USE_UINT16 0
#include "lut-buffer.slang"
#include "openpbr.h"
#include "cases.h"
[shader("fragment")]
float4 main(float4 position : SV_Position) : SV_Target0
{
    int id=int(position.y);
    int caseId=id >= 27 ? id - 16 : (id >= 24 ? id - 3 : id);
    OpenPBR_ResolvedInputs inputs=mu3d_openpbr_case(caseId);
    OpenPBR_PreparedBsdf prepared=openpbr_prepare(inputs,id >= 24 && id < 27 ? float3(1) : mu3d_openpbr_throughput(),mu3d_openpbr_wavelengths(caseId),OpenPBR_VacuumIor,mu3d_openpbr_view(caseId));
    if (id >= 27)
    {
        uint index=uint(position.x);
        if(index >= 3) return float4(0);
        float distance=index == 0 ? 0 : (index == 1 ? .1 : 1);
        return float4(openpbr_calculate_transmittance_at_distance(prepared.volume.extinction_coefficient,distance),1);
    }
    if (id >= 24)
    {
        // Eight pixels partition the same 256 by 256 midpoint hemisphere quadrature as C++.
        // Eval includes the incoming cosine; integration is over dphi * d(cos theta).
        float3 integral=float3(0);
        uint first=uint(position.x)*32;
        for(uint a=first;a<first+32;a++)
        for(uint b=0;b<256;b++)
        {
            float z=(float(a)+.5)/256;
            float phi=(float(b)+.5)*(6.283185307179586/256);
            float radius=sqrt(max(0,1-z*z));
            float3 direction=float3(radius*cos(phi),radius*sin(phi),z);
            integral+=openpbr_get_sum_of_diffuse_specular(openpbr_eval(prepared,direction));
        }
        return float4(integral*(6.283185307179586/(256*256)),1);
    }
    float3 light=mu3d_openpbr_light(id);
    OpenPBR_DiffuseSpecular value=openpbr_eval(prepared,light);
    float3 sampled; OpenPBR_DiffuseSpecular weight; float pdf; OpenPBR_BsdfLobeType lobe;
    openpbr_sample(prepared,mu3d_openpbr_random(),sampled,weight,pdf,lobe);
    float sampledPdf=0;
    if(pdf>0) sampledPdf=openpbr_pdf(prepared,sampled);
    else {sampled=float3(0);weight=openpbr_make_zero_diffuse_specular();lobe=0;}
    switch(uint(position.x)) {
      case 0:return float4(value.diffuse,openpbr_pdf(prepared,light));
      case 1:return float4(value.specular,prepared.volume.anisotropy);
      case 2:return float4(prepared.volume.extinction_coefficient,pdf);
      case 3:return float4(prepared.volume.albedo,float(lobe));
      case 4:return float4(prepared.emission,openpbr_needs_rgb_wavelengths(inputs)?1:0);
      case 5:return float4(sampled,sampledPdf);
      case 6:return float4(weight.diffuse,0);
      default:return float4(weight.specular,0);
    }
}

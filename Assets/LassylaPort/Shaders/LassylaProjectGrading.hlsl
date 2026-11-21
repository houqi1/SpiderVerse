#include "LassylaForwardGrading.hlsl"
TEXTURE2D(_InverseProjectLut);
// Evaluated only on a small LUT strip, once per camera render.
float4 BuildInverseProjectLut(V input):SV_Target {
    const int size=33;
    int2 pixel=(int2)input.positionCS.xy;
    float3 target=float3(pixel.x%size,pixel.y,pixel.x/size)/(size-1);
    float3 x=target/max(.001,_ProjectLutParams.w);
    float3 best=x;float bestError=1e10;
    [loop]for(int i=0;i<8;i++){
        float3 value=ProjectGrade(x),err=target-value;
        float sq=dot(err,err);if(sq<bestError){best=x;bestError=sq;}
        if(sq<1e-8)break;
        float3 stepSize=.009*(1+x);
        float3 dx=(ProjectGrade(x+float3(stepSize.x,0,0))-value)/stepSize.x;
        float3 dy=(ProjectGrade(x+float3(0,stepSize.y,0))-value)/stepSize.y;
        float3 dz=(ProjectGrade(x+float3(0,0,stepSize.z))-value)/stepSize.z;
        float det=dot(dx,cross(dy,dz));if(abs(det)<1e-8)break;
        float3 change=float3(dot(err,cross(dy,dz)),dot(err,cross(dz,dx)),dot(err,cross(dx,dy)))/det;
        x=clamp(x+clamp(change,-.70*(1+x),1+x),0,32);
    }
    return float4(best,1);
}
float3 BeforeProjectGrade(float3 displayed) {
    if(_ProjectLutParams.w<=0)return displayed;
    return ApplyLut2D(TEXTURE2D_ARGS(_InverseProjectLut,sampler_LinearClamp),saturate(displayed),float3(1.0/1089,1.0/33,32));
}

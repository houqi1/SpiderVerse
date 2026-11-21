#ifndef LASSYLA_FORWARD_GRADE_INCLUDED
#define LASSYLA_FORWARD_GRADE_INCLUDED
// URP applies the internal colour LUT after this object's linear-light film resolve.
// Use the current camera's LUT, exposure and tone mapper to invert that final grade.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
TEXTURE2D(_ProjectLut);
TEXTURE2D(_ProjectUserLut);
float4 _ProjectLutParams, _ProjectUserLutParams;
int _ProjectToneMapper;

float3 UserGrade(float3 v) {
    if(_ProjectUserLutParams.w<=0)return v;
    float3 s=LinearToSRGB(saturate(v));
    float3 user=ApplyLut2D(TEXTURE2D_ARGS(_ProjectUserLut,sampler_LinearClamp),s,_ProjectUserLutParams.xyz);
    return SRGBToLinear(lerp(s,user,_ProjectUserLutParams.w));
}
float3 ProjectGrade(float3 v) {
    v=max(0,v)*_ProjectLutParams.w;
    if(_ProjectToneMapper==3){
        float3 logRGB=saturate(LinearToLogC(v));
        return UserGrade(ApplyLut2D(TEXTURE2D_ARGS(_ProjectLut,sampler_LinearClamp),logRGB,_ProjectLutParams.xyz));
    }
    if(_ProjectToneMapper==2)v=AcesTonemap(unity_to_ACES(v));
    else if(_ProjectToneMapper==1)v=NeutralTonemap(v);
    return ApplyLut2D(TEXTURE2D_ARGS(_ProjectLut,sampler_LinearClamp),UserGrade(saturate(v)),_ProjectLutParams.xyz);
}
float3 DisplayedBackground(float3 raw) {
    if(_ProjectLutParams.w<=0)return raw;
    return ProjectGrade(raw);
}
#endif

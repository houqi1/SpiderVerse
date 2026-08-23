#ifndef TOON_INPUT_INCLUDED
#define TOON_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_LambertPerturbMap);
SAMPLER(sampler_LambertPerturbMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _LambertPerturbMap_ST;
    half4 _BaseColor;
    half4 _ShadeColor;
    half4 _SpecularColor;
    half _ShadeThreshold;
    half _ShadeSmooth;
    half _SpecularSize;
    half _SpecularThreshold;
    half _SpecularSmooth;
    half _AmbientStrength;
    half _LambertPerturbSpace;
    half _LambertPerturbSwapUV;
    half _LambertPerturbMode;
    half _LambertPerturbStrength;
    half _LambertPerturbWidth;
    half _LambertPerturbDebug;
CBUFFER_END

#endif

#ifndef TOON_INPUT_INCLUDED
#define TOON_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_LambertPerturbMap);
SAMPLER(sampler_LambertPerturbMap);
TEXTURE2D(_UVMap);
SAMPLER(sampler_UVMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _LambertPerturbMap_ST;
    float4 _UVMap_ST;
    half4 _BaseColor;
    half4 _ShadeColor;
    half4 _OverlayColorA;
    half4 _OverlayColorB;
    half4 _WrapShadeColor;
    half _ShadeThreshold;
    half _ShadeSmooth;
    half _WrapShadeThreshold;
    half _WrapShadeSmooth;
    half _WrapAmount;
    half _WrapToonStrength;
    half _OutputWrapToon;
    half _AmbientStrength;
    half _LambertPerturbSpace;
    half _LambertPerturbSwapUV;
    half _LambertPerturbMode;
    half _LambertPerturbStrength;
    half _LambertPerturbWidth;
    half _LambertPerturbDebug;
    half _OutputOverlayMap;
    half _OverlayBlendMode;
    half _OverlayBlendStrength;
CBUFFER_END

#endif

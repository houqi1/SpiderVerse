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
TEXTURE2D(_ExtraMap);
SAMPLER(sampler_ExtraMap);
TEXTURE2D(_SpMap);
SAMPLER(sampler_SpMap);
TEXTURE2D(_ColorMaskMap);
SAMPLER(sampler_ColorMaskMap);
TEXTURE2D(_DetailMap);
SAMPLER(sampler_DetailMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _LambertPerturbMap_ST;
    float4 _UVMap_ST;
    float4 _ExtraMap_ST;
    float4 _SpMap_ST;
    float4 _ColorMaskMap_ST;
    float4 _DetailMap_ST;
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
    half _OutputExtraMap;
    half _OutputSpMap;
    half _SpMapChannel;
    half _ExtraDarkStrength;
    half _ShadeFromAmbient;
    half _OverlayBlendMode;
    half _OverlayBlendStrength;
    half _ColorMaskMix;
    half _ColorMaskStrength;
    half _OutputColorMask;
    half _OutputDetailMap;
    half _DetailMapThreshold;
CBUFFER_END

#endif

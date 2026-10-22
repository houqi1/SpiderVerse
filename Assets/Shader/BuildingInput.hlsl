#ifndef BUILDING_INPUT_INCLUDED
#define BUILDING_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

TEXTURE2D(_TriplanarMap);
SAMPLER(sampler_TriplanarMap);
TEXTURE2D(_UVMap);
SAMPLER(sampler_UVMap);
TEXTURE2D(_HueNoiseMap);
SAMPLER(sampler_HueNoiseMap);
TEXTURE2D(_Noise2Map);
SAMPLER(sampler_Noise2Map);
TEXTURE2D(_DissolveNoiseMap);
SAMPLER(sampler_DissolveNoiseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _TriplanarMap_ST;
    float4 _UVMap_ST;
    half4 _ColorX;
    half4 _ColorY;
    half4 _ColorZ;
    half _TriplanarSharpness;
    half _DarkOverlayStrength;
    half _ShadeThreshold;
    half _ShadeSmooth;
    half _AmbientStrength;
    half _OutputOverlayMap;
    float4 _HueNoiseMap_ST;
    half4 _NoiseAddColor;
    half _NoiseAddStrength;
    half _NoiseMaskMin;
    half _NoiseMaskMax;
    float _HueNoisePositionDivisor;
    half _HueShiftFadeStart;
    half _HueShiftFadeEnd;
    half _HueShiftFadePower;
    float4 _Noise2Map_ST;
    half4 _Noise2Color;
    half _Noise2Strength;
    half _Noise2MaskMin;
    half _Noise2MaskMax;
    float _Noise2PositionDivisor;
    half _Noise2FadeStart;
    half _Noise2FadeEnd;
    half _Noise2FadePower;
    float4 _DissolveNoiseMap_ST;
    half _DissolveProgress;
    float _DissolveYTop;
    float _DissolveYBottom;
    half _DissolveWeightY;
    half _DissolveWeightDist;
    half _DissolveSoftness;
    half _DissolveNoiseAmount;
    float _DissolveNoiseDivisor;
    half4 _DistanceFogColorNear;
    half4 _DistanceFogColorMid;
    half4 _DistanceFogColorFar;
    float _DistanceFogStart;
    float _DistanceFogEnd;
    half _DistanceFogFalloff;
    half _DistanceFogMaxDensity;
CBUFFER_END

#endif

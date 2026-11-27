#ifndef TOON_CEL_INPUT_INCLUDED
#define TOON_CEL_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);
TEXTURE2D(_BumpMap);
SAMPLER(sampler_BumpMap);
TEXTURE2D(_WrapNormalMap);
SAMPLER(sampler_WrapNormalMap);

TEXTURE2D(_EdgeTex);
SAMPLER(sampler_EdgeTex);

TEXTURE2D(_GwenShadeless);
SAMPLER(sampler_GwenShadeless);
TEXTURE2D(_GwenGrunge);
SAMPLER(sampler_GwenGrunge);

CBUFFER_START(UnityPerMaterial)
    float4 _EdgeTex_ST;
    float _EdgeTexStrength;
    float4 _GwenGrunge_ST;
    float4 _GwenProjectionAnchor;
    half _GwenScreenStrength;
    half _GwenGrungeStrength;
    float4 _BaseMap_ST;
    float4 _BumpMap_ST;
    float4 _WrapNormalMap_ST;
    half _BaseMapUV;
    half _WrapNormalUV;
    half _WrapNormalStrength;
    half _WrapNormalTangentSpace;
    half4 _BaseColor;
    half _BumpScale;
    half4 _ShadeColor;
    half _CelModelNormalEnabled;
    half4 _MidShadeColor;
    half _MidShadeThreshold;
    half4 _AmbientDarkColor;
    half _AmbientDarkBlend;
    half _ShadeThreshold;
    half _ShadeSmooth;
    half _AmbientStrength;
    half _ShadeFromAmbient;
    half _PBRSpecular;
    float4 _SpecularF0;
    float _SpecularRoughness;
    float _SpecularMetallic;
    float _SpecularIntensity;
    float _SpecularShapeAmount;
    float _SpecularThreshold;
    float _SpecularSmooth;
    float _LassylaEdgeEnabled;
    float _LassylaHideGrain;
    float _Cull;
    float _IOR, _NoiseScale, _NoiseDetail, _NoiseRoughness, _NoiseLacunarity, _NoiseDistortion;
    float _EdgeStart, _EdgeEnd, _NoiseStart, _NoiseEnd, _NoiseDark, _HashScale, _HashOffset;
    float _NormalEdgeStrength, _NormalEdgeStart, _NormalEdgeEnd, _NormalEdgeDebug;
    float _LassylaEdgeBlend, _EdgeBlendMin, _EdgeBlendStart, _EdgeBlendEnd, _EdgeBlendDepthCutoff;
CBUFFER_END

float _ToonLassylaCamera;
bool ToonUsesLassylaCamera() { return _ToonLassylaCamera > 0.5 && _LassylaEdgeEnabled > 0.5; }
// The Gwen FBX uses an X reflection. Quantising signed normals commutes with axis permutations.
static const float4 _BlenderLocal0=float4(-1,0,0,0);
static const float4 _BlenderLocal1=float4(0,1,0,0);
static const float4 _BlenderLocal2=float4(0,0,1,0);
float4 _ToonBlenderWorld0,_ToonBlenderWorld1,_ToonBlenderWorld2;
#define _BlenderWorld0 _ToonBlenderWorld0
#define _BlenderWorld1 _ToonBlenderWorld1
#define _BlenderWorld2 _ToonBlenderWorld2

#endif

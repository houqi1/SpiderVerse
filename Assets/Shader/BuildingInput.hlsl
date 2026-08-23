#ifndef BUILDING_INPUT_INCLUDED
#define BUILDING_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

TEXTURE2D(_TriplanarMap);
SAMPLER(sampler_TriplanarMap);

CBUFFER_START(UnityPerMaterial)
    float4 _TriplanarMap_ST;
    half4 _ColorX;
    half4 _ColorY;
    half4 _ColorZ;
    half _TriplanarSharpness;
    half _DarkOverlayStrength;
    half _ShadeThreshold;
    half _ShadeSmooth;
    half _AmbientStrength;
CBUFFER_END

#endif

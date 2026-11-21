#ifndef TOON_CEL_EDGE_INCLUDED
#define TOON_CEL_EDGE_INCLUDED
#include "../LassylaPort/Shaders/LassylaEdgeCoverage.hlsl"
#include "ToonCelGenerated.hlsl"

// Screen-space normal difference: how much the interpolated normal changes across one pixel.
// Large on creases / silhouettes-in-the-making, ~0 on flat or gently curved areas.
float ToonNormalDifference(float3 normalWS)
{
    float3 n = normalize(normalWS);
    float3 dx = ddx(n), dy = ddy(n);
    return sqrt(dot(dx, dx) + dot(dy, dy));
}

void ClipToonCelEdge(float3 generated, float3 normalWS, float3 positionWS, bool frontFace, float hashScale)
{
    // Derivatives must be taken in uniform control flow, before any clip.
    float normalDiff = ToonNormalDifference(normalWS);
    float4 signals = LassylaEdgeSignals(generated, normalWS, positionWS, frontFace);
    float transparency = signals.w;
    if (_NormalEdgeStrength > 0.0)
    {
        // Rebuild the fresnel ramp value (signals.y is the raw fresnel term) and
        // union it with the normal-difference ramp, then re-apply the noise mask (signals.z).
        float edgeMul = 1.0 / (_EdgeEnd - _EdgeStart);
        float fresnelEdge = saturate(signals.y * edgeMul - edgeMul * _EdgeStart);
        float ndRange = max(_NormalEdgeEnd - _NormalEdgeStart, 1e-5);
        float normalEdge = saturate((normalDiff - _NormalEdgeStart) / ndRange) * _NormalEdgeStrength;
        transparency = saturate(max(fresnelEdge, normalEdge) * signals.z);
    }
    if (_LassylaHideGrain > 0.5)
        clip(0.5 - transparency);
    else
        LassylaClipHashAtScale(positionWS, transparency, hashScale);
}
#endif

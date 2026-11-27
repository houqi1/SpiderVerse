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

// Shared edge signal for both edge modes (hash dissolve and texture alpha blend).
// Returns the amount of coverage REMOVED: 0 = fully kept, 1 = fully removed.
// The normal-difference debug view returns 0 (kept) or 2 (forced removed).
// Derivatives are taken here, so call from uniform control flow (before any clip).
float ToonCelEdgeTransparency(float3 generated, float3 normalWS, float3 positionWS, bool frontFace, float2 edgeUV)
{
    float normalDiff = ToonNormalDifference(normalWS);
    float4 signals = LassylaEdgeSignals(generated, normalWS, positionWS, frontFace);
    float ndRange = max(_NormalEdgeEnd - _NormalEdgeStart, 1e-5);
    float normalEdge = saturate((normalDiff - _NormalEdgeStart) / ndRange);
    if (_NormalEdgeDebug > 0.5)
    {
        // Debug: show only the region selected by the normal-difference ramp
        // (no fresnel, no noise, no strength scaling), as hard binary coverage.
        return normalEdge >= 0.5 ? 0.0 : 2.0;
    }
    float transparency = signals.w;
    if (_NormalEdgeStrength > 0.0)
    {
        // Rebuild the fresnel ramp value (signals.y is the raw fresnel term) and
        // intersect it with the normal-difference ramp, then re-apply the noise mask (signals.z).
        float edgeMul = 1.0 / (_EdgeEnd - _EdgeStart);
        float fresnelEdge = saturate(signals.y * edgeMul - edgeMul * _EdgeStart);
        // Intersection: fresnel selects the rim, normal difference trims it to real edges.
        // Strength 0 = fresnel only, 1 = full intersection.
        float combined = lerp(fresnelEdge, min(fresnelEdge, normalEdge), _NormalEdgeStrength);
        transparency = saturate(combined * signals.z);
    }
    // Rim texture: multiplied onto the rim signal (white = unchanged, dark = rim removed there).
    float rimTex = SAMPLE_TEXTURE2D(_EdgeTex, sampler_EdgeTex, edgeUV * _EdgeTex_ST.xy + _EdgeTex_ST.zw).r;
    return transparency * lerp(1.0, rimTex, _EdgeTexStrength);
}

// Texture-blend mode: continuous opacity (1 = solid).
// transparency <= Start stays fully solid (so the body interior is untouched, like the hash
// mode with a hard threshold); transparency >= End is fully faded to the floor; smooth between.
float ToonCelEdgeOpacity(float transparency)
{
    if (transparency > 1.5) return 0.0; // normal-difference debug: forced removed
    float range = max(_EdgeBlendEnd - _EdgeBlendStart, 1e-4);
    float t = saturate((transparency - _EdgeBlendStart) / range);
    return lerp(1.0, _EdgeBlendMin, t * t * (3.0 - 2.0 * t));
}

void ClipToonCelEdge(float3 generated, float3 normalWS, float3 positionWS, bool frontFace, float2 edgeUV, float hashScale)
{
    // Derivatives must be taken in uniform control flow, before any clip.
    float transparency = ToonCelEdgeTransparency(generated, normalWS, positionWS, frontFace, edgeUV);
    // Texture-blend mode: coverage stays the plain (anti-aliased) silhouette; the edge
    // opacity is written to the unfiltered colour texture's alpha instead.
    if (_LassylaEdgeBlend > 0.5)
        return;
    if (_LassylaHideGrain > 0.5)
        clip(0.5 - transparency);
    else
        LassylaClipHashAtScale(positionWS, transparency, hashScale);
}

// Centre-sample depth: in blend mode only reasonably solid pixels write depth.
void ClipToonCelEdgeDepth(float3 generated, float3 normalWS, float3 positionWS, bool frontFace, float2 edgeUV, float hashScale)
{
    if (_LassylaEdgeBlend > 0.5)
    {
        float opacity = ToonCelEdgeOpacity(ToonCelEdgeTransparency(generated, normalWS, positionWS, frontFace, edgeUV));
        clip(opacity - max(_EdgeBlendDepthCutoff, 1e-4));
        return;
    }
    ClipToonCelEdge(generated, normalWS, positionWS, frontFace, edgeUV, hashScale);
}
#endif

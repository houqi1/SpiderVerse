// Shared by the Blender material and ToonCel. Both passes use the same coverage operations.
#ifndef LASSYLA_EDGE_COVERAGE_INCLUDED
#define LASSYLA_EDGE_COVERAGE_INCLUDED
#include "BlenderNodes.hlsl"
float _LassylaTemporalOffset;
float _LassylaUseSceneDepth;
float4 _LassylaJitter;
TEXTURE2D_X_FLOAT(_LassylaSceneDepth);
float3 LassylaEdgeNormalWS(float3 normalOS) {
    float3 bx=normalize(_BlenderLocal0.xyz),by=normalize(_BlenderLocal1.xyz),bz=normalize(_BlenderLocal2.xyz);
    float3 bn=float3(dot(bx,normalOS),dot(by,normalOS),dot(bz,normalOS));
    bn=trunc(bn*511.0)/511.0;
    float3 packed=bx*bn.x+by*bn.y+bz*bn.z;
    return TransformObjectToWorldNormal(packed,false);
}
float4 LassylaEdgeSignals(float3 generated,float3 normalWS,float3 positionWS,bool frontFace) {
    float3 N=normalize(normalWS),V=GetWorldSpaceNormalizeViewDir(positionWS);
    precise float edgeMul=1.0/(_EdgeEnd-_EdgeStart),noiseMul=1.0/(_NoiseEnd-_NoiseStart);
    return Lassyla_edge(generated,dot(N,V),frontFace,_IOR,
        float4(_NoiseScale,_NoiseDetail,_NoiseRoughness,_NoiseLacunarity),_NoiseDistortion,
        float4(edgeMul,-edgeMul*_EdgeStart,noiseMul,-noiseMul*_NoiseStart),_NoiseDark);
}
void LassylaClipHashAtScale(float3 positionWS,float transparency,float scale) {
    // EEVEE discards when TRANSMITTANCE exceeds the stochastic threshold.
    float4 p=float4(positionWS,1);
    float3 blenderWorld=float3(dot(_BlenderWorld0,p),dot(_BlenderWorld1,p),dot(_BlenderWorld2,p));
    float threshold=Lassyla_alpha_threshold(scale,frac(_HashOffset+_LassylaTemporalOffset),blenderWorld);
    clip(threshold-transparency);
}
void LassylaClipHash(float3 positionWS,float transparency) {
    LassylaClipHashAtScale(positionWS,transparency,_LassylaJitter.z>0?1.0:_HashScale);
}
void LassylaClipSceneDepth(float4 positionCS) {
    if(_LassylaUseSceneDepth>0.5){
        float sceneDepth=LOAD_TEXTURE2D_X(_LassylaSceneDepth,(uint2)positionCS.xy).r;
        #if UNITY_REVERSED_Z
        clip(positionCS.z-sceneDepth);
        #else
        clip(sceneDepth-positionCS.z);
        #endif
    }
}
#endif

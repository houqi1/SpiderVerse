// Keep URP's old-position and camera-velocity calculations. Only mask the pixels that
// the centre depth sample discards, so a hole cannot write a foreground velocity.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
#include "ToonCelEdge.hlsl"
float _ToonEdgeCameraMoving;

Varyings ToonMotionVert(Attributes input, float3 normalOS : NORMAL,
    float3 authored : TEXCOORD2, float4 baked : TEXCOORD7, uint vertexID : SV_VertexID,
    float2 uv0 : TEXCOORD0,
    out float3 generated : TEXCOORD1, out float3 normalWS : TEXCOORD2, out float3 positionWS : TEXCOORD3,
    out float2 edgeUV : TEXCOORD4)
{
    UNITY_SETUP_INSTANCE_ID(input);
    edgeUV = uv0;
    generated = ToonGenerated(authored,baked,vertexID);
    normalWS = LassylaEdgeNormalWS(normalOS);
    positionWS = TransformObjectToWorld(input.position.xyz);
    return vert(input);
}

float4 ToonMotionFrag(Varyings input, float3 generated : TEXCOORD1, float3 normalWS : TEXCOORD2,
    float3 positionWS : TEXCOORD3, float2 edgeUV : TEXCOORD4, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    bool front=IS_FRONT_VFACE(face,true,false);
    if (ToonUsesLassylaCamera()) {
        ClipToonCelEdge(generated, normalWS, positionWS, front, edgeUV,
            _ToonEdgeCameraMoving > 0.5 ? 1.0 : _HashScale);
    } else if ((_Cull==1 && front) || (_Cull==2 && !front)) discard;
    return frag(input);
}

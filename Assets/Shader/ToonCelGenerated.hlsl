#ifndef TOON_CEL_GENERATED_INCLUDED
#define TOON_CEL_GENERATED_INCLUDED
TEXTURE2D(_ToonCoordTable);
float _ToonCoordFirst;
float3 ToonGenerated(float3 authored, float4 baked, uint vertexID)
{
    if (baked.w == -97.25) return baked.xyz;
    if (_ToonCoordFirst > 0.5) {
        uint index=(uint)_ToonCoordFirst-1u+vertexID;
        return LOAD_TEXTURE2D_LOD(_ToonCoordTable,uint2(index & 255u,index >> 8),0).xyz;
    }
    return authored;
}
#endif

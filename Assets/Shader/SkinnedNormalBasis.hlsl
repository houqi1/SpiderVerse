#ifndef SPIDERVERSE_SKINNED_NORMAL_BASIS_INCLUDED
#define SPIDERVERSE_SKINNED_NORMAL_BASIS_INCLUDED
// NORMAL and TANGENT already contain Unity's skinned vertex data. Match the
// orthonormal frame used by the offline bake, including mirrored UV handedness.
float3 ReviewedNormalTangentToWorld(float3 normalTS, float3 interpolatedNormalWS, float4 interpolatedTangentWS)
{
    float3 n = SafeNormalize(interpolatedNormalWS);
    float3 t = SafeNormalize(interpolatedTangentWS.xyz - n * dot(n, interpolatedTangentWS.xyz));
    float3 b = cross(n, t) * (interpolatedTangentWS.w < 0.0 ? -1.0 : 1.0);
    return SafeNormalize(t * normalTS.x + b * normalTS.y + n * normalTS.z);
}
#endif

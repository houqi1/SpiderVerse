#ifndef SURFACE_NOISE_BILLBOARD_ROTATION_INCLUDED
#define SURFACE_NOISE_BILLBOARD_ROTATION_INCLUDED
StructuredBuffer<float2> _SurfaceBillboardAxes;
float4 _SurfaceBillboardRotation; // enabled, angle offset in radians, unused, unused
float2 RotateSurfaceBillboardCorner(float2 corner, uint instanceID)
{
    if (_SurfaceBillboardRotation.x < 0.5) return corner;
    float2 axis = _SurfaceBillboardAxes[instanceID];
    float s, c;
    sincos(_SurfaceBillboardRotation.y, s, c);
    axis = float2(c * axis.x - s * axis.y, s * axis.x + c * axis.y);
    // The local Y axis follows the surface; both axes remain in the view plane.
    return float2(corner.x * axis.y + corner.y * axis.x,
                 -corner.x * axis.x + corner.y * axis.y);
}
#endif

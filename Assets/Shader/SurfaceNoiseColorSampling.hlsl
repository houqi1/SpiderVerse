#ifndef SURFACE_NOISE_COLOR_SAMPLING_INCLUDED
#define SURFACE_NOISE_COLOR_SAMPLING_INCLUDED

// Integer loads are deliberate: interpolating two source coordinates can point
// outside the character, even when both original coordinates were valid.
Texture2D<uint2> _SurfaceColorSeeds;
Texture2D<float> _SurfaceColorMask;
Texture2D<float4> _SurfaceSourceColor;
float _SurfaceColorFieldEnabled;
float4 _SurfaceColorFieldRect;   // full-resolution origin, field dimensions
float4 _SurfaceColorFieldParams; // downsample, radius, full-resolution dimensions

float SurfaceParticleEyeDepth(float rawDepth)
{
    if (unity_OrthoParams.w > 0.5)
    {
        #if UNITY_REVERSED_Z
        rawDepth = 1.0 - rawDepth;
        #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
    }
    return LinearEyeDepth(rawDepth, _ZBufferParams);
}

half3 SampleSurfaceColorField(float2 pixelCenter, float sceneEye, out half visibility)
{
    visibility = 1;
    int2 pixel = int2(pixelCenter);
    if (_SurfaceColorMask.Load(int3(pixel, 0)) > 0)
        return _SurfaceSourceColor.Load(int3(pixel, 0)).rgb;

    float2 grid = (pixelCenter - _SurfaceColorFieldRect.xy) / _SurfaceColorFieldParams.x - 0.5;
    int2 baseCell = int2(floor(grid));
    float nearest = 1e20;
    uint2 best = 0;
    // Select among the four neighboring cells using full-resolution distance.
    // This refines the half-resolution boundary without blending source colors.
    [unroll] for (int y = 0; y < 2; y++)
    [unroll] for (int x = 0; x < 2; x++)
    {
        int2 cell = baseCell + int2(x, y);
        if (any(cell < 0) || any(cell >= int2(_SurfaceColorFieldRect.zw))) continue;
        uint2 seed = _SurfaceColorSeeds.Load(int3(cell, 0));
        if (any(seed == 0)) continue;
        float2 delta = float2(seed) - 0.5 - pixelCenter;
        float distanceSq = dot(delta, delta);
        if (distanceSq < nearest)
        {
            best = seed;
            nearest = distanceSq;
        }
    }
    if (any(best == 0)) { visibility = 0; return 0; }
    int2 sourcePixel = int2(best) - 1;
    float sourceDepth = _SurfaceColorMask.Load(int3(sourcePixel, 0));
    float radius = _SurfaceColorFieldParams.y;
    visibility = 1.0 - smoothstep(max(0, radius - 4), radius, sqrt(nearest));
    // Do not propagate a hidden character's color onto a foreground occluder.
    visibility *= step(sourceDepth - max(0.003, sourceDepth * 0.001), sceneEye);
    return _SurfaceSourceColor.Load(int3(sourcePixel, 0)).rgb;
}
#endif

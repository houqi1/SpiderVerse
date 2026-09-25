#ifndef SURFACE_NOISE_DISTRIBUTION_INCLUDED
#define SURFACE_NOISE_DISTRIBUTION_INCLUDED
float4 _SurfaceSilhouetteDistribution; // enabled, front weight, width, choices per particle
float4 _SurfaceDistributionRange; // original offset, layer sample count, instance offset, layer instance count
StructuredBuffer<uint> _SurfaceDistributionCdf;

uint SelectSurfaceDistributionIndex(uint instanceID)
{
    uint choices = max(1u, (uint)_SurfaceSilhouetteDistribution.w);
    uint first = (uint)_SurfaceDistributionRange.x + instanceID * choices;
    if (_SurfaceSilhouetteDistribution.x < 0.5 || choices == 1 || _SurfaceSilhouetteDistribution.y >= 1.0)
        return first;
    uint sampleCount = (uint)_SurfaceDistributionRange.y;
    uint total = _SurfaceDistributionCdf[sampleCount - 1];
    // Only an entirely unsupported layer can fall back; never a local pool.
    if (total <= 0) return first;
    uint outputID = (uint)_SurfaceDistributionRange.z + instanceID;
    float unit = (outputID + 0.5) / max(1.0, _SurfaceDistributionRange.w);
    uint target = min((uint)(unit * total), total - 1u);
    uint low = 0, high = sampleCount - 1;
    [loop] while (low < high)
    {
        uint mid = low + (high - low) / 2;
        // Strict upper bound skips zero-weight intervals, including front faces.
        if (_SurfaceDistributionCdf[mid] <= target) low = mid + 1;
        else high = mid;
    }
    return low;
}

SurfaceParticle SelectSurfaceDistributionParticle(uint instanceID)
{
    uint first = (uint)_SurfaceDistributionRange.x + instanceID * max(1u, (uint)_SurfaceSilhouetteDistribution.w);
    SurfaceParticle original = _SurfaceParticles[first];
    SurfaceParticle selected = _SurfaceParticles[SelectSurfaceDistributionIndex(instanceID)];
    selected.color = original.color;
    selected.positionSize.w = original.positionSize.w;
    return selected;
}
#endif

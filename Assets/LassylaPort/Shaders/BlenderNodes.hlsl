#ifndef LASSYLA_BLENDER_NODES
#define LASSYLA_BLENDER_NODES
/* SPDX-FileCopyrightText: 2017-2023 Blender Authors
 * SPDX-License-Identifier: GPL-2.0-or-later
 * Extracted from Blender e13b728c6880. See blender-source/manifest.json.
 * Original arithmetic and integer Lassyla_hash preserved; dialect changes only.
 */

#define rot(x, k) (((x) << (k)) | ((x) >> (32 - (k))))

#define mix(a, b, c) \
  { \
    a -= c; \
    a ^= rot(c, 4); \
    c += b; \
    b -= a; \
    b ^= rot(a, 6); \
    a += c; \
    c -= b; \
    c ^= rot(b, 8); \
    b += a; \
    a -= c; \
    a ^= rot(c, 16); \
    c += b; \
    b -= a; \
    b ^= rot(a, 19); \
    a += c; \
    c -= b; \
    c ^= rot(b, 4); \
    b += a; \
  }

#define final(a, b, c) \
  { \
    c ^= b; \
    c -= rot(b, 14); \
    a ^= c; \
    a -= rot(c, 11); \
    b ^= a; \
    b -= rot(a, 25); \
    c ^= b; \
    c -= rot(b, 16); \
    a ^= c; \
    a -= rot(c, 4); \
    b ^= a; \
    b -= rot(a, 14); \
    c ^= b; \
    c -= rot(b, 24); \
  }

uint Lassyla_hash_uint(uint kx)
{
  uint a, b, c;
  a = b = c = 0xdeadbeefu + (1u << 2u) + 13u;

  a += kx;
  final(a, b, c);

  return c;
}

uint Lassyla_hash_uint2(uint kx, uint ky)
{
  uint a, b, c;
  a = b = c = 0xdeadbeefu + (2u << 2u) + 13u;

  b += ky;
  a += kx;
  final(a, b, c);

  return c;
}

uint Lassyla_hash_uint3(uint kx, uint ky, uint kz)
{
  uint a, b, c;
  a = b = c = 0xdeadbeefu + (3u << 2u) + 13u;

  c += kz;
  b += ky;
  a += kx;
  final(a, b, c);

  return c;
}

uint Lassyla_hash_uint4(uint kx, uint ky, uint kz, uint kw)
{
  uint a, b, c;
  a = b = c = 0xdeadbeefu + (4u << 2u) + 13u;

  a += kx;
  b += ky;
  c += kz;
  mix(a, b, c);

  a += kw;
  final(a, b, c);

  return c;
}

#undef rot
#undef final
#undef mix

uint Lassyla_hash_int(int kx)
{
  return Lassyla_hash_uint(uint(kx));
}

uint Lassyla_hash_int2(int kx, int ky)
{
  return Lassyla_hash_uint2(uint(kx), uint(ky));
}

uint Lassyla_hash_int3(int kx, int ky, int kz)
{
  return Lassyla_hash_uint3(uint(kx), uint(ky), uint(kz));
}

uint Lassyla_hash_int4(int kx, int ky, int kz, int kw)
{
  return Lassyla_hash_uint4(uint(kx), uint(ky), uint(kz), uint(kw));
}



#define FLOORFRAC(x, x_int, x_fract) { precise float x_floor = floor(x); x_int = int(x_floor); x_fract = x - x_floor; }

precise float Lassyla_tri_mix(precise float v0,
              precise float v1,
              precise float v2,
              precise float v3,
              precise float v4,
              precise float v5,
              precise float v6,
              precise float v7,
              precise float x,
              precise float y,
              precise float z)
{
  precise float x1 = 1.0 - x;
  precise float y1 = 1.0 - y;
  precise float z1 = 1.0 - z;
  return z1 * (y1 * (v0 * x1 + v1 * x) + y * (v2 * x1 + v3 * x)) +
         z * (y1 * (v4 * x1 + v5 * x) + y * (v6 * x1 + v7 * x));
}

precise float Lassyla_fade(precise float t)
{
  return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

precise float Lassyla_negate_if(precise float value, uint condition)
{
  return (condition != 0u) ? -value : value;
}

precise float Lassyla_noise_grad(uint Lassyla_hash, precise float x, precise float y, precise float z)
{
  uint h = Lassyla_hash & 15u;
  precise float u = h < 8u ? x : y;
  precise float vt = ((h == 12u) || (h == 14u)) ? x : z;
  precise float v = h < 4u ? y : vt;
  return Lassyla_negate_if(u, h & 1u) + Lassyla_negate_if(v, h & 2u);
}

precise float Lassyla_noise_perlin(precise float3 vec)
{
  int X, Y, Z;
  precise float fx, fy, fz;

  FLOORFRAC(vec.x, X, fx);
  FLOORFRAC(vec.y, Y, fy);
  FLOORFRAC(vec.z, Z, fz);

  precise float u = Lassyla_fade(fx);
  precise float v = Lassyla_fade(fy);
  precise float w = Lassyla_fade(fz);

  precise float r = Lassyla_tri_mix(Lassyla_noise_grad(Lassyla_hash_int3(X, Y, Z), fx, fy, fz),
                    Lassyla_noise_grad(Lassyla_hash_int3(X + 1, Y, Z), fx - 1.0, fy, fz),
                    Lassyla_noise_grad(Lassyla_hash_int3(X, Y + 1, Z), fx, fy - 1.0, fz),
                    Lassyla_noise_grad(Lassyla_hash_int3(X + 1, Y + 1, Z), fx - 1.0, fy - 1.0, fz),
                    Lassyla_noise_grad(Lassyla_hash_int3(X, Y, Z + 1), fx, fy, fz - 1.0),
                    Lassyla_noise_grad(Lassyla_hash_int3(X + 1, Y, Z + 1), fx - 1.0, fy, fz - 1.0),
                    Lassyla_noise_grad(Lassyla_hash_int3(X, Y + 1, Z + 1), fx, fy - 1.0, fz - 1.0),
                    Lassyla_noise_grad(Lassyla_hash_int3(X + 1, Y + 1, Z + 1), fx - 1.0, fy - 1.0, fz - 1.0),
                    u,
                    v,
                    w);

  return r;
}

precise float Lassyla_noise_scale3(precise float result)
{
  return 0.9820 * result;
}

// Blender Lassyla_compatible_mod has fmod / truncation semantics, including negatives.
precise float3 Lassyla_compatible_mod(precise float3 a,precise float b){return a-trunc(a/b)*b;}

precise float Lassyla_snoise(precise float3 p)
{
  precise float3 precision_correction = 0.5 * float3(float(abs(p.x) >= 1000000.0),
                                              float(abs(p.y) >= 1000000.0),
                                              float(abs(p.z) >= 1000000.0));
  /* Repeat Perlin noise texture every 100000.0 on each axis to prevent floating point
   * representation issues. This causes discontinuities every 100000.0, however at such scales
   * this usually shouldn't be noticeable. */
  p = Lassyla_compatible_mod(p, 100000.0) + precision_correction;

  return Lassyla_noise_scale3(Lassyla_noise_perlin(p));
}

  precise float Lassyla_noise_fbm(precise float3 co, 
                  precise float detail, 
                  precise float roughness, 
                  precise float lacunarity, 
                  precise float offset, 
                  precise float gain, 
                  bool normalize) 
  { 
    precise float3 p = co; 
    precise float fscale = 1.0; 
    precise float amp = 1.0; 
    precise float maxamp = 0.0; 
    precise float sum = 0.0; 

    for (int i = 0; i <= int(detail); i++) { 
      precise float t = Lassyla_snoise(fscale * p); 
      sum += t * amp; 
      maxamp += amp; 
      amp *= roughness; 
      fscale *= lacunarity; 
    } 
    precise float rmd = detail - floor(detail); 
    if (rmd != 0.0) { 
      precise float t = Lassyla_snoise(fscale * p); 
      precise float sum2 = sum + t * amp; 
      return normalize ? 
                 lerp(0.5 * sum / maxamp + 0.5, 0.5 * sum2 / (maxamp + amp) + 0.5, rmd) : 
                 lerp(sum, sum2, rmd); 
    } 
    else { 
      return normalize ? 0.5 * sum / maxamp + 0.5 : sum; 
    } 
  }



precise float Lassyla_fresnel_dielectric_cos(precise float cosi, precise float eta)
{
  /* compute fresnel reflectance without explicitly computing
   * the refracted direction */
  precise float c = abs(cosi);
  precise float g = eta * eta - 1.0 + c * c;
  precise float result;

  if (g > 0.0) {
    g = sqrt(g);
    precise float A = (g - c) / (g + c);
    precise float B = (c * (g + c) - 1.0) / (c * (g - c) + 1.0);
    result = 0.5 * A * A * (1.0 + B * B);
  }
  else {
    result = 1.0; /* TIR (no refracted component) */
  }

  return result;
}


int3 Lassyla_hash_pcg3d_i(int3 v)
{
  v = v * 1664525 + 1013904223;
  v.x += v.y * v.z;
  v.y += v.z * v.x;
  v.z += v.x * v.y;
  v = v ^ (v >> 16);
  v.x += v.y * v.z;
  v.y += v.z * v.x;
  v.z += v.x * v.y;
  return v;
}
precise float Lassyla_hash_uint_to_float(uint kx)
{
  return float(Lassyla_hash_uint(kx)) / float(0xFFFFFFFFu);
}
precise float Lassyla_hash_uint2_to_float(uint kx, uint ky)
{
  return float(Lassyla_hash_uint2(kx, ky)) / float(0xFFFFFFFFu);
}
precise float Lassyla_hash_uint3_to_float(uint kx, uint ky, uint kz)
{
  return float(Lassyla_hash_uint3(kx, ky, kz)) / float(0xFFFFFFFFu);
}
precise float Lassyla_hash_uint4_to_float(uint kx, uint ky, uint kz, uint kw)
{
  return float(Lassyla_hash_uint4(kx, ky, kz, kw)) / float(0xFFFFFFFFu);
}
precise float Lassyla_hash_float_to_float(precise float k)
{
  return Lassyla_hash_uint_to_float(asuint(k));
}
precise float Lassyla_hash_vec2_to_float(precise float2 k)
{
  return Lassyla_hash_uint2_to_float(asuint(k.x), asuint(k.y));
}
precise float Lassyla_hash_vec3_to_float(precise float3 k)
{
  return Lassyla_hash_uint3_to_float(asuint(k.x), asuint(k.y), asuint(k.z));
}
precise float Lassyla_hash_vec4_to_float(precise float4 k)
{
  return Lassyla_hash_uint4_to_float(
      asuint(k.x), asuint(k.y), asuint(k.z), asuint(k.w));
}
precise float3 Lassyla_hash_int3_to_vec3(int3 k)
{
  int3 h = Lassyla_hash_pcg3d_i(k);
  return float3(h & int3(0x7fffffff,0x7fffffff,0x7fffffff)) * (1.0 / float(0x7fffffff));
}
precise float3 Lassyla_random_vec3_offset(precise float seed)
{
  return float3(100.0f + Lassyla_hash_vec2_to_float(float2(seed, 0.0f)) * 100.0f,
                100.0f + Lassyla_hash_vec2_to_float(float2(seed, 1.0f)) * 100.0f,
                100.0f + Lassyla_hash_vec2_to_float(float2(seed, 2.0f)) * 100.0f);
}
precise float3 Lassyla_noise_color(precise float3 p, precise float detail, precise float roughness, precise float lacunarity) {
    return float3(Lassyla_noise_fbm(p,detail,roughness,lacunarity,0,1,true),
       Lassyla_noise_fbm(p+Lassyla_random_vec3_offset(3.0),detail,roughness,lacunarity,0,1,true),
       Lassyla_noise_fbm(p+Lassyla_random_vec3_offset(4.0),detail,roughness,lacunarity,0,1,true));
}
precise float Lassyla_noise_texture(precise float3 p,precise float scale,precise float detail,precise float roughness,precise float lacunarity,precise float distortion){
    p *= scale;
    if(distortion!=0.0) p += float3(Lassyla_snoise(p+Lassyla_random_vec3_offset(0.0)),Lassyla_snoise(p+Lassyla_random_vec3_offset(1.0)),Lassyla_snoise(p+Lassyla_random_vec3_offset(2.0)))*distortion;
    return Lassyla_noise_fbm(p,clamp(detail,0.0,15.0),max(roughness,0.0),lacunarity,0,1,true);
}
// Source Voronoi F1: 3D, Manhattan, Detail 0, Randomness 1, Normalize off.
// 5.2 uses signed PCG integer hashes for Voronoi; Perlin uses Jenkins hashes.
void Lassyla_voronoi_f1(precise float3 coord,precise float scale,out precise float3 color,out precise float3 position){
    coord *= scale;
    precise float3 cellPosition_f=floor(coord),localPosition=coord-cellPosition_f;
    int3 cellPosition=int3(cellPosition_f),targetOffset=int3(0,0,0);
    precise float minDistance=3.402823466e+38;
    precise float3 targetPosition=float3(0,0,0);
    [loop] for(int k=-1;k<=1;k++) [loop] for(int j=-1;j<=1;j++) [loop] for(int i=-1;i<=1;i++){
        int3 cellOffset=int3(i,j,k);
        precise float3 pointPosition=float3(cellOffset)+Lassyla_hash_int3_to_vec3(cellPosition+cellOffset);
        precise float3 d=abs(pointPosition-localPosition);
        precise float distanceToPoint=d.x+d.y+d.z;
        if(distanceToPoint<minDistance){targetOffset=cellOffset;minDistance=distanceToPoint;targetPosition=pointPosition;}
    }
    color=Lassyla_hash_int3_to_vec3(cellPosition+targetOffset);
    position=scale==0.0?float3(0,0,0):(targetPosition+cellPosition_f)/scale;
}
void Lassyla_blurry_voronoi(precise float3 objectPosition,precise float4 parameters,out precise float3 color,out precise float3 position){
    precise float3 low=Lassyla_noise_color(objectPosition*parameters.x,2.0,0.5,2.0);
    precise float high=Lassyla_noise_texture(objectPosition,1000.0,2.0,0.5,2.0,203.79998779296875);
    // Original Map Range FLOAT_VECTOR and FLOAT, both [-1,1] with clamp enabled.
    precise float3 lowOffset=clamp(-1.0+low*2.0,-1.0,1.0)*parameters.y;
    precise float highOffset=clamp(-1.0+high*2.0,-1.0,1.0)*parameters.z;
    precise float3 displaced=(objectPosition+lowOffset)+highOffset;
    Lassyla_voronoi_f1(displaced,parameters.w,color,position);
}
precise float Lassyla_ramp_linear(precise float fac,precise float2 mulbias,precise float a,precise float b){
    return lerp(a,b,saturate(fac*mulbias.x+mulbias.y));
}
precise float4 Lassyla_edge(precise float3 generated,precise float cosNV,bool frontFace,precise float ior,precise float4 noiseParameters,precise float distortion,precise float4 rampMulBias,precise float noiseDark){
    precise float noiseValue=Lassyla_noise_texture(generated,noiseParameters.x,noiseParameters.y,noiseParameters.z,noiseParameters.w,distortion);
    precise float eta=max(ior,0.00001);
    precise float f=Lassyla_fresnel_dielectric_cos(cosNV,frontFace?eta:1.0/eta);
    precise float edgeValue=Lassyla_ramp_linear(f,rampMulBias.xy,0.0,1.0);
    precise float noiseColor=Lassyla_ramp_linear(noiseValue,rampMulBias.zw,noiseDark,1.0);
    return float4(noiseValue,f,noiseColor,saturate(edgeValue*noiseColor));
}

#ifndef LASSYLA_COMPUTE
precise float Lassyla_hash(precise float2 a)
{
  return frac(1e4 * sin(17.0 * a.x + 0.1 * a.y) * (0.1 + abs(sin(13.0 * a.y + a.x))));
}

precise float Lassyla_hash_3d(precise float3 a)
{
  return Lassyla_hash(float2(Lassyla_hash(a.xy), a.z));
}

precise float Lassyla_alpha_threshold(precise float hash_scale, precise float hash_offset, precise float3 P)
{
  /* Find the discretized derivatives of our coordinates. */
  precise float max_deriv = max(length(ddx(P)), length(ddy(P)));
  precise float pix_scale = 1.0 / (hash_scale * max_deriv);
  /* Find two nearest log-discretized noise scales. */
  precise float pix_scale_log = log2(pix_scale);
  precise float2 pix_scales;
  pix_scales.x = exp2(floor(pix_scale_log));
  pix_scales.y = exp2(ceil(pix_scale_log));
  /* Compute alpha thresholds at our two noise scales. */
  precise float2 alpha;
  alpha.x = Lassyla_hash_3d(floor(pix_scales.x * P));
  alpha.y = Lassyla_hash_3d(floor(pix_scales.y * P));
  /* Factor to interpolate lerp with. */
  precise float fac = frac(log2(pix_scale));
  /* Interpolate alpha threshold from noise at two scales. */
  precise float x = lerp(alpha.x, alpha.y, fac);
  /* Pass into CDF to compute uniformly distributed threshold. */
  precise float a = min(fac, 1.0 - fac);
  precise float one_a = 1.0 - a;
  precise float denom = 1.0 / (2.0 * a * one_a);
  precise float one_x = (1.0 - x);
  precise float3 cases = float3((x * x) * denom, (x - 0.5 * a) / one_a, 1.0 - (one_x * one_x * denom));
  /* Find our final, uniformly distributed alpha threshold. */
  precise float threshold = (x < one_a) ? ((x < a) ? cases.x : cases.y) : cases.z;
  /* Jitter the threshold for TAA accumulation. */
  threshold = frac(threshold + hash_offset);
  /* Avoids threshold == 0. */
  threshold = clamp(threshold, 1.0e-6, 1.0);
  return threshold;
}



#endif
#endif

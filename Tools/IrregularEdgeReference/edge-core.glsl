/* SPDX-FileCopyrightText: 2017-2023 Blender Authors
 * SPDX-License-Identifier: GPL-2.0-or-later
 * Extracted from Blender e13b728c6880. See blender-source/manifest.json.
 * Original arithmetic and integer hash preserved; dialect changes only.
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

uint hash_uint(uint kx)
{
  uint a, b, c;
  a = b = c = 0xdeadbeefu + (1u << 2u) + 13u;

  a += kx;
  final(a, b, c);

  return c;
}

uint hash_uint2(uint kx, uint ky)
{
  uint a, b, c;
  a = b = c = 0xdeadbeefu + (2u << 2u) + 13u;

  b += ky;
  a += kx;
  final(a, b, c);

  return c;
}

uint hash_uint3(uint kx, uint ky, uint kz)
{
  uint a, b, c;
  a = b = c = 0xdeadbeefu + (3u << 2u) + 13u;

  c += kz;
  b += ky;
  a += kx;
  final(a, b, c);

  return c;
}

uint hash_uint4(uint kx, uint ky, uint kz, uint kw)
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

uint hash_int(int kx)
{
  return hash_uint(uint(kx));
}

uint hash_int2(int kx, int ky)
{
  return hash_uint2(uint(kx), uint(ky));
}

uint hash_int3(int kx, int ky, int kz)
{
  return hash_uint3(uint(kx), uint(ky), uint(kz));
}

uint hash_int4(int kx, int ky, int kz, int kw)
{
  return hash_uint4(uint(kx), uint(ky), uint(kz), uint(kw));
}



#define FLOORFRAC(x, x_int, x_fract) { float x_floor = floor(x); x_int = int(x_floor); x_fract = x - x_floor; }

float tri_mix(float v0,
              float v1,
              float v2,
              float v3,
              float v4,
              float v5,
              float v6,
              float v7,
              float x,
              float y,
              float z)
{
  float x1 = 1.0 - x;
  float y1 = 1.0 - y;
  float z1 = 1.0 - z;
  return z1 * (y1 * (v0 * x1 + v1 * x) + y * (v2 * x1 + v3 * x)) +
         z * (y1 * (v4 * x1 + v5 * x) + y * (v6 * x1 + v7 * x));
}

float fade(float t)
{
  return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
}

float negate_if(float value, uint condition)
{
  return (condition != 0u) ? -value : value;
}

float noise_grad(uint hash, float x, float y, float z)
{
  uint h = hash & 15u;
  float u = h < 8u ? x : y;
  float vt = ((h == 12u) || (h == 14u)) ? x : z;
  float v = h < 4u ? y : vt;
  return negate_if(u, h & 1u) + negate_if(v, h & 2u);
}

float noise_perlin(vec3 vec)
{
  int X, Y, Z;
  float fx, fy, fz;

  FLOORFRAC(vec.x, X, fx);
  FLOORFRAC(vec.y, Y, fy);
  FLOORFRAC(vec.z, Z, fz);

  float u = fade(fx);
  float v = fade(fy);
  float w = fade(fz);

  float r = tri_mix(noise_grad(hash_int3(X, Y, Z), fx, fy, fz),
                    noise_grad(hash_int3(X + 1, Y, Z), fx - 1.0, fy, fz),
                    noise_grad(hash_int3(X, Y + 1, Z), fx, fy - 1.0, fz),
                    noise_grad(hash_int3(X + 1, Y + 1, Z), fx - 1.0, fy - 1.0, fz),
                    noise_grad(hash_int3(X, Y, Z + 1), fx, fy, fz - 1.0),
                    noise_grad(hash_int3(X + 1, Y, Z + 1), fx - 1.0, fy, fz - 1.0),
                    noise_grad(hash_int3(X, Y + 1, Z + 1), fx, fy - 1.0, fz - 1.0),
                    noise_grad(hash_int3(X + 1, Y + 1, Z + 1), fx - 1.0, fy - 1.0, fz - 1.0),
                    u,
                    v,
                    w);

  return r;
}

float noise_scale3(float result)
{
  return 0.9820 * result;
}

// Blender compatible_mod has fmod / truncation semantics, including negatives.
vec3 compatible_mod(vec3 a,float b){return a-trunc(a/b)*b;}

float snoise(vec3 p)
{
  vec3 precision_correction = 0.5 * vec3(float(abs(p.x) >= 1000000.0),
                                              float(abs(p.y) >= 1000000.0),
                                              float(abs(p.z) >= 1000000.0));
  /* Repeat Perlin noise texture every 100000.0 on each axis to prevent floating point
   * representation issues. This causes discontinuities every 100000.0, however at such scales
   * this usually shouldn't be noticeable. */
  p = compatible_mod(p, 100000.0) + precision_correction;

  return noise_scale3(noise_perlin(p));
}

  float noise_fbm(vec3 co, 
                  float detail, 
                  float roughness, 
                  float lacunarity, 
                  float offset, 
                  float gain, 
                  bool normalize) 
  { 
    vec3 p = co; 
    float fscale = 1.0; 
    float amp = 1.0; 
    float maxamp = 0.0; 
    float sum = 0.0; 

    for (int i = 0; i <= int(detail); i++) { 
      float t = snoise(fscale * p); 
      sum += t * amp; 
      maxamp += amp; 
      amp *= roughness; 
      fscale *= lacunarity; 
    } 
    float rmd = detail - floor(detail); 
    if (rmd != 0.0) { 
      float t = snoise(fscale * p); 
      float sum2 = sum + t * amp; 
      return normalize ? 
                 mix(0.5 * sum / maxamp + 0.5, 0.5 * sum2 / (maxamp + amp) + 0.5, rmd) : 
                 mix(sum, sum2, rmd); 
    } 
    else { 
      return normalize ? 0.5 * sum / maxamp + 0.5 : sum; 
    } 
  }



float fresnel_dielectric_cos(float cosi, float eta)
{
  /* compute fresnel reflectance without explicitly computing
   * the refracted direction */
  float c = abs(cosi);
  float g = eta * eta - 1.0 + c * c;
  float result;

  if (g > 0.0) {
    g = sqrt(g);
    float A = (g - c) / (g + c);
    float B = (c * (g + c) - 1.0) / (c * (g - c) + 1.0);
    result = 0.5 * A * A * (1.0 + B * B);
  }
  else {
    result = 1.0; /* TIR (no refracted component) */
  }

  return result;
}

float hash(vec2 a)
{
  return fract(1e4 * sin(17.0 * a.x + 0.1 * a.y) * (0.1 + abs(sin(13.0 * a.y + a.x))));
}

float hash_3d(vec3 a)
{
  return hash(vec2(hash(a.xy), a.z));
}

float alpha_threshold(float hash_scale, float hash_offset, vec3 P)
{
  /* Find the discretized derivatives of our coordinates. */
  float max_deriv = max(length(dFdx(P)), length(dFdy(P)));
  float pix_scale = 1.0 / (hash_scale * max_deriv);
  /* Find two nearest log-discretized noise scales. */
  float pix_scale_log = log2(pix_scale);
  vec2 pix_scales;
  pix_scales.x = exp2(floor(pix_scale_log));
  pix_scales.y = exp2(ceil(pix_scale_log));
  /* Compute alpha thresholds at our two noise scales. */
  vec2 alpha;
  alpha.x = hash_3d(floor(pix_scales.x * P));
  alpha.y = hash_3d(floor(pix_scales.y * P));
  /* Factor to interpolate lerp with. */
  float fac = fract(log2(pix_scale));
  /* Interpolate alpha threshold from noise at two scales. */
  float x = mix(alpha.x, alpha.y, fac);
  /* Pass into CDF to compute uniformly distributed threshold. */
  float a = min(fac, 1.0 - fac);
  float one_a = 1.0 - a;
  float denom = 1.0 / (2.0 * a * one_a);
  float one_x = (1.0 - x);
  vec3 cases = vec3((x * x) * denom, (x - 0.5 * a) / one_a, 1.0 - (one_x * one_x * denom));
  /* Find our final, uniformly distributed alpha threshold. */
  float threshold = (x < one_a) ? ((x < a) ? cases.x : cases.y) : cases.z;
  /* Jitter the threshold for TAA accumulation. */
  threshold = fract(threshold + hash_offset);
  /* Avoids threshold == 0. */
  threshold = clamp(threshold, 1.0e-6, 1.0);
  return threshold;
}


uniform float uIOR,uScale,uEdgeStart,uEdgeEnd,uNoiseStart,uNoiseEnd;
// Exact extracted dark endpoint, linear RGB (not sRGB 0.05 and not zero).
const float NOISE_DARK=0.003952979110181332;
float linear_ramp(float fac,float start,float end,float a,float b){
  // mul/bias are calculated as float32 on the CPU in Blender's node compiler.
  float mul=1.0/(end-start),bias=-mul*start;
  return mix(a,b,clamp(fac*mul+bias,0.0,1.0));
}
vec4 evaluate_edge(vec3 generated,float cosNV,bool frontFace){
  // Noise Texture: 3D, fBM, Normalize on; Scale 15, Detail 2, Roughness .5,
  // Lacunarity 2, Distortion 0. Vector unconnected -> Generated.
  float noiseValue=noise_fbm(generated*uScale,2.0,0.5,2.0,0.0,1.0,true);
  float eta=max(uIOR,0.00001);
  float f=fresnel_dielectric_cos(cosNV,frontFace?eta:1.0/eta);
  float edge=linear_ramp(f,uEdgeStart,uEdgeEnd,0.0,1.0);
  float noiseColor=linear_ramp(noiseValue,uNoiseStart,uNoiseEnd,NOISE_DARK,1.0);
  // Mix Color / Multiply, Factor 1, Clamp Result, then gray RGB -> float.
  float transparency=clamp(edge*noiseColor,0.0,1.0);
  return vec4(noiseValue,f,noiseColor,transparency);
}

"""Extract upstream routines; only translate Blender's shader dialect to GLSL ES."""
import re
from pathlib import Path
R=Path(__file__).resolve().parent
S=R/'blender-source'
def function(file,signature):
    text=(S/file).read_text(encoding='utf-8');start=text.index(signature);brace=text.index('{',start);depth=1;end=brace+1
    while depth:
        if text[end]=='{':depth+=1
        if text[end]=='}':depth-=1
        end+=1
    return text[start:end]
def dialect(code):
    code=re.sub(r'\bfloat([234])\b',r'vec\1',code)
    code=re.sub(r'\bint([234])\b',r'ivec\1',code)
    code=re.sub(r'(?<=[0-9.])f\b','',code)
    code=code.replace('gpu_dfdx','dFdx').replace('gpu_dfdy','dFdy')
    code=re.sub(r'\b(f[xyz]) - 1\b(?!\.)',r'\1 - 1.0',code)
    code=code.replace('2 * a * one_a','2.0 * a * one_a').replace('(1 - x)','(1.0 - x)')
    return code
hash_text=(S/'gpu_shader_common_hash.glsl').read_text()
hash_code=hash_text[hash_text.index('#define rot'):hash_text.index('/* PCG')]
pieces=['''/* SPDX-FileCopyrightText: 2017-2023 Blender Authors
 * SPDX-License-Identifier: GPL-2.0-or-later
 * Extracted from Blender e13b728c6880. See blender-source/manifest.json.
 * Original arithmetic and integer hash preserved; dialect changes only.
 */''',hash_code]
for signature in ['float tri_mix(', 'float fade(', 'float negate_if(',
                  'float noise_grad(uint hash, float x, float y, float z)',
                  'float noise_perlin(float3 vec)', 'float noise_scale3(']:
    pieces.append(function('gpu_shader_material_noise.glsl',signature))
pieces.insert(2,'#define FLOORFRAC(x, x_int, x_fract) { float x_floor = floor(x); x_int = int(x_floor); x_fract = x - x_floor; }')
pieces.append('''// Blender compatible_mod has fmod / truncation semantics, including negatives.
vec3 compatible_mod(vec3 a,float b){return a-trunc(a/b)*b;}''')
pieces.append(function('gpu_shader_material_noise.glsl','float snoise(float3 p)'))
fractal=(S/'gpu_shader_material_fractal_noise.glsl').read_text()
fbm=fractal[fractal.index('#define NOISE_FBM'):fractal.index('#define NOISE_MULTI_FRACTAL')]
fbm=fbm[fbm.index('  float noise_fbm'):].replace('\\\n','\n')
fbm=re.sub(r'\bT\b','vec3',fbm)
pieces.extend([fbm,function('gpu_shader_material_fresnel.glsl','float fresnel_dielectric_cos(')])
pieces.extend([function('eevee_transparency.bsl.hh','float hash(float2 a)'),function('eevee_transparency.bsl.hh','float hash_3d('),function('eevee_transparency.bsl.hh','float alpha_threshold(')])
pieces.append('''
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
''')
(R/'edge-core.glsl').write_text(dialect('\n\n'.join(pieces)),encoding='utf-8')
print('Wrote extracted core',len((R/'edge-core.glsl').read_bytes()))

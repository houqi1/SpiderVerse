#ifndef CHARACTER_SHELL_LAYER_INCLUDED
#define CHARACTER_SHELL_LAYER_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
TEXTURE2D(_ClipMap); SAMPLER(sampler_ClipMap);
TEXTURE2D(_BaseMap1); SAMPLER(sampler_BaseMap1);
TEXTURE2D(_NoiseMap1); SAMPLER(sampler_NoiseMap1);
TEXTURE2D(_ClipMap1); SAMPLER(sampler_ClipMap1);
TEXTURE2D(_BaseMap2); SAMPLER(sampler_BaseMap2);
TEXTURE2D(_NoiseMap2); SAMPLER(sampler_NoiseMap2);
TEXTURE2D(_ClipMap2); SAMPLER(sampler_ClipMap2);
TEXTURE2D(_BaseMap3); SAMPLER(sampler_BaseMap3);
TEXTURE2D(_NoiseMap3); SAMPLER(sampler_NoiseMap3);
TEXTURE2D(_ClipMap3); SAMPLER(sampler_ClipMap3);

// xyz = camera held at the line-art update rate. w > 0 means the hold is valid.
float4 _LineArtCameraPosition;

CBUFFER_START(UnityPerMaterial)
    float _LayerCount;
    float4 _BaseMap_ST;
    half4 _BaseColor;
    float4 _NoiseMap_ST;
    float4 _ClipMap_ST;
    half _ClipThreshold;
    half _ClipSoftness;
    half _FresnelPower;
    half _FresnelIntensity;
    half _FresnelRimPower;
    half _FresnelRimIntensity;
    float _NormalExtrusion;
    half _UseDotMap;
    float _DotDensity;
    float _DotSize;
    half _InvertDots;
    float _Cull;
    float4 _BaseMap1_ST;
    half4 _BaseColor1;
    float4 _NoiseMap1_ST;
    float4 _ClipMap1_ST;
    half _ClipThreshold1;
    half _ClipSoftness1;
    half _FresnelPower1;
    half _FresnelIntensity1;
    half _FresnelRimPower1;
    half _FresnelRimIntensity1;
    float _NormalExtrusion1;
    half _UseDotMap1;
    float _DotDensity1;
    float _DotSize1;
    half _InvertDots1;
    float _Cull1;
    float4 _BaseMap2_ST;
    half4 _BaseColor2;
    float4 _NoiseMap2_ST;
    float4 _ClipMap2_ST;
    half _ClipThreshold2;
    half _ClipSoftness2;
    half _FresnelPower2;
    half _FresnelIntensity2;
    half _FresnelRimPower2;
    half _FresnelRimIntensity2;
    float _NormalExtrusion2;
    half _UseDotMap2;
    float _DotDensity2;
    float _DotSize2;
    half _InvertDots2;
    float _Cull2;
    float4 _BaseMap3_ST;
    half4 _BaseColor3;
    float4 _NoiseMap3_ST;
    float4 _ClipMap3_ST;
    half _ClipThreshold3;
    half _ClipSoftness3;
    half _FresnelPower3;
    half _FresnelIntensity3;
    half _FresnelRimPower3;
    half _FresnelRimIntensity3;
    float _NormalExtrusion3;
    half _UseDotMap3;
    float _DotDensity3;
    float _DotSize3;
    half _InvertDots3;
    float _Cull3;
CBUFFER_END

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VertToGeom
{
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    nointerpolation float layer : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float3 HeldCameraPosition()
{
    return _LineArtCameraPosition.w > 0.5 ? _LineArtCameraPosition.xyz : _WorldSpaceCameraPos.xyz;
}

float2 CameraPlaneMeters(float3 positionWS)
{
    float3 anchorWS = UNITY_MATRIX_M._m03_m13_m23;
    float3 toCam = HeldCameraPosition() - anchorWS;
    float toCamLen = length(toCam);
    float3 normal = toCamLen > 1e-4 ? toCam / toCamLen : float3(0.0, 0.0, 1.0);

    float3 upRef = abs(normal.y) > 0.999 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
    float3 right = normalize(cross(upRef, -normal));
    float3 up = cross(-normal, right);

    float3 rel = positionWS - anchorWS;
    return float2(dot(rel, right), dot(rel, up));
}

half3 ProceduralDots(float2 uv, float density, float radius, half invert)
{
    density = max(density, 1e-4);
    float2 cell = frac(uv * density) - 0.5;
    float dist = length(cell) / density;
    float aa = max(fwidth(dist), 1e-5);
    float mask = 1.0 - smoothstep(max(radius, 0.0) - aa, max(radius, 0.0) + aa, dist);
    mask = invert > 0.5h ? 1.0 - mask : mask;
    return half3(mask, mask, mask);
}

half3 OverlayBlend(half3 baseColor, half3 blendColor)
{
    half3 low = 2.0h * baseColor * blendColor;
    half3 high = 1.0h - 2.0h * (1.0h - baseColor) * (1.0h - blendColor);
    half3 useHigh = step(0.5h, baseColor);
    return lerp(low, high, useHigh);
}

void ApplyCull(float3 normalWS, float3 positionWS, float cullMode)
{
    float3 viewWS = HeldCameraPosition() - positionWS;
    float vLen = length(viewWS);
    viewWS = vLen > 1e-6 ? viewWS / vLen : float3(0.0, 0.0, 1.0);
    float facing = dot(normalWS, viewWS);
    if (cullMode > 1.5 && facing < 0.0)
        clip(-1);
    else if (cullMode > 0.5 && cullMode < 1.5 && facing > 0.0)
        clip(-1);
}

half4 Shade(
    float3 positionWS, float3 normalWS,
    Texture2D baseMap, SamplerState baseSamp, float4 baseST, half4 color,
    Texture2D noiseMap, SamplerState noiseSamp, float4 noiseST,
    Texture2D clipMap, SamplerState clipSamp, float4 clipST,
    half clipThreshold, half clipSoftness,
    half fresnelPower, half fresnelIntensity, half rimPower, half rimIntensity,
    half useDots, float dotDensity, float dotSize, half invertDots,
    float cullMode)
{
    ApplyCull(normalWS, positionWS, cullMode);

    float2 meters = CameraPlaneMeters(positionWS);
    float2 baseUV = meters * baseST.xy + baseST.zw;
    float2 noiseUV = meters * noiseST.xy + noiseST.zw;
    float2 clipUV = meters * clipST.xy + clipST.zw;

    half3 baseRgb = useDots > 0.5h
        ? ProceduralDots(baseUV, dotDensity, dotSize, invertDots)
        : SAMPLE_TEXTURE2D(baseMap, baseSamp, baseUV).rgb;
    half3 noise = SAMPLE_TEXTURE2D(noiseMap, noiseSamp, noiseUV).rgb;
    half clipNoise = SAMPLE_TEXTURE2D(clipMap, clipSamp, clipUV).r;

    float nLen = length(normalWS);
    float3 nWS = nLen > 1e-6 ? normalWS / nLen : float3(0.0, 1.0, 0.0);
    float3 viewWS = HeldCameraPosition() - positionWS;
    float vLen = length(viewWS);
    viewWS = vLen > 1e-6 ? viewWS / vLen : float3(0.0, 0.0, 1.0);
    half facing = saturate(dot(nWS, viewWS));
    half centerFresnel = pow(saturate(1.0h - facing), max(fresnelPower, 1e-4h));
    centerFresnel = lerp(1.0h, centerFresnel, saturate(fresnelIntensity));
    half rimFresnel = pow(facing, max(rimPower, 1e-4h));
    rimFresnel = lerp(1.0h, rimFresnel, saturate(rimIntensity));

    half3 overlaid = OverlayBlend(baseRgb, noise);
    half3 rgb = overlaid * color.rgb;

    half edge0 = clipThreshold - clipSoftness;
    half edge1 = max(clipThreshold + clipSoftness, edge0 + 1e-4h);
    clip(rimFresnel - edge0);
    half rimFeather = smoothstep(edge0, edge1, rimFresnel);
    half clipValue = saturate(clipNoise * centerFresnel);
    clip(clipValue - edge0);
    half noiseFeather = smoothstep(edge0, edge1, clipValue);
    half alpha = dot(overlaid, half3(0.2126h, 0.7152h, 0.0722h)) * rimFeather * noiseFeather * color.a;
    return half4(rgb, alpha);
}

VertToGeom Vert(Attributes input)
{
    VertToGeom output = (VertToGeom)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 nWS = TransformObjectToWorldNormal(input.normalOS);
    float nLen = length(nWS);
    output.normalWS = nLen > 1e-6 ? nWS / nLen : float3(0.0, 1.0, 0.0);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
    return output;
}

[maxvertexcount(12)]
void Geom(triangle VertToGeom input[3], inout TriangleStream<Varyings> stream)
{
    UNITY_SETUP_INSTANCE_ID(input[0]);
    float extrude[4] = { _NormalExtrusion, _NormalExtrusion1, _NormalExtrusion2, _NormalExtrusion3 };
    int count = (int)floor(clamp(_LayerCount, 1.0, 4.0) + 0.5);

    [unroll]
    for (int layer = 3; layer >= 0; layer--)
    {
        if (layer < count)
        {
            [unroll]
            for (int i = 0; i < 3; i++)
            {
                Varyings output = (Varyings)0;
                UNITY_TRANSFER_INSTANCE_ID(input[0], output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 posWS = input[i].positionWS + input[i].normalWS * extrude[layer];
                output.positionWS = posWS;
                output.normalWS = input[i].normalWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.layer = layer;
                stream.Append(output);
            }
            stream.RestartStrip();
        }
    }
}

half4 Frag(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    int layer = (int)input.layer;
    if (layer == 3)
        return Shade(input.positionWS, input.normalWS, _BaseMap3, sampler_BaseMap3, _BaseMap3_ST, _BaseColor3, _NoiseMap3, sampler_NoiseMap3, _NoiseMap3_ST, _ClipMap3, sampler_ClipMap3, _ClipMap3_ST, _ClipThreshold3, _ClipSoftness3, _FresnelPower3, _FresnelIntensity3, _FresnelRimPower3, _FresnelRimIntensity3, _UseDotMap3, _DotDensity3, _DotSize3, _InvertDots3, _Cull3);
    if (layer == 2)
        return Shade(input.positionWS, input.normalWS, _BaseMap2, sampler_BaseMap2, _BaseMap2_ST, _BaseColor2, _NoiseMap2, sampler_NoiseMap2, _NoiseMap2_ST, _ClipMap2, sampler_ClipMap2, _ClipMap2_ST, _ClipThreshold2, _ClipSoftness2, _FresnelPower2, _FresnelIntensity2, _FresnelRimPower2, _FresnelRimIntensity2, _UseDotMap2, _DotDensity2, _DotSize2, _InvertDots2, _Cull2);
    if (layer == 1)
        return Shade(input.positionWS, input.normalWS, _BaseMap1, sampler_BaseMap1, _BaseMap1_ST, _BaseColor1, _NoiseMap1, sampler_NoiseMap1, _NoiseMap1_ST, _ClipMap1, sampler_ClipMap1, _ClipMap1_ST, _ClipThreshold1, _ClipSoftness1, _FresnelPower1, _FresnelIntensity1, _FresnelRimPower1, _FresnelRimIntensity1, _UseDotMap1, _DotDensity1, _DotSize1, _InvertDots1, _Cull1);
    return Shade(input.positionWS, input.normalWS, _BaseMap, sampler_BaseMap, _BaseMap_ST, _BaseColor, _NoiseMap, sampler_NoiseMap, _NoiseMap_ST, _ClipMap, sampler_ClipMap, _ClipMap_ST, _ClipThreshold, _ClipSoftness, _FresnelPower, _FresnelIntensity, _FresnelRimPower, _FresnelRimIntensity, _UseDotMap, _DotDensity, _DotSize, _InvertDots, _Cull);
}

#endif

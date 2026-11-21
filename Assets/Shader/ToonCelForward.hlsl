// Specular needs a per-pixel view/half vector even when the URP asset
// requests vertex additional lights. Reuse URP's per-object light list.
#if defined(_PBR_SPECULAR) && defined(_ADDITIONAL_LIGHTS_VERTEX)
    #undef _ADDITIONAL_LIGHTS_VERTEX
    #define _ADDITIONAL_LIGHTS 1
#endif

#include "ToonCelInput.hlsl"
#include "SkinnedNormalBasis.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

#if defined(TOON_LASSYLA_SAMPLE)
#include "ToonCelEdge.hlsl"
#endif

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float2 uv1 : TEXCOORD1;
#if defined(TOON_LASSYLA_SAMPLE)
    float3 sourceGenerated : TEXCOORD2;
    float4 bakedGenerated : TEXCOORD7;
    uint vertexID : SV_VertexID;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float4 uv : TEXCOORD0; // xy: base map, zw: normal map.
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    float4 tangentWS : TEXCOORD3;
    float2 uvWrapNormal : TEXCOORD4;
#if defined(TOON_LASSYLA_SAMPLE)
    float3 generated : TEXCOORD5;
    float3 edgeNormalWS : TEXCOORD6;
#endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

half CelStep(half value, half threshold, half smoothness)
{
    half edge = max(smoothness, HALF_MIN);
    return smoothstep(threshold - edge, threshold + edge, value);
}

Varyings ToonVert(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

    output.positionCS = posInputs.positionCS;
#if defined(TOON_LASSYLA_SAMPLE)
    if (_LassylaEdgeEnabled < 0.5) {
        output.positionCS = float4(2,2,2,1);
        return output;
    }
    output.positionCS.xy += _LassylaJitter.xy * output.positionCS.w;
    output.generated = ToonGenerated(input.sourceGenerated,input.bakedGenerated,input.vertexID);
    output.edgeNormalWS = LassylaEdgeNormalWS(input.normalOS);
#elif defined(TOON_LASSYLA_COLOUR)
    if (_LassylaEdgeEnabled < 0.5) output.positionCS = float4(2,2,2,1);
#else
    if (ToonUsesLassylaCamera()) output.positionCS = float4(2,2,2,1);
#endif
    output.positionWS = posInputs.positionWS;
    output.normalWS = normalInputs.normalWS;
    output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    float2 baseUV = lerp(input.uv, input.uv1, saturate(_BaseMapUV));
    float2 wrapUV = lerp(input.uv, input.uv1, saturate(_WrapNormalUV));
    output.uv.xy = TRANSFORM_TEX(baseUV, _BaseMap);
    output.uvWrapNormal = TRANSFORM_TEX(wrapUV, _WrapNormalMap);
    output.uv.zw = TRANSFORM_TEX(input.uv, _BumpMap);
    return output;
}

half EvaluateLitMask(Light light, half3 normalWS, half threshold, half smoothness)
{
    // Lambert normalized from [-1, 1] to [0, 1] (terminator at 0.5).
    half NdotL = saturate(dot(normalWS, light.direction) * 0.5h + 0.5h);
    half attenuation = light.distanceAttenuation * light.shadowAttenuation;
    // Terminator shape comes from the normalized Lambert alone (same as Lambert + step).
    // Attenuation / shadow only gates the result afterwards, so it never shifts the threshold.
    return CelStep(NdotL, threshold, smoothness) * saturate(attenuation);
}

half3 GetShadeColor(half3 normalWS)
{
#if defined(_SHADE_FROM_AMBIENT)
    return SampleSH(normalWS);
#else
    return _ShadeColor.rgb;
#endif
}

// Main light owns shade/lit lerp so shade color is applied once.
half3 ToonLightingMain(Light light, half3 albedo, half3 normalWS, half3 shadeColor)
{
    half litMask = EvaluateLitMask(light, normalWS, _ShadeThreshold, _ShadeSmooth);
#if defined(_CEL_THREE_TONE)
    // Three tones: dark (Shade Color) -> mid (Mid Shade Color) -> lit (albedo).
    half midMask = EvaluateLitMask(light, normalWS, _MidShadeThreshold, _ShadeSmooth);
    // Darkest layer. With Shade From Ambient the dark layer is the ambient (SH) color and the extra color is
    // layered on top of it (Multiply = tint, Add = lift). Without Shade From Ambient it stays the plain Shade Color.
    half3 darkColor = shadeColor;
#if defined(_SHADE_FROM_AMBIENT)
    darkColor = lerp(shadeColor * _AmbientDarkColor.rgb, shadeColor + _AmbientDarkColor.rgb, saturate(_AmbientDarkBlend));
#endif
    half3 darkToMid = lerp(darkColor * albedo, _MidShadeColor.rgb * albedo, midMask);
    half3 diffuse = lerp(darkToMid, albedo, litMask);
#else
    half3 diffuse = lerp(shadeColor * albedo, albedo, litMask);
#endif
    return diffuse * light.color;
}

// Additional lights only add lit contribution (no shade base).
half3 ToonLightingAdditional(Light light, half3 albedo, half3 normalWS)
{
    half litMask = EvaluateLitMask(light, normalWS, _ShadeThreshold, _ShadeSmooth);
    return albedo * light.color * litMask;
}

#if defined(_PBR_SPECULAR)
float3 ToonCookTorranceSpecular(Light light, float3 normalWS, float3 viewDirWS, float3 f0)
{
    float3 lightDirWS = SafeNormalize(float3(light.direction));
    float NdotL = saturate(dot(normalWS, lightDirWS));
    float NdotV = saturate(dot(normalWS, viewDirWS));
    // No back-face highlights; also avoid the singular grazing limit.
    if (NdotL <= 1e-6 || NdotV <= 1e-6)
        return 0.0;

    float3 halfVector = lightDirWS + viewDirWS;
    float halfLengthSq = dot(halfVector, halfVector);
    if (halfLengthSq <= 1e-12)
        return 0.0;
    float3 H = halfVector * rsqrt(halfLengthSq);
    float NdotH = saturate(dot(normalWS, H));
    float VdotH = saturate(dot(viewDirWS, H));

    // Perceptual roughness -> GGX alpha. Keep all BRDF math in float:
    // sharp GGX peaks and grazing denominators exceed half precision.
    float roughness = clamp((float)_SpecularRoughness, 0.05, 1.0);
    float alpha = roughness * roughness;
    float alphaSq = alpha * alpha;
    float NdotHSq = NdotH * NdotH;
    float d = (1.0 - NdotHSq) + NdotHSq * alphaSq;
    float D = alphaSq / (PI * d * d);

    // Exact height-correlated Smith-GGX masking-shadowing:
    // G = 1 / (1 + Lambda(L) + Lambda(V)), without Schlick-G shortcuts.
    float smithL = sqrt(alphaSq + (1.0 - alphaSq) * NdotL * NdotL);
    float smithV = sqrt(alphaSq + (1.0 - alphaSq) * NdotV * NdotV);
    float G = (2.0 * NdotL * NdotV) / (NdotV * smithL + NdotL * smithV);

    // Schlick's RGB Fresnel, evaluated with V.H (not N.H).
    float oneMinusVdotH = 1.0 - VdotH;
    float fresnel2 = oneMinusVdotH * oneMinusVdotH;
    float fresnel5 = fresnel2 * fresnel2 * oneMinusVdotH;
    float3 F = f0 + (1.0 - f0) * fresnel5;

    // Complete Cook-Torrance BRDF: D * F * G / (4 * N.L * N.V).
    float3 specularBRDF = (D * G * F) / (4.0 * NdotL * NdotV);

    // D / D(N.H=1) is a bounded, roughness-dependent lobe shape.
    // Only mask the full BRDF; never replace its Fresnel or geometry.
    // Light intensity, distance and shadows cannot move this contour.
    float normalizedLobe = saturate((alphaSq * alphaSq) / (d * d));
    float edge = max((float)_SpecularSmooth, 1e-5);
    float shapeMask = smoothstep(_SpecularThreshold - edge,
        _SpecularThreshold + edge, normalizedLobe);
    shapeMask = lerp(1.0, shapeMask, saturate(_SpecularShapeAmount));

    float attenuation = light.distanceAttenuation * light.shadowAttenuation;
    return specularBRDF * NdotL * float3(light.color) * attenuation
        * shapeMask * max((float)_SpecularIntensity, 0.0);
}
#endif

void InitializeToonInputData(Varyings input, half3 normalWS, half3 viewDirWS, out InputData inputData)
{
    inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.positionCS = input.positionCS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = viewDirWS;
    inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = half4(1.0h, 1.0h, 1.0h, 1.0h);
}

half4 ShadeToonSurface(Varyings input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half3 normalTS = UnpackNormalScale(
        SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv.zw), _BumpScale);
    // Reconstruct the signed tangent basis before evaluating any lighting.
    float3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
    half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(
        normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS)));
    bool sampleModelNormal = _CelModelNormalEnabled > 0.5h;
#if defined(_PBR_SPECULAR)
    // Specular always uses the model-normal atlas, independently of
    // the cel diffuse toggle. Invalid atlas texels fall back to geometry.
    float3 specularNormalWS = NormalizeNormalPerPixel(input.normalWS);
    sampleModelNormal = true;
#endif

    // Decode the shared linear RGB atlas once for specular and optional
    // cel diffuse. Never use UnpackNormal on this model-normal texture.
    if (sampleModelNormal)
    {
        float3 geometricNormalWS = NormalizeNormalPerPixel(input.normalWS);
        float3 normalOS = SAMPLE_TEXTURE2D(_WrapNormalMap, sampler_WrapNormalMap, input.uvWrapNormal).rgb * 2.0 - 1.0;
        if (dot(normalOS, normalOS) > 1e-4)
        {
            // Default: mesh-space (object-space) normals -> valid for the bind pose only.
            // _WrapNormalTangentSpace = 1: map holds the normal expressed in the ORIGINAL vertex TBN,
            // rebuilt with the skinned TBN so it follows the animation.
            float3 mapNormalWS;
            if (_WrapNormalTangentSpace > 0.5h)
                mapNormalWS = ReviewedNormalTangentToWorld(normalize(normalOS), input.normalWS, input.tangentWS);
            else
                mapNormalWS = TransformObjectToWorldNormal(normalize(normalOS));
            float3 modelNormalWS = SafeNormalize(lerp(geometricNormalWS,
                SafeNormalize(mapNormalWS), saturate(_WrapNormalStrength)));
            if (_CelModelNormalEnabled > 0.5h)
                normalWS = modelNormalWS;
        #if defined(_PBR_SPECULAR)
            specularNormalWS = modelNormalWS;
        #endif
        }
    }

#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 mainShadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
#else
    float4 mainShadowCoord = TransformWorldToShadowCoord(input.positionWS);
#endif
    Light mainLight = GetMainLight(mainShadowCoord);

    half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy);
    half3 albedo = baseSample.rgb * _BaseColor.rgb;
    half alpha = baseSample.a * _BaseColor.a;

    half3 shadeColor = GetShadeColor(normalWS);
    float3 color = ToonLightingMain(mainLight, albedo, normalWS, shadeColor);
    color += SampleSH(normalWS) * albedo * _AmbientStrength;

#if defined(_PBR_SPECULAR) || defined(_ADDITIONAL_LIGHTS)
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#endif
#if defined(_PBR_SPECULAR)
    float3 specularF0 = lerp(saturate(_SpecularF0.rgb), saturate(albedo), saturate(_SpecularMetallic));
    // Additive artistic layer: the existing cel diffuse is preserved.
    color += ToonCookTorranceSpecular(mainLight, specularNormalWS, viewDirWS, specularF0);
#endif

#if defined(_ADDITIONAL_LIGHTS)
    InputData inputData;
    InitializeToonInputData(input, normalWS, viewDirWS, inputData);
    half4 shadowMask = inputData.shadowMask;
    AmbientOcclusionFactor aoFactor = (AmbientOcclusionFactor)0;
    aoFactor.directAmbientOcclusion = 1.0h;
    aoFactor.indirectAmbientOcclusion = 1.0h;

    uint pixelLightCount = GetAdditionalLightsCount();

#if USE_FORWARD_PLUS
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        color += ToonLightingAdditional(light, albedo, normalWS);
    #if defined(_PBR_SPECULAR)
        color += ToonCookTorranceSpecular(light, specularNormalWS, viewDirWS, specularF0);
    #endif
    }
#endif

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
        color += ToonLightingAdditional(light, albedo, normalWS);
    #if defined(_PBR_SPECULAR)
        color += ToonCookTorranceSpecular(light, specularNormalWS, viewDirWS, specularF0);
    #endif
    LIGHT_LOOP_END
#endif

    // Keep very sharp/HDR highlights finite on native-half backends.
    return half4(min(color, (float)HALF_MAX), alpha);
}

#if defined(TOON_LASSYLA_SAMPLE)
void ClipToonEdge(Varyings input,bool frontFace)
{
    ClipToonCelEdge(input.generated, input.edgeNormalWS, input.positionWS, frontFace,
        _LassylaJitter.z > 0 ? 1.0 : _HashScale);
}
float4 ToonEdgeFrag(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    LassylaClipSceneDepth(input.positionCS);
    ClipToonEdge(input,IS_FRONT_VFACE(face,true,false));
    return 1; // The film receives binary coverage, never ToonCel lighting.
}
float4 ToonEdgeDepth(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    ClipToonEdge(input,IS_FRONT_VFACE(face,true,false));
    return 0;
}
#else
half4 ToonFrag(Varyings input) : SV_Target { return ShadeToonSurface(input); }
#endif

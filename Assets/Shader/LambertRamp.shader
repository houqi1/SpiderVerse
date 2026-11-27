// Normalized (half) Lambert -> gradient lookup, plus a soft rim light.
// _RampMap is a 1D gradient texture (baked from the Gradient editor in the material inspector).
Shader "Custom/LambertRamp"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [Enum(UV0, 0, UV1, 1)] _BaseMapUV ("Base Map UV", Float) = 1
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Debug)]
        [Toggle(_OUTPUT_NORMAL)] _OutputNormal ("Output World Normal", Float) = 0
        [Toggle(_OUTPUT_LAMBERT)] _OutputLambert ("Output Pure Lambert (ignore all other effects)", Float) = 0

        [Header(Gradient)]
        [NoScaleOffset] _RampMap ("Gradient (U = normalized lambert)", 2D) = "white" {}

        [Header(Channel Color Blend)]
        _MaskMap ("Intensity Map (R channel)", 2D) = "white" {}
        _MaskStrength ("Blend Strength", Range(0, 1)) = 1
        _ColorMaskMap ("Color Select Map (RGBA = colors 1-4)", 2D) = "black" {}
        _ColorR ("Color 1 (R channel)", Color) = (1, 0, 0, 1)
        _ColorG ("Color 2 (G channel)", Color) = (0, 1, 0, 1)
        _ColorB ("Color 3 (B channel)", Color) = (0, 0, 1, 1)
        _ColorA ("Color 4 (A channel)", Color) = (1, 1, 0, 1)

        [Header(Solid Color Mask)]
        _SolidMask ("Solid Mask (R channel, white = solid colour)", 2D) = "black" {}
        _SolidColor ("Solid Color (x base colour)", Color) = (1, 1, 1, 1)

        [Header(Rim Light)]
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimIntensity ("Rim Intensity", Range(0, 5)) = 1
        _RimPower ("Rim Power (higher = thinner)", Range(0.5, 10)) = 3
        _RimThreshold ("Rim Threshold", Range(0, 1)) = 0.2
        _RimSmooth ("Rim Smoothness (larger = softer falloff)", Range(0.01, 1)) = 0.2
        _RimMap ("Rim Texture (R channel, multiplied onto rim)", 2D) = "white" {}
        _RimMapContrast ("Rim Texture Contrast", Range(0, 4)) = 1
        [Enum(UV0, 0, UV1, 1)] _RimMapUV ("Rim Texture UV", Float) = 1
        _RimMapStrength ("Rim Texture Strength (0 = ignore, 1 = full multiply)", Range(0, 1)) = 1

        [Header(Rim Light 2)]
        _Rim2Color ("Rim 2 Color", Color) = (1, 1, 1, 1)
        _Rim2Intensity ("Rim 2 Intensity (0 = off)", Range(0, 5)) = 0
        _Rim2Power ("Rim 2 Power (higher = thinner)", Range(0.5, 10)) = 3
        _Rim2Threshold ("Rim 2 Threshold", Range(0, 1)) = 0.2
        _Rim2Smooth ("Rim 2 Smoothness (larger = softer falloff)", Range(0.01, 1)) = 0.2
        _Rim2Map ("Rim 2 Texture (R channel, multiplied onto rim 2)", 2D) = "white" {}
        [Enum(UV0, 0, UV1, 1)] _Rim2MapUV ("Rim 2 Texture UV", Float) = 1
        _Rim2MapStrength ("Rim 2 Texture Strength (0 = ignore, 1 = full multiply)", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

        TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
        TEXTURE2D(_RampMap);
        TEXTURE2D(_MaskMap);    SAMPLER(sampler_MaskMap);
        TEXTURE2D(_ColorMaskMap); SAMPLER(sampler_ColorMaskMap);
        TEXTURE2D(_SolidMask);  SAMPLER(sampler_SolidMask);
        TEXTURE2D(_RimMap);     SAMPLER(sampler_RimMap);
        TEXTURE2D(_Rim2Map);    SAMPLER(sampler_Rim2Map);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4  _BaseColor;
            half   _BaseMapUV;
            half   _Cutoff;
            float4 _MaskMap_ST;
            float4 _ColorMaskMap_ST;
            float4 _SolidMask_ST;
            half4  _SolidColor;
            half   _MaskStrength;
            half4  _ColorR, _ColorG, _ColorB, _ColorA;
            half4  _RimColor;
            half   _RimIntensity;
            half   _RimPower;
            half   _RimThreshold;
            half   _RimSmooth;
            float4 _RimMap_ST;
            half   _RimMapContrast;
            half   _RimMapUV;
            half   _RimMapStrength;
            half4  _Rim2Color;
            half   _Rim2Intensity;
            half   _Rim2Power;
            half   _Rim2Threshold;
            half   _Rim2Smooth;
            float4 _Rim2Map_ST;
            half   _Rim2MapUV;
            half   _Rim2MapStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma shader_feature_local_fragment _OUTPUT_NORMAL
            #pragma shader_feature_local_fragment _OUTPUT_LAMBERT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                float2 maskUV     : TEXCOORD3;
                float2 colorMaskUV : TEXCOORD4;
                float2 solidUV    : TEXCOORD5;
                float2 rimUV      : TEXCOORD6;
                float2 rim2UV     : TEXCOORD7;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS   = TransformObjectToWorldNormal(input.normalOS);
                float2 baseUV = lerp(input.uv, input.uv1, saturate(_BaseMapUV));
                o.uv         = TRANSFORM_TEX(baseUV, _BaseMap);
                o.maskUV     = TRANSFORM_TEX(baseUV, _MaskMap);
                o.colorMaskUV = TRANSFORM_TEX(baseUV, _ColorMaskMap);
                o.solidUV    = TRANSFORM_TEX(baseUV, _SolidMask);
                o.rimUV      = TRANSFORM_TEX(lerp(input.uv, input.uv1, saturate(_RimMapUV)), _RimMap);
                o.rim2UV     = TRANSFORM_TEX(lerp(input.uv, input.uv1, saturate(_Rim2MapUV)), _Rim2Map);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 normalWS = normalize(input.normalWS);
            #if defined(_OUTPUT_NORMAL)
                return half4(normalWS * 0.5 + 0.5, 1);   // world-space normal as colour
            #endif
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor;
                Light mainLight = GetMainLight();

                // Normalized (half) Lambert -> gradient
                half lambert = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
            #if defined(_OUTPUT_LAMBERT)
                half pureLambert = saturate(dot(normalWS, mainLight.direction));   // plain N.L, no 0.5 remap
                return half4(pureLambert.xxx, 1);   // no gradient / blend / rim
            #endif
                half3 ramp = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_LinearClamp, float2(lambert, 0.5), 0).rgb;

                // Intensity map (R) x strength = how far the base colour moves;
                // colour select map (RGBA) = which of the four colours it moves towards.
                half intensity = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, input.maskUV).r * _MaskStrength;
                half4 sel = SAMPLE_TEXTURE2D(_ColorMaskMap, sampler_ColorMaskMap, input.colorMaskUV) * intensity;
                half3 baseRGB = albedo.rgb;
                baseRGB = lerp(baseRGB, _ColorR.rgb, sel.r);
                baseRGB = lerp(baseRGB, _ColorG.rgb, sel.g);
                baseRGB = lerp(baseRGB, _ColorB.rgb, sel.b);
                baseRGB = lerp(baseRGB, _ColorA.rgb, sel.a);

                half3 color = baseRGB * ramp;

            #if defined(_ADDITIONAL_LIGHTS)
                // Additional lights: each one runs through the same gradient with its own N.L,
                // tinted by its colour and faded by distance / spot attenuation and shadow.
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half4 shadowMask = half4(1, 1, 1, 1);
                AmbientOcclusionFactor aoFactor = (AmbientOcclusionFactor)0;
                aoFactor.directAmbientOcclusion = 1;
                aoFactor.indirectAmbientOcclusion = 1;

                uint pixelLightCount = GetAdditionalLightsCount();
            #if USE_FORWARD_PLUS
                [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                    Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                    half l = saturate(dot(normalWS, light.direction) * 0.5 + 0.5);
                    half3 r = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_LinearClamp, float2(l, 0.5), 0).rgb;
                    color += baseRGB * r * light.color * saturate(light.distanceAttenuation * light.shadowAttenuation * 4.0);
                }
            #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                    half l = saturate(dot(normalWS, light.direction) * 0.5 + 0.5);
                    half3 r = SAMPLE_TEXTURE2D_LOD(_RampMap, sampler_LinearClamp, float2(l, 0.5), 0).rgb;
                    color += baseRGB * r * light.color * saturate(light.distanceAttenuation * light.shadowAttenuation * 4.0);
                LIGHT_LOOP_END
            #endif

                // Soft fresnel rim
                half3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                half fres = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _RimPower);
                half rim = smoothstep(max(_RimThreshold - _RimSmooth, 0.0), _RimThreshold + _RimSmooth, fres);
                half rimTex = SAMPLE_TEXTURE2D(_RimMap, sampler_RimMap, input.rimUV).r;
                rimTex = saturate(rimTex + (rimTex - 0.5h) * (_RimMapContrast - 1.0h));
                rim *= lerp(1.0h, rimTex, _RimMapStrength);
                color += _RimColor.rgb * rim * _RimIntensity;

                // Second rim layer (independent colour / shape / texture)
                half fres2 = pow(1.0 - saturate(dot(normalWS, viewDirWS)), _Rim2Power);
                half rim2 = smoothstep(max(_Rim2Threshold - _Rim2Smooth, 0.0), _Rim2Threshold + _Rim2Smooth, fres2);
                half rim2Tex = SAMPLE_TEXTURE2D(_Rim2Map, sampler_Rim2Map, input.rim2UV).r;
                rim2 *= lerp(1.0h, rim2Tex, _Rim2MapStrength);
                color += _Rim2Color.rgb * rim2 * _Rim2Intensity;

                // Solid colour mask: where white, output only SolidColor x base colour (no shading / blend / rim)
                half solid = SAMPLE_TEXTURE2D(_SolidMask, sampler_SolidMask, input.solidUV).r;
                color = lerp(color, _SolidColor.rgb * albedo.rgb, solid);

                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
    CustomEditor "LambertRampGUI"
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}

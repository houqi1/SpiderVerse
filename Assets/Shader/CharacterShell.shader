Shader "Custom/CharacterShell"
{
    Properties
    {
        [MainTexture] _BaseMap ("Map", 2D) = "white" {}
        [MainColor][HDR] _BaseColor ("Tint", Color) = (1, 1, 1, 1)

        [Header(Noise)]
        _NoiseMap ("Noise", 2D) = "gray" {}
        _ClipMap ("Clip Noise", 2D) = "white" {}
        _ClipThreshold ("Clip Threshold", Range(0, 1)) = 0.5
        _ClipSoftness ("Clip Softness", Range(0, 1)) = 0.1

        [Header(Fresnel)]
        _FresnelPower ("Fresnel Power", Range(0.01, 8)) = 2
        _FresnelIntensity ("Fresnel Intensity", Range(0, 1)) = 1
        _FresnelRimPower ("Rim Fresnel Power", Range(0.01, 8)) = 2
        _FresnelRimIntensity ("Rim Fresnel Intensity", Range(0, 1)) = 0

        [Header(Extrusion)]
        _NormalExtrusion ("Normal Extrusion", Float) = 0

        [Header(Surface)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NoiseMap);
            SAMPLER(sampler_NoiseMap);
            TEXTURE2D(_ClipMap);
            SAMPLER(sampler_ClipMap);

            CBUFFER_START(UnityPerMaterial)
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
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Camera yaw only. Pitch and roll leave the axes unchanged.
            // Meters from the object origin. X is horizontal camera right, Y is world up.
            float2 YawFacingMeters(float3 positionWS)
            {
                float3 camFwd = -normalize(((float3x3)UNITY_MATRIX_V)[2]);
                float3 flat = float3(camFwd.x, 0.0, camFwd.z);
                float flatLen = length(flat);
                // Straight up or down has no yaw. Freeze to world forward so the map does not spin.
                float3 fwd = flatLen > 1e-4 ? flat / flatLen : float3(0.0, 0.0, 1.0);
                float3 right = normalize(cross(float3(0.0, 1.0, 0.0), fwd));

                float3 anchorWS = UNITY_MATRIX_M._m03_m13_m23;
                float3 rel = positionWS - anchorWS;
                return float2(dot(rel, right), rel.y);
            }

            half3 OverlayBlend(half3 baseColor, half3 blendColor)
            {
                half3 low = 2.0h * baseColor * blendColor;
                half3 high = 1.0h - 2.0h * (1.0h - baseColor) * (1.0h - blendColor);
                half3 useHigh = step(0.5h, baseColor);
                return lerp(low, high, useHigh);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(input.normalOS);
                float nLen = length(nWS);
                nWS = nLen > 1e-6 ? nWS / nLen : float3(0.0, 1.0, 0.0);
                posWS += nWS * _NormalExtrusion;

                output.positionWS = posWS;
                output.normalWS = nWS;
                output.positionCS = TransformWorldToHClip(posWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 meters = YawFacingMeters(input.positionWS);
                float2 baseUV = meters * _BaseMap_ST.xy + _BaseMap_ST.zw;
                float2 noiseUV = meters * _NoiseMap_ST.xy + _NoiseMap_ST.zw;
                float2 clipUV = meters * _ClipMap_ST.xy + _ClipMap_ST.zw;

                half3 baseRgb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, baseUV).rgb;
                half3 noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseUV).rgb;
                half clipNoise = SAMPLE_TEXTURE2D(_ClipMap, sampler_ClipMap, clipUV).r;

                float3 nWS = input.normalWS;
                float nLen = length(nWS);
                nWS = nLen > 1e-6 ? nWS / nLen : float3(0.0, 1.0, 0.0);
                float3 viewWS = _WorldSpaceCameraPos.xyz - input.positionWS;
                float vLen = length(viewWS);
                viewWS = vLen > 1e-6 ? viewWS / vLen : float3(0.0, 0.0, 1.0);
                half facing = saturate(dot(nWS, viewWS));
                // Facing the camera is 0, so the center drops out first.
                half centerFresnel = pow(saturate(1.0h - facing), max(_FresnelPower, 1e-4h));
                centerFresnel = lerp(1.0h, centerFresnel, saturate(_FresnelIntensity));
                // 1 facing the camera, 0 at the silhouette. Clipped on its own, before the noise.
                half rimFresnel = pow(facing, max(_FresnelRimPower, 1e-4h));
                rimFresnel = lerp(1.0h, rimFresnel, saturate(_FresnelRimIntensity));

                half3 overlaid = OverlayBlend(baseRgb, noise);
                half3 rgb = overlaid * _BaseColor.rgb;

                half edge0 = _ClipThreshold - _ClipSoftness;
                half edge1 = max(_ClipThreshold + _ClipSoftness, edge0 + 1e-4h);

                clip(rimFresnel - edge0);
                half rimFeather = smoothstep(edge0, edge1, rimFresnel);

                half clipValue = saturate(clipNoise * centerFresnel);
                clip(clipValue - edge0);
                half noiseFeather = smoothstep(edge0, edge1, clipValue);

                half alpha = dot(overlaid, half3(0.2126h, 0.7152h, 0.0722h)) * rimFeather * noiseFeather * _BaseColor.a;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

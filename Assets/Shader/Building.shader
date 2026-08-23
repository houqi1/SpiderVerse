Shader "Custom/Building"
{
    Properties
    {
        [Header(Axis Colors)]
        _ColorX ("Color X (World)", Color) = (0.85, 0.75, 0.65, 1)
        _ColorY ("Color Y (World)", Color) = (0.95, 0.95, 0.9, 1)
        _ColorZ ("Color Z (World)", Color) = (0.7, 0.78, 0.85, 1)

        [Header(Triplanar)]
        _TriplanarMap ("Triplanar Map", 2D) = "white" {}
        _TriplanarSharpness ("Triplanar Sharpness", Range(1, 8)) = 4
        _DarkOverlayStrength ("Dark Overlay Strength", Range(0, 1)) = 0.75

        [Header(Shading)]
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.35
        _ShadeSmooth ("Shade Smooth", Range(0, 0.5)) = 0.05
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.35

        [Header(Surface)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex BuildingVert
            #pragma fragment BuildingFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "BuildingInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half CelStep(half value, half threshold, half smoothness)
            {
                half edge = max(smoothness, HALF_MIN);
                return smoothstep(threshold - edge, threshold + edge, value);
            }

            // Blend three axis colors by world-space normal orientation.
            half3 AxisBlendColor(float3 normalWS)
            {
                float3 weights = abs(SafeNormalize(normalWS));
                weights = pow(weights, _TriplanarSharpness);
                weights /= max(weights.x + weights.y + weights.z, 1e-5);
                return weights.x * _ColorX.rgb + weights.y * _ColorY.rgb + weights.z * _ColorZ.rgb;
            }

            // World-space triplanar sample.
            half4 TriplanarSample(float3 positionWS, float3 normalWS)
            {
                float2 uvX = positionWS.zy * _TriplanarMap_ST.xy + _TriplanarMap_ST.zw;
                float2 uvY = positionWS.xz * _TriplanarMap_ST.xy + _TriplanarMap_ST.zw;
                float2 uvZ = positionWS.xy * _TriplanarMap_ST.xy + _TriplanarMap_ST.zw;

                half4 texX = SAMPLE_TEXTURE2D(_TriplanarMap, sampler_TriplanarMap, uvX);
                half4 texY = SAMPLE_TEXTURE2D(_TriplanarMap, sampler_TriplanarMap, uvY);
                half4 texZ = SAMPLE_TEXTURE2D(_TriplanarMap, sampler_TriplanarMap, uvZ);

                float3 weights = abs(SafeNormalize(normalWS));
                weights = pow(weights, _TriplanarSharpness);
                weights /= max(weights.x + weights.y + weights.z, 1e-5);

                return texX * weights.x + texY * weights.y + texZ * weights.z;
            }

            // Overlay blend: keeps highlight structure while laying detail into shade.
            half3 OverlayBlend(half3 baseColor, half3 blendColor)
            {
                half3 low = 2.0h * baseColor * blendColor;
                half3 high = 1.0h - 2.0h * (1.0h - baseColor) * (1.0h - blendColor);
                half3 useHigh = step(0.5h, baseColor);
                return lerp(low, high, useHigh);
            }

            Varyings BuildingVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 BuildingFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 axisColor = AxisBlendColor(normalWS);
                half4 triplanar = TriplanarSample(input.positionWS, normalWS);

                half NdotL = saturate(dot(normalWS, mainLight.direction));
                half attenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                half litMask = CelStep(NdotL * attenuation, _ShadeThreshold, _ShadeSmooth);
                half shadeMask = 1.0h - litMask;

                // Lit side: axis colors. Dark side: overlay triplanar detail on axis colors.
                half3 darkColor = OverlayBlend(axisColor, triplanar.rgb);
                darkColor = lerp(axisColor, darkColor, _DarkOverlayStrength);
                half3 albedo = lerp(darkColor, axisColor, litMask);

                half3 color = albedo * mainLight.color * litMask;
                color += SampleSH(normalWS) * albedo * _AmbientStrength;
                // Keep some albedo in shade so overlay remains visible.
                color += albedo * shadeMask * mainLight.color * 0.15h;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "BuildingInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "BuildingInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}

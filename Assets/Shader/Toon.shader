Shader "Custom/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Cel Diffuse)]
        _ShadeColor ("Shade Color", Color) = (0.4, 0.4, 0.5, 1)
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.45
        _ShadeSmooth ("Shade Smooth", Range(0, 0.5)) = 0.02

        [Header(Specular)]
        [HDR] _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _SpecularSize ("Specular Size", Range(2, 256)) = 64
        _SpecularThreshold ("Specular Threshold", Range(0, 1)) = 0.5
        _SpecularSmooth ("Specular Smooth", Range(0, 0.5)) = 0.02

        [Header(Ambient)]
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.35

        [Header(Lambert Perturb)]
        [Toggle(_LAMBERT_PERTURB_ON)] _LambertPerturbOn ("Enable Lambert Perturb", Float) = 0
        [Toggle(_LAMBERT_PERTURB_DEBUG)] _LambertPerturbDebug ("Output Perturb Map", Float) = 0
        [KeywordEnum(World, Object)] _LambertPerturbSpace ("Perturb Space", Float) = 0
        [KeywordEnum(Add, Multiply)] _LambertPerturbMode ("Perturb Mode", Float) = 0
        [Toggle(_LAMBERT_PERTURB_SWAP_UV)] _LambertPerturbSwapUV ("Swap Perturb UV", Float) = 0
        _LambertPerturbMap ("Perturb Map", 2D) = "gray" {}
        _LambertPerturbStrength ("Perturb Strength", Range(0, 10)) = 0.5
        _LambertPerturbWidth ("Perturb Width", Range(0.01, 1)) = 0.2

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
            #pragma vertex ToonVert
            #pragma fragment ToonFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _LAMBERT_PERTURB_ON
            #pragma shader_feature_local _LAMBERT_PERTURB_DEBUG
            #pragma shader_feature_local _LAMBERTPERTURBSPACE_WORLD _LAMBERTPERTURBSPACE_OBJECT
            #pragma shader_feature_local _LAMBERTPERTURBMODE_ADD _LAMBERTPERTURBMODE_MULTIPLY
            #pragma shader_feature_local _LAMBERT_PERTURB_SWAP_UV

            #include "ToonInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half CelStep(half value, half threshold, half smoothness)
            {
                half edge = max(smoothness, HALF_MIN);
                return smoothstep(threshold - edge, threshold + edge, value);
            }

            // U = Lambert (N·L). V = azimuth of N around light axis on the ⊥L plane.
            float2 LightTerminatorUV(float3 normal, float3 lightDir)
            {
                float3 N = SafeNormalize(normal);
                float3 L = SafeNormalize(lightDir);

                float3 upRef = abs(L.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                float3 axisT = SafeNormalize(cross(upRef, L));
                float3 axisB = cross(L, axisT);

                float u = saturate(dot(N, L));
                float v = atan2(dot(N, axisB), dot(N, axisT)) / TWO_PI + 0.5;

                float2 uv = float2(u, v);
            #if defined(_LAMBERT_PERTURB_SWAP_UV)
                uv = uv.yx;
            #endif
                return uv * _LambertPerturbMap_ST.xy + _LambertPerturbMap_ST.zw;
            }

            half4 SampleLambertPerturbMap(float3 normalWS, float3 lightDirWS)
            {
                float2 perturbUV;
            #if defined(_LAMBERTPERTURBSPACE_OBJECT)
                perturbUV = LightTerminatorUV(
                    TransformWorldToObjectNormal(normalWS),
                    TransformWorldToObjectDir(lightDirWS));
            #else
                perturbUV = LightTerminatorUV(normalWS, lightDirWS);
            #endif
                return SAMPLE_TEXTURE2D(_LambertPerturbMap, sampler_LambertPerturbMap, perturbUV);
            }

            // Only affect a band around the cel shade threshold.
            half TerminatorBandMask(half NdotL)
            {
                half width = max(_LambertPerturbWidth, HALF_MIN);
                half dist = abs(NdotL - _ShadeThreshold);
                return saturate(1.0h - dist / width);
            }

            // Perturb the post-cel (binarized) lit mask. The sample itself is not cel-filtered.
            half ApplyLitMaskPerturb(half litMask, half NdotL, half perturbSample)
            {
                half mask = TerminatorBandMask(NdotL);
                half amount = _LambertPerturbStrength * mask;

            #if defined(_LAMBERTPERTURBMODE_MULTIPLY)
                litMask *= lerp(1.0h, perturbSample, amount);
            #else
                // Add: gray(0.5) is neutral; brighter pushes lit, darker pushes shade.
                litMask += (perturbSample - 0.5h) * amount;
            #endif
                return saturate(litMask);
            }

            Varyings ToonVert(Attributes input)
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
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half3 ToonLighting(Light light, half3 albedo, half3 normalWS, half3 viewDirWS)
            {
                half NdotL = saturate(dot(normalWS, light.direction));
                half attenuation = light.distanceAttenuation * light.shadowAttenuation;

                // Binarize Lambert first, then perturb the cel result (perturb bypasses CelStep).
                half litMask = CelStep(NdotL * attenuation, _ShadeThreshold, _ShadeSmooth);

            #if defined(_LAMBERT_PERTURB_ON)
                half perturbSample = SampleLambertPerturbMap(normalWS, light.direction).r;
                litMask = ApplyLitMaskPerturb(litMask, NdotL, perturbSample);
            #endif

                half3 diffuse = lerp(_ShadeColor.rgb * albedo, albedo, litMask);

                half3 halfDir = SafeNormalize(light.direction + viewDirWS);
                half NdotH = saturate(dot(normalWS, halfDir));
                half spec = pow(NdotH, max(_SpecularSize, 1.0h));
                spec = CelStep(spec * attenuation, _SpecularThreshold, _SpecularSmooth);

                half3 color = diffuse * light.color;
                color += _SpecularColor.rgb * light.color * spec;
                return color;
            }

            half4 ToonFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

            #if defined(_LAMBERT_PERTURB_DEBUG)
                // Debug: directly output the terminator-mapped Perturb Map.
                return SampleLambertPerturbMap(normalWS, mainLight.direction);
            #else
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = baseSample.rgb * _BaseColor.rgb;
                half alpha = baseSample.a * _BaseColor.a;

                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 color = ToonLighting(mainLight, albedo, normalWS, viewDirWS);
                color += SampleSH(normalWS) * albedo * _AmbientStrength;
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            #endif
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

            #include "ToonInput.hlsl"
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

            #include "ToonInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}

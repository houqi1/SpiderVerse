Shader "Custom/ObjectSpaceNormalWrapLambert"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Object Space Normal)]
        _ObjectNormalMap ("Object Space Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 1)) = 1
        [Toggle] _NormalTangentSpace ("Normal Map Is Tangent Space (Skinned)", Float) = 0
        [Toggle(_FLIP_Y)] _FlipY ("Flip Normal Y", Float) = 0
        [Toggle(_FLIP_Z)] _FlipZ ("Flip Normal Z", Float) = 0

        [Header(Wrap Lambert)]
        _Wrap ("Wrap", Range(0, 1)) = 0.5
        _ShadowColor ("Shadow Tint", Color) = (0.6, 0.6, 0.7, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #pragma shader_feature_local _FLIP_Y
            #pragma shader_feature_local _FLIP_Z
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SkinnedNormalBasis.hlsl"

            TEXTURE2D(_BaseMap);         SAMPLER(sampler_BaseMap);
            TEXTURE2D(_ObjectNormalMap); SAMPLER(sampler_ObjectNormalMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _ObjectNormalMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half _NormalStrength;
                half _NormalTangentSpace;
                half _Wrap;
                half _AmbientStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv1        : TEXCOORD1; // UV1(第二套UV)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2; // 几何法线，作为贴图强度为0时的回退
                float4 tangentWS  : TEXCOORD3;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.positionWS = p.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.tangentWS = float4(TransformObjectToWorldDir(IN.tangentOS.xyz), IN.tangentOS.w * GetOddNegativeScale());
                OUT.uv = IN.uv1;
                return OUT;
            }

            // wrap lambert: (NdotL + w) / (1 + w)
            half WrapLambert(half3 N, half3 L, half w)
            {
                return saturate((dot(N, L) + w) / (1.0h + w));
            }

            half3 ShadeLight(Light l, half3 N)
            {
                half diff = WrapLambert(N, l.direction, _Wrap);
                return l.color * diff * l.distanceAttenuation * l.shadowAttenuation;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(IN.uv, _BaseMap)) * _BaseColor;

                // 物体空间法线: rgb [0,1] -> [-1,1]，不使用 UnpackNormal（那是切线空间）
                half3 nOS = SAMPLE_TEXTURE2D(_ObjectNormalMap, sampler_ObjectNormalMap,
                                             TRANSFORM_TEX(IN.uv, _ObjectNormalMap)).rgb * 2.0h - 1.0h;
                #if defined(_FLIP_Y)
                    nOS.y = -nOS.y;
                #endif
                #if defined(_FLIP_Z)
                    nOS.z = -nOS.z;
                #endif
                nOS = normalize(nOS);

                // 物体空间 -> 世界空间（逆转置矩阵，支持非均匀缩放）
                half3 nWS = _NormalTangentSpace > 0.5h
                    ? ReviewedNormalTangentToWorld(nOS, IN.normalWS, IN.tangentWS)
                    : normalize(TransformObjectToWorldNormal(nOS));
                half3 N = normalize(lerp(normalize(IN.normalWS), nWS, _NormalStrength));

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 diffuse = ShadeLight(mainLight, N);

                #if defined(_ADDITIONAL_LIGHTS)
                    uint count = GetAdditionalLightsCount();
                    for (uint i = 0u; i < count; i++)
                    {
                        Light l = GetAdditionalLight(i, IN.positionWS);
                        diffuse += ShadeLight(l, N);
                    }
                #endif

                half3 ambient = SampleSH(N) * _AmbientStrength;

                // 阴影侧染色：按主光 wrap 值从 shadow tint 过渡到白
                half wrapTerm = WrapLambert(N, mainLight.direction, _Wrap);
                half3 tint = lerp(_ShadowColor.rgb, half3(1, 1, 1), saturate(wrapTerm * mainLight.shadowAttenuation));

                half3 col = albedo.rgb * (diffuse + ambient) * tint;
                return half4(col, albedo.a);
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
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float3 positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE * cs.w);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE * cs.w);
                #endif
                OUT.positionCS = cs;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}

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
        [Toggle(_SHADE_FROM_AMBIENT)] _ShadeFromAmbient ("Shade From Ambient", Float) = 0

        [Header(Wrap Toon Layer)]
        _WrapShadeColor ("Wrap Shade Color", Color) = (0.4, 0.4, 0.5, 1)
        _WrapShadeThreshold ("Wrap Shade Threshold", Range(0, 1)) = 0.45
        _WrapShadeSmooth ("Wrap Shade Smooth", Range(0, 0.5)) = 0.02
        _WrapAmount ("Wrap Amount", Range(0, 1)) = 0.5
        _WrapToonStrength ("Wrap Toon Strength", Range(0, 1)) = 0
        [Toggle(_OUTPUT_WRAP_TOON)] _OutputWrapToon ("Output Wrap Toon", Float) = 0

        [Header(Ambient)]
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.35

        [Header(UV Map)]
        _UVMap ("UV Map", 2D) = "white" {}
        _OverlayColorA ("UV Map Color A", Color) = (0, 0, 0, 1)
        _OverlayColorB ("UV Map Color B", Color) = (1, 1, 1, 1)
        [KeywordEnum(Multiply, Add)] _OverlayBlendMode ("UV Map Blend Mode", Float) = 0
        _OverlayBlendStrength ("UV Map Blend Strength", Range(0, 1)) = 0
        [Toggle(_OUTPUT_OVERLAY_MAP)] _OutputOverlayMap ("Output UV Map", Float) = 0

        [Header(Extra Map)]
        _ExtraMap ("Extra Map", 2D) = "white" {}
        _ExtraDarkStrength ("Extra Dark Strength", Range(0, 1)) = 0
        [Toggle(_OUTPUT_EXTRA_MAP)] _OutputExtraMap ("Output Extra Map", Float) = 0

        [Header(SP Map)]
        _SpMap ("SP Map", 2D) = "white" {}
        [KeywordEnum(R, G, B, A)] _SpMapChannel ("SP Map Channel", Float) = 0
        [Toggle(_OUTPUT_SP_MAP)] _OutputSpMap ("Output SP Map", Float) = 0

        [Header(Color Mask)]
        _ColorMaskMap ("Color Mask", 2D) = "black" {}
        _ColorMaskMix ("Color Mask Mix", Range(0, 1)) = 0.5
        _ColorMaskStrength ("Color Mask Strength", Range(0, 1)) = 1
        [Toggle(_OUTPUT_COLOR_MASK)] _OutputColorMask ("Output Color Mask", Float) = 0

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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _LAMBERT_PERTURB_ON
            #pragma shader_feature_local _LAMBERT_PERTURB_DEBUG
            #pragma shader_feature_local _LAMBERTPERTURBSPACE_WORLD _LAMBERTPERTURBSPACE_OBJECT
            #pragma shader_feature_local _LAMBERTPERTURBMODE_ADD _LAMBERTPERTURBMODE_MULTIPLY
            #pragma shader_feature_local _LAMBERT_PERTURB_SWAP_UV
            #pragma shader_feature_local _OUTPUT_OVERLAY_MAP
            #pragma shader_feature_local _OUTPUT_EXTRA_MAP
            #pragma shader_feature_local _OUTPUT_SP_MAP
            #pragma shader_feature_local _SPMAPCHANNEL_R _SPMAPCHANNEL_G _SPMAPCHANNEL_B _SPMAPCHANNEL_A
            #pragma shader_feature_local _OUTPUT_WRAP_TOON
            #pragma shader_feature_local _OUTPUT_COLOR_MASK
            #pragma shader_feature_local _OVERLAYBLENDMODE_MULTIPLY _OVERLAYBLENDMODE_ADD
            #pragma shader_feature_local _SHADE_FROM_AMBIENT

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
                float2 uvMap : TEXCOORD1;
                float2 uvExtra : TEXCOORD2;
                float2 uvSp : TEXCOORD3;
                float2 uvColorMask : TEXCOORD4;
                float3 positionWS : TEXCOORD5;
                float3 normalWS : TEXCOORD6;
                float fogFactor : TEXCOORD7;
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

            // Only affect a band around the given cel shade threshold.
            half TerminatorBandMask(half NdotL, half threshold)
            {
                half width = max(_LambertPerturbWidth, HALF_MIN);
                half dist = abs(NdotL - threshold);
                return saturate(1.0h - dist / width);
            }

            // Perturb the post-cel (binarized) lit mask. The sample itself is not cel-filtered.
            half ApplyLitMaskPerturb(half litMask, half NdotL, half perturbSample, half threshold)
            {
                half mask = TerminatorBandMask(NdotL, threshold);
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
                output.uvMap = TRANSFORM_TEX(input.uv, _UVMap);
                output.uvExtra = TRANSFORM_TEX(input.uv, _ExtraMap);
                output.uvSp = TRANSFORM_TEX(input.uv, _SpMap);
                output.uvColorMask = TRANSFORM_TEX(input.uv, _ColorMaskMap);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half EvaluateLitMask(Light light, half3 normalWS, half threshold, half smoothness)
            {
                half NdotL = saturate(dot(normalWS, light.direction));
                half attenuation = light.distanceAttenuation * light.shadowAttenuation;
                half litMask = CelStep(NdotL * attenuation, threshold, smoothness);

            #if defined(_LAMBERT_PERTURB_ON)
                half perturbSample = SampleLambertPerturbMap(normalWS, light.direction).r;
                litMask = ApplyLitMaskPerturb(litMask, NdotL, perturbSample, threshold);
            #endif
                return litMask;
            }

            // Wrap toon lit mask: wrap Lambert first, then cel + same perturb.
            half EvaluateWrapToonLitMask(Light light, half3 normalWS)
            {
                half NdotL = dot(normalWS, light.direction);
                half wrap = _WrapAmount;
                half wrapped = saturate((NdotL + wrap) / (1.0h + wrap));
                half attenuation = light.distanceAttenuation * light.shadowAttenuation;
                half litMask = CelStep(wrapped * attenuation, _WrapShadeThreshold, _WrapShadeSmooth);

            #if defined(_LAMBERT_PERTURB_ON)
                half perturbSample = SampleLambertPerturbMap(normalWS, light.direction).r;
                litMask = ApplyLitMaskPerturb(litMask, wrapped, perturbSample, _WrapShadeThreshold);
            #endif
                return litMask;
            }

            half3 GetShadeColor(half3 normalWS)
            {
            #if defined(_SHADE_FROM_AMBIENT)
                return SampleSH(normalWS);
            #else
                return _ShadeColor.rgb;
            #endif
            }

            half3 GetWrapShadeColor(half3 normalWS)
            {
            #if defined(_SHADE_FROM_AMBIENT)
                return SampleSH(normalWS);
            #else
                return _WrapShadeColor.rgb;
            #endif
            }

            // Pure wrap-toon factor (no albedo, no light.color) for multiply onto final color.
            // Extra Map is multiply-blended only into the dark side.
            half3 EvaluateWrapToonFactor(half wrapLitMask, half3 extraMapRgb, half3 wrapShadeColor)
            {
                half3 darkColor = lerp(
                    wrapShadeColor,
                    wrapShadeColor * extraMapRgb,
                    saturate(_ExtraDarkStrength));
                return lerp(darkColor, half3(1.0h, 1.0h, 1.0h), saturate(wrapLitMask));
            }

            half3 BlendMultiplyLayer(half3 baseColor, half3 layerColor, half strength)
            {
                return lerp(baseColor, baseColor * layerColor, saturate(strength));
            }

            half SampleSpMapChannel(half4 spSample)
            {
            #if defined(_SPMAPCHANNEL_G)
                return spSample.g;
            #elif defined(_SPMAPCHANNEL_B)
                return spSample.b;
            #elif defined(_SPMAPCHANNEL_A)
                return spSample.a;
            #else
                return spSample.r;
            #endif
            }

            // Main light owns shade/lit lerp so shade color is applied once.
            half3 ToonLightingMain(Light light, half3 albedo, half3 normalWS, half3 shadeColor)
            {
                half litMask = EvaluateLitMask(light, normalWS, _ShadeThreshold, _ShadeSmooth);
                half3 diffuse = lerp(shadeColor * albedo, albedo, litMask);
                return diffuse * light.color;
            }

            // Additional lights only add lit contribution (no shade base).
            half3 ToonLightingAdditional(Light light, half3 albedo, half3 normalWS)
            {
                half litMask = EvaluateLitMask(light, normalWS, _ShadeThreshold, _ShadeSmooth);
                return albedo * light.color * litMask;
            }

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

            // Map sample drives lerp between Color A/B, then that color is blended onto the result.
            half3 BlendOverlayMap(half3 baseColor, half4 mapSample, half strength)
            {
                half t = mapSample.r;
                half3 tintColor = lerp(_OverlayColorA.rgb, _OverlayColorB.rgb, t);

            #if defined(_OVERLAYBLENDMODE_ADD)
                half3 blended = baseColor + tintColor;
            #else
                half3 blended = baseColor * tintColor;
            #endif
                return lerp(baseColor, blended, saturate(strength));
            }

            // White: lerp between albedo(baseColor) and final lit color. Black: keep final lit color.
            half3 ApplyColorMask(half3 litColor, half3 albedo, half mask)
            {
                half3 maskedColor = lerp(albedo, litColor, saturate(_ColorMaskMix));
                return lerp(litColor, maskedColor, saturate(mask) * saturate(_ColorMaskStrength));
            }

            half4 ToonFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            #if defined(_OUTPUT_OVERLAY_MAP)
                // Direct output: UV Map sampled with model UV.
                return SAMPLE_TEXTURE2D(_UVMap, sampler_UVMap, input.uvMap);
            #elif defined(_OUTPUT_EXTRA_MAP)
                // Direct output: Extra Map sampled with model UV.
                return SAMPLE_TEXTURE2D(_ExtraMap, sampler_ExtraMap, input.uvExtra);
            #elif defined(_OUTPUT_SP_MAP)
                // Direct output: selected channel of SP Map (model UV) as grayscale.
                half4 spSample = SAMPLE_TEXTURE2D(_SpMap, sampler_SpMap, input.uvSp);
                half ch = SampleSpMapChannel(spSample);
                return half4(ch, ch, ch, 1.0h);
            #elif defined(_OUTPUT_COLOR_MASK)
                half mask = SAMPLE_TEXTURE2D(_ColorMaskMap, sampler_ColorMaskMap, input.uvColorMask).r;
                return half4(mask, mask, mask, 1.0h);
            #else
                half4 uvMapSample = SAMPLE_TEXTURE2D(_UVMap, sampler_UVMap, input.uvMap);
                half4 extraMapSample = SAMPLE_TEXTURE2D(_ExtraMap, sampler_ExtraMap, input.uvExtra);
                half colorMask = SAMPLE_TEXTURE2D(_ColorMaskMap, sampler_ColorMaskMap, input.uvColorMask).r;
                half3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

            #if defined(_LAMBERT_PERTURB_DEBUG)
                // Debug: directly output the terminator-mapped Perturb Map.
                return SampleLambertPerturbMap(normalWS, mainLight.direction);
            #else
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = baseSample.rgb * _BaseColor.rgb;
                half alpha = baseSample.a * _BaseColor.a;

                half3 shadeColor = GetShadeColor(normalWS);
                half3 wrapShadeColor = GetWrapShadeColor(normalWS);

                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 color = ToonLightingMain(mainLight, albedo, normalWS, shadeColor);
                color += SampleSH(normalWS) * albedo * _AmbientStrength;

                // Combine wrap-toon lit masks across lights (any light can open the lit side).
                half wrapLitMask = EvaluateWrapToonLitMask(mainLight, normalWS);

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
                    wrapLitMask = max(wrapLitMask, EvaluateWrapToonLitMask(light, normalWS));
                }
            #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                    color += ToonLightingAdditional(light, albedo, normalWS);
                    wrapLitMask = max(wrapLitMask, EvaluateWrapToonLitMask(light, normalWS));
                LIGHT_LOOP_END
            #endif

                half3 wrapFactor = EvaluateWrapToonFactor(wrapLitMask, extraMapSample.rgb, wrapShadeColor);

            #if defined(_OUTPUT_WRAP_TOON)
                return half4(wrapFactor, alpha);
            #else
                // Final composite, then multiply wrap-toon factor onto the result.
                color = BlendOverlayMap(color, uvMapSample, _OverlayBlendStrength);
                color = BlendMultiplyLayer(color, wrapFactor, _WrapToonStrength);
                color = ApplyColorMask(color, albedo, colorMask);
                color = MixFog(color, input.fogFactor);
                return half4(color, alpha);
            #endif
            #endif
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

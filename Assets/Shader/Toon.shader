Shader "Custom/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [Enum(UV0, 0, UV1, 1)] _BaseMapUV ("Base Map UV", Float) = 0
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Normal Map)]
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1

        [Header(Cel Diffuse)]
        _ShadeColor ("Shade Color", Color) = (0.4, 0.4, 0.5, 1)
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.45
        _ShadeSmooth ("Shade Smooth", Range(0, 0.5)) = 0.02
        [Toggle(_SHADE_FROM_AMBIENT)] _ShadeFromAmbient ("Shade From Ambient", Float) = 0
        [Toggle] _CelModelNormalEnabled ("Cel Diffuse Use Model Space Normal Map", Float) = 0
        [Toggle(_CEL_THREE_TONE)] _CelThreeTone ("Cel Diffuse Three Tone", Float) = 0
        _MidShadeColor ("Mid Shade Color (Three Tone)", Color) = (0.7, 0.7, 0.75, 1)
        _MidShadeThreshold ("Mid/Dark Threshold (Three Tone, below Shade Threshold)", Range(0, 1)) = 0.2
        _AmbientDarkColor ("Darkest Layer Extra Color (Three Tone + Shade From Ambient)", Color) = (1, 1, 1, 1)
        [Enum(Multiply, 0, Add, 1)] _AmbientDarkBlend ("Darkest Layer Extra Color Blend", Float) = 0
        [Toggle(_OUTPUT_CEL_DIFFUSE)] _OutputCelDiffuse ("Output Cel Diffuse", Float) = 0

        [Header(Wrap Toon Layer)]
        [Toggle] _WrapNormalEnabled ("Use Model Space Normal", Float) = 0
        _WrapNormalMap ("Model Space Normal (Linear RGB)", 2D) = "gray" {}
        [Enum(UV0, 0, UV1, 1)] _WrapNormalUV ("Model Space Normal UV", Float) = 1
        _WrapNormalStrength ("Model Space Normal Strength", Range(0, 1)) = 1
        [Toggle] _WrapNormalTangentSpace ("Model Normal Map Is Tangent Space (follows skinning)", Float) = 0
        _WrapShadeColor ("Wrap Shade Color", Color) = (0.4, 0.4, 0.5, 1)
        _WrapShadeThreshold ("Wrap Shade Threshold", Range(0, 1)) = 0.45
        _WrapShadeSmooth ("Wrap Shade Smooth", Range(0, 0.5)) = 0.02
        _WrapAmount ("Wrap Amount", Range(0, 1)) = 0.5
        _WrapToonStrength ("Wrap Toon Strength", Range(0, 1)) = 0
        [Toggle(_OUTPUT_WRAP_TOON)] _OutputWrapToon ("Output Wrap Toon", Float) = 0
        [Toggle(_OUTPUT_WRAP_LAMBERT)] _OutputWrapLambert ("Output Wrap Lambert", Float) = 0
        [Toggle(_CEL_WRAP_LAMBERT_ON)] _CelWrapLambert ("Cel Diffuse Wrap Lambert Lerp (layer)", Float) = 0
        _CelWrapLambertColor ("Cel Wrap Lambert Target Color (x Base Color)", Color) = (0.3, 0.3, 0.45, 1)
        _CelWrapLambertStrength ("Cel Wrap Lambert Strength", Range(0, 1)) = 1
        [Toggle(_DISABLE_CEL_DIFFUSE)] _DisableCelDiffuse ("Disable Cel Diffuse (use flat Base Color)", Float) = 0
        [Toggle(_OUTPUT_WRAP_TOON_BASE)] _OutputWrapToonBase ("Output Wrap Toon x Base Color", Float) = 0
        [Toggle(_OUTPUT_WRAP_NORMAL_MAP)] _OutputWrapNormalMap ("Output Model Space Normal Map", Float) = 0
        [Toggle(_OUTPUT_NORMAL_MAP)] _OutputNormalMap ("Output Normal Map (Tangent Space)", Float) = 0
        [Toggle(_OUTPUT_VERTEX_NORMAL)] _OutputVertexNormal ("Output Vertex Normal (World Space)", Float) = 0

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
        _ExtraMap ("Extra Map (R = hue shift, 0.5 = none)", 2D) = "gray" {}
        _ExtraDarkStrength ("Extra Dark Strength", Range(0, 1)) = 0
        _ExtraHueShift ("Extra Hue Shift Max (turns)", Range(0, 0.5)) = 0.1
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

        [Header(Detail Map)]
        _DetailMap ("Detail Map", 2D) = "white" {}
        _DetailMapThreshold ("Detail Map Threshold", Range(0, 1)) = 0.5
        [Toggle(_OUTPUT_DETAIL_MAP)] _OutputDetailMap ("Output Detail Map", Float) = 0

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
            #pragma target 3.0
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
            #pragma shader_feature_local _OUTPUT_CEL_DIFFUSE
            #pragma shader_feature_local _CEL_THREE_TONE
            #pragma shader_feature_local _OUTPUT_OVERLAY_MAP
            #pragma shader_feature_local _OUTPUT_EXTRA_MAP
            #pragma shader_feature_local _OUTPUT_SP_MAP
            #pragma shader_feature_local _SPMAPCHANNEL_R _SPMAPCHANNEL_G _SPMAPCHANNEL_B _SPMAPCHANNEL_A
            #pragma shader_feature_local _OUTPUT_WRAP_TOON
            #pragma shader_feature_local _OUTPUT_WRAP_LAMBERT
            #pragma shader_feature_local _OUTPUT_WRAP_TOON_BASE
            #pragma shader_feature_local _DISABLE_CEL_DIFFUSE
            #pragma shader_feature_local _CEL_WRAP_LAMBERT_ON
            #pragma shader_feature_local _OUTPUT_WRAP_NORMAL_MAP
            #pragma shader_feature_local _OUTPUT_NORMAL_MAP
            #pragma shader_feature_local _OUTPUT_VERTEX_NORMAL
            #pragma shader_feature_local _OUTPUT_COLOR_MASK
            #pragma shader_feature_local _OUTPUT_DETAIL_MAP
            #pragma shader_feature_local _OVERLAYBLENDMODE_MULTIPLY _OVERLAYBLENDMODE_ADD
            #pragma shader_feature_local _SHADE_FROM_AMBIENT

            #include "ToonInput.hlsl"
            #include "SkinnedNormalBasis.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0; // xy: base map, zw: normal map.
                float2 uvMap : TEXCOORD1;
                float2 uvExtra : TEXCOORD2;
                float2 uvSp : TEXCOORD3;
                float2 uvColorMask : TEXCOORD4;
                float2 uvDetail : TEXCOORD5;
                float3 positionWS : TEXCOORD6;
                float3 normalWS : TEXCOORD7;
                float fogFactor : TEXCOORD8;
                float4 tangentWS : TEXCOORD9;
                float2 uvWrapNormal : TEXCOORD10;
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
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                float2 baseUV = lerp(input.uv, input.uv1, saturate(_BaseMapUV));
                float2 wrapUV = lerp(input.uv, input.uv1, saturate(_WrapNormalUV));
                output.uv.xy = TRANSFORM_TEX(baseUV, _BaseMap);
                output.uvWrapNormal = TRANSFORM_TEX(wrapUV, _WrapNormalMap);
                output.uv.zw = TRANSFORM_TEX(input.uv, _BumpMap);
                output.uvMap = TRANSFORM_TEX(input.uv, _UVMap);
                output.uvExtra = TRANSFORM_TEX(input.uv, _ExtraMap);
                output.uvSp = TRANSFORM_TEX(input.uv, _SpMap);
                output.uvColorMask = TRANSFORM_TEX(input.uv, _ColorMaskMap);
                output.uvDetail = TRANSFORM_TEX(input.uv, _DetailMap);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half EvaluateLitMask(Light light, half3 normalWS, half threshold, half smoothness)
            {
                // Lambert normalized from [-1, 1] to [0, 1] (terminator at 0.5).
                half NdotL = saturate(dot(normalWS, light.direction) * 0.5h + 0.5h);
                half attenuation = light.distanceAttenuation * light.shadowAttenuation;
                // Terminator shape comes from the normalized Lambert alone (same as Lambert + step).
                // Attenuation / shadow only gates the result afterwards, so it never shifts the threshold.
                half litMask = CelStep(NdotL, threshold, smoothness) * saturate(attenuation);

            #if defined(_LAMBERT_PERTURB_ON)
                half perturbSample = SampleLambertPerturbMap(normalWS, light.direction).r;
                litMask = ApplyLitMaskPerturb(litMask, NdotL, perturbSample, threshold);
            #endif
                return litMask;
            }

            // Wrap Lambert: N·L remapped so light wraps past the terminator, in [0, 1].
            half EvaluateWrapLambert(Light light, half3 normalWS)
            {
                half NdotL = dot(normalWS, light.direction);
                half wrap = _WrapAmount;
                return saturate((NdotL + wrap) / (1.0h + wrap));
            }

            // Wrap toon lit mask: wrap Lambert first, then cel + same perturb.
            half EvaluateWrapToonLitMask(Light light, half3 normalWS)
            {
                half wrapped = EvaluateWrapLambert(light, normalWS);
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

            // Rotate hue around the gray axis (1,1,1); angle in turns (1 = 360 degrees).
            half3 HueRotate(half3 color, half turns)
            {
                half angle = turns * 6.28318531h;
                half s, c;
                sincos(angle, s, c);
                const half3 k = half3(0.57735027h, 0.57735027h, 0.57735027h);
                return color * c + cross(k, color) * s + k * dot(k, color) * (1.0h - c);
            }

            // Pure wrap-toon factor (no albedo, no light.color) for multiply onto final color.
            half3 EvaluateWrapToonFactor(half wrapLitMask, half3 wrapShadeColor)
            {
                return lerp(wrapShadeColor, half3(1.0h, 1.0h, 1.0h), saturate(wrapLitMask));
            }

            // Extra Map (R, 0.5 = neutral) hue-shifts the already shaded color, dark side only.
            half3 ApplyExtraHueShift(half3 color, half extraR, half wrapLitMask)
            {
                half hueTurns = (extraR - 0.5h) * 2.0h * _ExtraHueShift;
                half weight = saturate(_ExtraDarkStrength) * (1.0h - saturate(wrapLitMask));
                return lerp(color, HueRotate(color, hueTurns), weight);
            }

            half3 BlendMultiplyLayer(half3 baseColor, half3 layerColor, half strength)
            {
                return lerp(baseColor, baseColor * layerColor, saturate(strength));
            }

            half SampleDetailStep(float2 uv)
            {
                half noise = saturate(SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, uv).r);
                return step(saturate(_DetailMapThreshold), noise);
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

            #if defined(_OUTPUT_WRAP_NORMAL_MAP)
                // Direct output: raw Model Space Normal map RGB (linear, not unpacked), sampled with its UV set.
                return half4(SAMPLE_TEXTURE2D(_WrapNormalMap, sampler_WrapNormalMap, input.uvWrapNormal).rgb, 1.0h);
            #elif defined(_OUTPUT_VERTEX_NORMAL)
                // Direct output: interpolated vertex normal in world space, remapped to 0-1.
                return half4(NormalizeNormalPerPixel(input.normalWS) * 0.5h + 0.5h, 1.0h);
            #elif defined(_OUTPUT_NORMAL_MAP)
                // Direct output: tangent space Normal Map (unpacked, scaled by Normal Strength) remapped to 0-1.
                half3 debugNormalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv.zw), _BumpScale);
                return half4(debugNormalTS * 0.5h + 0.5h, 1.0h);
            #elif defined(_OUTPUT_OVERLAY_MAP)
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
            #elif defined(_OUTPUT_DETAIL_MAP)
                half detailStep = SampleDetailStep(input.uvDetail);
                return half4(detailStep, detailStep, detailStep, 1.0h);
            #else
                half4 uvMapSample = SAMPLE_TEXTURE2D(_UVMap, sampler_UVMap, input.uvMap);
                half4 extraMapSample = SAMPLE_TEXTURE2D(_ExtraMap, sampler_ExtraMap, input.uvExtra);
                half colorMask = SAMPLE_TEXTURE2D(_ColorMaskMap, sampler_ColorMaskMap, input.uvColorMask).r;
                half3 normalTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv.zw), _BumpScale);
                // Reconstruct the signed tangent basis before evaluating any lighting.
                float3 bitangentWS = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3 normalWS = NormalizeNormalPerPixel(TransformTangentToWorld(
                    normalTS, half3x3(input.tangentWS.xyz, bitangentWS, input.normalWS)));
                // Dedicated linear RGB object-space atlas. Never use UnpackNormal here.
                // A missing/disabled map falls back to the geometric normal.
                float3 geometricNormalWS = NormalizeNormalPerPixel(input.normalWS);
                float3 wrapNormalWS = geometricNormalWS;
                if (_WrapNormalEnabled > 0.5h || _CelModelNormalEnabled > 0.5h)
                {
                    float3 normalOS = SAMPLE_TEXTURE2D(_WrapNormalMap, sampler_WrapNormalMap, input.uvWrapNormal).rgb * 2.0 - 1.0;
                    if (dot(normalOS, normalOS) > 1e-4)
                    {
                        // Default: the map holds mesh-space (object-space) normals -> valid for the bind pose only.
                        // _WrapNormalTangentSpace = 1: the map holds the full normal direction expressed in the ORIGINAL vertex
                        // TBN (n = S.T, S.B, S.N). It is rebuilt with the skinned TBN, so it follows the animation, and
                        // strength 1 -> 0 fades from the map direction to the vertex normal.
                        float3 mapNormalWS;
                        if (_WrapNormalTangentSpace > 0.5h)
                            mapNormalWS = ReviewedNormalTangentToWorld(normalize(normalOS), input.normalWS, input.tangentWS);
                        else
                            mapNormalWS = TransformObjectToWorldNormal(normalize(normalOS));
                        float3 paintedNormalWS = SafeNormalize(lerp(geometricNormalWS,
                            SafeNormalize(mapNormalWS), saturate(_WrapNormalStrength)));
                        if (_WrapNormalEnabled > 0.5h)
                            wrapNormalWS = paintedNormalWS;
                        // Cel diffuse uses the model space normal map instead of the tangent space normal map.
                        if (_CelModelNormalEnabled > 0.5h)
                            normalWS = paintedNormalWS;
                    }
                }
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));

            #if defined(_LAMBERT_PERTURB_DEBUG)
                // Debug: directly output the terminator-mapped Perturb Map.
                return SampleLambertPerturbMap(normalWS, mainLight.direction);
            #elif defined(_OUTPUT_WRAP_LAMBERT)
                // Debug: wrap Lambert of the main light as grayscale (before cel step / attenuation).
                half wrapLambert = EvaluateWrapLambert(mainLight, wrapNormalWS);
                return half4(wrapLambert, wrapLambert, wrapLambert, 1.0h);
            #else
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy);
                half3 albedo = baseSample.rgb * _BaseColor.rgb;
                half alpha = baseSample.a * _BaseColor.a;

                half3 shadeColor = GetShadeColor(normalWS);
                half3 wrapShadeColor = GetWrapShadeColor(wrapNormalWS);

                half3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half3 color = ToonLightingMain(mainLight, albedo, normalWS, shadeColor);
                color += SampleSH(normalWS) * albedo * _AmbientStrength;

                // Combine wrap-toon lit masks across lights (any light can open the lit side).
                half wrapLitMask = EvaluateWrapToonLitMask(mainLight, wrapNormalWS);

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
                    wrapLitMask = max(wrapLitMask, EvaluateWrapToonLitMask(light, wrapNormalWS));
                }
            #endif

                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
                    color += ToonLightingAdditional(light, albedo, normalWS);
                    wrapLitMask = max(wrapLitMask, EvaluateWrapToonLitMask(light, wrapNormalWS));
                LIGHT_LOOP_END
            #endif

                half3 wrapFactor = EvaluateWrapToonFactor(wrapLitMask, wrapShadeColor);

                // Solo-output one effect layer (mutually exclusive via keyword chain).
            #if defined(_OUTPUT_CEL_DIFFUSE)
                // Layer 1: cel lighting only (main + ambient + additional lights).
                return half4(color, alpha);
            #elif defined(_OUTPUT_WRAP_TOON)
                // Layer 3: wrap-toon factor (Extra Map hue shift is applied afterwards, not shown here).
                return half4(wrapFactor, alpha);
            #elif defined(_OUTPUT_WRAP_TOON_BASE)
                // Layer 3 + Base Map: wrap-toon factor multiplied onto Base Map * Base Color (full strength, no lighting/other layers).
                return half4(albedo * wrapFactor, alpha);
            #else
                // Final composite: Cel (lerp by Wrap Lambert) → UV Map → Wrap Toon → Color Mask → Detail → Fog.
            #if defined(_DISABLE_CEL_DIFFUSE)
                // Cel diffuse (main/additional lights + ambient) off: start from the flat Base Map * Base Color.
                color = albedo;
            #endif
            #if defined(_CEL_WRAP_LAMBERT_ON)
                // Layer: wrap Lambert of the main light is the lerp factor toward the target color
                // (lit side keeps the cel color, the wrapped-dark side goes to the target color).
                half celWrapLambert = EvaluateWrapLambert(mainLight, wrapNormalWS);
                half celWrapT = (1.0h - celWrapLambert) * saturate(_CelWrapLambertStrength);
                color = lerp(color, _CelWrapLambertColor.rgb * albedo, celWrapT);
            #endif
                color = BlendOverlayMap(color, uvMapSample, _OverlayBlendStrength);
                color = BlendMultiplyLayer(color, wrapFactor, _WrapToonStrength);
                color = ApplyExtraHueShift(color, extraMapSample.r, wrapLitMask);
                color = ApplyColorMask(color, albedo, colorMask);
                color *= SampleDetailStep(input.uvDetail);
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

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual
            ColorMask RG

            HLSLPROGRAM
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            // Keep the material buffer identical to the visible/depth passes.
            #include "ToonInput.hlsl"
            // URP reads the previous skinned position from TEXCOORD4, as well as
            // previous object/camera matrices. This captures bone animation even
            // when the character's root transform does not move.
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}

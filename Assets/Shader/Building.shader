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

        [Header(UV Map)]
        _UVMap ("UV Map", 2D) = "white" {}
        [Toggle(_OUTPUT_OVERLAY_MAP)] _OutputOverlayMap ("Output UV Map", Float) = 0

        [Header(Shading)]
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.35
        _ShadeSmooth ("Shade Smooth", Range(0, 0.5)) = 0.05
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.35

        [Header(Distance Fog)]
        [Toggle(_DISTANCE_FOG)] _DistanceFog ("Enable Distance Fog", Float) = 0
        [Toggle(_DISTANCE_FOG_XZ)] _DistanceFogXZ ("Use XZ Distance Only", Float) = 0
        _DistanceFogColorNear ("Fog Color Near", Color) = (0.75, 0.7, 0.65, 1)
        _DistanceFogColorMid ("Fog Color Mid", Color) = (0.68, 0.68, 0.7, 1)
        _DistanceFogColorFar ("Fog Color Far", Color) = (0.6, 0.65, 0.75, 1)
        _DistanceFogStart ("Fog Start", Float) = 20
        _DistanceFogEnd ("Fog End", Float) = 150
        _DistanceFogFalloff ("Fog Falloff", Range(0.1, 4)) = 1
        _DistanceFogMaxDensity ("Fog Max Density", Range(0, 1)) = 1

        [Header(Fog Noise Layer 1)]
        [Toggle(_HUE_NOISE)] _HueNoise ("Enable Fog Noise Layer 1", Float) = 0
        _HueNoiseMap ("Hue Noise Map (Object Space Triplanar)", 2D) = "gray" {}
        _NoiseAddColor ("Noise Blend Color", Color) = (0.3, 0.2, 0.4, 1)
        _NoiseAddStrength ("Noise Blend Strength", Range(0, 1)) = 1
        _NoiseMaskMin ("Noise Mask Min (smoothstep)", Range(0, 1)) = 0.4
        _NoiseMaskMax ("Noise Mask Max (smoothstep)", Range(0, 1)) = 0.6
        _HueNoisePositionDivisor ("Noise Position Divisor", Float) = 1
        _HueShiftFadeStart ("Hue Shift Fade Start (0-1 of fog range)", Range(0, 1)) = 0
        _HueShiftFadeEnd ("Hue Shift Fade End (0-1 of fog range)", Range(0, 1)) = 1
        _HueShiftFadePower ("Hue Shift Fade Power", Range(0.1, 4)) = 1

        [Header(Fog Noise Layer 2)]
        [Toggle(_NOISE_LAYER2)] _NoiseLayer2 ("Enable Fog Noise Layer 2", Float) = 0
        _Noise2Map ("Noise 2 Map (Object Space Triplanar)", 2D) = "gray" {}
        _Noise2Color ("Noise 2 Blend Color", Color) = (0.2, 0.4, 0.3, 1)
        _Noise2Strength ("Noise 2 Blend Strength", Range(0, 1)) = 1
        _Noise2MaskMin ("Noise 2 Mask Min (smoothstep)", Range(0, 1)) = 0.4
        _Noise2MaskMax ("Noise 2 Mask Max (smoothstep)", Range(0, 1)) = 0.6
        _Noise2PositionDivisor ("Noise 2 Position Divisor", Float) = 1
        _Noise2FadeStart ("Noise 2 Fade Start (0-1 of fog range)", Range(0, 1)) = 0
        _Noise2FadeEnd ("Noise 2 Fade End (0-1 of fog range)", Range(0, 1)) = 1
        _Noise2FadePower ("Noise 2 Fade Power", Range(0.1, 4)) = 1

        [Header(Fog Color Dissolve)]
        _DissolveProgress ("Dissolve Progress (0 = white, 1 = full)", Range(0, 1)) = 1
        _DissolveYTop ("Dissolve Y Top (World Space)", Float) = 10
        _DissolveYBottom ("Dissolve Y Bottom (World Space)", Float) = 0
        _DissolveWeightY ("Dissolve Weight Top-Down", Range(0, 1)) = 0.5
        _DissolveWeightDist ("Dissolve Weight Far-Near", Range(0, 1)) = 0.5
        _DissolveSoftness ("Dissolve Edge Softness", Range(0.001, 0.5)) = 0.1
        _DissolveNoiseMap ("Dissolve Noise Map (World Space XZ)", 2D) = "gray" {}
        _DissolveNoiseAmount ("Dissolve Noise Amount (Top-Down)", Range(0, 1)) = 0.3
        _DissolveNoiseDivisor ("Dissolve Noise Position Divisor", Float) = 1

        [Header(Debug Output)]
        [Toggle(_OUTPUT_FOG_DENSITY)] _OutputFogDensity ("Output Fog T", Float) = 0
        [Toggle(_OUTPUT_HUE_NOISE)] _OutputHueNoise ("Output Hue Noise", Float) = 0
        [Toggle(_OUTPUT_HUE_NOISE_UV)] _OutputHueNoiseUV ("Output Hue Noise UV (positionOS.zy)", Float) = 0

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
            #pragma shader_feature_local _OUTPUT_OVERLAY_MAP
            #pragma shader_feature_local_fragment _HUE_NOISE
            #pragma shader_feature_local_fragment _NOISE_LAYER2
            #pragma shader_feature_local_fragment _OUTPUT_FOG_DENSITY
            #pragma shader_feature_local_fragment _OUTPUT_HUE_NOISE
            #pragma shader_feature_local_fragment _OUTPUT_HUE_NOISE_UV
            #pragma shader_feature_local_fragment _DISTANCE_FOG
            #pragma shader_feature_local_fragment _DISTANCE_FOG_XZ

            #include "BuildingInput.hlsl"
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
                float3 positionOS : TEXCOORD4;
                float3 normalOS : TEXCOORD5;
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

            // Object-space triplanar sample of a noise map (single channel).
            half NoiseTriplanar(TEXTURE2D_PARAM(noiseMap, noiseSampler), float4 st, float divisor,
                float3 positionOS, float3 normalOS)
            {
                // Divide position first, then apply Tiling / Offset.
                float3 scaledPos = positionOS / max(abs(divisor), 1e-5);
                float2 uvX = scaledPos.zy * st.xy + st.zw;
                float2 uvY = scaledPos.xz * st.xy + st.zw;
                float2 uvZ = scaledPos.xy * st.xy + st.zw;

                half nX = SAMPLE_TEXTURE2D(noiseMap, noiseSampler, uvX).r;
                half nY = SAMPLE_TEXTURE2D(noiseMap, noiseSampler, uvY).r;
                half nZ = SAMPLE_TEXTURE2D(noiseMap, noiseSampler, uvZ).r;

                float3 weights = abs(SafeNormalize(normalOS));
                weights = pow(weights, _TriplanarSharpness);
                weights /= max(weights.x + weights.y + weights.z, 1e-5);

                return nX * weights.x + nY * weights.y + nZ * weights.z;
            }

            half HueNoiseTriplanar(float3 positionOS, float3 normalOS)
            {
                return NoiseTriplanar(TEXTURE2D_ARGS(_HueNoiseMap, sampler_HueNoiseMap),
                    _HueNoiseMap_ST, _HueNoisePositionDivisor, positionOS, normalOS);
            }

            // Lerp color toward blendColor. Smoothstep mask picks where; noise value drives the weight;
            // the weight fades with distance (t = 0-1 of fog range) from fadeStart to fadeEnd.
            half3 BlendNoiseColor(half3 color, half3 blendColor, half noiseValue, half maskMin, half maskMax,
                half strength, half fadeStart, half fadeEnd, half fadePower, half t)
            {
                half fade = 1.0h - smoothstep(fadeStart, max(fadeEnd, fadeStart + 1e-4h), t);
                fade = pow(fade, fadePower);
                half mask = smoothstep(maskMin, max(maskMax, maskMin + 1e-4h), noiseValue);
                half weight = saturate(noiseValue * mask * strength * fade);
                return lerp(color, blendColor, weight);
            }

            // Camera-distance fog: 0 before Start, ramps to MaxDensity at End.
            // Fog color shifts Near -> Mid -> Far across the same range (Mid at the halfway point).
            // Optionally lerps the fog color toward a noise color, weighted by object-space triplanar noise.
            // Returns the fog density; t is the normalized 0-1 position inside the fog range.
            half DistanceFogAmount(float3 positionWS, out half t)
            {
            #if defined(_DISTANCE_FOG_XZ)
                float dist = distance(positionWS.xz, _WorldSpaceCameraPos.xz);
            #else
                float dist = distance(positionWS, _WorldSpaceCameraPos.xyz);
            #endif
                float range = max(_DistanceFogEnd - _DistanceFogStart, 1e-4);
                t = saturate((dist - _DistanceFogStart) / range);
                return pow(t, _DistanceFogFalloff) * _DistanceFogMaxDensity;
            }

            half3 ApplyDistanceFog(half3 color, float3 positionWS, float3 positionOS, float3 normalOS)
            {
                half t;
                half fog = DistanceFogAmount(positionWS, t);
                half3 fogColor = t < 0.5h
                    ? lerp(_DistanceFogColorNear.rgb, _DistanceFogColorMid.rgb, t * 2.0h)
                    : lerp(_DistanceFogColorMid.rgb, _DistanceFogColorFar.rgb, t * 2.0h - 1.0h);
            #if defined(_HUE_NOISE)
                half noise1 = HueNoiseTriplanar(positionOS, normalOS);
                fogColor = BlendNoiseColor(fogColor, _NoiseAddColor.rgb, noise1, _NoiseMaskMin, _NoiseMaskMax,
                    _NoiseAddStrength, _HueShiftFadeStart, _HueShiftFadeEnd, _HueShiftFadePower, t);
            #endif
            #if defined(_NOISE_LAYER2)
                half noise2 = NoiseTriplanar(TEXTURE2D_ARGS(_Noise2Map, sampler_Noise2Map),
                    _Noise2Map_ST, _Noise2PositionDivisor, positionOS, normalOS);
                fogColor = BlendNoiseColor(fogColor, _Noise2Color.rgb, noise2, _Noise2MaskMin, _Noise2MaskMax,
                    _Noise2Strength, _Noise2FadeStart, _Noise2FadeEnd, _Noise2FadePower, t);
            #endif
                return lerp(color, fogColor, fog);
            }

            // Color dissolve: progress 0 = unlit pure white, 1 = untouched color.
            // Each pixel has an "appear time": the top appears first (world-space Y, perturbed by noise),
            // the far side appears first (fog t); the two are blended by their weights.
            half3 ApplyFogDissolve(half3 color, float3 positionWS)
            {
                if (_DissolveProgress >= 1.0h)
                    return color;

                half t;
                DistanceFogAmount(positionWS, t);

                half yN = saturate((positionWS.y - _DissolveYBottom) / max(_DissolveYTop - _DissolveYBottom, 1e-4));
                // Planar sample with world-space XZ (divide position first, then Tiling / Offset).
                float2 noiseUV = positionWS.xz / max(abs(_DissolveNoiseDivisor), 1e-5)
                    * _DissolveNoiseMap_ST.xy + _DissolveNoiseMap_ST.zw;
                half noise = SAMPLE_TEXTURE2D(_DissolveNoiseMap, sampler_DissolveNoiseMap, noiseUV).r;
                half topDown = (1.0h - yN) + (noise - 0.5h) * _DissolveNoiseAmount;
                half farNear = 1.0h - t;

                half weightSum = max(_DissolveWeightY + _DissolveWeightDist, 1e-4h);
                half appear = (topDown * _DissolveWeightY + farNear * _DissolveWeightDist) / weightSum;

                // Remap progress so 0 hides every pixel and 1 reveals every pixel (including edge softness).
                half halfAmp = 0.5h * _DissolveNoiseAmount;
                half p = lerp(-halfAmp, 1.0h + halfAmp + _DissolveSoftness, _DissolveProgress);
                half mask = smoothstep(appear, appear + _DissolveSoftness, p);
                return lerp(half3(1.0h, 1.0h, 1.0h), color, mask);
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
                output.uv = TRANSFORM_TEX(input.uv, _UVMap);
                output.positionWS = posInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.positionOS = input.positionOS.xyz;
                output.normalOS = input.normalOS;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            half4 BuildingFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            #if defined(_OUTPUT_OVERLAY_MAP)
                // Direct output: sample with model UV only.
                return SAMPLE_TEXTURE2D(_UVMap, sampler_UVMap, input.uv);
            #elif defined(_OUTPUT_FOG_DENSITY)
                // Debug: output t (normalized 0-1 position inside the fog range) as grayscale.
                half fogT;
                DistanceFogAmount(input.positionWS, fogT);
                return half4(fogT, fogT, fogT, 1.0h);
            #elif defined(_OUTPUT_HUE_NOISE_UV)
                // Debug: output positionOS.zy / divisor (frac'd) as R = z, G = y.
                float2 debugUV = input.positionOS.zy / max(abs(_HueNoisePositionDivisor), 1e-5);
                return half4(frac(debugUV), 0.0h, 1.0h);
            #elif defined(_OUTPUT_HUE_NOISE)
                // Debug: output the object-space triplanar noise as grayscale.
                half noiseValue = HueNoiseTriplanar(input.positionOS, input.normalOS);
                return half4(noiseValue, noiseValue, noiseValue, 1.0h);
            #else
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

            #if defined(_DISTANCE_FOG)
                color = ApplyDistanceFog(color, input.positionWS, input.positionOS, input.normalOS);
            #endif
                color = MixFog(color, input.fogFactor);
            #if defined(_DISTANCE_FOG)
                color = ApplyFogDissolve(color, input.positionWS);
            #endif
                return half4(color, 1.0h);
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

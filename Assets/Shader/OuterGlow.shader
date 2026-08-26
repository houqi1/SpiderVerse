Shader "Hidden/Custom/OuterGlow"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        Cull Off
        ZTest Always

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float _VerticalBlur;
        float _Intensity;
        float _BloomThreshold;
        float _BloomKnee;
        float2 _BaseUVOffset;
        float2 _UVOffset;
        float4 _OffsetStrengthMap_ST;
        float4 _OffsetStrengthMapST2;
        float2 _OffsetStrengthSmoothstep; // x = edge0, y = edge1

        // Stabilized screen UV (scheme 2): local = (screenUV - anchor) * aspect * depthScale
        float2 _StabilizedAnchorUV;
        float _StabilizedDepthScale;
        float _StabilizedEnabled;

        TEXTURE2D_X(_CharacterTex);
        SAMPLER(sampler_CharacterTex);

        TEXTURE2D(_OffsetStrengthMap);
        SAMPLER(sampler_OffsetStrengthMap);

        float2 GetStrengthMapUV(float2 screenUV)
        {
            float2 mapUV = screenUV;
            if (_StabilizedEnabled > 0.5)
            {
                float2 local = screenUV - _StabilizedAnchorUV;
                // Keep tiling isotropic on screen.
                local.x *= (_ScreenParams.x / max(_ScreenParams.y, 1.0));
                local *= max(_StabilizedDepthScale, 1e-4);
                mapUV = local;
            }
            return mapUV;
        }

        // Bloom-style soft-knee brightness filter (URP Bloom prefilter idea).
        // Only pixels brighter than threshold survive into blur / offset.
        float4 BrightnessPrefilter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            // Grayscale luminance (Rec.709 via Core Color.hlsl), not max(RGB).
            float br = Luminance(color.rgb);

            float threshold = max(_BloomThreshold, 0.0);
            float knee = max(_BloomKnee * threshold, 1e-5);
            // curve.x = threshold - knee, curve.y = 2*knee, curve.z = 0.25/knee
            float soft = br - threshold + knee;
            soft = clamp(soft, 0.0, 2.0 * knee);
            soft = (soft * soft) * (0.25 / knee);
            float contrib = max(soft, br - threshold) / max(br, 1e-5);

            color.rgb *= contrib;
            color.a = 0.0;
            return color;
        }

        float4 BlurVertical(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            const float samples = 32.0;
            const float halfSamples = samples * 0.5;

            float3 color = 0;
            float blurPixels = _VerticalBlur * _ScreenParams.y;

            for (float i = -halfSamples; i <= halfSamples; i++)
            {
                float2 offset = float2(0.0, (blurPixels * (i / halfSamples)) * _BlitTexture_TexelSize.y);
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + offset).rgb;
            }

            return float4(color / (samples + 1.0), 1.0);
        }

        // Blur/offset uses brightness-filtered color; body mask still from full character alpha.
        // Strength map sampled twice:
        //   A: signed offset (0.5 = neutral) with ST
        //   B: same map, independent ST2, smoothstep → weight, multiplied onto A
        float4 CompositeOuter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord;
            float4 sharp = SAMPLE_TEXTURE2D_X(_CharacterTex, sampler_CharacterTex, uv);

            // Strength map uses stabilized UV (relative to character anchor), not raw screen UV.
            float2 strengthUV = GetStrengthMapUV(uv);

            float2 offsetUV = strengthUV * _OffsetStrengthMap_ST.xy + _OffsetStrengthMap_ST.zw;
            float mapValue = SAMPLE_TEXTURE2D(_OffsetStrengthMap, sampler_OffsetStrengthMap, offsetUV).r;
            float offsetStrength = (mapValue - 0.5) * 2.0;

            float2 offsetUV2 = strengthUV * _OffsetStrengthMapST2.xy + _OffsetStrengthMapST2.zw;
            float mapValue2 = SAMPLE_TEXTURE2D(_OffsetStrengthMap, sampler_OffsetStrengthMap, offsetUV2).r;
            float edge0 = _OffsetStrengthSmoothstep.x;
            float edge1 = _OffsetStrengthSmoothstep.y;
            float weight = smoothstep(edge0, edge1, mapValue2);
            offsetStrength *= weight;

            // Blur color offset still in screen UV (glow displacement on the image).
            float2 sampleUV = uv + _BaseUVOffset + _UVOffset * offsetStrength;

            float3 blurred = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV).rgb;
            float mask = saturate(sharp.a);
            float3 outer = blurred * (1.0 - mask);
            return float4(outer * _Intensity, 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "BrightnessPrefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BrightnessPrefilter
            ENDHLSL
        }

        Pass
        {
            Name "BlurVertical"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment BlurVertical
            ENDHLSL
        }

        Pass
        {
            Name "Composite"
            Blend One One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeOuter
            ENDHLSL
        }
    }

    FallBack Off
}

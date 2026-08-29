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

        // Derivative scale for blur / UV offset (anchor-plane world pos ddx/ddy).
        float _DerivativeScaleEnabled;
        float _AnchorViewDepth;  // positive view distance to anchor
        float _RefWorldHeight;   // world frustum height at reference distance; / _ScreenParams.y = ref mpp

        // Mask edge fix: erode coverage (shrink hole) then soft-feather so glow fills the silhouette seam.
        float _MaskErode;
        float _MaskSoftness;

        TEXTURE2D_X(_CharacterTex);
        SAMPLER(sampler_CharacterTex);
        float4 _CharacterTex_TexelSize;

        TEXTURE2D(_OffsetStrengthMap);
        SAMPLER(sampler_OffsetStrengthMap);

        // World position on the camera-facing plane through the character anchor.
        // Stable derivatives everywhere (no scene-depth silhouette spikes).
        float3 ReconstructAnchorPlaneWS(float2 uv)
        {
            float2 ndc = uv * 2.0 - 1.0;
            float3 viewPos = float3(
                ndc.x / unity_CameraProjection._m00,
                ndc.y / unity_CameraProjection._m11,
                -1.0) * max(_AnchorViewDepth, 1e-4);
            return mul(UNITY_MATRIX_I_V, float4(viewPos, 1.0)).xyz;
        }

        // scale = refMpp / mpp → farther (larger mpp) shrinks screen-space blur/offset.
        float GetDerivativeScale(float2 uv)
        {
            if (_DerivativeScaleEnabled < 0.5)
                return 1.0;

            float3 wp = ReconstructAnchorPlaneWS(uv);
            float mpp = 0.5 * (length(ddx(wp)) + length(ddy(wp)));
            // Use current pass pixel height so downsample blur and full-res composite both stay correct.
            float refMpp = max(_RefWorldHeight, 1e-8) / max(_ScreenParams.y, 1.0);
            mpp = clamp(mpp, refMpp * 0.15, refMpp * 6.0);
            return refMpp / max(mpp, 1e-8);
        }

        float2 GetStrengthMapUV(float2 screenUV)
        {
            float2 mapUV = screenUV;
            if (_StabilizedEnabled > 0.5)
            {
                float2 local = screenUV - _StabilizedAnchorUV;
                local.x *= (_ScreenParams.x / max(_ScreenParams.y, 1.0));
                local *= max(_StabilizedDepthScale, 1e-4);
                mapUV = local;
            }
            return mapUV;
        }

        float4 BrightnessPrefilter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            float br = Luminance(color.rgb);

            float threshold = max(_BloomThreshold, 0.0);
            float knee = max(_BloomKnee * threshold, 1e-5);
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
            float derivScale = GetDerivativeScale(input.texcoord);
            float blurPixels = _VerticalBlur * _ScreenParams.y * derivScale;

            for (float i = -halfSamples; i <= halfSamples; i++)
            {
                float2 offset = float2(0.0, (blurPixels * (i / halfSamples)) * _BlitTexture_TexelSize.y);
                color += SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord + offset).rgb;
            }

            return float4(color / (samples + 1.0), 1.0);
        }

        float SampleCoverage(float2 uv)
        {
            return saturate(SAMPLE_TEXTURE2D_X(_CharacterTex, sampler_CharacterTex, uv).a);
        }

        float SoftBodyMask(float2 uv)
        {
            float mask = SampleCoverage(uv);
            if (_MaskErode > 1e-4)
            {
                float2 texel = _CharacterTex_TexelSize.xy * _MaskErode;
                mask = min(mask, SampleCoverage(uv + float2(texel.x, 0.0)));
                mask = min(mask, SampleCoverage(uv + float2(-texel.x, 0.0)));
                mask = min(mask, SampleCoverage(uv + float2(0.0, texel.y)));
                mask = min(mask, SampleCoverage(uv + float2(0.0, -texel.y)));
                mask = min(mask, SampleCoverage(uv + float2(texel.x, texel.y)));
                mask = min(mask, SampleCoverage(uv + float2(-texel.x, texel.y)));
                mask = min(mask, SampleCoverage(uv + float2(texel.x, -texel.y)));
                mask = min(mask, SampleCoverage(uv + float2(-texel.x, -texel.y)));
            }

            float softness = max(_MaskSoftness, 1e-4);
            return smoothstep(0.0, softness, mask);
        }

        float4 CompositeOuter(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord;
            float derivScale = GetDerivativeScale(uv);

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

            // Screen UV offsets scaled by derivatives → closer to constant world displacement.
            float2 sampleUV = uv + (_BaseUVOffset + _UVOffset * offsetStrength) * derivScale;

            float3 blurred = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, sampleUV).rgb;
            float mask = SoftBodyMask(uv);
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

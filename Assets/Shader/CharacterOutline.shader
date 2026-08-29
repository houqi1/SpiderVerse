Shader "Hidden/Custom/CharacterOutline"
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
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        float4 _OutlineColor;
        float _OutlineWidthOuter;
        float _OutlineWidthInner;
        float2 _OutlineOffset; // pixels
        float _OutlineIntensity;
        float2 _CharacterScreenUV;
        float4 _CharacterMaskTex_TexelSize;
        // 0 = screen-space expand from body mask; 1 = outerMask - innerMask (normal extrusion)
        float _UseExtrudedMask;

        TEXTURE2D_X(_CharacterMaskTex);
        TEXTURE2D_X(_CharacterMaskOuterTex);
        TEXTURE2D_X(_CharacterMaskInnerTex);

        float SampleCharacterMask(float2 uv)
        {
            return saturate(SAMPLE_TEXTURE2D_X(_CharacterMaskTex, sampler_PointClamp, uv).r);
        }

        float SampleExpandMask(float2 occlusionUV, float2 sampleUV)
        {
            float mask = SampleCharacterMask(sampleUV);
            if (mask < 1e-4)
                return 0.0;

            float depthAtPixel = SampleSceneDepth(occlusionUV);
            float depthAtCharacter = SampleSceneDepth(sampleUV);
            const float eps = 1e-4;

            #if UNITY_REVERSED_Z
            float notOccluded = step(depthAtPixel, depthAtCharacter + eps);
            #else
            float notOccluded = step(depthAtCharacter - eps, depthAtPixel);
            #endif

            return mask * notOccluded;
        }

        // Scheme 1 (screen): Expand toward character screen UV.
        float ExpandCharacterMaskByScreenPos(float2 expandUV, float2 characterUV, float widthPixels, float2 occlusionUV)
        {
            float2 texel = _CharacterMaskTex_TexelSize.xy;
            float2 toCharacterPx = (characterUV - expandUV) / max(texel, 1e-8);
            float distPx = length(toCharacterPx);

            if (distPx < 1e-5 || widthPixels < 1e-5)
                return SampleExpandMask(occlusionUV, expandUV);

            float2 dirPx = toCharacterPx / distPx;
            float maxStepPx = min(widthPixels, distPx);

            float expanded = SampleExpandMask(occlusionUV, expandUV);
            const int steps = 8;
            [unroll]
            for (int i = 1; i <= steps; i++)
            {
                float t = maxStepPx * ((float)i / (float)steps);
                float2 sampleUV = expandUV + dirPx * t * texel;
                expanded = max(expanded, SampleExpandMask(occlusionUV, sampleUV));
            }

            return saturate(expanded);
        }

        float4 CompositeOutline(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = input.texcoord;
            float3 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

            float2 texel = _CharacterMaskTex_TexelSize.xy;
            float2 offsetUV = _OutlineOffset * texel;
            float2 sampleUV = uv - offsetUV;

            float outline = 0.0;

            if (_UseExtrudedMask > 0.5)
            {
                // Scheme 1 (extrusion): ring = Mask(outerExt) - Mask(innerExt).
                float outerMask = saturate(SAMPLE_TEXTURE2D_X(_CharacterMaskOuterTex, sampler_PointClamp, sampleUV).r);
                float innerMask = saturate(SAMPLE_TEXTURE2D_X(_CharacterMaskInnerTex, sampler_PointClamp, sampleUV).r);
                outline = saturate(outerMask - innerMask);
            }
            else
            {
                // Scheme 1 (screen): ring = Expand(outer) - Expand(inner).
                float2 characterUV = _CharacterScreenUV - offsetUV;
                float outerW = max(_OutlineWidthOuter, _OutlineWidthInner);
                float innerW = min(_OutlineWidthOuter, _OutlineWidthInner);
                float outerMask = ExpandCharacterMaskByScreenPos(sampleUV, characterUV, outerW, uv);
                float innerMask = ExpandCharacterMaskByScreenPos(sampleUV, characterUV, innerW, uv);
                outline = saturate(outerMask - innerMask);
            }

            // Replace: outline color fully covers scene on the ring.
            float3 result = lerp(sceneColor, _OutlineColor.rgb * _OutlineIntensity, outline);
            return float4(result, 1.0);
        }
        ENDHLSL

        Pass
        {
            Name "CompositeOutline"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeOutline
            ENDHLSL
        }
    }

    FallBack Off
}

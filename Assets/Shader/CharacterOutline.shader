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
        float _UseExtrudedMask;
        float _OutlineControlEnabled;
        float _OutlineControlAffectsOpacity;
        float _OutlineControlThreshold;
        float _OutlineControlInvert;
        float4 _OutlineControlMap_ST;
        float2 _OutlineControlScrollAmplitude;
        float _OutlineControlScrollFrequency;
        float _OutlineFresnelPower;
        float _OutlineFresnelIntensity;

        // Mask: R = coverage, G = meshU, B = meshV
        TEXTURE2D_X(_CharacterMaskTex);
        TEXTURE2D_X(_CharacterMaskOuterTex);
        TEXTURE2D_X(_CharacterMaskInnerTex);
        TEXTURE2D(_OutlineControlMap);
        SAMPLER(sampler_OutlineControlMap);

        bool UvInside01(float2 uv)
        {
            return all(uv >= 0.0) && all(uv <= 1.0);
        }

        float4 SampleCharacterMask(float2 uv)
        {
            if (!UvInside01(uv))
                return 0.0;
            return saturate(SAMPLE_TEXTURE2D_X(_CharacterMaskTex, sampler_PointClamp, uv));
        }

        float4 SampleOuterMask(float2 uv)
        {
            if (!UvInside01(uv))
                return 0.0;
            return saturate(SAMPLE_TEXTURE2D_X(_CharacterMaskOuterTex, sampler_PointClamp, uv));
        }

        float4 SampleInnerMask(float2 uv)
        {
            if (!UvInside01(uv))
                return 0.0;
            return saturate(SAMPLE_TEXTURE2D_X(_CharacterMaskInnerTex, sampler_PointClamp, uv));
        }

        float SampleExpandCoverage(float2 occlusionUV, float2 sampleUV)
        {
            float4 m = SampleCharacterMask(sampleUV);
            float mask = m.r;
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

        // x = expanded coverage, yz = mesh UV, w = fresnel from a contributing sample
        float4 ExpandCharacterMaskByScreenPos(float2 expandUV, float2 characterUV, float widthPixels, float2 occlusionUV)
        {
            float2 texel = _CharacterMaskTex_TexelSize.xy;
            float2 toCharacterPx = (characterUV - expandUV) / max(texel, 1e-8);
            float distPx = length(toCharacterPx);

            float4 selfM = SampleCharacterMask(expandUV);
            float coverage = SampleExpandCoverage(occlusionUV, expandUV);
            float2 meshUV = selfM.gb;
            float fresnel = selfM.a;

            if (distPx < 1e-5 || widthPixels < 1e-5)
                return float4(coverage, meshUV, fresnel);

            float2 dirPx = toCharacterPx / distPx;
            float maxStepPx = min(widthPixels, distPx);

            const int steps = 8;
            [unroll]
            for (int i = 1; i <= steps; i++)
            {
                float t = maxStepPx * ((float)i / (float)steps);
                float2 sampleUV = expandUV + dirPx * t * texel;
                float c = SampleExpandCoverage(occlusionUV, sampleUV);
                if (c > coverage)
                {
                    coverage = c;
                    float4 sm = SampleCharacterMask(sampleUV);
                    meshUV = sm.gb;
                    fresnel = sm.a;
                }
            }

            return float4(saturate(coverage), meshUV, saturate(fresnel));
        }

        float SampleControlMap(float2 meshUV)
        {
            if (_OutlineControlEnabled < 0.5)
                return 1.0;

            float2 noiseUV = meshUV * _OutlineControlMap_ST.xy + _OutlineControlMap_ST.zw;
            float freq = max(_OutlineControlScrollFrequency, 0.0);
            if (freq > 1e-6)
            {
                float stepIndex = floor(_Time.y * freq);
                noiseUV += _OutlineControlScrollAmplitude * stepIndex;
            }
            return saturate(SAMPLE_TEXTURE2D(_OutlineControlMap, sampler_OutlineControlMap, noiseUV).r);
        }

        // Shared fresnel (center 0, rim 1) multiplied onto the control map, then threshold/opacity.
        float SampleControlOpacity(float2 meshUV, float fresnelFactor)
        {
            float noise = SampleControlMap(meshUV);
            float power = max(_OutlineFresnelPower, 1e-4);
            float fresnel = pow(saturate(fresnelFactor), power);
            fresnel = lerp(1.0, fresnel, saturate(_OutlineFresnelIntensity));
            float combined = saturate(noise * fresnel);

            if (_OutlineControlEnabled < 0.5 && saturate(_OutlineFresnelIntensity) < 1e-4)
                return 1.0;

            float thr = saturate(_OutlineControlThreshold);

            float gate;
            float fade;
            if (_OutlineControlInvert > 0.5)
            {
                gate = step(combined, thr);
                fade = saturate((thr - combined) / max(thr, 1e-5));
            }
            else
            {
                gate = step(thr, combined);
                fade = saturate((combined - thr) / max(1.0 - thr, 1e-5));
            }

            if (_OutlineControlAffectsOpacity < 0.5)
                return gate;

            return saturate(gate * fade);
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
            float2 meshUV = float2(0.5, 0.5);
            float fresnelFactor = 0.0;

            if (_UseExtrudedMask > 0.5)
            {
                float4 outerM = SampleOuterMask(sampleUV);
                float4 innerM = SampleInnerMask(sampleUV);
                outline = saturate(outerM.r - innerM.r);
                meshUV = outerM.gb;
                fresnelFactor = outerM.a;
            }
            else
            {
                float2 characterUV = _CharacterScreenUV - offsetUV;
                float outerW = max(_OutlineWidthOuter, _OutlineWidthInner);
                float innerW = min(_OutlineWidthOuter, _OutlineWidthInner);
                float4 outerX = ExpandCharacterMaskByScreenPos(sampleUV, characterUV, outerW, uv);
                float4 innerX = ExpandCharacterMaskByScreenPos(sampleUV, characterUV, innerW, uv);
                outline = saturate(outerX.x - innerX.x);
                meshUV = outerX.yz;
                fresnelFactor = outerX.w;
            }

            // Control map * shared fresnel, then per-layer threshold / opacity.
            outline *= SampleControlOpacity(meshUV, fresnelFactor);

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

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
        float4 _GlitchParams; // opacity, displacement, RGB split, slice UV height
        float4 _GlitchTiming; // quantized time, density, burst probability, seed
        float4 _GlitchStyle;  // echo opacity, anchor Y, anchored flag
        float4 _RGBColorR;    // tint of the displaced red-channel layer
        float4 _RGBColorB;    // tint of the displaced blue-channel layer

        float GlitchHash(float2 p)
        {
            return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
        }

        float4 GlitchSample(float2 uv)
        {
            // Transparent border instead of smearing a character touching the viewport edge.
            float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv) * inside;
        }

        float4 CompositeGlitch(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float tick = _GlitchTiming.x;
            float seed = _GlitchTiming.w;
            float band = floor((uv.y - _GlitchStyle.y * _GlitchStyle.z) / max(_GlitchParams.w, 1e-6));
            float burst = 1.0 - step(_GlitchTiming.z, GlitchHash(float2(tick, seed + 17.0)));
            float active = burst * (1.0 - step(_GlitchTiming.y, GlitchHash(float2(band + seed, tick))));
            float shift = (GlitchHash(float2(band + seed + 31.0, tick)) * 2.0 - 1.0) * _GlitchParams.y;
            float2 sampleUV = uv + float2(shift, 0.0);
            float split = _GlitchParams.z;
            float4 r = GlitchSample(sampleUV + float2(split, 0.0));
            float4 g = GlitchSample(sampleUV);
            float4 b = GlitchSample(sampleUV - float2(split, 0.0));
            // Capture RGB already fades to transparent black at filtered edges.
            // Per-channel coverage travels with the color; never clip to the original silhouette.
            float coverage = max(r.a, max(g.a, b.a));
            float3 color = r.r * _RGBColorR.rgb + float3(0.0, g.g, 0.0) + b.b * _RGBColorB.rgb;
            float4 echo = GlitchSample(uv - float2(shift * 0.65, 0.0));
            float echoAlpha = saturate(echo.a) * _GlitchStyle.x;
            color += echo.rgb * _GlitchStyle.x * (1.0 - coverage);
            coverage += echoAlpha * (1.0 - coverage);
            float weight = active * _GlitchParams.x;
            return float4(color * weight, saturate(coverage) * weight);
        }
        float4 _RGBOffsets; // max red XY, max blue XY (already scaled by opacity); green is the spatial reference
        float4 _RGBNoiseParams; // noise UV per tick XY, blue tick offset
        float4 _RGBGateParams;  // gate noise UV per second XY, continuous time, step threshold
        float _RGBActive;
        float _RGBOutsideOnly;
        TEXTURE2D(_RGBNoiseTex);
        SAMPLER(sampler_RGBNoiseTex);
        TEXTURE2D(_RGBGateTex);
        SAMPLER(sampler_RGBGateTex);

        float4 CompositeRGBFlicker(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            // Off frames must preserve the full-resolution camera image exactly.
            if (_RGBActive < 0.5)
                return 0;
            // Gate noise sampled by continuous time: flicker is on only where noise R >= threshold.
            float gate = step(_RGBGateParams.w, SAMPLE_TEXTURE2D_LOD(_RGBGateTex, sampler_RGBGateTex,
                _RGBGateParams.xy * _RGBGateParams.z, 0).r);
            if (gate < 0.5)
                return 0;
            float2 uv = input.texcoord;
            // Noise RG sampled per tick (same interval as noise 1; no screen UV), remapped to (-1,1)
            // as per-axis offset strength. The blue channel samples the same noise a few ticks later.
            float tick = _GlitchTiming.x;
            float2 redStrength = SAMPLE_TEXTURE2D_LOD(_RGBNoiseTex, sampler_RGBNoiseTex,
                _RGBNoiseParams.xy * tick, 0).rg * 2.0 - 1.0;
            float2 blueStrength = SAMPLE_TEXTURE2D_LOD(_RGBNoiseTex, sampler_RGBNoiseTex,
                _RGBNoiseParams.xy * (tick + _RGBNoiseParams.z), 0).rg * 2.0 - 1.0;
            float2 redOffset = _RGBOffsets.xy * redStrength;
            float2 blueOffset = _RGBOffsets.zw * blueStrength;
            float4 r = GlitchSample(uv - redOffset);
            float4 g = GlitchSample(uv);
            float4 b = GlitchSample(uv - blueOffset);
            // Green retains coverage of the original body. Replace that body's RGB entirely,
            // including channels that have moved away, instead of adding a colored duplicate.
            float coverage = saturate(max(g.a, max(r.a, b.a)));
            float3 color = r.r * _RGBColorR.rgb + float3(0.0, g.g, 0.0) + b.b * _RGBColorB.rgb;
            // Optionally keep only the spill outside the original silhouette (green = stationary reference).
            float outside = lerp(1.0, 1.0 - saturate(g.a), _RGBOutsideOnly);
            return float4(color * outside, coverage * outside);
        }

        float4 _NoiseParams; // displacement (UV), hue shift (turns at noise B = 1), multiply strength, continuous time
        float2 _NoiseScroll; // noise UV scroll per tick
        float4 _NoiseST;     // tiling XY, offset ZW (screen UV)
        float4 _NoiseST2;    // second noise tiling XY, offset ZW
        float2 _NoiseSpeed2; // second noise sample UV per second of time
        TEXTURE2D(_GlitchNoiseTex);
        SAMPLER(sampler_GlitchNoiseTex);
        TEXTURE2D(_GlitchNoiseTex2);
        SAMPLER(sampler_GlitchNoiseTex2);
        float _NoiseTiling3;   // third texture tiling (screen UV)
        float2 _NoiseScroll3;  // third texture UV scroll per tick
        TEXTURE2D(_GlitchNoiseTex3);
        SAMPLER(sampler_GlitchNoiseTex3);

        // Rotate around the gray axis; linear, so it is safe on premultiplied color.
        float3 HueShift(float3 c, float turns)
        {
            float a = turns * 6.28318530718;
            float cs = cos(a), sn = sin(a);
            const float3 k = 0.57735026919;
            return c * cs + cross(k, c) * sn + k * dot(k, c) * (1.0 - cs);
        }

        float4 CompositeNoiseDisplace(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float tick = _GlitchTiming.x;
            // Noise scrolls in whole steps, once per tick.
            float4 noise = SAMPLE_TEXTURE2D_LOD(_GlitchNoiseTex, sampler_GlitchNoiseTex,
                uv * _NoiseST.xy + _NoiseST.zw + _NoiseScroll * tick, 0);
            // Second noise is sampled by continuous time only (one value for the whole screen),
            // remapped and multiplied onto the first remapped noise.
            float4 noise2 = SAMPLE_TEXTURE2D_LOD(_GlitchNoiseTex2, sampler_GlitchNoiseTex2,
                _NoiseSpeed2 * _NoiseParams.w * _NoiseST2.xy + _NoiseST2.zw, 0);
            float2 n = (noise.rg * 2.0 - 1.0) * lerp(1.0, noise2.rg * 2.0 - 1.0, _NoiseParams.z);
            // Displaced sampling also reveals the character outside its original silhouette.
            float4 c = GlitchSample(uv + n * _NoiseParams.x);
            float coverage = saturate(c.a);
            // Third texture scrolls per tick like the first noise; max-blended into the character color
            // (premultiplied, so limited to coverage) before the hue shift.
            float3 layer = SAMPLE_TEXTURE2D_LOD(_GlitchNoiseTex3, sampler_GlitchNoiseTex3,
                uv * _NoiseTiling3 + _NoiseScroll3 * tick, 0).rgb;
            float3 color = HueShift(max(c.rgb, layer * coverage), noise.b * _NoiseParams.y);
            float weight = _GlitchParams.x;
            return float4(color * weight, coverage * weight);
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

        Pass
        {
            Name "CharacterGlitch"
            Blend One OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeGlitch
            ENDHLSL
        }

        Pass
        {
            Name "CharacterRGBFlicker"
            Blend One OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeRGBFlicker
            ENDHLSL
        }

        Pass
        {
            Name "CharacterNoiseDisplace"
            Blend One OneMinusSrcAlpha, Zero One
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment CompositeNoiseDisplace
            ENDHLSL
        }
    }

    FallBack Off
}

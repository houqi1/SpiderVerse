Shader "Hidden/SpiderVerse/MotionVectorDebug"
{
    Properties
    {
        [Enum(Direction,0,SignedRG,1,Speed,2,NoiseOverlay,3,NoiseOnly,4)] _DisplayMode ("Display Mode", Float) = 3
        _Sensitivity ("Sensitivity (UV displacement multiplier)", Range(1, 1000)) = 100
        [Header(Motion Oriented Noise)]
        [ToggleUI] _NoiseOutputOnly ("Output Mapped Noise Only", Float) = 0
        _NoiseMap ("Noise Map (R)", 2D) = "gray" {}
        [HDR] _NoiseTint ("Noise Tint", Color) = (1, 1, 1, 1)
        _NoiseOpacity ("Noise Add Intensity", Range(0, 1)) = 0.35
        _NoiseContrast ("Noise Contrast", Range(0, 4)) = 1.5
        _NoiseAngle ("Noise Angle Offset (Degrees)", Range(-180, 180)) = 0
        _MotionThreshold ("Direction Threshold (Pixels Per Frame)", Range(0.001, 5)) = 0.1
        [Header(Motion Oriented Scene Distortion)]
        [ToggleUI] _DistortionEnabled ("Enable Scene Distortion", Float) = 1
        _DistortionMap ("Distortion Strength Map (R)", 2D) = "gray" {}
        _DistortionPixels ("Distortion Distance (Pixels)", Range(-64, 64)) = 8
        _DistortionAngle ("Distortion Map Angle Offset (Degrees)", Range(-180, 180)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D_X_FLOAT(_MotionVectorTexture);
        TEXTURE2D_X_FLOAT(_CachedMotionDirections);
        TEXTURE2D(_NoiseMap);
        TEXTURE2D(_DistortionMap);

        CBUFFER_START(UnityPerMaterial)
            float _DisplayMode;
            float _Sensitivity;
            float4 _NoiseMap_ST;
            float4 _NoiseTint;
            float _NoiseOpacity;
            float _NoiseContrast;
            float _NoiseAngle;
            float _MotionThreshold;
            float _NoiseOutputOnly;
            float4 _DistortionMap_ST;
            float _DistortionEnabled;
            float _DistortionPixels;
            float _DistortionAngle;
        CBUFFER_END

        // Per-camera values are supplied through property blocks, never a shared
        // material/global mutation during deferred Render Graph recording.
        float _UseCachedMotionDirections;
        float _CacheValid;
        float _CacheTime;
        float _HoldLastDirection;
        float _ResetDirectionAfter;

        float2 RotateNoiseUV(float2 position, float2 direction)
        {
            // Inverse sampling rotation: the texture's +X axis follows direction.
            return float2(dot(position, direction),
                          dot(position, float2(-direction.y, direction.x)));
        }
        ENDHLSL

        Pass
        {
            Name "Motion Vector Debug"
            Cull Off
            ZWrite Off
            ZTest Always
            // RGB = source + destination * (1 - source alpha).
            // NoiseOverlay returns weighted RGB and alpha=0 for addition;
            // debug/direct output returns alpha=1 for replacement. Preserve scene alpha.
            Blend One OneMinusSrcAlpha, Zero One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            float4 MotionNoise(Varyings input, float2 motion)
            {
                float2 screenSize = max(_ScaledScreenParams.xy, float2(1.0, 1.0));
                float2 motionPixels = motion * screenSize;
                float pixelSpeed = length(motionPixels);
                float threshold = max(_MotionThreshold, 0.001);
                float2 direction = pixelSpeed >= threshold
                    ? motionPixels / max(pixelSpeed, 1e-6) : float2(1.0, 0.0);
                // Select nonzero magnitude, including negative X/Y directions.
                float motionMask = pixelSpeed > 0.0 ? 1.0 : 0.0;

                float sine, cosine;
                sincos(radians(_NoiseAngle), sine, cosine);
                direction = float2(cosine * direction.x - sine * direction.y,
                                   sine * direction.x + cosine * direction.y);

                // Use an aspect-correct screen plane centered at the image center.
                // Both coordinates and velocity must use the same pixel metric.
                float2 position = (input.positionCS.xy - 0.5 * screenSize) / screenSize.y;
                float2 noiseUV = RotateNoiseUV(position, direction) * _NoiseMap_ST.xy
                               + 0.5 + _NoiseMap_ST.zw;

                // Rotate the footprint as well. Do not use derivatives of the
                // discontinuous direction field at object/velocity boundaries.
                float2 uvDx = RotateNoiseUV(ddx(position), direction) * _NoiseMap_ST.xy;
                float2 uvDy = RotateNoiseUV(ddy(position), direction) * _NoiseMap_ST.xy;
                float noise = SAMPLE_TEXTURE2D_GRAD(_NoiseMap, sampler_LinearRepeat,
                                                    noiseUV, uvDx, uvDy).r;
                noise = saturate((noise - 0.5) * _NoiseContrast + 0.5);

                if (_DisplayMode > 3.5)
                    return float4((noise * motionMask).xxx, 1.0);

                // Alpha=1 fully replaces scene RGB in the existing blend state.
                // Keep mapping, contrast and tint, but ignore overlay opacity.
                if (_NoiseOutputOnly > 0.5)
                    return float4(noise * _NoiseTint.rgb * motionMask, 1.0);

                float3 contribution = noise * motionMask * _NoiseTint.rgb
                                    * saturate(_NoiseOpacity * _NoiseTint.a);
                return float4(contribution, 0.0);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Every display mode and the noise rotation use the same held
                // signed UV displacement. Only the collection pass reads live MV.
                float2 motion;
                if (_UseCachedMotionDirections > 0.5)
                    motion = SAMPLE_TEXTURE2D_X_LOD(
                        _CachedMotionDirections, sampler_PointClamp, input.texcoord, 0).rg;
                else
                    motion = SAMPLE_TEXTURE2D_X_LOD(
                        _MotionVectorTexture, sampler_PointClamp, input.texcoord, 0).rg;
                if (_DisplayMode > 2.5)
                    return MotionNoise(input, motion);

                float2 scaledMotion = motion * max(_Sensitivity, 0.0);
                float speed = saturate(length(scaledMotion));

                if (_DisplayMode < 0.5)
                {
                    // Hue = direction, brightness = speed. Stationary pixels are black.
                    if (speed < 1e-6)
                        return float4(0.0, 0.0, 0.0, 1.0);

                    float hue = frac(atan2(motion.y, motion.x) / TWO_PI + 1.0);
                    float3 color = saturate(abs(frac(hue + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0);
                    return float4(color * speed, 1.0);
                }

                if (_DisplayMode < 1.5)
                {
                    // R = X, G = Y, 0.5 = zero. Remap negatives for display.
                    return float4(saturate(0.5 + scaledMotion), 0.5, 1.0);
                }

                return float4(speed, speed, speed, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Collect Motion Directions"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment CollectDirection

            float4 CollectDirection(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 motion = SAMPLE_TEXTURE2D_X_LOD(
                    _MotionVectorTexture, sampler_PointClamp, input.texcoord, 0).rg;
                float2 pixels = motion * max(_ScaledScreenParams.xy, float2(1, 1));
                float speed = length(pixels);
                // RG: signed UV displacement. B: held movement mask. A: last motion
                // time modulo 64 seconds, quantized down to an exact half-float.
                // Never accumulate tiny frame deltas in a half-float age counter.
                float timestamp = floor(_CacheTime * 32.0) / 32.0;
                // Preserve every nonzero vector for the additive movement mask.
                // Direction Threshold only stabilizes the noise's orientation.
                if (speed > 0.0)
                    return float4(motion, 1.0, timestamp);

                if (_CacheValid > 0.5 && _HoldLastDirection > 0.5)
                {
                    float4 previous = SAMPLE_TEXTURE2D_X_LOD(
                        _BlitTexture, sampler_PointClamp, input.texcoord, 0);
                    float age = _CacheTime - previous.a;
                    if (age < 0) age += 64.0;
                    if (_ResetDirectionAfter <= 0 || age < _ResetDirectionAfter)
                        return previous;
                }
                return float4(0, 0, 0, timestamp);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Motion Oriented Scene Distortion"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero, Zero One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DistortScene

            float4 DistortScene(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenSize = max(_ScaledScreenParams.xy, float2(1, 1));
                float2 motion = SAMPLE_TEXTURE2D_X_LOD(
                    _CachedMotionDirections, sampler_PointClamp, input.texcoord, 0).rg;
                float2 pixels = motion * screenSize;
                float speed = length(pixels);
                float2 direction = pixels / max(speed, 1e-6);
                float2 mapDirection = speed > 0 ? direction : float2(1, 0);
                float sine, cosine;
                sincos(radians(_DistortionAngle), sine, cosine);
                mapDirection = float2(cosine * mapDirection.x - sine * mapDirection.y,
                                      sine * mapDirection.x + cosine * mapDirection.y);

                float2 position = (input.positionCS.xy - 0.5 * screenSize) / screenSize.y;
                float2 mapUV = RotateNoiseUV(position, mapDirection) * _DistortionMap_ST.xy
                             + 0.5 + _DistortionMap_ST.zw;
                float2 uvDx = RotateNoiseUV(ddx(position), mapDirection) * _DistortionMap_ST.xy;
                float2 uvDy = RotateNoiseUV(ddy(position), mapDirection) * _DistortionMap_ST.xy;
                float strength = saturate(SAMPLE_TEXTURE2D_GRAD(
                    _DistortionMap, sampler_LinearRepeat, mapUV, uvDx, uvDy).r);

                // Backward sampling moves visible features along the MV direction.
                // Magnitude is controlled by the separate map and a pixel distance.
                float2 offsetUV = direction * (_DistortionPixels * strength) / screenSize;
                float2 halfTexel = 0.5 / screenSize;
                float2 sceneUV = clamp(input.texcoord - offsetUV, halfTexel, 1.0 - halfTexel);
                // _BlitTexture is a copy of this frame's scene BEFORE this effect.
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, sceneUV, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

Shader "Hidden/SpiderVerse/MotionVectorDebug"
{
    Properties
    {
        [Enum(Direction,0,SignedRG,1,Speed,2,NoiseOverlay,3,NoiseOnly,4)] _DisplayMode ("Display Mode", Float) = 3
        _Sensitivity ("Sensitivity (UV displacement multiplier)", Range(1, 1000)) = 100
        [Header(Motion Vector Source Debug)]
        [ToggleUI] _DebugThirdLayerMotion ("Output Third Layer MV (Current Animation Sample)", Float) = 0
        [ToggleUI] _DebugOtherLayersMotion ("Output Other Layers MV (Published Sample)", Float) = 0
        [Header(Motion Oriented Noise)]
        [ToggleUI] _NoiseOutputOnly ("Output Mapped Noise Only", Float) = 0
        [ToggleUI] _NoiseOnlyIgnoreMask ("Noise Only Ignore Motion Mask", Float) = 0
        _NoiseMap ("Noise Map (R)", 2D) = "gray" {}
        _NoiseFlowStep ("Noise Flow Step (UV per Sample, XY)", Vector) = (0.02, 0, 0, 0)
        [HDR] _NoiseTint ("Noise Tint", Color) = (1, 1, 1, 1)
        _NoiseOpacity ("Noise Add Intensity", Range(0, 1)) = 0.35
        _NoiseContrast ("Noise Contrast", Range(0, 4)) = 1.5
        _NoiseAngle ("Noise Angle Offset (Degrees)", Range(-180, 180)) = 0
        _MotionThreshold ("Direction Threshold (Pixels Per Frame)", Range(0.001, 5)) = 0.1
        [Header(Motion Mask)]
        _MotionMaskMinSpeed ("Mask Start Speed (Pixels Per Frame)", Range(0, 32)) = 0.1
        _MotionMaskMaxSpeed ("Mask Full Speed (Pixels Per Frame)", Range(0, 64)) = 2
        [Header(Motion Oriented Scene Distortion)]
        [ToggleUI] _DistortionEnabled ("Enable Scene Distortion", Float) = 1
        _DistortionMap ("Distortion Strength Map (R)", 2D) = "gray" {}
        _DistortionFlowStep ("Distortion Flow Step (UV per Sample, XY)", Vector) = (0.02, 0, 0, 0)
        _DistortionNoiseThreshold ("Distortion Map Cutoff", Range(0, 0.9)) = 0.2
        _DistortionPixels ("Distortion Distance (Pixels)", Range(-64, 64)) = 8
        _DistortionAngle ("Distortion Map Angle Offset (Degrees)", Range(-180, 180)) = 0
        [Header(Motion Oriented Offset Smear)]
        [ToggleUI] _OffsetSmearEnabled ("Enable Offset Smear", Float) = 1
        [NoScaleOffset] _OffsetSmearMap ("Offset Smear Map (R)", 2D) = "gray" {}
        _OffsetSmearFlowStep ("Offset Smear Flow Step (UV per Sample, XY)", Vector) = (0.02, 0, 0, 0)
        _OffsetSmearUVScale ("Offset Smear UV Scale", Vector) = (1, 1, 0, 0)
        _OffsetSmearPixels ("Offset Smear Distance (Pixels)", Range(-64, 64)) = 4
        _OffsetSmearAngle ("Offset Smear Map Angle Offset (Degrees)", Range(-180, 180)) = 0
        [Header(Motion Oriented Unscaled Scene Distortion)]
        [ToggleUI] _UnscaledSceneDistortionEnabled ("Enable Unscaled Scene Distortion", Float) = 1
        _UnscaledSceneDistortionMap ("Unscaled Distortion Strength Map (R)", 2D) = "gray" {}
        _UnscaledDistortionFlowStep ("Unscaled Distortion Flow Step (UV per Sample, XY)", Vector) = (0.02, 0, 0, 0)
        _UnscaledSceneDistortionThreshold ("Unscaled Distortion Map Cutoff", Range(0, 0.9)) = 0.2
        _UnscaledSceneDistortionPixels ("Unscaled Distortion Distance (Pixels)", Range(-64, 64)) = 8
        _UnscaledSceneDistortionAngle ("Unscaled Distortion Map Angle Offset (Degrees)", Range(-180, 180)) = 0
        _UnscaledMotionMaskMinSpeed ("Unscaled Mask Start Speed (Pixels Per Frame)", Range(0, 32)) = 0.1
        _UnscaledMotionMaskMaxSpeed ("Unscaled Mask Full Speed (Pixels Per Frame)", Range(0, 64)) = 2
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
        TEXTURE2D(_OffsetSmearMap);
        TEXTURE2D(_UnscaledSceneDistortionMap);

        CBUFFER_START(UnityPerMaterial)
            float _DisplayMode;
            float _Sensitivity;
            float _DebugThirdLayerMotion;
            float _DebugOtherLayersMotion;
            float4 _NoiseMap_ST;
            float4 _NoiseTint;
            float _NoiseOpacity;
            float _NoiseContrast;
            float _NoiseAngle;
            float _MotionThreshold;
            float _MotionMaskMinSpeed;
            float _MotionMaskMaxSpeed;
            float _NoiseOutputOnly;
            float _NoiseOnlyIgnoreMask;
            float4 _DistortionMap_ST;
            float _DistortionEnabled;
            float _DistortionNoiseThreshold;
            float _DistortionPixels;
            float _DistortionAngle;
            float _OffsetSmearEnabled;
            float4 _OffsetSmearUVScale;
            float _OffsetSmearPixels;
            float _OffsetSmearAngle;
            float4 _UnscaledSceneDistortionMap_ST;
            float _UnscaledSceneDistortionEnabled;
            float _UnscaledSceneDistortionThreshold;
            float _UnscaledSceneDistortionPixels;
            float _UnscaledSceneDistortionAngle;
            float _UnscaledMotionMaskMinSpeed;
            float _UnscaledMotionMaskMaxSpeed;
            float4 _NoiseFlowStep;
            float4 _DistortionFlowStep;
            float4 _OffsetSmearFlowStep;
            float4 _UnscaledDistortionFlowStep;
        CBUFFER_END

        // Per-camera values are supplied through property blocks, never a shared
        // material/global mutation during deferred Render Graph recording.
        float _UseCachedMotionDirections;
        float _CacheValid;
        float _CacheTime;
        float _HoldLastDirection;
        float _ResetDirectionAfter;
        float4 _NoiseFlowOffset;
        float4 _DistortionFlowOffset;
        float4 _OffsetSmearFlowOffset;
        float4 _UnscaledDistortionFlowOffset;

        float MotionSpeedMask(float pixelSpeed)
        {
            // URP stores signed UV displacement with zero at (0, 0).
            // Keep a nonzero transition width even for equal/reversed settings.
            float minimum = max(_MotionMaskMinSpeed, 0.0);
            float maximum = max(_MotionMaskMaxSpeed, minimum + 0.001);
            return smoothstep(minimum, maximum, pixelSpeed);
        }

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
                // Speed controls coverage continuously, irrespective of direction sign.
                float motionMask = MotionSpeedMask(pixelSpeed);

                float sine, cosine;
                sincos(radians(_NoiseAngle), sine, cosine);
                direction = float2(cosine * direction.x - sine * direction.y,
                                   sine * direction.x + cosine * direction.y);

                // Use an aspect-correct screen plane centered at the image center.
                // Both coordinates and velocity must use the same pixel metric.
                float2 position = (input.positionCS.xy - 0.5 * screenSize) / screenSize.y;
                float2 noiseUV = RotateNoiseUV(position, direction) * _NoiseMap_ST.xy
                               + 0.5 + _NoiseMap_ST.zw + _NoiseFlowOffset.xy;

                // Rotate the footprint as well. Do not use derivatives of the
                // discontinuous direction field at object/velocity boundaries.
                float2 uvDx = RotateNoiseUV(ddx(position), direction) * _NoiseMap_ST.xy;
                float2 uvDy = RotateNoiseUV(ddy(position), direction) * _NoiseMap_ST.xy;
                float noise = SAMPLE_TEXTURE2D_GRAD(_NoiseMap, sampler_LinearRepeat,
                                                    noiseUV, uvDx, uvDy).r;
                noise = saturate((noise - 0.5) * _NoiseContrast + 0.5);

                // Both noise-only views can reveal the full mapped texture.
                // Overlay coverage still uses the original motion mask.
                float noiseOnlyMask = _NoiseOnlyIgnoreMask > 0.5 ? 1.0 : motionMask;
                if (_DisplayMode > 3.5)
                    return float4((noise * noiseOnlyMask).xxx, 1.0);

                // Alpha=1 fully replaces scene RGB in the existing blend state.
                // Keep mapping, contrast and tint, but ignore overlay opacity.
                if (_NoiseOutputOnly > 0.5)
                    return float4(noise * _NoiseTint.rgb * noiseOnlyMask, 1.0);

                float3 contribution = noise * motionMask * _NoiseTint.rgb
                                    * saturate(_NoiseOpacity * _NoiseTint.a);
                return float4(contribution, 0.0);
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // The renderer binds the third layer's current snapshot when
                // requested; otherwise this is the other layers' published MV.
                float2 motion;
                if (_UseCachedMotionDirections > 0.5)
                    motion = SAMPLE_TEXTURE2D_X_LOD(
                        _CachedMotionDirections, sampler_PointClamp, input.texcoord, 0).rg;
                else
                    motion = SAMPLE_TEXTURE2D_X_LOD(
                        _MotionVectorTexture, sampler_PointClamp, input.texcoord, 0).rg;

                // Alpha=1 replaces the scene. Source debug takes precedence over
                // Display Mode and noise-only output, with no noise/speed mask.
                if (_DebugThirdLayerMotion > 0.5 || _DebugOtherLayersMotion > 0.5)
                    return float4(saturate(0.5 + motion * max(_Sensitivity, 0.0)), 0.5, 1.0);

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
                             + 0.5 + _DistortionMap_ST.zw + _DistortionFlowOffset.xy;
                float2 uvDx = RotateNoiseUV(ddx(position), mapDirection) * _DistortionMap_ST.xy;
                float2 uvDy = RotateNoiseUV(ddy(position), mapDirection) * _DistortionMap_ST.xy;
                float rawStrength = SAMPLE_TEXTURE2D_GRAD(
                    _DistortionMap, sampler_LinearRepeat, mapUV, uvDx, uvDy).r;
                float strength = saturate((rawStrength - _DistortionNoiseThreshold) /
                                          max(1.0 - _DistortionNoiseThreshold, 1e-5));

                // Backward sampling moves visible features along the MV direction.
                // Magnitude is controlled by the separate map and a pixel distance.
                float2 offsetUV = direction * (_DistortionPixels * strength * MotionSpeedMask(speed)) / screenSize;
                float2 halfTexel = 0.5 / screenSize;
                float2 sceneUV = clamp(input.texcoord - offsetUV, halfTexel, 1.0 - halfTexel);
                // _BlitTexture is a copy of this frame's scene BEFORE this effect.
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, sceneUV, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Motion Oriented Offset Smear"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero, Zero One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DistortOffsetSmear

            float4 DistortOffsetSmear(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenSize = max(_ScaledScreenParams.xy, float2(1, 1));
                float2 uvScale = _OffsetSmearUVScale.xy;
                float2 halfTexel = 0.5 / screenSize;
                float2 scaledSceneUV = 0.5 + (input.texcoord - 0.5) * uvScale;
                scaledSceneUV = clamp(scaledSceneUV, halfTexel, 1.0 - halfTexel);

                // The sampled color and its motion vector must describe the same
                // scaled screen-space location.
                float2 motion = SAMPLE_TEXTURE2D_X_LOD(
                    _CachedMotionDirections, sampler_PointClamp, scaledSceneUV, 0).rg;
                float2 pixels = motion * screenSize;
                float speed = length(pixels);
                float2 direction = pixels / max(speed, 1e-6);
                float2 mapDirection = speed > 0 ? direction : float2(1, 0);
                float sine, cosine;
                sincos(radians(_OffsetSmearAngle), sine, cosine);
                mapDirection = float2(cosine * mapDirection.x - sine * mapDirection.y,
                                      sine * mapDirection.x + cosine * mapDirection.y);

                // Scale the screen-space noise pattern around the same center.
                float2 position = (input.positionCS.xy - 0.5 * screenSize) / screenSize.y;
                float2 scaledPosition = position * uvScale;
                float2 rotatedPosition = RotateNoiseUV(scaledPosition, mapDirection);
                float2 offsetNoiseUV = rotatedPosition + 0.5 + _OffsetSmearFlowOffset.xy;
                float2 scaledUVdx = RotateNoiseUV(ddx(scaledPosition), mapDirection);
                float2 scaledUVdy = RotateNoiseUV(ddy(scaledPosition), mapDirection);
                float offsetNoise = saturate(SAMPLE_TEXTURE2D_GRAD(
                    _OffsetSmearMap, sampler_LinearRepeat,
                    offsetNoiseUV, scaledUVdx, scaledUVdy).r);

                float motionMask = MotionSpeedMask(speed);
                float2 offsetUV = direction * (_OffsetSmearPixels * offsetNoise * motionMask) / screenSize;
                float2 offsetSceneUV = clamp(scaledSceneUV - offsetUV, halfTexel, 1.0 - halfTexel);

                // Undo the centered screen-UV scale to find the unscaled screen
                // location represented by this scaled/offset scene sample.
                // Guard zero scale components to avoid division by zero.
                float2 safeUVScale = float2(
                    abs(uvScale.x) > 1e-4 ? uvScale.x : (uvScale.x < 0 ? -1e-4 : 1e-4),
                    abs(uvScale.y) > 1e-4 ? uvScale.y : (uvScale.y < 0 ? -1e-4 : 1e-4));
                float2 correspondingUV = 0.5 + (offsetSceneUV - 0.5) / safeUVScale;
                correspondingUV = clamp(correspondingUV, halfTexel, 1.0 - halfTexel);
                float2 correspondingPosition = (correspondingUV * screenSize - 0.5 * screenSize)
                                             / screenSize.y;
                float originalNoise = 0.0;
                if (_DistortionEnabled > 0.5 && abs(_DistortionPixels) > 0.0001)
                {
                    float2 correspondingMotion = SAMPLE_TEXTURE2D_X_LOD(
                        _CachedMotionDirections, sampler_PointClamp, correspondingUV, 0).rg;
                    float2 correspondingPixels = correspondingMotion * screenSize;
                    float correspondingSpeed = length(correspondingPixels);
                    float2 originalMapDirection = correspondingSpeed > 0
                        ? correspondingPixels / max(correspondingSpeed, 1e-6)
                        : float2(1, 0);
                    sincos(radians(_DistortionAngle), sine, cosine);
                    originalMapDirection = float2(cosine * originalMapDirection.x - sine * originalMapDirection.y,
                                                  sine * originalMapDirection.x + cosine * originalMapDirection.y);
                    float2 originalNoiseUV = RotateNoiseUV(correspondingPosition, originalMapDirection)
                                           * _DistortionMap_ST.xy + 0.5 + _DistortionMap_ST.zw
                                           + _DistortionFlowOffset.xy;
                    float2 originalNoiseDx = RotateNoiseUV(ddx(correspondingPosition), originalMapDirection)
                                           * _DistortionMap_ST.xy;
                    float2 originalNoiseDy = RotateNoiseUV(ddy(correspondingPosition), originalMapDirection)
                                           * _DistortionMap_ST.xy;
                    float rawOriginalNoise = SAMPLE_TEXTURE2D_GRAD(
                        _DistortionMap, sampler_LinearRepeat,
                        originalNoiseUV, originalNoiseDx, originalNoiseDy).r;
                    originalNoise = saturate((rawOriginalNoise - _DistortionNoiseThreshold) /
                                             max(1.0 - _DistortionNoiseThreshold, 1e-5));
                }
                float allowOffsetSmear = 1.0 - step(0.00001, originalNoise);
                float blend = saturate(offsetNoise * motionMask * allowOffsetSmear);

                float4 sceneColor = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture, sampler_LinearClamp, input.texcoord, 0);
                float4 offsetColor = SAMPLE_TEXTURE2D_X_LOD(
                    _BlitTexture, sampler_LinearClamp, offsetSceneUV, 0);
                // Blend the scaled/offset sample over the unscaled scene only
                // where the first layer's distortion mask is empty.
                return lerp(sceneColor, offsetColor, blend);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Motion Oriented Unscaled Scene Distortion"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend One Zero, Zero One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment DistortSceneUnscaled

            float4 DistortSceneUnscaled(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenSize = max(_ScaledScreenParams.xy, float2(1, 1));

                // This pass is bound to the current animation sample MV(N),
                // held between animation ticks. Sample it at the unscaled UV.
                float2 motion = SAMPLE_TEXTURE2D_X_LOD(
                    _CachedMotionDirections, sampler_PointClamp, input.texcoord, 0).rg;
                float2 pixels = motion * screenSize;
                float speed = length(pixels);
                float2 direction = pixels / max(speed, 1e-6);
                float2 mapDirection = speed > 0 ? direction : float2(1, 0);
                float sine, cosine;
                sincos(radians(_UnscaledSceneDistortionAngle), sine, cosine);
                mapDirection = float2(cosine * mapDirection.x - sine * mapDirection.y,
                                      sine * mapDirection.x + cosine * mapDirection.y);

                float2 position = (input.positionCS.xy - 0.5 * screenSize) / screenSize.y;
                float2 mapUV = RotateNoiseUV(position, mapDirection) * _UnscaledSceneDistortionMap_ST.xy
                             + 0.5 + _UnscaledSceneDistortionMap_ST.zw + _UnscaledDistortionFlowOffset.xy;
                float2 uvDx = RotateNoiseUV(ddx(position), mapDirection) * _UnscaledSceneDistortionMap_ST.xy;
                float2 uvDy = RotateNoiseUV(ddy(position), mapDirection) * _UnscaledSceneDistortionMap_ST.xy;
                float rawStrength = SAMPLE_TEXTURE2D_GRAD(
                    _UnscaledSceneDistortionMap, sampler_LinearRepeat, mapUV, uvDx, uvDy).r;
                float strength = saturate((rawStrength - _UnscaledSceneDistortionThreshold) /
                                          max(1.0 - _UnscaledSceneDistortionThreshold, 1e-5));

                float minimum = max(_UnscaledMotionMaskMinSpeed, 0.0);
                float maximum = max(_UnscaledMotionMaskMaxSpeed, minimum + 0.001);
                float motionMask = smoothstep(minimum, maximum, speed);
                float2 offsetUV = direction *
                    (_UnscaledSceneDistortionPixels * strength * motionMask) / screenSize;
                float2 halfTexel = 0.5 / screenSize;
                float2 sceneUV = clamp(input.texcoord - offsetUV, halfTexel, 1.0 - halfTexel);
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, sceneUV, 0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}

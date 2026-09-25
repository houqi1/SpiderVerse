Shader "Custom/UVDots"
{
    Properties
    {
        [MainColor][HDR] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Opacity ("Overall Opacity", Range(0, 1)) = 1
        _OpacityAdd ("Opacity Add (Noise)", Range(0, 1)) = 0
        [Toggle] _UseAnchorOpacityFalloff ("Blend Center Opacity To Dot Mask", Float) = 0
        _AnchorCenterOpacity ("Center Opacity", Range(0, 1)) = 1
        _AnchorOpacityFalloffStart ("Center UV Radius", Range(0, 1)) = 0
        _AnchorOpacityFalloffEnd ("Texture Blend End UV Radius", Range(0.001, 1)) = 1
        [Toggle] _UseSampledSurfaceColor ("Sample Character Color", Float) = 1
        _ColorSampleNormalOffset ("Color Sample Normal Offset (World Units)", Float) = 0
        _DotDensity ("Dot Density", Float) = 10
        _DotSize ("Dot Size", Range(0, 0.5)) = 0.03
        [Toggle] _UseDotTexture ("Use Black/White Dot Texture", Float) = 0
        _DotTexture ("Dot Texture (White = Visible)", 2D) = "white" {}
        [Toggle] _InvertDots ("Invert Dots", Float) = 0
        _NoiseMap ("Noise", 2D) = "gray" {}
        _NoiseThreshold ("Dissolve", Range(0, 1)) = 0
        [ToggleUI] _UseRadialDissolve ("Use Radial Dissolve", Float) = 1
        _DissolveEdgeSoftness ("Dissolve Edge Softness", Range(0, 2)) = 0.05
        _ParticleDissolveVariation ("Per-Particle Dissolve Variation", Range(0, 1)) = 0.25
        _DissolveIrregularity ("Dissolve Irregularity", Range(0, 1)) = 0.65
        [ToggleUI] _DisableDepthFade ("Disable Depth Fade", Float) = 0
        _DepthFade ("Depth Fade", Float) = 1
        [HideInInspector] _SurfaceCameraPosition ("Surface Camera Position", Vector) = (0, 0, 0, 1)
        [HideInInspector] _SurfaceCameraForward ("Surface Camera Forward", Vector) = (0, 0, 1, 0)
        [HideInInspector] _SurfaceCameraInfluence ("Surface Camera Influence", Vector) = (0, 1, 0.15, 0.35)

        [Header(Surface)]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth Test (ZTest)", Float) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_local _ SURFACE_NOISE_GPU
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "SurfaceNoiseColorSampling.hlsl"

            TEXTURE2D(_NoiseMap);
            SAMPLER(sampler_NoiseMap);
            TEXTURE2D(_DotTexture);
            SAMPLER(sampler_DotTexture);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Opacity;
                half _OpacityAdd;
                float _UseAnchorOpacityFalloff;
                float _AnchorCenterOpacity;
                float _AnchorOpacityFalloffStart;
                float _AnchorOpacityFalloffEnd;
                float _UseSampledSurfaceColor;
                float _ColorSampleNormalOffset;
                float _DotDensity;
                float _DotSize;
                float _UseDotTexture;
                float4 _DotTexture_ST;
                half _InvertDots;
                float4 _NoiseMap_ST;
                half _NoiseThreshold;
                float _UseRadialDissolve;
                float _DissolveEdgeSoftness;
                half _ParticleDissolveVariation;
                half _DissolveIrregularity;
                float _DisableDepthFade;
                float _DepthFade;
                float4 _SurfaceCameraPosition;
                float4 _SurfaceCameraForward;
                float4 _SurfaceCameraInfluence;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float particleSeed : TEXCOORD1;
                nointerpolation half viewInfluence : TEXCOORD2;
                nointerpolation float2 anchorScreenUV : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float SampleParticleNoise(float2 uv, float particleSeed)
            {
                float2 noiseUV = uv * _NoiseMap_ST.xy + _NoiseMap_ST.zw +
                    particleSeed * float2(19.19, 37.71);
                return SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseUV).r;
            }

            // White dots on black, or the reverse. Density is dots per UV unit, size is radius in UV units.
            void DotMask(float2 uv, float particleSeed, out float dotOpacity, out float dissolveFade)
            {
                float density = max(_DotDensity, 1e-4);
                float2 cell = frac(uv * density) - 0.5;
                float dist = length(cell) / density;
                float aa = max(fwidth(dist), 1e-5);
                float radius = max(_DotSize, 0.0);
                float proceduralMask = 1.0 - smoothstep(radius - aa, radius + aa, dist);
                float textureMask = SAMPLE_TEXTURE2D(
                    _DotTexture, sampler_DotTexture, TRANSFORM_TEX(uv, _DotTexture)).r;
                // White texels keep particles; black texels remove them. Grays stay antialiased.
                float mask = lerp(proceduralMask, textureMask, step(0.5, _UseDotTexture));
                // Procedural dots dissolve per cell; texture masks dissolve continuously.
                // Optional radial shaping makes the dissolve progress from outside inward.
                float2 cellCenter = (floor(uv * density) + 0.5) / density;
                float2 dissolveUV = lerp(cellCenter, uv, step(0.5, _UseDotTexture));
                float radial = saturate(length((dissolveUV - 0.5) * 2.0));
                float noise = SampleParticleNoise(dissolveUV, particleSeed);
                float field = noise;
                if (_UseRadialDissolve > 0.5)
                    field = (1.0 - radial) + (noise - 0.5) * _DissolveIrregularity;
                float perParticleThreshold = _NoiseThreshold +
                    (particleSeed - 0.5) * _ParticleDissolveVariation;
                float edgeWidth = max(_DissolveEdgeSoftness, fwidth(field));
                dissolveFade = smoothstep(perParticleThreshold - edgeWidth,
                    perParticleThreshold + edgeWidth, field);
                dotOpacity = mask;
                if (_InvertDots > 0.5h)
                {
                    // Preserve inversion of the complete mask, including dissolution.
                    dissolveFade = 1.0 - mask * dissolveFade;
                    dotOpacity = 1.0;
                }
            }

#if defined(SURFACE_NOISE_GPU)
            struct SurfaceParticle
            {
                float4 positionSize;
                float4 color;
                float4 normalWS;
                float4 surfacePosition;
                float4 distributionNormal;
            };
            StructuredBuffer<SurfaceParticle> _SurfaceParticles;
            #include "SurfaceNoiseDistribution.hlsl"
            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings output = (Varyings)0;
                SurfaceParticle p = SelectSurfaceDistributionParticle(instanceID);
                const float2 corners[6] = {
                    float2(-0.5,-0.5), float2(-0.5,0.5), float2(0.5,0.5),
                    float2(-0.5,-0.5), float2(0.5,0.5), float2(0.5,-0.5)
                };
                float2 corner = corners[vertexID];
                float3 right = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 up = UNITY_MATRIX_I_V._m01_m11_m21;
                float influenceEnabled = step(0.5, _SurfaceCameraInfluence.x);
                float3 cameraForward = normalize(_SurfaceCameraForward.xyz);
                float3 toCamera = _SurfaceCameraPosition.xyz - p.positionSize.xyz;
                float toCameraLengthSq = dot(toCamera, toCamera);
                float3 perspectiveView = toCameraLengthSq > 1e-8
                    ? toCamera * rsqrt(toCameraLengthSq)
                    : -cameraForward;
                float3 viewDirection = _SurfaceCameraForward.w > 0.5 ? -cameraForward : perspectiveView;
                float facing = dot(normalize(p.normalWS.xyz), viewDirection);
                float frontFacing = smoothstep(-max(_SurfaceCameraInfluence.z, 0.01),
                    max(_SurfaceCameraInfluence.z, 0.01), facing);
                output.viewInfluence = lerp(1.0, frontFacing,
                    influenceEnabled * saturate(_SurfaceCameraInfluence.y));
                float silhouette = 1.0 - abs(facing);
                float sizeMultiplier = 1.0 + silhouette * _SurfaceCameraInfluence.w * influenceEnabled;
                float3 positionWS = p.positionSize.xyz +
                    (right * corner.x + up * corner.y) * (p.positionSize.w * sizeMultiplier);
                output.positionCS = TransformWorldToHClip(positionWS);
                // This offset only moves the screen-color lookup point. The billboard
                // geometry continues to use positionWS above, so its position is unchanged.
                float3 colorSamplePositionWS = p.surfacePosition.xyz +
                    SafeNormalize(p.normalWS.xyz) * _ColorSampleNormalOffset;
                float4 anchorPositionCS = TransformWorldToHClip(colorSamplePositionWS);
                // Convert clip space to 0..1 screen UV: perspective divide, NDC
                // remap, and platform render-target Y flip. SampleSceneColor applies
                // XR eye mapping and RTHandle scaling later.
                float4 anchorScreenPos = ComputeScreenPos(anchorPositionCS);
                output.anchorScreenUV = anchorScreenPos.xy / anchorScreenPos.w;
                output.uv = corner + 0.5;
                uint h = instanceID * 747796405u + 2891336453u;
                h = ((h >> ((h >> 28u) + 4u)) ^ h) * 277803737u;
                h = (h >> 22u) ^ h;
                output.particleSeed = (h & 0x00ffffffu) / 16777215.0;
                return output;
            }
#else
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.particleSeed = 0.0;
                output.viewInfluence = 1.0;
                output.anchorScreenUV = 0.0;
                return output;
            }
#endif

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float dotOpacity, dissolveFade;
                DotMask(input.uv, input.particleSeed, dotOpacity, dissolveFade);
                float mask = dotOpacity * dissolveFade;
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS.xy);
                float sceneEye = SurfaceParticleEyeDepth(SampleSceneDepth(screenUV));
                float selfEye = SurfaceParticleEyeDepth(input.positionCS.z);
                float fade = 1.0;
                if (_DisableDepthFade < 0.5)
                    fade = saturate((sceneEye - selfEye) / max(_DepthFade, 1e-4));
                half3 particleColor = _BaseColor.rgb;
                half colorVisibility = 1.0h;
#if defined(SURFACE_NOISE_GPU)
                if (_UseSampledSurfaceColor > 0.5)
                {
                    if (_SurfaceColorFieldEnabled > 0.5)
                        particleColor = SampleSurfaceColorField(input.positionCS.xy, sceneEye, colorVisibility);
                    else
                        particleColor = SampleSceneColor(input.anchorScreenUV);
                }
#endif

                float particleOpacity = dotOpacity;
                if (_UseAnchorOpacityFalloff > 0.5)
                {
                    // UV (0.5, 0.5) is the billboard anchor. Keep the requested
                    // center opacity, then blend to the texture-driven dot mask.
                    float radius = saturate(length((input.uv - 0.5) * 1.41421356));
                    float fadeStart = min(saturate(_AnchorOpacityFalloffStart), 0.999);
                    float fadeEnd = max(saturate(_AnchorOpacityFalloffEnd), fadeStart + 1e-3);
                    float textureBlend = smoothstep(fadeStart, fadeEnd, radius);
                    particleOpacity = lerp(_AnchorCenterOpacity, dotOpacity, textureBlend);
                }

                // Sample before discard so texture derivatives remain available.
                float opacityNoise = saturate(SampleParticleNoise(input.uv, input.particleSeed));

                // Visibility is determined solely by the original dot/dissolve mask.
                // Neither center opacity nor additive opacity can restore clipped texels.
                // Discard only zero coverage. Apply the soft dissolve AFTER opacity
                // adjustments so they cannot lift its zero-alpha boundary into a hard edge.
                clip(mask > 0.0h ? 1.0h : -1.0h);
                particleOpacity = saturate(particleOpacity + _OpacityAdd * opacityNoise);

                half alpha = particleOpacity * dissolveFade * _BaseColor.a * _Opacity * fade * input.viewInfluence * colorVisibility;
                return half4(particleColor, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

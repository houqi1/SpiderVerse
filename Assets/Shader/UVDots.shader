Shader "Custom/UVDots"
{
    Properties
    {
        [MainColor][HDR] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _Opacity ("Overall Opacity", Range(0, 1)) = 1
        _DotDensity ("Dot Density", Float) = 10
        _DotSize ("Dot Size", Range(0, 0.5)) = 0.03
        [Toggle] _UseDotTexture ("Use Black/White Dot Texture", Float) = 0
        _DotTexture ("Dot Texture (White = Visible)", 2D) = "white" {}
        [Toggle] _InvertDots ("Invert Dots", Float) = 0
        _NoiseMap ("Noise", 2D) = "gray" {}
        _NoiseThreshold ("Dissolve", Range(0, 1)) = 0
        _ParticleDissolveVariation ("Per-Particle Dissolve Variation", Range(0, 1)) = 0.25
        _DissolveIrregularity ("Dissolve Irregularity", Range(0, 1)) = 0.65
        _DepthFade ("Depth Fade", Float) = 1

        [Header(Surface)]
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

            TEXTURE2D(_NoiseMap);
            SAMPLER(sampler_NoiseMap);
            TEXTURE2D(_DotTexture);
            SAMPLER(sampler_DotTexture);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Opacity;
                float _DotDensity;
                float _DotSize;
                float _UseDotTexture;
                float4 _DotTexture_ST;
                half _InvertDots;
                float4 _NoiseMap_ST;
                half _NoiseThreshold;
                half _ParticleDissolveVariation;
                half _DissolveIrregularity;
                float _DepthFade;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // White dots on black, or the reverse. Density is dots per UV unit, size is radius in UV units.
            float DotMask(float2 uv, float particleSeed)
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
                // Procedural dots dissolve per cell. A custom texture dissolves as one
                // continuous particle mask, from its outside edge toward the center.
                float2 cellCenter = (floor(uv * density) + 0.5) / density;
                float2 dissolveUV = lerp(cellCenter, uv, step(0.5, _UseDotTexture));
                float radial = saturate(length((dissolveUV - 0.5) * 2.0));
                float2 noiseUV = dissolveUV * _NoiseMap_ST.xy + _NoiseMap_ST.zw +
                    particleSeed * float2(19.19, 37.71);
                float noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseUV).r;
                float field = (1.0 - radial) + (noise - 0.5) * _DissolveIrregularity;
                float perParticleThreshold = _NoiseThreshold +
                    (particleSeed - 0.5) * _ParticleDissolveVariation;
                mask *= step(perParticleThreshold, field);
                return _InvertDots > 0.5h ? 1.0 - mask : mask;
            }

#if defined(SURFACE_NOISE_GPU)
            struct SurfaceParticle
            {
                float4 positionSize;
                float4 color;
            };
            StructuredBuffer<SurfaceParticle> _SurfaceParticles;
            Varyings Vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
            {
                Varyings output = (Varyings)0;
                SurfaceParticle p = _SurfaceParticles[instanceID];
                const float2 corners[6] = {
                    float2(-0.5,-0.5), float2(-0.5,0.5), float2(0.5,0.5),
                    float2(-0.5,-0.5), float2(0.5,0.5), float2(0.5,-0.5)
                };
                float2 corner = corners[vertexID];
                float3 right = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 up = UNITY_MATRIX_I_V._m01_m11_m21;
                float3 positionWS = p.positionSize.xyz +
                    (right * corner.x + up * corner.y) * p.positionSize.w;
                output.positionCS = TransformWorldToHClip(positionWS);
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
                return output;
            }
#endif

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half mask = DotMask(input.uv, input.particleSeed);
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float selfEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float fade = saturate((sceneEye - selfEye) / max(_DepthFade, 1e-4));
                return half4(_BaseColor.rgb, mask * _BaseColor.a * _Opacity * fade);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

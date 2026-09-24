Shader "Custom/Character Noise Billboard"
{
    Properties
    {
        [HDR] _Color ("Particle Tint", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha Clip", Range(0, 1)) = 0.32
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.12
        [HideInInspector] _SurfaceCameraPosition ("Surface Camera Position", Vector) = (0, 0, 0, 1)
        [HideInInspector] _SurfaceCameraForward ("Surface Camera Forward", Vector) = (0, 0, 1, 0)
        [HideInInspector] _SurfaceCameraInfluence ("Surface Camera Influence", Vector) = (0, 1, 0.15, 0.35)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }

        Pass
        {
            Name "ParticleDepthClip"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZTest LEqual
            ZWrite On
            Blend One Zero

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_local _ SURFACE_NOISE_GPU
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Cutoff;
                half _EdgeSoftness;
                float4 _SurfaceCameraPosition;
                float4 _SurfaceCameraForward;
                float4 _SurfaceCameraInfluence;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                nointerpolation half viewInfluence : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

#if defined(SURFACE_NOISE_GPU)
            struct SurfaceParticle
            {
                float4 positionSize;
                float4 color;
                float4 normalWS;
                float4 surfacePosition;
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
                // View-plane axes affect the quad only; the world-space center stays fixed.
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
                output.uv = corner + 0.5;
                output.color = p.color * _Color;
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
                output.color = input.color * _Color;
                output.viewInfluence = 1.0;
                return output;
            }
#endif

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 centered = input.uv * 2.0 - 1.0;
                float inside = 1.0 - dot(centered, centered);
                float coverage = smoothstep(0.0, max(_EdgeSoftness, 1e-4), inside);
                clip(coverage * input.color.a * input.viewInfluence - _Cutoff);
                return half4(input.color.rgb, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

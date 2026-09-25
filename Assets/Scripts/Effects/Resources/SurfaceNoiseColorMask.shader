Shader "Hidden/SpiderVerse/SurfaceNoiseColorMask"
{
    Properties { _Cull ("Cull", Float) = 2 }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Cull [_Cull]
            ZWrite Off
            ZTest Always
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            float EyeDepth(float raw)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    raw = 1.0 - raw;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }
            float Frag(Varyings input) : SV_Target
            {
                float scene = EyeDepth(LoadSceneDepth(uint2(input.positionCS.xy)));
                float self = EyeDepth(input.positionCS.z);
                // Equality with scene depth excludes both external occluders and hidden
                // back surfaces. The mask stores depth for discontinuity-safe seeds.
                clip(max(0.0002, scene * 0.00002) - abs(scene - self));
                return self;
            }
            ENDHLSL
        }
    }
}

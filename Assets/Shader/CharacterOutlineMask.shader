Shader "Hidden/Custom/CharacterOutlineMask"
{
    Properties
    {
        _NormalExtrusion ("Normal Extrusion", Float) = 0
        _UseSmoothNormalVC ("Use Smooth Normal Vertex Color", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "CharacterMaskRed"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Outside UnityPerMaterial so per-draw SetGlobal* works with RendererList.
            float _NormalExtrusion;
            float _UseSmoothNormalVC;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float3 GetExtrusionNormalOS(Attributes input)
            {
                float3 normalOS = input.normalOS;
                float nLen = length(normalOS);
                if (nLen > 1e-6)
                    normalOS /= nLen;
                else
                    normalOS = float3(0.0, 0.0, 1.0);

                if (_UseSmoothNormalVC < 0.5)
                    return normalOS;

                float3 smoothTS = input.color.rgb * 2.0 - 1.0;
                float tsLen = length(smoothTS);
                if (tsLen < 1e-6)
                    return normalOS;
                smoothTS /= tsLen;

                float3 tangentOS = input.tangentOS.xyz;
                float tLen = length(tangentOS);
                if (tLen < 1e-6)
                    return normalOS;
                tangentOS /= tLen;

                float3 bitangentOS = cross(normalOS, tangentOS) * input.tangentOS.w * unity_WorldTransformParams.w;
                float bLen = length(bitangentOS);
                if (bLen > 1e-6)
                    bitangentOS /= bLen;

                float3 smoothOS = tangentOS * smoothTS.x + bitangentOS * smoothTS.y + normalOS * smoothTS.z;
                float sLen = length(smoothOS);
                return sLen > 1e-6 ? smoothOS / sLen : normalOS;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posOS = input.positionOS.xyz;
                float3 n = GetExtrusionNormalOS(input);
                posOS += n * _NormalExtrusion;

                output.positionCS = TransformObjectToHClip(posOS);
                // Store raw mesh UV0; per-layer tiling/offset applied in composite.
                output.uv0 = input.uv0;
                return output;
            }

            // R = coverage, G = mesh UV0.x, B = mesh UV0.y
            float4 Frag(Varyings input) : SV_Target
            {
                return float4(1.0, saturate(input.uv0.x), saturate(input.uv0.y), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}

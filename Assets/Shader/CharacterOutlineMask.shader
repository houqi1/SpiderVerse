Shader "Hidden/Custom/CharacterOutlineMask"
{
    Properties
    {
        _NormalExtrusion ("Normal Extrusion", Float) = 0
        _DepthOffset ("Depth Offset", Float) = 0
        _UseSmoothNormalVC ("Use Smooth Normal Vertex Color", Float) = 0
        _OutlineControlMap ("Outline Control Map", 2D) = "white" {}
        _OutlineControlEnabled ("Outline Control Enabled", Float) = 0
        _ExtrusionNoiseStrength ("Extrusion Noise Strength", Float) = 0
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
            float _DepthOffset;
            float _UseSmoothNormalVC;
            float _OutlineControlEnabled;
            float _ExtrusionNoiseStrength;
            float4 _OutlineControlMap_ST;

            TEXTURE2D(_OutlineControlMap);
            SAMPLER(sampler_OutlineControlMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv1 : TEXCOORD0;
                float fresnel : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Extrusion scale only: raw noise * ST. No threshold / scroll / invert.
            float SampleExtrusionNoise(float2 meshUV)
            {
                if (_OutlineControlEnabled < 0.5)
                    return 1.0;

                float2 noiseUV = meshUV * _OutlineControlMap_ST.xy + _OutlineControlMap_ST.zw;
                return saturate(SAMPLE_TEXTURE2D_LOD(_OutlineControlMap, sampler_OutlineControlMap, noiseUV, 0).r);
            }

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
                float noise = SampleExtrusionNoise(input.uv1);
                // 0 = uniform extrusion. 1 = scale in [0, 2]. Unbounded strength
                // swings around the base amount: scale = 1 + (2*noise - 1) * strength.
                float scale = 1.0 + (noise * 2.0 - 1.0) * _ExtrusionNoiseStrength;
                posOS += n * (_NormalExtrusion * scale);

                float3 posWS = TransformObjectToWorld(posOS);
                float3 nWS = TransformObjectToWorldNormal(n);
                float nLen = length(nWS);
                nWS = nLen > 1e-6 ? nWS / nLen : float3(0.0, 0.0, 1.0);
                float3 viewWS = _WorldSpaceCameraPos.xyz - posWS;
                float vLen = length(viewWS);
                viewWS = vLen > 1e-6 ? viewWS / vLen : float3(0.0, 0.0, 1.0);
                // 0 = facing camera (center), 1 = grazing (rim).
                output.fresnel = saturate(1.0 - saturate(dot(nWS, viewWS)));

                // Per-layer depth bias along view axis (meters). Positive = toward camera.
                posWS += viewWS * _DepthOffset;
                output.positionCS = TransformWorldToHClip(posWS);
                output.uv1 = input.uv1;
                return output;
            }

            // R = coverage, G = mesh UV1.x, B = mesh UV1.y, A = raw fresnel (1 - N·V)
            float4 Frag(Varyings input) : SV_Target
            {
                return float4(1.0, saturate(input.uv1.x), saturate(input.uv1.y), saturate(input.fresnel));
            }
            ENDHLSL
        }
    }

    FallBack Off
}

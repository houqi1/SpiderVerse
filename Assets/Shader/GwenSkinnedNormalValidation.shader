Shader "Hidden/Gwen/SkinnedNormalValidation"
{
    Properties { _NormalAtlas("Normal Atlas",2D)="bump"{} _Mode("Mode",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SkinnedNormalBasis.hlsl"
            TEXTURE2D(_NormalAtlas); SAMPLER(sampler_NormalAtlas);
            CBUFFER_START(UnityPerMaterial)
            float _Mode;
            CBUFFER_END
            struct A {float4 p:POSITION;float3 n:NORMAL;float4 t:TANGENT;float2 uv:TEXCOORD1;};
            struct V {float4 p:SV_POSITION;float3 n:TEXCOORD0;float4 t:TEXCOORD1;float2 uv:TEXCOORD2;};
            V vert(A a) {V o;o.p=TransformObjectToHClip(a.p.xyz);o.n=TransformObjectToWorldNormal(a.n);o.t=float4(TransformObjectToWorldDir(a.t.xyz),a.t.w*GetOddNegativeScale());o.uv=a.uv;return o;}
            half4 frag(V i):SV_Target {
                float3 ts=SAMPLE_TEXTURE2D(_NormalAtlas,sampler_NormalAtlas,i.uv).rgb*2-1;
                float3 n=ReviewedNormalTangentToWorld(ts,i.n,i.t);
                if(_Mode<.5) return half4(n*.5+.5,1);
                float v=saturate(dot(n,normalize(float3(-.6,.7,.8))))*.8+.12;
                return half4(v,v,v,1);
            }
            ENDHLSL
        }
    }
}

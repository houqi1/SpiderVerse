// Linear-progress threshold reveal.
// u = CDF(v) (histogram equalisation, baked into _CdfLut) makes u ~ Uniform[0,1],
// so step(1 - _Progress, u) covers exactly _Progress of the pixels -> constant-speed change.
Shader "Custom/LinearThreshold"
{
    Properties
    {
        _MainTex   ("Source Value Texture (R)", 2D) = "white" {}
        _CdfLut    ("CDF LUT (baked, 512x1, indexed by sqrt(v), linear, clamp)", 2D) = "white" {}
        _Progress  ("Progress", Range(0, 1)) = 0
        _Softness  ("Edge Softness (in equalised space)", Range(0, 0.1)) = 0
        [Toggle] _UseLut ("Use CDF LUT (off = raw value, for comparison)", Float) = 1
        [Toggle] _Invert ("Reveal dark first", Float) = 0
        _ColorOn  ("Color On",  Color) = (1, 1, 1, 1)
        _ColorOff ("Color Off", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_CdfLut);  SAMPLER(sampler_CdfLut);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _ColorOn, _ColorOff;
                float  _Progress, _Softness, _UseLut, _Invert;
            CBUFFER_END

            struct Attr { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V2F  { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            V2F vert(Attr i)
            {
                V2F o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.uv  = TRANSFORM_TEX(i.uv, _MainTex);
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                float v = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).r;

                // Remap through CDF. LUT is indexed by sqrt(v) to give the dense dark range more texels.
                // Half-texel offset so v=0/1 hit LUT texel centres (LUT is 512 wide).
                float lutU = sqrt(saturate(v)) * (511.0 / 512.0) + 0.5 / 512.0;
                float u = SAMPLE_TEXTURE2D_LOD(_CdfLut, sampler_CdfLut, float2(lutU, 0.5), 0).r;
                u = lerp(v, u, _UseLut);
                u = lerp(u, 1.0 - u, _Invert);

                float t = 1.0 - _Progress;            // bright (high u) pixels appear first
                float m = _Softness > 1e-5
                    ? smoothstep(t - _Softness, t + _Softness, u)
                    : step(t, u);
                return lerp(_ColorOff, _ColorOn, m);
            }
            ENDHLSL
        }
    }
}

Shader "SpiderVerse/Gwen Painterly Object Space Preview"
{
    Properties
    {
        _BaseMap("Base color", 2D) = "white" {}
        [Toggle] _ColorUsesUV1("Use painterly atlas UV1 for base color", Float) = 0
        _BaseColor("Color", Color) = (1,1,1,1)
        _NormalOS("Object-space normal atlas (UV1, linear RGB)", 2D) = "white" {}
        _Strength("Brush strength", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "PainterlyPreview"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalOS); SAMPLER(sampler_NormalOS);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Strength;
                half _ColorUsesUV1;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; float2 normalUV:TEXCOORD1; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 normalUV:TEXCOORD1; float3 normalOS:TEXCOORD2; };
            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS=TransformObjectToHClip(i.positionOS.xyz);
                o.uv=TRANSFORM_TEX(i.uv,_BaseMap);
                o.normalUV=i.normalUV;
                o.normalOS=i.normalOS;
                return o;
            }
            half4 frag(Varyings i):SV_Target
            {
                float2 colorUV=lerp(i.uv,i.normalUV,_ColorUsesUV1);
                half4 base=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,colorUV)*_BaseColor;
                clip(base.a-.4h);
                // Default texture, sRGB OFF: never use UnpackNormal on this RGB vector map.
                float3 paintedOS=SAMPLE_TEXTURE2D(_NormalOS,sampler_NormalOS,i.normalUV).rgb*2.0-1.0;
                float3 nOS=normalize(lerp(normalize(i.normalOS),paintedOS,_Strength));
                float3 nWS=TransformObjectToWorldNormal(nOS);
                Light main=GetMainLight();
                half diffuse=saturate(dot(nWS,main.direction));
                half3 ambient=max(SampleSH(nWS),half3(.12,.12,.12));
                return half4(base.rgb*(ambient+main.color*diffuse),1);
            }
            ENDHLSL
        }
    }
}

Shader "Hidden/SpiderVerse/ObjectLineArt"
{
    Properties { _StrokeTex("Stroke texture", 2D) = "white" {} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        Pass
        {
            Name "ObjectLineArt"
            ZWrite Off ZTest Always Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_StrokeTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _Resolution, _Offset, _TextureRotation, _RandomOffset, _TextureST;
                float _Width, _Taper, _Transition, _Noise;
                float _HasTexture, _TextureStrength, _TextureRepeat, _TextureMask;
            CBUFFER_END
            struct Attributes {float3 position:POSITION;float3 previous:TEXCOORD0;float3 next:TEXCOORD1;float4 stroke:TEXCOORD2;};
            struct Varyings {float4 position:SV_POSITION;float2 uv:TEXCOORD0;};
            float2 Pixel(float4 p) {return p.xy/max(p.w,.000001)*_Resolution.xy*.5;}
            float2 Direction(float2 p,float2 fallback) {return dot(p,p)>.000001?normalize(p):fallback;}
            float4 ClipNeighbor(float4 p,float4 n)
            {
                #if UNITY_REVERSED_Z
                    float a=p.w-p.z,b=n.w-n.z;
                #else
                    float a=p.z-UNITY_NEAR_CLIP_VALUE*p.w,b=n.z-UNITY_NEAR_CLIP_VALUE*n.w;
                #endif
                if(b<0)n=lerp(p,n,saturate(a/max(.000001,a-b)));
                return n;
            }
            float RandomSigned(uint seed)
            {
                seed=(seed^(seed>>16))*0x7feb352du;
                seed=(seed^(seed>>15))*0x846ca68bu;
                seed=seed^(seed>>16);
                return (seed&0x00ffffffu)*(2.0/16777215.0)-1.0;
            }
            Varyings Vert(Attributes input)
            {
                Varyings o; o.uv=float2(input.stroke.y,input.stroke.x*.5+.5);
                float4 p=TransformWorldToHClip(input.position);
                if(p.w<=.000001){o.position=float4(2,2,2,1);return o;}
                float4 a=ClipNeighbor(p,TransformWorldToHClip(input.previous));
                float4 b=ClipNeighbor(p,TransformWorldToHClip(input.next));
                float2 q=Pixel(p),before=q-Pixel(a),after=Pixel(b)-q;
                float2 d0=Direction(before,Direction(after,float2(1,0))),d1=Direction(after,d0);
                float2 n0=float2(-d0.y,d0.x),n1=float2(-d1.y,d1.x),normal=Direction(n0+n1,n1);
                float miter=min(1.5,1/max(.25,abs(dot(normal,n1))));
                float curve=pow(max(0,sin(PI*saturate(input.stroke.y))),_Transition);
                float envelope=input.stroke.z>.5?1:lerp(1,curve,_Taper);
                float halfWidth=max(.1,_Width*.5*envelope);
                float noise=_Noise*sin(input.stroke.y*6*PI+input.stroke.w)*sin(PI*input.stroke.y);
                p.xy+=normal*(input.stroke.x*halfWidth*miter+noise)*2/_Resolution.xy*p.w;
                // Projection sign preserves +Y down for both backbuffer and render textures.
                // stroke.w is constant across a connected stroke; no time or vertex position
                // enters the hash, so all its vertices translate together without frame jitter.
                uint seed=(uint)round(input.stroke.w*1000.0);
                float2 random=float2(RandomSigned(seed^0x68bc21ebu),RandomSigned(seed^0x02e5be93u));
                float2 offset=_Offset.xy+random*abs(_RandomOffset.xy);
                p.xy+=float2(offset.x,-offset.y*_ProjectionParams.x)*2/_Resolution.xy*p.w;
                o.position=p;return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float side=input.uv.y*2-1;
                float coverage=1-smoothstep(1-max(fwidth(side),.001),1,abs(side));
                float mask=1;
                if(_HasTexture>.5) {
                    float2 uv=input.uv-.5;
                    uv=float2(_TextureRotation.x*uv.x-_TextureRotation.y*uv.y,
                              _TextureRotation.y*uv.x+_TextureRotation.x*uv.y)+.5;
                    // Rotate first, then tile (including legacy U repeats), then translate.
                    uv=uv*float2(_TextureST.x*_TextureRepeat,_TextureST.y)+_TextureST.zw;
                    float4 tex=SAMPLE_TEXTURE2D(_StrokeTex,sampler_LinearRepeat,uv);
                    float luminance=saturate(dot(tex.rgb,float3(.2126,.7152,.0722)));
                    float ink=_TextureMask>.5?1-luminance:luminance;
                    mask=lerp(1,tex.a*ink,_TextureStrength);
                }
                return half4(_Color.rgb,_Color.a*coverage*mask);
            }
            ENDHLSL
        }
    }
}


Shader "Hidden/Lassyla/Accumulate"
{
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline"}
        ZTest Always ZWrite Off Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D_X(_Source);TEXTURE2D_X(_History);float _HistoryWeight,_ExposureScale;float4 _Taps[25];
        struct V {float4 positionCS:SV_POSITION;};
        V Vert(uint id:SV_VertexID){V o;o.positionCS=GetFullScreenTriangleVertexPosition(id);return o;}
        #include "LassylaProjectGrading.hlsl"
        float LumaWeight(float3 color){return rcp(4+dot(color,float3(1,2,1))*_ExposureScale);}
        float4 Accumulate(V i):SV_Target{
            int2 p=(int2)i.positionCS.xy;float4 sum=0;float weight=0;
            [unroll]for(int j=0;j<25;j++){
                if(_Taps[j].z==0)continue;
                float4 c=LOAD_TEXTURE2D_X(_Source,p+(int2)_Taps[j].xy);
                float w=LumaWeight(c.rgb)*_Taps[j].z;sum+=c*w;weight+=w;
            }
            if(_HistoryWeight>0){float4 c=LOAD_TEXTURE2D_X(_History,p);float w=LumaWeight(c.rgb)*_HistoryWeight;sum+=c*w;weight+=w;}
            float4 color=sum/max(weight,1e-10);if(color.a>.995)color.a=1;color=max(color,0);
            // Blender patches by half an FP16 ULP before a truncating image store.
            // D3D render targets round; explicitly drop the 13 low mantissa bits so
            // that the destination cannot round the already-patched value a second time.
            return asfloat((asuint(color)+0x1000u)&0xffffe000u);
        }
        TEXTURE3D(_Film);SAMPLER(sampler_Film);
        TEXTURE3D(_Inverse);SAMPLER(sampler_Inverse);
        float3 TransformDisplay(float3 c){
            float3 a=abs(c)*20;
            float3 uv=.5+sign(c)*log(a+sqrt(a*a+1))/14;
            return SAMPLE_TEXTURE3D_LOD(_Film,sampler_Film,(saturate(uv)*64+.5)/65,0).rgb;
        }
        float4 ColourComposite(V i):SV_Target{
            int2 p=(int2)i.positionCS.xy;
            float4 foreground=LOAD_TEXTURE2D_X(_Source,p);
            float4 background=LOAD_TEXTURE2D_X(_History,p);
            if(foreground.a<=0)return background;
            float transmission=1-saturate(foreground.a);
            if(transmission<=0)return float4(BeforeProjectGrade(TransformDisplay(foreground.rgb)),1);
            float3 displayedBG=DisplayedBackground(background.rgb);
            float3 bg=SAMPLE_TEXTURE3D_LOD(_Inverse,sampler_Inverse,(saturate(displayedBG)*64+.5)/65,0).rgb;
            // Retain the existing project's background, including HDR colours outside
            // the studio display gamut. Apply the view transform after linear coverage.
            float3 displayed=TransformDisplay(foreground.rgb+transmission*bg);
            displayed+=transmission*(displayedBG-TransformDisplay(bg));
            return float4(BeforeProjectGrade(max(displayed,0)),foreground.a+transmission*background.a);
        }
        float4 Frag(V i):SV_Target{return LOAD_TEXTURE2D_X(_Source,(int2)i.positionCS.xy);}
        float4 ReferenceBackground(V i):SV_Target{
            float3 bg=DisplayedBackground(LOAD_TEXTURE2D_X(_Source,(int2)i.positionCS.xy).rgb);
            return float4(SAMPLE_TEXTURE3D_LOD(_Inverse,sampler_Inverse,(saturate(bg)*64+.5)/65,0).rgb,1);
        }
        float4 PlaybackComposite(V i):SV_Target{
            int2 p=(int2)i.positionCS.xy;
            float4 film=LOAD_TEXTURE2D_X(_Source,p),bg=LOAD_TEXTURE2D_X(_History,p);
            if(film.a<=0)return bg;
            float3 displayedBG=DisplayedBackground(bg.rgb);
            float3 linearBG=SAMPLE_TEXTURE3D_LOD(_Inverse,sampler_Inverse,(saturate(displayedBG)*64+.5)/65,0).rgb;
            float3 c=TransformDisplay(film.rgb)+(1-saturate(film.a))*(displayedBG-TransformDisplay(linearBG));
            return float4(BeforeProjectGrade(max(c,0)),film.a+(1-film.a)*bg.a);
        }
        ENDHLSL
        Pass
        {
            Name "Accumulate" Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Accumulate
            ENDHLSL
        }
        Pass
        {
            Name "Composite" Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
        Pass
        {
            Name "Blender Film composite" Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment ColourComposite
            ENDHLSL
        }
        Pass {
            Name "Reference background" Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment ReferenceBackground
            ENDHLSL
        }
        Pass {
            Name "EEVEE playback reprojection" Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment PlaybackFilm
            #include "LassylaInteractiveFilm.hlsl"
            ENDHLSL
        }
        Pass {
            Name "Playback display" Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment PlaybackComposite
            ENDHLSL
        }
        Pass {
            Name "Invert camera colour grading" Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment BuildInverseProjectLut
            ENDHLSL
        }
        Pass {
            Name "Forest material preview" Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment ForestBackdrop
            TEXTURE2D(_ForestEnvironment);SAMPLER(sampler_ForestEnvironment);
            float4x4 _ForestWorldToBlender;float2 _ForestPixelSize;
            float4 ForestBackdrop(V i):SV_Target {
                float2 uv=i.positionCS.xy*_ForestPixelSize;
                float3 world=ComputeWorldSpacePosition(uv,.5,UNITY_MATRIX_I_VP);
                float3 d=normalize(mul((float3x3)_ForestWorldToBlender,-GetWorldSpaceNormalizeViewDir(world)));
                float2 coords=float2(atan2(d.y,d.x)/(2*PI)+.5,asin(clamp(d.z,-1,1))/PI+.5);
                return float4(SAMPLE_TEXTURE2D_LOD(_ForestEnvironment,sampler_ForestEnvironment,coords,0).rgb,1);
            }
            ENDHLSL
        }
        Pass {
            Name "Accumulate Toon coverage" Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment StaticCoverage
            float StaticCoverage(V i):SV_Target {
                int2 p=(int2)i.positionCS.xy;
                float sum=0,weights=0;
                [unroll] for(int j=0;j<25;j++){
                    if(_Taps[j].z==0)continue;
                    float c=LOAD_TEXTURE2D_X(_Source,p+(int2)_Taps[j].xy).r;
                    float w=LumaWeight(c.xxx)*_Taps[j].z;
                    sum+=c*w;weights+=w;
                }
                if(_HistoryWeight>0){
                    float c=LOAD_TEXTURE2D_X(_History,p).r,w=LumaWeight(c.xxx)*_HistoryWeight;
                    sum+=c*w;weights+=w;
                }
                float a=saturate(sum/max(weights,1e-10));
                return a>.995?1:asfloat((asuint(a)+0x1000u)&0xffffe000u);
            }
            ENDHLSL
        }
        Pass {
            Name "Reproject Toon coverage" Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment PlaybackCoverage
            #define TOON_COVERAGE_HISTORY 1
            #include "LassylaInteractiveFilm.hlsl"
            float PlaybackCoverage(V i):SV_Target { return PlaybackFilm(i).a; }
            ENDHLSL
        }
        Pass {
            Name "Native ToonCel colour"
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment NativeToon
            TEXTURE2D_X(_ToonColour);TEXTURE2D_X_FLOAT(_ToonDepth);TEXTURE2D_X(_ToonCoverage);
            float4 _ToonTargetSize;
            bool HasSurface(int2 p) {return LOAD_TEXTURE2D_X(_ToonDepth,p).r != UNITY_RAW_FAR_CLIP_VALUE;}
            float4 NativeToon(V i):SV_Target {
                int2 p=(int2)i.positionCS.xy;
                float a=saturate(LOAD_TEXTURE2D_X(_ToonCoverage,p).r);
                if(a<=0)discard;
                if(HasSurface(p))return float4(LOAD_TEXTURE2D_X(_ToonColour,p).rgb,a);
                // A jittered silhouette may reach just outside the centre raster. Extend
                // the nearest valid surface colour only into those covered fringe pixels.
                int best=10;float3 rgb=0;
                [unroll]for(int y=-2;y<=2;y++)[unroll]for(int x=-2;x<=2;x++){
                    int d=x*x+y*y;int2 q=clamp(p+int2(x,y),0,(int2)_ToonTargetSize.xy-1);
                    if(d<best && HasSurface(q)){best=d;rgb=LOAD_TEXTURE2D_X(_ToonColour,q).rgb;}
                }
                if(best==10)discard;
                return float4(rgb,a);
            }
            ENDHLSL
        }
    }
}

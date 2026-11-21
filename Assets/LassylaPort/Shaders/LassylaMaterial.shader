Shader "Lassyla/Blender Material Nodes"
{
    Properties
    {
        _IOR ("Fresnel / IOR", Float) = 1.4500000476837158
        _NoiseScale ("Edge Noise / Scale", Float) = 15
        _NoiseDetail ("Edge Noise / Detail", Range(0,15)) = 2
        _NoiseRoughness ("Edge Noise / Roughness", Float) = 0.5
        _NoiseLacunarity ("Edge Noise / Lacunarity", Float) = 2
        _NoiseDistortion ("Edge Noise / Distortion", Float) = 0
        _EdgeStart ("Fresnel Ramp / Black Position", Range(0,1)) = 0
        _EdgeEnd ("Fresnel Ramp / White Position", Range(0,1)) = 0.3772728443145752
        _NoiseStart ("Noise Ramp / Dark Position", Range(0,1)) = 0.23636361956596375
        _NoiseEnd ("Noise Ramp / White Position", Range(0,1)) = 0.5772733688354492
        _NoiseDark ("Noise Ramp / Dark Value (linear)", Range(0,1)) = 0.003952979110181332
        _HashScale ("EEVEE Alpha Hash / Scale", Float) = 0.1
        _HashOffset ("EEVEE Alpha Hash / Sample Offset", Range(0,1)) = 0.142857142857
        _NormalMix ("Mix / Normal Factor", Range(0,1)) = 0.8666666150093079
        _ColorMix ("Mix.002 / Color Factor", Range(0,1)) = 0.20000001788139343
        _NormalGroup ("Normal Voronoi / LF Freq, LF Amp, HF Amp, Scale", Vector) = (1,0.2,1,3.599998712539673)
        _ColorGroup ("Color Voronoi / LF Freq, LF Amp, HF Amp, Scale", Vector) = (1,0.2,1,2.0999999046325684)
        _DiffuseColor ("Diffuse BSDF / Color (linear RGB)", Vector) = (0.8,0.8,0.8,1)
        [NoScaleOffset] _ForestDiffuse ("Blender forest studio / diffuse response",2D) = "white" {}
        [NoScaleOffset] _Palette ("ColorRamp.005 / Blender 257 texels", 2D) = "white" {}
        [HideInInspector] _BlenderLocal0 ("Blender Local X",Vector) = (-1,0,0,0)
        [HideInInspector] _BlenderLocal1 ("Blender Local Y",Vector) = (0,0,1,0)
        [HideInInspector] _BlenderLocal2 ("Blender Local Z",Vector) = (0,1,0,0)
        [HideInInspector] _BlenderNormalToUnity0 ("Normal Basis X",Vector) = (-1,0,0,0)
        [HideInInspector] _BlenderNormalToUnity1 ("Normal Basis Y",Vector) = (0,0,1,0)
        [HideInInspector] _BlenderNormalToUnity2 ("Normal Basis Z",Vector) = (0,1,0,0)
        [HideInInspector] _WorldToSourceMesh0 ("Authored mesh X",Vector) = (1,0,0,0)
        [HideInInspector] _WorldToSourceMesh1 ("Authored mesh Y",Vector) = (0,1,0,0)
        [HideInInspector] _WorldToSourceMesh2 ("Authored mesh Z",Vector) = (0,0,1,0)
        [HideInInspector] _BlenderWorld0 ("Blender World X",Vector) = (1,0,0,0)
        [HideInInspector] _BlenderWorld1 ("Blender World Y",Vector) = (0,1,0,0)
        [HideInInspector] _BlenderWorld2 ("Blender World Z",Vector) = (0,0,1,0)
        [Enum(Material,0,Transparency,1,Noise,2,Fresnel,3,Generated,4,VoronoiColor,5)] _DebugView ("Diagnostic View",Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off
        ZWrite On
        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float _IOR,_NoiseScale,_NoiseDetail,_NoiseRoughness,_NoiseLacunarity,_NoiseDistortion;
            float _EdgeStart,_EdgeEnd,_NoiseStart,_NoiseEnd,_NoiseDark,_HashScale,_HashOffset;
            float _NormalMix,_ColorMix,_DebugView;
            float4 _NormalGroup,_ColorGroup,_DiffuseColor,_BlenderLocal0,_BlenderLocal1,_BlenderLocal2;
            float4 _BlenderNormalToUnity0,_BlenderNormalToUnity1,_BlenderNormalToUnity2;
            float4 _BlenderWorld0,_BlenderWorld1,_BlenderWorld2;
            float4 _WorldToSourceMesh0,_WorldToSourceMesh1,_WorldToSourceMesh2;
        CBUFFER_END
        TEXTURE2D(_Palette); SAMPLER(sampler_Palette);
        TEXTURE2D(_ForestDiffuse); SAMPLER(sampler_ForestDiffuse);
        TEXTURE2D(_LassylaStudioShadow);
        float4x4 _LassylaStudioProjection;
        float StudioVisibility(float3 worldPosition,float3 view)
        {
            float3 ng=normalize(cross(ddx(worldPosition),ddy(worldPosition)));
            ng*=dot(ng,view)<0?-1:1;
            float3 coord=mul(_LassylaStudioProjection,float4(worldPosition+ng*.001,1)).xyz;
            if(any(coord<=0)||any(coord>=1))return 1;
            float2 p=coord.xy*1024-.5;
            int2 base=int2(floor(p));float2 f=frac(p);
            float4 samples=float4(LOAD_TEXTURE2D(_LassylaStudioShadow,base).r,LOAD_TEXTURE2D(_LassylaStudioShadow,base+int2(1,0)).r,LOAD_TEXTURE2D(_LassylaStudioShadow,base+int2(0,1)).r,LOAD_TEXTURE2D(_LassylaStudioShadow,base+1).r);
            samples=step(coord.z-.0002,samples);
            return lerp(lerp(samples.x,samples.y,f.x),lerp(samples.z,samples.w,f.x),f.y);
        }
        #include "LassylaEdgeCoverage.hlsl"
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float3 generated:TEXCOORD2; };
        struct Varyings {
            float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1;
            float3 generated:TEXCOORD2; float3 blenderObject:TEXCOORD3;
        };
        Varyings Vert(Attributes v) {
            Varyings o;
            o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS=TransformWorldToHClip(o.positionWS);
            o.positionCS.xy+=_LassylaJitter.xy*o.positionCS.w;
            // Native Blender's mesh loop normals use SNORM10 packing before interpolation.
            o.normalWS=LassylaEdgeNormalWS(v.normalOS);
            o.generated=v.generated;
            // Unity can submit skinned vertices in a different draw basis than the FBX mesh.
            // Recover Blender Texture Coordinate > Object using the actual renderer transform.
            float4 worldPosition=float4(o.positionWS,1);
            o.blenderObject=float3(dot(_WorldToSourceMesh0,worldPosition),dot(_WorldToSourceMesh1,worldPosition),dot(_WorldToSourceMesh2,worldPosition));
            return o;
        }
        float4 EdgeSignals(Varyings i,bool frontFace) {
            return LassylaEdgeSignals(i.generated,i.normalWS,i.positionWS,frontFace);
        }
        void AlphaHash(Varyings i,float transparency) {
            LassylaClipHash(i.positionWS,transparency);
        }
        float3 BlenderNormalToUnity(float3 n) {
            return float3(dot(_BlenderNormalToUnity0.xyz,n),dot(_BlenderNormalToUnity1.xyz,n),dot(_BlenderNormalToUnity2.xyz,n));
        }
        float3 EvaluateColor(Varyings i,bool frontFace) {
            float3 cellColor,cellPosition,unused;
            Lassyla_blurry_voronoi(i.blenderObject,_NormalGroup,unused,cellPosition);
            Lassyla_blurry_voronoi(i.blenderObject,_ColorGroup,cellColor,unused);
            if(_DebugView==5) return cellColor;
            float3 N=normalize(i.normalWS)*(frontFace?1.0:-1.0);
            N=normalize(lerp(N,BlenderNormalToUnity(cellPosition),saturate(_NormalMix)));
            // Evaluate the diffuse response of the source forest studio for this normal.
            // The chart was sampled in native LookDev using the same 0.8 diffuse closure.
            float3 bn=normalize(float3(dot(_BlenderWorld0.xyz,N),dot(_BlenderWorld1.xyz,N),dot(_BlenderWorld2.xyz,N)));
            if(_DebugView==8)return .5+.45*bn;
            if(_DebugView==9)return cellColor;
            if(_DebugView==10)return i.blenderObject*.2+.5;
            float2 uv=float2(atan2(bn.z,bn.x)/(2*PI),asin(clamp(-bn.y,-1,1))/PI+0.5);
            float3 diffuse=SAMPLE_TEXTURE2D_LOD(_ForestDiffuse,sampler_ForestDiffuse,uv,0).rgb*_DiffuseColor.rgb/0.8;
            float visibility=StudioVisibility(i.positionWS,GetWorldSpaceNormalizeViewDir(i.positionWS));
            float3 sun=float3(.481163,.405629,.311520)*saturate(dot(bn,float3(.783121,-.529982,.325331)));
            diffuse=max(diffuse-(1-visibility)*sun*(_DiffuseColor.rgb/.8),0);
            if(_DebugView==11)return diffuse;
            float3 mixed=lerp(diffuse,cellColor,saturate(_ColorMix));
            float fac=dot(mixed,float3(0.2126,0.7152,0.0722));
            // Original GPU ColorRamp uses a 257-sample texture, not analytic stop interpolation.
            float3 color=SAMPLE_TEXTURE2D_LOD(_Palette,sampler_Palette,float2(fac*(256.0/257.0)+0.5/257.0,0.5),0).rgb;
            return color;
        }
        float4 Frag(Varyings i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target {
            LassylaClipSceneDepth(i.positionCS);
            bool frontFace=IS_FRONT_VFACE(face,true,false);
            float4 edge=EdgeSignals(i,frontFace);
            if(_DebugView==7)return float4(edge.x,edge.y,edge.w,1);
            if(_DebugView==1)return float4(edge.www,1);
            if(_DebugView==2)return float4(edge.xxx,1);
            if(_DebugView==3)return float4(edge.yyy,1);
            if(_DebugView==4)return float4(i.generated,1);
            if(_DebugView>=8)return float4(EvaluateColor(i,frontFace),1);
            AlphaHash(i,edge.w);
            if(_DebugView==6)return float4(1,1,1,1);
            return float4(EvaluateColor(i,frontFace),1);
        }
        float4 DepthFrag(Varyings i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target {
            AlphaHash(i,EdgeSignals(i,IS_FRONT_VFACE(face,true,false)).w);return 0;
        }
        float4 NormalFrag(Varyings i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target {
            bool frontFace=IS_FRONT_VFACE(face,true,false);AlphaHash(i,EdgeSignals(i,frontFace).w);
            return float4(NormalizeNormalPerPixel(i.normalWS)*(frontFace?1:-1),0);
        }
        ENDHLSL
        Pass {
            Name "Forest Studio Sun" Tags {"LightMode"="LassylaStudioCaster"}
            ZTest Always ZWrite Off Cull Off Blend One One BlendOp Min ColorMask R
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            struct StudioVaryings {float4 pos:SV_POSITION;float depth:TEXCOORD0;};
            StudioVaryings ShadowVert(Attributes v){
                float3 p=mul(_LassylaStudioProjection,float4(TransformObjectToWorld(v.positionOS.xyz),1)).xyz;
                StudioVaryings o;o.pos=float4(p.x*2-1,1-2*p.y,.5,1);o.depth=p.z;return o;
            }
            float4 ShadowFrag(StudioVaryings i):SV_Target{return i.depth.xxxx;}
            ENDHLSL
        }
        Pass {
            Name "LassylaForward"
            Tags { "LightMode"="LassylaSurface" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            ENDHLSL
        }
        Pass {
            Name "DepthOnly"
            Tags { "LightMode"="LassylaDepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            ENDHLSL
        }
        Pass {
            Name "DepthNormals"
            Tags { "LightMode"="LassylaDepthNormals" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment NormalFrag
            ENDHLSL
        }
    }
    CustomEditor "LassylaPort.Editor.LassylaMaterialGUI"
}

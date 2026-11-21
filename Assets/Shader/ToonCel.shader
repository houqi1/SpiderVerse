// Cel diffuse with optional Cook-Torrance direct specular lighting.
// Property names match Custom/Toon so existing materials keep their values when switched over.
Shader "Custom/ToonCel"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [Enum(UV0, 0, UV1, 1)] _BaseMapUV ("Base Map UV", Float) = 0
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)

        [Header(Normal Map)]
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1

        [Header(Cel Diffuse)]
        _ShadeColor ("Shade Color", Color) = (0.4, 0.4, 0.5, 1)
        _ShadeThreshold ("Shade Threshold", Range(0, 1)) = 0.45
        _ShadeSmooth ("Shade Smooth", Range(0, 0.5)) = 0.02
        [Toggle(_SHADE_FROM_AMBIENT)] _ShadeFromAmbient ("Shade From Ambient", Float) = 0
        [Toggle] _CelModelNormalEnabled ("Cel Diffuse Use Model Space Normal Map", Float) = 0
        [Toggle(_CEL_THREE_TONE)] _CelThreeTone ("Cel Diffuse Three Tone", Float) = 0
        _MidShadeColor ("Mid Shade Color (Three Tone)", Color) = (0.7, 0.7, 0.75, 1)
        _MidShadeThreshold ("Mid/Dark Threshold (Three Tone, below Shade Threshold)", Range(0, 1)) = 0.2
        _AmbientDarkColor ("Darkest Layer Extra Color (Three Tone + Shade From Ambient)", Color) = (1, 1, 1, 1)
        [Enum(Multiply, 0, Add, 1)] _AmbientDarkBlend ("Darkest Layer Extra Color Blend", Float) = 0

        [Header(Model Space Normal Map)]
        _WrapNormalMap ("Model Space Normal (Linear RGB)", 2D) = "gray" {}
        [Enum(UV0, 0, UV1, 1)] _WrapNormalUV ("Model Space Normal UV", Float) = 1
        _WrapNormalStrength ("Model Space Normal Strength", Range(0, 1)) = 1
        [Toggle] _WrapNormalTangentSpace ("Model Normal Map Is Tangent Space (follows skinning)", Float) = 0

        [Header(Ambient)]
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 0.35

        [Header(PBR Specular)]
        [Toggle(_PBR_SPECULAR)] _PBRSpecular ("Enable PBR Specular", Float) = 0
        _SpecularRoughness ("Specular Roughness", Range(0.05, 1)) = 0.35
        _SpecularF0 ("Specular F0 (Linear Reflectance)", Vector) = (0.04, 0.04, 0.04, 0)
        _SpecularMetallic ("Specular Metallic (Base Color as F0)", Range(0, 1)) = 0
        _SpecularIntensity ("Specular Intensity", Range(0, 5)) = 1
        _SpecularShapeAmount ("Specular Shape Clamp Amount", Range(0, 1)) = 1
        _SpecularThreshold ("Specular Shape Threshold (Normalized GGX)", Range(0, 1)) = 0.1
        _SpecularSmooth ("Specular Shape Smoothness", Range(0, 0.5)) = 0.02

        [Header(Lassyla Irregular Edge)]
        [ToggleUI] _LassylaEdgeEnabled ("Enable Lassyla Irregular Edge", Float) = 0
        [ToggleUI] _LassylaHideGrain ("Hide Fine Edge Grain", Float) = 0
        _IOR ("Fresnel / IOR", Float) = 1.4500000476837158
        _NoiseScale ("Edge Noise / Scale", Float) = 15
        _NoiseDetail ("Edge Noise / Detail", Range(0,15)) = 2
        _NoiseRoughness ("Edge Noise / Roughness", Float) = 0.5
        _NoiseLacunarity ("Edge Noise / Lacunarity", Float) = 2
        _NoiseDistortion ("Edge Noise / Distortion", Float) = 0
        _EdgeStart ("Fresnel Ramp / Black Position", Range(0,1)) = 0
        _EdgeEnd ("Fresnel Ramp / White Position", Range(0,1)) = 0.3772728443145752
        _NormalEdgeStrength ("Normal Difference Edge / Strength (0 = fresnel only)", Range(0,1)) = 0
        _NormalEdgeStart ("Normal Difference Edge / Black Position (screen-space normal change)", Range(0,2)) = 0.05
        _NormalEdgeEnd ("Normal Difference Edge / White Position", Range(0,2)) = 0.3
        _NoiseStart ("Noise Ramp / Dark Position", Range(0,1)) = 0.23636361956596375
        _NoiseEnd ("Noise Ramp / White Position", Range(0,1)) = 0.5772733688354492
        _NoiseDark ("Noise Ramp / Dark Value (linear)", Range(0,1)) = 0.003952979110181332
        _HashScale ("EEVEE Alpha Hash / Scale", Float) = 0.1
        _HashOffset ("EEVEE Alpha Hash / Sample Offset", Range(0,1)) = 0.142857142857

        [Header(Surface)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVert
            #pragma fragment ToonFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing
            #pragma shader_feature_local _CEL_THREE_TONE
            #pragma shader_feature_local _SHADE_FROM_AMBIENT
            #pragma shader_feature_local _PBR_SPECULAR

            #include "ToonCelForward.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Lassyla Toon Original Colour"
            Tags { "LightMode" = "ToonCelEdgeColour" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ToonVert
            #pragma fragment ToonFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_instancing
            #pragma shader_feature_local _CEL_THREE_TONE
            #pragma shader_feature_local _SHADE_FROM_AMBIENT
            #pragma shader_feature_local _PBR_SPECULAR

            #define TOON_LASSYLA_COLOUR 1
            #include "ToonCelForward.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Lassyla Toon Surface"
            Tags { "LightMode" = "ToonCelCoverage" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ToonVert
            #pragma fragment ToonEdgeFrag
            #pragma multi_compile_instancing
            #define TOON_LASSYLA_SAMPLE 1
            #include "ToonCelForward.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "Lassyla Toon Centre Depth"
            Tags { "LightMode" = "LassylaDepthOnly" }
            Cull Off
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ToonVert
            #pragma fragment ToonEdgeDepth
            #pragma multi_compile_instancing
            #define TOON_LASSYLA_SAMPLE 1
            #include "ToonCelForward.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "ToonCelInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ToonDepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing

            #include "ToonCelInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            Varyings ToonDepthOnlyVertex(Attributes input) {
                Varyings output=DepthOnlyVertex(input);
                if(ToonUsesLassylaCamera())output.positionCS=float4(2,2,2,1);
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            // The enabled surface is double sided. The fragment shader applies the
            // original material cull mode when this camera draws regular ToonCel.
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask RG

            HLSLPROGRAM
            #pragma multi_compile _ LOD_FADE_CROSSFADE

            // Keep the material buffer identical to the visible/depth passes.
            #include "ToonCelInput.hlsl"
            // URP reads the previous skinned position from TEXCOORD4, as well as
            // previous object/camera matrices. This captures bone animation even
            // when the character's root transform does not move.
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma vertex ToonMotionVert
            #pragma fragment ToonMotionFrag
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include "ToonCelMotion.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}

Shader "Custom/CharacterShell"
{
    Properties
    {
        [Header(Layers)]
        _LayerCount ("Layer Count", Range(1, 4)) = 1

        [Header(Layer 1)]
        [MainTexture] _BaseMap ("Map", 2D) = "white" {}
        [MainColor][HDR] _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _UseDotMap ("Procedural Dots", Float) = 0
        _DotDensity ("Dot Density", Float) = 10
        _DotSize ("Dot Size", Range(0, 0.5)) = 0.03
        [Toggle] _InvertDots ("Invert Dots", Float) = 0
        _NoiseMap ("Noise", 2D) = "gray" {}
        _ClipMap ("Clip Noise", 2D) = "white" {}
        _ClipThreshold ("Clip Threshold", Range(0, 1)) = 0.5
        _ClipSoftness ("Clip Softness", Range(0, 1)) = 0.1
        _FresnelPower ("Fresnel Power", Range(0.01, 8)) = 2
        _FresnelIntensity ("Fresnel Intensity", Range(0, 1)) = 1
        _FresnelRimPower ("Rim Fresnel Power", Range(0.01, 8)) = 2
        _FresnelRimIntensity ("Rim Fresnel Intensity", Range(0, 1)) = 0
        _NormalExtrusion ("Normal Extrusion", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        [Header(Layer 2)]
        _BaseMap1 ("Map", 2D) = "white" {}
        [HDR] _BaseColor1 ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _UseDotMap1 ("Procedural Dots", Float) = 0
        _DotDensity1 ("Dot Density", Float) = 10
        _DotSize1 ("Dot Size", Range(0, 0.5)) = 0.03
        [Toggle] _InvertDots1 ("Invert Dots", Float) = 0
        _NoiseMap1 ("Noise", 2D) = "gray" {}
        _ClipMap1 ("Clip Noise", 2D) = "white" {}
        _ClipThreshold1 ("Clip Threshold", Range(0, 1)) = 0.5
        _ClipSoftness1 ("Clip Softness", Range(0, 1)) = 0.1
        _FresnelPower1 ("Fresnel Power", Range(0.01, 8)) = 2
        _FresnelIntensity1 ("Fresnel Intensity", Range(0, 1)) = 1
        _FresnelRimPower1 ("Rim Fresnel Power", Range(0.01, 8)) = 2
        _FresnelRimIntensity1 ("Rim Fresnel Intensity", Range(0, 1)) = 0
        _NormalExtrusion1 ("Normal Extrusion", Float) = 0.015
        [Enum(UnityEngine.Rendering.CullMode)] _Cull1 ("Cull", Float) = 2

        [Header(Layer 3)]
        _BaseMap2 ("Map", 2D) = "white" {}
        [HDR] _BaseColor2 ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _UseDotMap2 ("Procedural Dots", Float) = 0
        _DotDensity2 ("Dot Density", Float) = 10
        _DotSize2 ("Dot Size", Range(0, 0.5)) = 0.03
        [Toggle] _InvertDots2 ("Invert Dots", Float) = 0
        _NoiseMap2 ("Noise", 2D) = "gray" {}
        _ClipMap2 ("Clip Noise", 2D) = "white" {}
        _ClipThreshold2 ("Clip Threshold", Range(0, 1)) = 0.5
        _ClipSoftness2 ("Clip Softness", Range(0, 1)) = 0.1
        _FresnelPower2 ("Fresnel Power", Range(0.01, 8)) = 2
        _FresnelIntensity2 ("Fresnel Intensity", Range(0, 1)) = 1
        _FresnelRimPower2 ("Rim Fresnel Power", Range(0.01, 8)) = 2
        _FresnelRimIntensity2 ("Rim Fresnel Intensity", Range(0, 1)) = 0
        _NormalExtrusion2 ("Normal Extrusion", Float) = 0.03
        [Enum(UnityEngine.Rendering.CullMode)] _Cull2 ("Cull", Float) = 2

        [Header(Layer 4)]
        _BaseMap3 ("Map", 2D) = "white" {}
        [HDR] _BaseColor3 ("Tint", Color) = (1, 1, 1, 1)
        [Toggle] _UseDotMap3 ("Procedural Dots", Float) = 0
        _DotDensity3 ("Dot Density", Float) = 10
        _DotSize3 ("Dot Size", Range(0, 0.5)) = 0.03
        [Toggle] _InvertDots3 ("Invert Dots", Float) = 0
        _NoiseMap3 ("Noise", 2D) = "gray" {}
        _ClipMap3 ("Clip Noise", 2D) = "white" {}
        _ClipThreshold3 ("Clip Threshold", Range(0, 1)) = 0.5
        _ClipSoftness3 ("Clip Softness", Range(0, 1)) = 0.1
        _FresnelPower3 ("Fresnel Power", Range(0.01, 8)) = 2
        _FresnelIntensity3 ("Fresnel Intensity", Range(0, 1)) = 1
        _FresnelRimPower3 ("Rim Fresnel Power", Range(0.01, 8)) = 2
        _FresnelRimIntensity3 ("Rim Fresnel Intensity", Range(0, 1)) = 0
        _NormalExtrusion3 ("Normal Extrusion", Float) = 0.045
        [Enum(UnityEngine.Rendering.CullMode)] _Cull3 ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "Shells"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma target 4.0
            #pragma require geometry
            #pragma vertex Vert
            #pragma geometry Geom
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "CharacterShellLayer.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}

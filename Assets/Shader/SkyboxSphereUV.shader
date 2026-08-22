Shader "Skybox/SphereUV"
{
    Properties
    {
        _MainTex ("Spherical Map (Equirectangular)", 2D) = "grey" {}
        _Tint ("Tint Color", Color) = (0.5, 0.5, 0.5, 1)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST; // xy = tiling, zw = offset
            half4 _MainTex_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;

            // Rotate direction around Y axis (horizontal skybox spin).
            float3 RotateAroundYInDegrees(float3 dir, float degrees)
            {
                float alpha = degrees * UNITY_PI / 180.0;
                float sina, cosa;
                sincos(alpha, sina, cosa);
                float2x2 m = float2x2(cosa, -sina, sina, cosa);
                return float3(mul(m, dir.xz), dir.y).xzy;
            }

            // General sphere / equirectangular UV from a world-space direction.
            // longitude (X): atan2(z, x)  ->  [0, 1]
            // latitude  (Y): acos(y)     ->  [0, 1]
            float2 DirToSphereUV(float3 dir)
            {
                float3 n = normalize(dir);
                float latitude = acos(clamp(n.y, -1.0, 1.0));
                float longitude = atan2(n.z, n.x);
                float2 sphereCoords = float2(longitude, latitude) * float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);
                return float2(0.5, 1.0) - sphereCoords;
            }

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 rotated = RotateAroundYInDegrees(v.vertex.xyz, _Rotation);
                o.vertex = UnityObjectToClipPos(rotated);
                // Pass unrotated object-space direction; skybox mesh is a unit cube/sphere.
                o.texcoord = v.vertex.xyz;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 uv = DirToSphereUV(i.texcoord);
                uv = uv * _MainTex_ST.xy + _MainTex_ST.zw;

                half4 tex = tex2D(_MainTex, uv);
                half3 col = DecodeHDR(tex, _MainTex_HDR);
                col *= _Tint.rgb * unity_ColorSpaceDouble.rgb;
                col *= _Exposure;
                return half4(col, 1);
            }
            ENDCG
        }
    }

    Fallback Off
}

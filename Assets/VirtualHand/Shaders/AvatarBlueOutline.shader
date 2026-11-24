Shader "Neuralife/AvatarBlueOutline"
{
    Properties
    {
        _InteriorColor ("Interior Color", Color) = (0.05, 0.48, 0.72, 1)
        _RimColor ("Fresnel Rim Color", Color) = (0.2, 0.9, 1, 1)
        _InteriorAlpha ("Interior Alpha", Range(0, 1)) = 0.12
        _RimAlpha ("Rim Alpha", Range(0, 1)) = 0.95
        _RimPower ("Rim Power", Range(0.25, 8)) = 2
        [Toggle] _DepthPrepass ("Depth Prepass", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ColorZTest ("Color Depth Test", Float) = 3
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100

        // The optional prepass writes only visible front faces. Its LEqual test keeps
        // opaque scene depth authoritative, and the color pass then shades the nearest
        // avatar surface once instead of accumulating internal surfaces.
        Pass
        {
            Name "AVATAR_DEPTH_PREPASS"
            Tags { "LightMode"="Always" }
            Cull Back
            ZTest LEqual
            ZWrite [_DepthPrepass]
            ColorMask 0

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _InteriorColor;
            fixed4 _RimColor;
            float _InteriorAlpha;
            float _RimAlpha;
            float _RimPower;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                o.worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 normal = normalize(i.worldNormal);
                float3 viewDirection = normalize(UnityWorldSpaceViewDir(i.worldPosition));
                float fresnel = pow(saturate(1.0 - saturate(dot(normal, viewDirection))), max(_RimPower, 0.001));
                fixed3 color = lerp(_InteriorColor.rgb, _RimColor.rgb, fresnel);
                float alpha = lerp(_InteriorAlpha, _RimAlpha, fresnel);
                return fixed4(color, saturate(alpha));
            }
            ENDCG
        }

        Pass
        {
            Name "AVATAR_BLUE_COLOR"
            Tags { "LightMode"="Always" }
            Cull Back
            ZTest [_ColorZTest]
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            fixed4 _InteriorColor;
            fixed4 _RimColor;
            float _InteriorAlpha;
            float _RimAlpha;
            float _RimPower;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 position : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                o.worldPosition = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 normal = normalize(i.worldNormal);
                float3 viewDirection = normalize(UnityWorldSpaceViewDir(i.worldPosition));
                float fresnel = pow(saturate(1.0 - saturate(dot(normal, viewDirection))), max(_RimPower, 0.001));
                fixed3 color = lerp(_InteriorColor.rgb, _RimColor.rgb, fresnel);
                float alpha = lerp(_InteriorAlpha, _RimAlpha, fresnel);
                return fixed4(color, saturate(alpha));
            }
            ENDCG
        }
    }

    Fallback Off
}

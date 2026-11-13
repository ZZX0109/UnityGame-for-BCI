// Original alpha-atlas UI shader. The public name is required by TMP's bitmap
// font constructor; the scene also references this asset so it survives stripping.
Shader "TextMeshPro/Mobile/Bitmap"
{
    Properties
    {
        [PerRendererData] _MainTex ("Glyph atlas", 2D) = "white" {}
        _FaceColor ("Face color", Color) = (1,1,1,1)
        _VertexOffsetX ("Vertex offset X", Float) = 0
        _VertexOffsetY ("Vertex offset Y", Float) = 0
        _TextureWidth ("Atlas width", Float) = 1024
        _TextureHeight ("Atlas height", Float) = 1024
        _ClipRect ("Clip rect", Vector) = (-32767,-32767,32767,32767)
        _MaskSoftnessX ("Mask softness X", Float) = 0
        _MaskSoftnessY ("Mask softness Y", Float) = 0
        _StencilComp ("Stencil comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil operation", Float) = 0
        _StencilWriteMask ("Stencil write mask", Float) = 255
        _StencilReadMask ("Stencil read mask", Float) = 255
        _ColorMask ("Color mask", Float) = 15
        _CullMode ("Cull mode", Float) = 0
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Alpha clip", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull [_CullMode]
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            Name "CLAW_BITMAP"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; float2 local : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex;
            fixed4 _FaceColor;
            float4 _ClipRect;
            float _VertexOffsetX, _VertexOffsetY;
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                v.vertex.xy += float2(_VertexOffsetX, _VertexOffsetY);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex.xy;
                o.uv = v.uv;
                o.color = v.color * _FaceColor;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 color = i.color;
                color.a *= tex2D(_MainTex, i.uv).a;
                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(i.local, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif
                return color;
            }
            ENDCG
        }
    }
}

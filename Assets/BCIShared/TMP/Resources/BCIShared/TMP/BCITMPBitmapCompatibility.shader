// Original constructor-name compatibility alias. No TMP package shader is copied.
Shader "TextMeshPro/Bitmap"
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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "CanUseSpriteAtlas"="True" }
        UsePass "TextMeshPro/Mobile/Bitmap/CLAW_BITMAP"
    }
}

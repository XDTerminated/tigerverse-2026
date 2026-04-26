// Tigerverse "drawing reveal card" shader.
// A flat paper card that progressively reveals the player's drawing as
// _RevealAmount goes 0 → 1. Used as the floating "while-you-wait" hologram
// during model load. Top-to-bottom wipe with a glowing pen-tip leading edge.

Shader "Tigerverse/DrawingRevealCard"
{
    Properties
    {
        _DrawingTex   ("Drawing",            2D)              = "white" {}
        _PaperTex     ("Paper Texture",      2D)              = "white" {}
        _PaperColor   ("Paper Color",        Color)           = (0.97,0.95,0.91,1)
        _InkColor     ("Ink Tint",           Color)           = (1,1,1,1)
        _PenColor     ("Pen Tip Glow",       Color)           = (0.30,0.55,1.0,1)
        _RevealAmount ("Reveal 0..1",        Range(0,1))      = 0
        _EdgeWidth    ("Wipe Edge Width",    Range(0.005,0.2))= 0.06
        _InkContrast  ("Ink Contrast",       Range(0.5,4))    = 2.0
        _PaperTexScale("Paper Tile Scale",   Range(0.5,8))    = 2.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" }
        LOD 100

        Pass
        {
            Name "Reveal"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                float4 _DrawingTex_ST;
                float4 _PaperTex_ST;
                float4 _PaperColor;
                float4 _InkColor;
                float4 _PenColor;
                float  _RevealAmount;
                float  _EdgeWidth;
                float  _InkContrast;
                float  _PaperTexScale;
            CBUFFER_END

            TEXTURE2D(_DrawingTex); SAMPLER(sampler_DrawingTex);
            TEXTURE2D(_PaperTex);   SAMPLER(sampler_PaperTex);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Drawing texture is usually painted with Y increasing downward
                // in the source (canvas top-left origin). Unity UV.y is bottom-up,
                // so flip Y when sampling the drawing.
                float2 drawingUV = float2(IN.uv.x, 1.0 - IN.uv.y);
                half3  drawing   = SAMPLE_TEXTURE2D(_DrawingTex, sampler_DrawingTex, drawingUV).rgb;

                // Paper background tiled a few times for fibre detail.
                half3 paperTile = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, IN.uv * _PaperTexScale).rgb;
                half3 paper     = _PaperColor.rgb * lerp(half3(1,1,1), paperTile * 1.6, 0.7);

                // How "ink-y" is this pixel, dark pixels have high inkAmount.
                half lum       = dot(drawing, half3(0.299, 0.587, 0.114));
                half inkAmount = saturate((1.0 - lum) * _InkContrast);

                // Top-to-bottom wipe: at reveal=0 nothing shown, at reveal=1 all shown.
                // Wipe leading edge is at uv.y == 1 - reveal (starts at top, moves down).
                float wipeEdge      = 1.0 - _RevealAmount;
                float visibleAmount = smoothstep(wipeEdge - _EdgeWidth, wipeEdge + _EdgeWidth, IN.uv.y);

                // Composite ink onto paper where the wipe has passed.
                half3 inkColor = drawing * _InkColor.rgb;
                half3 col      = lerp(paper, inkColor, visibleAmount * inkAmount);

                // Pen-tip glow band at the leading edge, only while still drawing.
                half  edgeProx = 1.0 - saturate(abs(IN.uv.y - wipeEdge) / max(_EdgeWidth, 1e-4));
                half  edgeGlow = pow(edgeProx, 2.0) * step(0.001, _RevealAmount) * step(_RevealAmount, 0.999);
                col += edgeGlow * _PenColor.rgb * 0.85;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Unlit"
}

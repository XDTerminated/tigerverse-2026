// Tigerverse procedural egg shader.
//
// Body: paper-craft base (matches monster aesthetic), with procedural
// crack lines that grow from the top down as _CrackAmount increases.
// Cracks are dark interior + lighter rim so they read as actual breaks
// in the paper shell. The player's drawing is plastered as a sticker on
// the front-facing axis so it's visible during the load wait.

Shader "Tigerverse/Egg"
{
    Properties
    {
        _PaperColor      ("Paper White",                 Color)         = (0.97,0.95,0.91,1)
        _ShadowTint      ("Shadow Tint",                 Color)         = (0.78,0.74,0.68,1)
        _CrackColor      ("Crack Interior",              Color)         = (0.06,0.05,0.07,1)
        _RimColor        ("Crack Rim",                   Color)         = (0.22,0.20,0.18,1)
        _PaperTex        ("Paper Texture",               2D)            = "white" {}
        _PaperTexScale   ("Paper Tile Scale",            Range(0.5,4))  = 1.4
        _CrackAmount     ("Crack Amount 0..1",           Range(0,1))    = 0
        _CrackJaggedness ("Crack Jaggedness",            Range(0.5,8))  = 4
        _CrackBaseWidth  ("Crack Base Width",            Range(0.001,0.05)) = 0.006
        _CrackMaxWidth   ("Crack Max Width",             Range(0.005,0.1))  = 0.030
        _DrawingTex      ("Drawing Sticker",             2D)            = "white" {}
        _DrawingHint     ("Drawing Sticker Strength",    Range(0,1))    = 0.45
        _DrawingFaceAxis ("Drawing Face Axis (xyz)",     Vector)        = (0,0,1,0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "EggLit"
            Tags { "LightMode"="UniversalForward" }
            // Cull back so the mesh reads as a solid opaque shell. Cull Off
            // was producing a holographic / see-through look because the
            // back faces of the thin shell rendered through the front.
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings   {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _PaperTex_ST;
                float4 _DrawingTex_ST;
                float4 _PaperColor;
                float4 _ShadowTint;
                float4 _CrackColor;
                float4 _RimColor;
                float  _PaperTexScale;
                float  _CrackAmount;
                float  _CrackJaggedness;
                float  _CrackBaseWidth;
                float  _CrackMaxWidth;
                float  _DrawingHint;
                float4 _DrawingFaceAxis;
            CBUFFER_END

            TEXTURE2D(_PaperTex);   SAMPLER(sampler_PaperTex);
            TEXTURE2D(_DrawingTex); SAMPLER(sampler_DrawingTex);

            // Cheap value noise for crack jitter.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float vnoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                float2 t = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, t.x), lerp(c, d, t.x), t.y);
            }

            // crackMask: returns (interior, rim) given UV.
            // 4 jagged meridian-ish cracks emanating from the top. Reach down further as amount→1.
            void crackMask(float2 uv, float amount, out float interior, out float rim)
            {
                interior = 0; rim = 0;
                if (amount <= 0.001) return;

                // V → 0 at bottom, 1 at top. We want cracks dense near the top.
                float topV = uv.y;             // 0..1
                float vNorm = 1.0 - topV;      // 0 at top

                // How far the cracks have descended along V (0 = barely past the apex, 1 = all the way)
                float reach = lerp(0.18, 0.95, amount);
                if (vNorm > reach) return;     // pixel is below the deepest crack reach

                // 4 main cracks at evenly distributed theta offsets.
                const int N = 4;
                float minDist = 1.0;
                [unroll] for (int k = 0; k < N; k++)
                {
                    float baseTheta = (float)k / (float)N + 0.073 * k;
                    // Jittered horizontal position per V.
                    float jitter1 = vnoise(float2(vNorm * _CrackJaggedness * 1.6, k * 13.7)) - 0.5;
                    float jitter2 = vnoise(float2(vNorm * _CrackJaggedness * 4.3, k * 7.41)) - 0.5;
                    float jitter  = jitter1 * 0.06 + jitter2 * 0.018;
                    jitter *= (0.4 + vNorm * 1.2);  // jitter grows the further from top
                    float crackX = baseTheta + jitter;

                    float dx = abs(uv.x - crackX);
                    dx = min(dx, 1.0 - dx);
                    minDist = min(minDist, dx);
                }

                // Width grows from top to whatever the current reach is.
                // Cracks taper as they extend further from the apex.
                float taper = saturate(1.0 - vNorm / max(reach, 1e-4));
                float width = lerp(_CrackBaseWidth, _CrackMaxWidth, amount) * lerp(0.4, 1.6, taper);

                // Build interior (sharp, dark gap) and rim (slightly wider, darker shading)
                interior = smoothstep(width, width * 0.45, minDist);
                rim      = smoothstep(width * 1.8, width, minDist) - interior;
                rim      = saturate(rim);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vn = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.normalWS   = vn.normalWS;
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 nWS = normalize(IN.normalWS);
                Light  mainLight = GetMainLight();
                float  ndl = saturate(dot(nWS, mainLight.direction));

                // Paper base.
                half3 paper = _PaperColor.rgb;
                half3 paperTile = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, IN.uv * _PaperTexScale).rgb;
                paper *= lerp(half3(1,1,1), paperTile * 1.6, 0.55);

                // Drawing sticker on the front face axis.
                half3 drawingSample = SAMPLE_TEXTURE2D(_DrawingTex, sampler_DrawingTex, IN.uv).rgb;
                float faceWeight = pow(saturate(dot(nWS, _DrawingFaceAxis.xyz)), 1.5);
                half3 baseCol = lerp(paper, paper * drawingSample * 1.05, _DrawingHint * faceWeight);

                // Cracks.
                float crackInterior, crackRim;
                crackMask(IN.uv, _CrackAmount, crackInterior, crackRim);
                half3 col = baseCol;
                col = lerp(col, _RimColor.rgb,   crackRim * 0.85);
                col = lerp(col, _CrackColor.rgb, crackInterior);

                // Soft form shading.
                half3 shade = lerp(_ShadowTint.rgb, half3(1,1,1), ndl * 0.55 + 0.45);
                col *= shade;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}

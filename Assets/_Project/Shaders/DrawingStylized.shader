// Tigerverse "white paper + ink outline" creature shader.
// Body: pure white paper (Paper003 texture, triplanar so it wraps the mesh).
// Linework: inverted-hull black outline pass + pencil cross-hatching in
// shadow/rim areas + a faint front projection of the actual drawing.
//
// No drawing-color tint, every monster reads as a hand-sketched white
// paper figure regardless of what colors the player drew.

Shader "Tigerverse/DrawingStylized"
{
    Properties
    {
        _PaperColor      ("Paper White",                   Color) = (0.97,0.95,0.91,1)
        _PaperShadowTint ("Paper Shadow Tint",             Color) = (0.85,0.82,0.78,1)

        _PaperTex        ("Paper Color Map (triplanar)",   2D) = "white" {}
        _PaperNormalTex  ("Paper Normal Map (triplanar)",  2D) = "bump" {}
        _PaperNormalStrength ("Paper Normal Strength",     Range(0,2)) = 0.7
        _PaperTexScale   ("Paper Texture World Scale",     Range(0.05,4)) = 0.6
        _PaperTexBlend   ("Paper Texture Strength",        Range(0,1)) = 0.85

        _OutlineColor    ("Ink Outline Color",             Color) = (0.05,0.05,0.08,1)
        _OutlineThickness("Ink Outline Thickness",         Range(0,0.05)) = 0.015

        _PencilColor     ("Pencil Color",                  Color) = (0.05,0.05,0.08,1)
        _PencilStrength  ("Pencil Hatch Strength",         Range(0,1)) = 0
        _PencilScale     ("Pencil Hatch Scale",            Range(20,400)) = 140
        _PencilContrast  ("Pencil Contrast",               Range(0.5,4)) = 1.2

        _Smoothness      ("Smoothness",                    Range(0,1)) = 0.04
        _Metallic        ("Metallic",                      Range(0,1)) = 0.0

        _DrawingTex      ("Drawing (front watermark)",     2D) = "white" {}
        _DrawingHint     ("Drawing Hint Strength",         Range(0,1)) = 0.55
        _BBoxMin         ("BBox Min (object)",             Vector) = (-0.5,-0.5,-0.5,0)
        _BBoxSize        ("BBox Size (object)",            Vector) = (1,1,1,0)

        // Unused but kept for compatibility with old material assignments.
        _BaseColor       ("(unused) Base Color",           Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        // ─── Pass 1: inverted-hull black outline ────────────────────────────
        Pass
        {
            Name "InkOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct AOIn  { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct AOOut { float4 positionCS : SV_POSITION; };

            CBUFFER_START(UnityPerMaterial)
                float4 _PaperColor;
                float4 _PaperShadowTint;
                float  _PaperNormalStrength;
                float  _PaperTexScale;
                float  _PaperTexBlend;
                float4 _OutlineColor;
                float  _OutlineThickness;
                float4 _PencilColor;
                float  _PencilStrength;
                float  _PencilScale;
                float  _PencilContrast;
                float  _Smoothness;
                float  _Metallic;
                float  _DrawingHint;
                float4 _BBoxMin;
                float4 _BBoxSize;
                float4 _BaseColor;
            CBUFFER_END

            AOOut vertOutline(AOIn IN)
            {
                AOOut OUT;
                float3 expanded = IN.positionOS.xyz + IN.normalOS * _OutlineThickness;
                OUT.positionCS = TransformObjectToHClip(expanded);
                return OUT;
            }

            half4 fragOutline(AOOut IN) : SV_Target { return _OutlineColor; }
            ENDHLSL
        }

        // ─── Pass 2: paper body ─────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
            };

            TEXTURE2D(_PaperTex);       SAMPLER(sampler_PaperTex);
            TEXTURE2D(_PaperNormalTex); SAMPLER(sampler_PaperNormalTex);
            TEXTURE2D(_DrawingTex);     SAMPLER(sampler_DrawingTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _PaperColor;
                float4 _PaperShadowTint;
                float  _PaperNormalStrength;
                float  _PaperTexScale;
                float  _PaperTexBlend;
                float4 _OutlineColor;
                float  _OutlineThickness;
                float4 _PencilColor;
                float  _PencilStrength;
                float  _PencilScale;
                float  _PencilContrast;
                float  _Smoothness;
                float  _Metallic;
                float  _DrawingHint;
                float4 _BBoxMin;
                float4 _BBoxSize;
                float4 _BaseColor;
            CBUFFER_END

            // ─── Triplanar helpers ───────────────────────────────────────────
            void TriplanarUVs(float3 wp, float scale, out float2 uvX, out float2 uvY, out float2 uvZ)
            { uvX = wp.zy / scale; uvY = wp.xz / scale; uvZ = wp.xy / scale; }
            float3 TriplanarBlend(float3 normalWS, float sharpness)
            { float3 b = pow(abs(normalWS), sharpness); return b / max(dot(b, float3(1,1,1)), 1e-4); }
            half3 TriplanarColor(float3 wp, float3 nWS, float scale, float sharpness)
            {
                float2 uX, uY, uZ; TriplanarUVs(wp, scale, uX, uY, uZ);
                float3 b = TriplanarBlend(nWS, sharpness);
                half3 cX = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, uX).rgb;
                half3 cY = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, uY).rgb;
                half3 cZ = SAMPLE_TEXTURE2D(_PaperTex, sampler_PaperTex, uZ).rgb;
                return cX * b.x + cY * b.y + cZ * b.z;
            }
            half3 TriplanarNormalWS(float3 wp, float3 nWS, float scale, float sharpness, float strength)
            {
                float2 uX, uY, uZ; TriplanarUVs(wp, scale, uX, uY, uZ);
                float3 b = TriplanarBlend(nWS, sharpness);
                half3 nX = UnpackNormal(SAMPLE_TEXTURE2D(_PaperNormalTex, sampler_PaperNormalTex, uX));
                half3 nY = UnpackNormal(SAMPLE_TEXTURE2D(_PaperNormalTex, sampler_PaperNormalTex, uY));
                half3 nZ = UnpackNormal(SAMPLE_TEXTURE2D(_PaperNormalTex, sampler_PaperNormalTex, uZ));
                half3 worldNX = half3(0, nX.y, nX.x) * sign(nWS.x);
                half3 worldNY = half3(nY.x, 0, nY.y) * sign(nWS.y);
                half3 worldNZ = half3(nZ.x, nZ.y, 0) * sign(nWS.z);
                half3 perturb = worldNX * b.x + worldNY * b.y + worldNZ * b.z;
                return normalize(nWS + perturb * strength);
            }

            // Cross-hatch density given screen UV. darkness 0 = no hatch, 1 = dense hatch.
            float crossHatch(float2 uv, float darkness)
            {
                const float A1 =  0.785398;
                const float A2 = -0.785398;
                float c1 = cos(A1), s1 = sin(A1);
                float c2 = cos(A2), s2 = sin(A2);
                float u1 = uv.x * c1 - uv.y * s1;
                float u2 = uv.x * c2 - uv.y * s2;
                float h1 = abs(frac(u1) - 0.5);
                float h2 = abs(frac(u2) - 0.5);
                float t1 = smoothstep(0.5 - 0.5  * darkness, 0.5, h1);
                float t2 = smoothstep(0.5 - 0.25 * darkness, 0.5, h2);
                return saturate(t1 * t2);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vn = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalWS   = vn.normalWS;
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 nWS    = normalize(IN.normalWS);
                // GLBs from Meshy can come in with flipped winding or inverted
                // vertex normals, without this, lighting gives ndl=0 every-
                // where and the pencil hatch maxes out, painting the model
                // solid black. Flip the normal for back-facing fragments so
                // both sides shade correctly.
                if (IS_FRONT_VFACE(isFront, 1, 0) == 0) nWS = -nWS;
                float3 viewWS = SafeNormalize(GetCameraPositionWS() - IN.positionWS);

                // 1) White paper base (slight cream).
                half3 paperRGB = TriplanarColor(IN.positionWS, nWS, _PaperTexScale, 4.0);
                half3 baseCol = lerp(_PaperColor.rgb, _PaperColor.rgb * paperRGB * 1.6, _PaperTexBlend);

                // 2) Paper-fiber normals.
                float3 nPaper = TriplanarNormalWS(IN.positionWS, nWS, _PaperTexScale, 4.0, _PaperNormalStrength);

                // 3) Drawing watermark from the front axis. We project in
                //    WORLD space using the model's world-space AABB (passed
                //    in by DrawingColorize at hatch time). Object-space
                //    projection broke whenever the GLB had nested
                //    transforms, because IN.positionOS is the LEAF mesh's
                //    local space, not the model root's space, Meshy
                //    output is always nested, so the drawing was previously
                //    landing on a tiny corner of the body.
                float2 uv = saturate((IN.positionWS.xy - _BBoxMin.xy) / max(_BBoxSize.xy, 1e-4));
                // Flip V so the drawing isn't upside down (textures load
                // with origin at top-left, world Y grows upward).
                uv.y = 1.0 - uv.y;
                half3  drawingSample = SAMPLE_TEXTURE2D(_DrawingTex, sampler_DrawingTex, uv).rgb;
                float  faceWeight = pow(saturate(abs(dot(nWS, float3(0,0,1)))), 1.5);
                baseCol = lerp(baseCol, baseCol * drawingSample, _DrawingHint * faceWeight);

                // 4) Cross-hatch pencil where shadow + rim build up, heavier than before
                //    so the white body still has visible hand-drawn shading.
                Light mainLight = GetMainLight();
                float ndlRaw = dot(nPaper, mainLight.direction);   // -1..1 signed
                float ndl    = saturate(ndlRaw);
                float fresnel = pow(1.0 - saturate(dot(nPaper, viewWS)), 3.0);
                float darkness = saturate((1.0 - ndl) * 0.7 + fresnel * 0.85);
                darkness = pow(darkness, 1.0 / max(_PencilContrast, 0.01));
                float2 screenUv = IN.screenPos.xy / max(IN.screenPos.w, 1e-4);
                float hatch = crossHatch(screenUv * _PencilScale, darkness);
                hatch = lerp(1.0, hatch, _PencilStrength * darkness);

                // 5) Subtle shadow tint into a darker paper colour.
                baseCol = lerp(baseCol, baseCol * _PaperShadowTint.rgb, (1.0 - ndl) * 0.4);

                // 6) Pencil ink overlay.
                half3 albedo = lerp(_PencilColor.rgb, baseCol, hatch);

                // 7) URP PBR lighting using the paper-perturbed normal.
                InputData inputData = (InputData)0;
                inputData.positionWS      = IN.positionWS;
                inputData.normalWS        = nPaper;
                inputData.viewDirectionWS = viewWS;
                inputData.shadowCoord     = TransformWorldToShadowCoord(IN.positionWS);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo     = albedo;
                // Force pure-matte: any smoothness > 0 combined with the
                // paper normal map produced a tiny dot-pattern of specular
                // glints across the model that swam as the player moved.
                // Paper-craft monsters should never glint, so kill specular
                // and reflections entirely.
                surf.smoothness = 0;
                surf.metallic   = 0;
                surf.specular   = half3(0,0,0);
                surf.alpha      = 1;
                surf.occlusion  = 1;

                half4 lit = UniversalFragmentPBR(inputData, surf);

                // Wrap-around fill: PBR shadows half the sphere by default
                // (everywhere ndl <= 0). Pull the SIDES of the sphere, the
                // band between the terminator and ~120° back, up toward
                // the unshaded albedo, so visible shadow only covers the
                // back quarter. The very back stays dark via PBR.
                float wrapLift = saturate(ndlRaw + 0.7) - ndl;  // >0 only on the sides
                lit.rgb = lerp(lit.rgb, albedo, saturate(wrapLift * 1.5));
                return lit;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}

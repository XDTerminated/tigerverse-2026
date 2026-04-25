Shader "Tigerverse/DrawingTriplanar"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _DrawingTex ("Drawing", 2D) = "white" {}
        _ProjectionScale ("Triplanar Scale (m)", Float) = 1.0
        _DrawingStrength ("Drawing Strength", Range(0,1)) = 0.6
        _Smoothness ("Smoothness", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            TEXTURE2D(_DrawingTex);
            SAMPLER(sampler_DrawingTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _ProjectionScale;
                float  _DrawingStrength;
                float  _Smoothness;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs vn = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.normalWS   = vn.normalWS;
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 nWS = normalize(IN.normalWS);

                // Triplanar weights (smooth blend so seams don't show).
                float3 blend = pow(abs(nWS), 4.0);
                blend /= max(dot(blend, float3(1,1,1)), 1e-4);

                float3 wp = IN.positionWS / max(_ProjectionScale, 1e-4);

                half4 cX = SAMPLE_TEXTURE2D(_DrawingTex, sampler_DrawingTex, wp.zy);
                half4 cY = SAMPLE_TEXTURE2D(_DrawingTex, sampler_DrawingTex, wp.zx);
                half4 cZ = SAMPLE_TEXTURE2D(_DrawingTex, sampler_DrawingTex, wp.xy);

                half3 drawing = cX.rgb * blend.x + cY.rgb * blend.y + cZ.rgb * blend.z;

                // Mix tint with drawing detail. Tint dominates at low strength,
                // drawing breaks through at high strength.
                half3 albedo = lerp(_BaseColor.rgb, drawing * _BaseColor.rgb, _DrawingStrength);

                InputData inputData = (InputData)0;
                inputData.positionWS    = IN.positionWS;
                inputData.normalWS      = nWS;
                inputData.viewDirectionWS = SafeNormalize(GetCameraPositionWS() - IN.positionWS);
                inputData.shadowCoord   = TransformWorldToShadowCoord(IN.positionWS);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo     = albedo;
                surf.smoothness = _Smoothness;
                surf.alpha      = 1;
                surf.occlusion  = 1;

                half4 col = UniversalFragmentPBR(inputData, surf);
                return col;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}

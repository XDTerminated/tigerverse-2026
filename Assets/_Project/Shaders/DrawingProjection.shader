Shader "Tigerverse/DrawingProjection"
{
    Properties
    {
        _DrawingTex ("Drawing", 2D) = "white" {}
        _BBoxMin ("BBox Min (object-space)", Vector) = (-0.5, -0.5, -0.5, 0)
        _BBoxSize ("BBox Size (object-space)", Vector) = (1, 1, 1, 0)
        _EdgeFadeAmount ("Edge Fade Amount", Range(0, 1)) = 0.3
        _EdgeColor ("Edge Tint", Color) = (0.6, 0.6, 0.6, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _FrontSharpness ("Front Sharpness", Range(0.5, 8)) = 1.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalOS   : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };

            TEXTURE2D(_DrawingTex);
            SAMPLER(sampler_DrawingTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BBoxMin;
                float4 _BBoxSize;
                float  _EdgeFadeAmount;
                float4 _EdgeColor;
                float  _Smoothness;
                float  _FrontSharpness;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs vp = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   vn = GetVertexNormalInputs(IN.normalOS);
                OUT.positionCS = vp.positionCS;
                OUT.positionWS = vp.positionWS;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.normalOS   = IN.normalOS;
                OUT.normalWS   = vn.normalWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Object-space planar UV: project along object +Z onto XY plane, normalize into bbox.
                float2 uv = (IN.positionOS.xy - _BBoxMin.xy) / max(_BBoxSize.xy, float2(0.0001, 0.0001));
                uv = saturate(uv);

                half4 frontSample = SAMPLE_TEXTURE2D(_DrawingTex, sampler_DrawingTex, uv);
                half4 avgSample   = SAMPLE_TEXTURE2D_LOD(_DrawingTex, sampler_DrawingTex, uv, 5);

                // Desaturated avg blended with edge tint for side surfaces.
                half  lum  = dot(avgSample.rgb, half3(0.299, 0.587, 0.114));
                half3 edge = lerp(_EdgeColor.rgb, half3(lum, lum, lum), 0.5);

                // Front weight: object-space normal Z component drives projection alignment.
                // Avoids tangent-space issues; works for skinned meshes since bind-pose normals stay aligned.
                float3 nOS = normalize(IN.normalOS);
                float  frontWeight = pow(saturate(abs(nOS.z)), _FrontSharpness);
                frontWeight = lerp(frontWeight, frontWeight * (1.0 - _EdgeFadeAmount), _EdgeFadeAmount);

                half3 albedo = lerp(edge, frontSample.rgb, frontWeight);

                InputData inputData = (InputData)0;
                inputData.positionWS      = IN.positionWS;
                inputData.normalWS        = normalize(IN.normalWS);
                inputData.viewDirectionWS = SafeNormalize(GetCameraPositionWS() - IN.positionWS);
                inputData.shadowCoord     = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord        = 0;
                inputData.bakedGI         = SampleSH(inputData.normalWS);

                SurfaceData surf = (SurfaceData)0;
                surf.albedo     = albedo;
                surf.smoothness = _Smoothness;
                surf.alpha      = 1;
                surf.occlusion  = 1;
                surf.metallic   = 0;
                surf.specular   = 0;
                surf.emission   = 0;

                return UniversalFragmentPBR(inputData, surf);
            }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}

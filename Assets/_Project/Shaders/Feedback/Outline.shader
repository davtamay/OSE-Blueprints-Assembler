Shader "Hidden/OSE/Outline"
{
    Properties
    {
        _OutlineColor ("Color", Color) = (0, 0.5, 1, 1)
        _OutlineWidth ("Width (pixels)", Float) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "OUTLINE"
            Cull Front
            ZWrite On
            ZTest LEqual
            ColorMask RGB

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings OutlineVert(Attributes input)
            {
                Varyings output;
                // Transform to clip space first
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // Extrude normal in clip space so width is constant in screen pixels
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = normalize(mul((float3x3)UNITY_MATRIX_VP, normalWS).xy);

                // Scale by w so the offset is in pixels after perspective divide
                output.positionCS.xy += normalCS * (_OutlineWidth / _ScreenParams.xy) * output.positionCS.w;

                return output;
            }

            half4 OutlineFrag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}

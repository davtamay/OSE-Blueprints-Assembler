Shader "OSE/GhostOverlay"
{
    Properties
    {
        _BaseColor ("Color", Color) = (0.4, 0.7, 1, 0.35)
        _EmissionColor ("Emission", Color) = (0.1, 0.15, 0.25, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Overlay"
        }

        Pass
        {
            Name "GHOST_OVERLAY"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Back
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex GhostVert
            #pragma fragment GhostFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings GhostVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 GhostFrag(Varyings input) : SV_Target
            {
                half3 col = _BaseColor.rgb + _EmissionColor.rgb;
                return half4(col, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}

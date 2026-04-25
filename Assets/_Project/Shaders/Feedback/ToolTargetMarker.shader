Shader "OSE/ToolTargetMarker"
{
    // Overlay shader for tool-action target marker spheres. Bakes the
    // render state (overlay queue, ZTest Always, alpha blend, ZWrite off)
    // into a single Pass so URP variant stripping cannot silently drop
    // the transparent/Always variant — which is what made every prior
    // attempt at URP/Lit + runtime SetFloat("_ZTest", Always) invisible.
    //
    // Mirrors OSE/GhostOverlay's pattern (which is known to render
    // correctly for placement-success overlays in this project).
    Properties
    {
        _BaseColor ("Base Color", Color) = (0, 1, 1, 0.3)
        _EmissionColor ("Emission Color", Color) = (0, 1, 1, 1)
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
            Name "TOOL_TARGET_MARKER"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // No CBUFFER(UnityPerMaterial) here — that locks the
            // properties into the SRP-Batcher constant buffer, which
            // BLOCKS MaterialPropertyBlock overrides from taking effect.
            // ToolTargetAnimator writes _BaseColor / _EmissionColor each
            // frame via SetPropertyBlock for the camera-distance fade
            // and pulse animation; those writes only land if the
            // properties are exposed as plain globals here. Trade-off:
            // SRP Batcher won't batch this draw, but a tool-target
            // marker renders one tiny mesh per active step — negligible.
            half4 _BaseColor;
            half4 _EmissionColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 rgb = _BaseColor.rgb + _EmissionColor.rgb;
                return half4(rgb, _BaseColor.a);
            }
            ENDHLSL
        }
    }
}

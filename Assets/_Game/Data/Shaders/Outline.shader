// Hover outline: inverted hull drawn by a URP RenderObjects pass for objects on the "Outlined" layer.
// Extrudes in clip space so the line keeps roughly the same on-screen thickness at any distance.
Shader "PleaseDontDrown/Outline"
{
    Properties
    {
        _OutlineColor ("Color", Color) = (1, 0.86, 0.25, 1)
        _OutlineWidth ("Width (screen fraction)", Range(0, 0.02)) = 0.004
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = TransformWorldToHClipDir(normalWS).xy;
                float lengthCS = max(length(normalCS), 1e-5);
                float2 offset = normalCS / lengthCS * _OutlineWidth * 2.0;
                offset.x *= _ScreenParams.y / _ScreenParams.x;   // keep it even on wide screens
                positionCS.xy += offset * positionCS.w;
                output.positionCS = positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}

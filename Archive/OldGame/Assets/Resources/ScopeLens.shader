// The glass at the back of a scope, seen from the hip: the picture a small camera takes from the player's eye
// through this glass (Combat/Weapons/ScopeLens.cs), looked up where each point of the glass lies in that view, so
// what shows is what is behind it. Darker toward the rim. Unlit: glass you look through.
Shader "PleaseDontDrown/ScopeLens"
{
    Properties
    {
        _MainTex ("View", 2D) = "black" {}
        _Tint ("Glass tint", Color) = (0.92, 0.97, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ScopeLens"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                float4x4 _LensView;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 lens : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.lens = mul(_LensView, float4(TransformObjectToWorld(input.positionOS.xyz), 1.0));
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;          // -1..1 across the glass
                float r = length(p);
                float2 at = input.lens.xy / max(input.lens.w, 1e-4) * 0.5 + 0.5;   // this point of the glass, in the lens camera's picture
                half3 view = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, saturate(at)).rgb * _Tint.rgb;
                view *= 1.0 - smoothstep(0.6, 1.0, r) * 0.7;   // the tube shades the edge
                return half4(view, 1);
            }
            ENDHLSL
        }
    }
}

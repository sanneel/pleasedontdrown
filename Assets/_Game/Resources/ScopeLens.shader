// The glass at the back of a scope, seen from the hip: the picture a small camera takes down the scope
// (Combat/Weapons/ScopeLens.cs), darker toward the rim, with the crosshair in it. Unlit: glass you look through.
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
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;          // -1..1 across the glass
                float r = length(p);
                half3 view = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb * _Tint.rgb;
                view *= 1.0 - smoothstep(0.55, 1.0, r) * 0.75;   // the tube shades the edge
                // Crosshair: two fine lines, open in the middle.
                float hair = saturate(step(abs(p.x), 0.012) + step(abs(p.y), 0.012));
                float gap = step(0.06, r);
                view = lerp(view, half3(0.02, 0.02, 0.02), hair * gap * 0.85);
                return half4(view, 1);
            }
            ENDHLSL
        }
    }
}

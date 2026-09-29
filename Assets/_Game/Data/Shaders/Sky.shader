// Stylised day sky: a banded gradient, a hard-edged HDR sun (blooms through the post stack) and chunky
// pixel-grid clouds drifting with the wind. Written from scratch; the look (few colour bands, pixelated
// sun and clouds) is the one we measured in How to Fish's sky, not its shader graph.
Shader "PleaseDontDrown/Sky"
{
    Properties
    {
        _TopColor ("Zenith", Color) = (0.298, 0.405, 1, 1)
        _HorizonColor ("Horizon (matches the fog)", Color) = (0.545, 0.78, 1, 1)
        _BottomColor ("Below the horizon", Color) = (0.545, 0.78, 1, 1)
        _Bands ("Gradient bands", Float) = 6
        _BandCurve ("Gradient curve (lower = more horizon)", Float) = 0.55
        _SunColor ("Sun", Color) = (1, 0.93, 0.72, 1)
        _SunIntensity ("Sun intensity (HDR, blooms)", Float) = 8
        _SunRadius ("Sun radius (degrees)", Float) = 2.4
        _SunGlow ("Sun glow", Float) = 0.5
        _CloudColor ("Cloud lit side", Color) = (1, 1, 1, 1)
        _CloudShade ("Cloud shaded side", Color) = (0.62, 0.72, 0.92, 1)
        _CloudCover ("Cloud cover (higher = clearer)", Range(0, 1)) = 0.52
        _CloudScale ("Cloud size", Float) = 2.2
        _CloudPixels ("Cloud pixel grid", Float) = 90
        _CloudSpeed ("Cloud drift", Float) = 0.004
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "Sky"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _TopColor;
                half4 _HorizonColor;
                half4 _BottomColor;
                half4 _SunColor;
                half4 _CloudColor;
                half4 _CloudShade;
                float _Bands;
                float _BandCurve;
                float _SunIntensity;
                float _SunRadius;
                float _SunGlow;
                float _CloudCover;
                float _CloudScale;
                float _CloudPixels;
                float _CloudSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.dir = input.positionOS.xyz;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float Fbm(float2 p)
            {
                float sum = 0.0;
                float amp = 0.5;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    sum += amp * ValueNoise(p);
                    p = p * 2.03 + 17.1;
                    amp *= 0.5;
                }
                return sum;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.dir);
                float3 sunDir = normalize(_MainLightPosition.xyz);
                float h = d.y;

                // Banded gradient: horizon colour up to the zenith colour in a few flat steps.
                float up = pow(saturate(h), _BandCurve);
                up = floor(up * _Bands + 0.5) / _Bands;
                half3 sky = lerp(_HorizonColor.rgb, _TopColor.rgb, up);
                float down = saturate(-h * 5.0);
                sky = lerp(sky, _BottomColor.rgb, down);

                // Sun: a hard disc plus a stepped halo.
                float sd = dot(d, sunDir);
                float discEdge = cos(radians(_SunRadius));
                float disc = step(discEdge, sd);
                float glow = floor(pow(saturate(sd), 300.0) * 3.0 + 0.5) / 3.0 * _SunGlow;
                sky = lerp(sky, _SunColor.rgb * 2.0, saturate(glow * 1.6));
                sky = lerp(sky, _SunColor.rgb * _SunIntensity, disc);

                // Clouds on a virtual plane so they shrink toward the horizon; snapped to a pixel grid.
                if (h > 0.02)
                {
                    float2 uv = d.xz / (h + 0.28) * _CloudScale;
                    uv += _Time.y * _CloudSpeed * float2(1.0, 0.45);
                    uv = floor(uv * _CloudPixels / _CloudScale) / (_CloudPixels / _CloudScale);
                    float dens = Fbm(uv);
                    float2 towardSun = normalize(sunDir.xz + 0.0001) * 0.09;
                    float densSun = Fbm(uv + towardSun);
                    float cloud = smoothstep(_CloudCover, _CloudCover + 0.12, dens);
                    cloud = floor(cloud * 3.0 + 0.5) / 3.0;
                    float lit = step(0.5, saturate((dens - densSun) * 9.0 + 0.55));
                    half3 cloudColor = lerp(_CloudShade.rgb, _CloudColor.rgb * 1.15, lit);
                    cloud *= smoothstep(0.02, 0.22, h);
                    sky = lerp(sky, cloudColor, cloud);
                }
                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
}

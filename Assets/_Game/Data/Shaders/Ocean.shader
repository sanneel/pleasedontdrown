// Ocean surface. Wave math MUST match WaterSurface.HeightAt (C#): same globals, same sum of sines.
// Uses the camera depth texture for shallow/deep color, transparency and shore foam.
Shader "PleaseDontDrown/Ocean"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.04, 0.55, 0.71, 1)
        _DeepColor ("Deep", Color) = (0, 0.15, 0.3, 1)
        _SkyColor ("Sky reflection", Color) = (0.55, 0.78, 1, 1)
        _RippleColor ("Ripple sparkle", Color) = (0.58, 0.87, 1, 1)
        _PixelSize ("Pixel size of foam and ripples (m)", Float) = 0.2
        _DepthBands ("Depth colour bands", Float) = 5
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _UnderColor ("Seen from below", Color) = (0.2, 0.5, 0.6, 1)
        _DepthFade ("Depth fade (m)", Float) = 5
        _ShoreFoam ("Shore foam width (m)", Float) = 0.45
        _MinAlpha ("Shallow alpha", Range(0, 1)) = 0.3
        _Specular ("Sun glint", Float) = 1.6
        _Shininess ("Shininess", Float) = 90
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "OceanForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                half4 _SkyColor;
                half4 _FoamColor;
                half4 _RippleColor;
                half4 _UnderColor;
                float _PixelSize;
                float _DepthBands;
                float _DepthFade;
                float _ShoreFoam;
                float _MinAlpha;
                float _Specular;
                float _Shininess;
            CBUFFER_END

            // Set every frame by WaterSurface.cs
            float4 _PDD_Waves[4];     // xy wave vector, z amplitude (already scaled), w angular frequency
            float4 _PDD_WavePhases;
            float _PDD_WaveTime;
            float _PDD_WaterLevel;
            float4 _PDD_OceanCenter;  // xy grid center (world x,z), z fade start, w fade end (half extents)
            float4 _PDD_SurgeRegion;
            float4 _PDD_SurgeField;

            // MUST match TsunamiState.HeightAt. The surge can inundate shallow ground.
            float SurgeHeight(float2 p)
            {
                float4 r = _PDD_SurgeRegion, f = _PDD_SurgeField;
                float side = 1.0 - smoothstep(r.y * 0.75, r.y, abs(p.x - r.x));
                float coast = smoothstep(r.z, r.z + 16.0, p.y) * (1.0 - smoothstep(r.w - 14.0, r.w, p.y));
                float d = (p.y - f.x) / 9.0;
                float behind = 1.0 - smoothstep(f.x - 8.0, f.x + 5.0, p.y);
                return side * coast * (exp(-d*d) * f.y + behind * f.z - f.w);
            }
            // Set by Seabed.cs: ground heights under the sea. Waves calm down in the shallows and vanish under the island.
            TEXTURE2D(_PDD_Seabed);
            SAMPLER(sampler_PDD_Seabed);
            float4 _PDD_SeabedRect;   // xy world min corner, zw 1 / world size
            float4 _PDD_SeabedParams; // x enabled, y 1 / calm depth

            // MUST match Seabed.WaveFactor (C#).
            float SeabedCalm(float2 xz)
            {
                if (_PDD_SeabedParams.x < 0.5) return 1.0;
                float2 uv = (xz - _PDD_SeabedRect.xy) * _PDD_SeabedRect.zw;
                float ground = SAMPLE_TEXTURE2D_LOD(_PDD_Seabed, sampler_PDD_Seabed, uv, 0).r;
                return smoothstep(0.0, 1.0, saturate((_PDD_WaterLevel - ground) * _PDD_SeabedParams.y));
            }

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float waveHeight : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            float Waves(float2 xz, out float2 slope)
            {
                float2 d = abs(xz - _PDD_OceanCenter.xy);
                float edge = max(d.x, d.y);
                float fade = 1.0 - saturate((edge - _PDD_OceanCenter.z) / max(_PDD_OceanCenter.w - _PDD_OceanCenter.z, 0.001));
                fade *= SeabedCalm(xz);
                float h = 0.0;
                slope = 0.0;
                [unroll]
                for (int i = 0; i < 4; i++)
                {
                    float4 w = _PDD_Waves[i];
                    float phase = dot(w.xy, xz) + w.w * _PDD_WaveTime + _PDD_WavePhases[i];
                    h += w.z * sin(phase);
                    slope += w.z * cos(phase) * w.xy;
                }
                slope *= fade;
                return h * fade;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth value noise sampled on a snapped grid: every "pixel" of foam / ripple is one flat square.
            float PixelNoise(float2 xz, float scale, float2 drift)
            {
                float2 cell = floor(xz / _PixelSize) * _PixelSize;
                float2 p = cell * scale + drift * _PDD_WaveTime;
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float2 slope;
                float h = Waves(positionWS.xz, slope);
                h += SurgeHeight(positionWS.xz);
                slope.x += (SurgeHeight(positionWS.xz + float2(0.25,0)) - SurgeHeight(positionWS.xz - float2(0.25,0))) * 2.0;
                slope.y += (SurgeHeight(positionWS.xz + float2(0,0.25)) - SurgeHeight(positionWS.xz - float2(0,0.25))) * 2.0;
                positionWS.y = _PDD_WaterLevel + h;
                output.positionWS = positionWS;
                output.normalWS = normalize(float3(-slope.x, 1.0, -slope.y));
                output.waveHeight = h;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // Camera below the surface: we're looking up at its underside.
                if (_WorldSpaceCameraPos.y < input.positionWS.y)
                    return half4(MixFog(_UnderColor.rgb, input.fogFactor), 1.0);

                float2 uv = input.screenPos.xy / input.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                float waterDepth = max(sceneDepth - input.screenPos.w, 0.0);
                float depthT = saturate(waterDepth / _DepthFade);
                depthT = lerp(depthT, floor(depthT * _DepthBands + 0.5) / _DepthBands, 0.6); // flat colour steps, not a smooth ramp

                Light sun = GetMainLight();
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
                water *= 0.55 + 0.45 * saturate(dot(n, sun.direction));

                float fresnel = pow(1.0 - saturate(dot(n, v)), 4.0);
                half3 color = lerp(water, _SkyColor.rgb, fresnel * 0.65);

                // Sun glint: a stepped highlight instead of a smooth blob.
                float3 halfVector = normalize(sun.direction + v);
                float glint = smoothstep(0.58, 1.07, pow(saturate(dot(n, halfVector)), _Shininess * 0.25));
                glint = floor(glint * 3.0 + 0.5) / 3.0;
                color += sun.color * (_Specular * glint);

                // Ripples: small light flecks that drift over the surface.
                float fleck = PixelNoise(input.positionWS.xz, 0.9, float2(0.06, 0.03));
                float fleck2 = PixelNoise(input.positionWS.xz + 31.0, 1.7, float2(-0.05, 0.08));
                color = lerp(color, _RippleColor.rgb, step(0.9, fleck * 0.55 + fleck2 * 0.45) * 0.35 * (1.0 - depthT * 0.5));

                // Foam: a lapping band along the shore plus flecks on the crests, all on the pixel grid.
                float shore = 1.0 - saturate(waterDepth / _ShoreFoam);
                float lap = 0.5 + 0.5 * sin(_PDD_WaveTime * 2.1 + (input.positionWS.x * 0.9 + input.positionWS.z * 1.7));
                float shoreNoise = PixelNoise(input.positionWS.xz, 2.2, float2(0.2, 0.1));
                float shoreFoam = step(0.42, shore * (0.55 + 0.45 * lap) * (0.6 + 0.6 * shoreNoise));
                float crestNoise = PixelNoise(input.positionWS.xz, 3.0, float2(0.15, -0.1));
                float crestFoam = step(0.5, saturate((input.waveHeight - 0.16) * 5.0) * crestNoise * 1.6);
                float foam = saturate(shoreFoam + crestFoam * 0.8);
                color = lerp(color, _FoamColor.rgb, foam);

                float alpha = lerp(_MinAlpha, 0.95, depthT);
                alpha = saturate(max(alpha, foam) + fresnel * 0.3);
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}

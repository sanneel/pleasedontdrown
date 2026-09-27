// Ocean surface. Wave math MUST match WaterSurface.HeightAt (C#): same globals, same sum of sines.
// Uses the camera depth texture for shallow/deep color, transparency and shore foam.
Shader "PleaseDontDrown/Ocean"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.22, 0.82, 0.8, 1)
        _DeepColor ("Deep", Color) = (0.03, 0.26, 0.45, 1)
        _SkyColor ("Sky reflection", Color) = (0.62, 0.8, 0.95, 1)
        _FoamColor ("Foam", Color) = (1, 1, 1, 1)
        _UnderColor ("Seen from below", Color) = (0.2, 0.5, 0.6, 1)
        _DepthFade ("Depth fade (m)", Float) = 5
        _ShoreFoam ("Shore foam width (m)", Float) = 0.45
        _MinAlpha ("Shallow alpha", Range(0, 1)) = 0.3
        _Specular ("Specular", Float) = 1.6
        _Shininess ("Shininess", Float) = 220
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
                half4 _UnderColor;
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

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float2 slope;
                float h = Waves(positionWS.xz, slope);
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

                Light sun = GetMainLight();
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthT);
                water *= 0.55 + 0.45 * saturate(dot(n, sun.direction));

                float fresnel = pow(1.0 - saturate(dot(n, v)), 4.0);
                half3 color = lerp(water, _SkyColor.rgb, fresnel * 0.65);

                float3 halfVector = normalize(sun.direction + v);
                color += sun.color * (_Specular * pow(saturate(dot(n, halfVector)), _Shininess));

                // Foam: a lapping band along the shore plus a little on crests.
                float shore = 1.0 - saturate(waterDepth / _ShoreFoam);
                float lap = 0.5 + 0.5 * sin(_PDD_WaveTime * 2.1 + (input.positionWS.x * 0.9 + input.positionWS.z * 1.7));
                float crest = saturate((input.waveHeight - 0.24) * 5.0);
                float foam = saturate(shore * (0.55 + 0.45 * lap) + crest * 0.35);
                color = lerp(color, _FoamColor.rgb, foam);

                float alpha = lerp(_MinAlpha, 0.95, depthT);
                alpha = saturate(max(alpha, foam) + fresnel * 0.3);
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}

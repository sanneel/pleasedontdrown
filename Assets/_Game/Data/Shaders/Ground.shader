// Island 3's surfaces with real grain (Poly Haven scans, ArtSource/Tools/make_island3_ground_textures.py).
// The scans are "detail" maps: their colour as a ratio to their own average (0.5 = average), so the colour comes from
// the material and the island keeps its palette; the scan adds the blades, pebbles, cracks and stones.
//   Ground: sand, grass, jungle floor, dirt and rock, weights in uv1 (grass, jungle, dirt, rock; sand is the rest),
//           blended by the scans' heights so the edges are pebbles and tufts, not a blur. Vertex colours shade each
//           vertex (x _VertexScale). Rock is triplanar: the cliff scan on the sides, the rock scan on top.
//   Rock:   only the triplanar rock (boulders, the coast breakwater).
//   Tripo:  a Tripo model's own colour map (uv0) with the triplanar grain over its grey stone parts (the castle).
Shader "PleaseDontDrown/Ground"
{
    Properties
    {
        [KeywordEnum(Ground, Rock, Tripo)] _Mode ("Mode", Float) = 0
        _SandD ("Sand detail", 2D) = "grey" {}
        _SandN ("Sand normal", 2D) = "bump" {}
        _GrassD ("Grass detail", 2D) = "grey" {}
        _GrassN ("Grass normal", 2D) = "bump" {}
        _JungleD ("Jungle floor detail", 2D) = "grey" {}
        _JungleN ("Jungle floor normal", 2D) = "bump" {}
        _DirtD ("Dirt detail", 2D) = "grey" {}
        _DirtN ("Dirt normal", 2D) = "bump" {}
        _RockD ("Rock detail (sides)", 2D) = "grey" {}
        _RockN ("Rock normal (sides)", 2D) = "bump" {}
        _RockTopD ("Rock detail (top)", 2D) = "grey" {}
        _RockTopN ("Rock normal (top)", 2D) = "bump" {}
        _SandColor ("Sand", Color) = (0.92, 0.82, 0.6, 1)
        _GrassColor ("Grass", Color) = (0.43, 0.62, 0.27, 1)
        _JungleColor ("Jungle floor", Color) = (0.33, 0.42, 0.2, 1)
        _DirtColor ("Dirt", Color) = (0.6, 0.46, 0.31, 1)
        _RockColor ("Rock", Color) = (0.53, 0.51, 0.48, 1)
        _Scales ("Metres per tile: sand, grass, jungle, dirt", Vector) = (5, 3, 4, 5)
        _RockScale ("Rock: metres per tile", Float) = 9
        _Contrast ("Grain: sand, grass, jungle, dirt", Vector) = (0.7, 1, 1, 1)
        _RockContrast ("Rock grain", Float) = 1
        _NormalStrength ("Bumps", Float) = 1
        _HeightBlend ("Blend by height", Float) = 0.6
        _BlendDepth ("Blend edge softness", Float) = 0.2
        _VertexColor ("Vertex colours", Range(0, 1)) = 1
        _VertexScale ("Vertex colour scale", Float) = 2
        _BaseMap ("Tripo colour map", 2D) = "white" {}
        _BaseTint ("Tripo tint", Color) = (1, 1, 1, 1)
        _Desaturate ("Tripo desaturate", Range(0, 1)) = 0
        _BaseDetail ("Tripo map detail (0: only its broad colour)", Range(0, 1)) = 1
        _BaseBlurMip ("Tripo broad colour mip", Float) = 6
        _StoneOnly ("Grain only on grey parts", Range(0, 1)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
        _Occlusion ("Scan occlusion", Range(0, 1)) = 0.6
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _SandColor, _GrassColor, _JungleColor, _DirtColor, _RockColor, _BaseTint;
            float4 _Scales, _Contrast, _BaseMap_ST;
            float _RockScale, _RockContrast, _NormalStrength, _HeightBlend, _BlendDepth, _VertexColor, _VertexScale;
            float _Desaturate, _BaseDetail, _BaseBlurMip, _StoneOnly, _Smoothness, _Occlusion;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _MODE_GROUND _MODE_ROCK _MODE_TRIPO
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_SandD); TEXTURE2D(_SandN); TEXTURE2D(_GrassD); TEXTURE2D(_GrassN);
            TEXTURE2D(_JungleD); TEXTURE2D(_JungleN); TEXTURE2D(_DirtD); TEXTURE2D(_DirtN);
            TEXTURE2D(_RockD); TEXTURE2D(_RockN); TEXTURE2D(_RockTopD); TEXTURE2D(_RockTopN);
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_trilinear_repeat_aniso8); // the scans: repeating, trilinear, sharp at grazing angles
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float4 weights : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                float2 uv0 : TEXCOORD2;
                float4 weights : TEXCOORD3;
                float fogFactor : TEXCOORD4;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = input.color;
                o.uv0 = TRANSFORM_TEX(input.uv0, _BaseMap);
                o.weights = input.weights;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            // One sampled scan: colour ratio (1 = its average), height, occlusion and a world-space normal.
            struct Grain
            {
                half3 ratio;
                half height;
                half ao;
                float3 normal;
            };

            half3 UnpackGL(half4 n) { return half3((n.xy * 2.0 - 1.0) * _NormalStrength, max(n.z * 2.0 - 1.0, 0.05)); }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            // A scan laid flat (world x/z) on ground facing up, sampled twice at different sizes and turns and mixed by
            // a large noise so the tiling doesn't show. Whiteout blend of its bumps onto the surface normal.
            Grain Planar(TEXTURE2D_PARAM(d, s), TEXTURE2D(n), float2 xz, float scale, float mixer, float3 normalWS, half contrast)
            {
                float2 uvA = xz / scale;
                float2 uvB = float2(uvA.x * 0.8 - uvA.y * 0.6, uvA.x * 0.6 + uvA.y * 0.8) * 0.62 + 0.37;
                half4 da = SAMPLE_TEXTURE2D(d, s, uvA), db = SAMPLE_TEXTURE2D(d, s, uvB);
                half4 na = SAMPLE_TEXTURE2D(n, s, uvA), nb = SAMPLE_TEXTURE2D(n, s, uvB);
                half3 ta = UnpackGL(na), tb = UnpackGL(nb);
                tb.xy = half2(tb.x * 0.8 + tb.y * 0.6, -tb.x * 0.6 + tb.y * 0.8); // B's bumps turned back to the world
                half3 t = lerp(ta, tb, mixer);
                half4 detail = lerp(da, db, mixer);
                Grain g;
                g.ratio = lerp(1.0, detail.rgb * 2.0, contrast);
                g.height = detail.a;
                g.ao = lerp(na.a, nb.a, mixer);
                g.normal = normalize(float3(t.x + normalWS.x, abs(t.z) * normalWS.y, t.y + normalWS.z));
                return g;
            }

            // Triplanar (Ben Golus' whiteout blend): the side scan on x and z, the top scan on y.
            Grain Triplanar(float3 p, float3 normalWS, float scale, half contrast)
            {
                float3 blend = pow(abs(normalWS), 4.0);
                blend /= dot(blend, 1.0);
                float3 axisSign = normalWS < 0 ? -1.0 : 1.0;
                float2 uvX = p.zy / scale, uvY = p.xz / scale, uvZ = p.xy / scale;
                uvX.x *= axisSign.x; uvY.x *= axisSign.y; uvZ.x *= -axisSign.z;
                half4 dX = SAMPLE_TEXTURE2D(_RockD, sampler_trilinear_repeat_aniso8, uvX), dY = SAMPLE_TEXTURE2D(_RockTopD, sampler_trilinear_repeat_aniso8, uvY), dZ = SAMPLE_TEXTURE2D(_RockD, sampler_trilinear_repeat_aniso8, uvZ);
                half4 nX = SAMPLE_TEXTURE2D(_RockN, sampler_trilinear_repeat_aniso8, uvX), nY = SAMPLE_TEXTURE2D(_RockTopN, sampler_trilinear_repeat_aniso8, uvY), nZ = SAMPLE_TEXTURE2D(_RockN, sampler_trilinear_repeat_aniso8, uvZ);
                half3 tX = UnpackGL(nX), tY = UnpackGL(nY), tZ = UnpackGL(nZ);
                tX.x *= axisSign.x; tY.x *= axisSign.y; tZ.x *= -axisSign.z;
                tX = half3(tX.xy + normalWS.zy, abs(tX.z) * normalWS.x);
                tY = half3(tY.xy + normalWS.xz, abs(tY.z) * normalWS.y);
                tZ = half3(tZ.xy + normalWS.xy, abs(tZ.z) * normalWS.z);
                half4 detail = dX * blend.x + dY * blend.y + dZ * blend.z;
                Grain g;
                g.ratio = lerp(1.0, detail.rgb * 2.0, contrast);
                g.height = detail.a;
                g.ao = nX.a * blend.x + nY.a * blend.y + nZ.a * blend.z;
                g.normal = normalize(tX.zyx * blend.x + tY.xzy * blend.y + tZ.xyz * blend.z);
                return g;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 p = input.positionWS;
                half3 albedo;
                half ao;
                float3 normal;
                half3 shade = lerp(1.0, input.color.rgb * _VertexScale, _VertexColor);

            #if defined(_MODE_GROUND)
                float mixer = smoothstep(0.25, 0.75, Noise(p.xz / 23.0));
                Grain sand = Planar(TEXTURE2D_ARGS(_SandD, sampler_trilinear_repeat_aniso8), _SandN, p.xz, _Scales.x, mixer, n, _Contrast.x);
                Grain grass = Planar(TEXTURE2D_ARGS(_GrassD, sampler_trilinear_repeat_aniso8), _GrassN, p.xz, _Scales.y, mixer, n, _Contrast.y);
                Grain jungle = Planar(TEXTURE2D_ARGS(_JungleD, sampler_trilinear_repeat_aniso8), _JungleN, p.xz, _Scales.z, mixer, n, _Contrast.z);
                Grain dirt = Planar(TEXTURE2D_ARGS(_DirtD, sampler_trilinear_repeat_aniso8), _DirtN, p.xz, _Scales.w, mixer, n, _Contrast.w);
                Grain rock = Triplanar(p, n, _RockScale, _RockContrast);

                // Height blend: where two layers meet, the higher grain (pebbles, tufts, stones) wins.
                float4 w = saturate(input.weights);
                float sandW = saturate(1.0 - dot(w, 1.0));
                float4 hb = w + float4(grass.height, jungle.height, dirt.height, rock.height) * _HeightBlend;
                float sandH = sandW + sand.height * _HeightBlend;
                float top = max(max(max(hb.x, hb.y), max(hb.z, hb.w)), sandH) - _BlendDepth;
                float4 b = max(hb - top, 0.0) * step(0.001, w);
                float sandB = max(sandH - top, 0.0) * step(0.001, sandW);
                float total = dot(b, 1.0) + sandB + 1e-5;
                b /= total;
                sandB /= total;

                albedo = sand.ratio * _SandColor.rgb * sandB + grass.ratio * _GrassColor.rgb * b.x + jungle.ratio * _JungleColor.rgb * b.y
                    + dirt.ratio * _DirtColor.rgb * b.z + rock.ratio * _RockColor.rgb * b.w;
                ao = sand.ao * sandB + grass.ao * b.x + jungle.ao * b.y + dirt.ao * b.z + rock.ao * b.w;
                normal = normalize(sand.normal * sandB + grass.normal * b.x + jungle.normal * b.y + dirt.normal * b.z + rock.normal * b.w);
                albedo *= shade;
            #elif defined(_MODE_ROCK)
                Grain rock = Triplanar(p, n, _RockScale, _RockContrast);
                albedo = rock.ratio * _RockColor.rgb * shade;
                ao = rock.ao;
                normal = rock.normal;
            #else
                // A model whose own map is smeary keeps only its broad colour (a low mip) and takes its detail from the scan.
                half3 baseColour = lerp(SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, input.uv0, _BaseBlurMip).rgb,
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv0).rgb, _BaseDetail);
                half grey = dot(baseColour, half3(0.2126, 0.7152, 0.0722));
                baseColour = lerp(baseColour, grey, _Desaturate) * _BaseTint.rgb;
                // Grey stone gets the grain; red roofs, black flags and gold don't.
                half chroma = max(max(baseColour.r, baseColour.g), baseColour.b) - min(min(baseColour.r, baseColour.g), baseColour.b);
                half stone = lerp(1.0, 1.0 - smoothstep(0.06, 0.16, chroma / max(grey, 0.05) * 0.5), _StoneOnly);
                Grain rock = Triplanar(p, n, _RockScale, _RockContrast * stone);
                albedo = baseColour * rock.ratio * shade;
                ao = lerp(1.0, rock.ao, stone);
                normal = normalize(lerp(n, rock.normal, stone));
            #endif

                InputData inputData = (InputData)0;
                inputData.positionWS = p;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normal;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(p);
                inputData.shadowCoord = TransformWorldToShadowCoord(p);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normal);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = saturate(albedo);
                surface.metallic = 0;
                surface.specular = 0;
                surface.smoothness = _Smoothness;
                surface.occlusion = lerp(1.0, ao, _Occlusion);
                surface.normalTS = half3(0, 0, 1);
                surface.alpha = 1;

                half4 colour = UniversalFragmentPBR(inputData, surface);
                colour.rgb = MixFog(colour.rgb, inputData.fogCoord);
                return half4(colour.rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return positionCS;
            }

            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 vert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
}

// A HUD picture cut to a circle with a soft edge (the speaker's portrait on the dialogue's name plate, UI/Hud.RoundPicture).
// The picture is drawn opaque inside the circle: a camera's picture may carry no useful alpha.
Shader "PleaseDontDrown/HudRound"
{
    Properties
    {
        [PerRendererData] _MainTex ("Picture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "CanUseSpriteAtlas" = "True" }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 c = fixed4(tex2D(_MainTex, i.uv).rgb, 1) * i.color;
                float d = length(i.uv - 0.5) * 2;
                float edge = max(fwidth(d), 0.002);
                c.a *= 1 - smoothstep(1 - edge * 1.5, 1, d);
                return c;
            }
            ENDCG
        }
    }
}

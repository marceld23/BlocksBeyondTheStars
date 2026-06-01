// Additive sun-disc glow billboard (M27). Drawn in the sky in the sun direction, tinted by the
// per-system sun colour. Soft radial glow texture, additive blend, depth-tested so terrain/hills
// occlude it (ZTest LEqual) but it never writes depth. Built-in render pipeline; always-included.
Shader "Spacecraft/SunGlow"
{
    Properties
    {
        _MainTex ("Glow", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                fixed3 col = _Color.rgb * t.rgb * (t.a * _Color.a);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}

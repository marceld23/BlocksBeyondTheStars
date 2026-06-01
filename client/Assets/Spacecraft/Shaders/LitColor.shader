// Simple lit, optionally-textured opaque shader (M27). Used by the menu backdrop so its planet,
// ship and asteroids are shaded in 3D (instead of flat Unlit/Color) and can carry a block texture.
// Uses a fixed key-light direction (no scene Light needed) plus an ambient floor, so it is robust
// in stripped builds and independent of any runtime lighting globals. Always-included.
Shader "Spacecraft/LitColor"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Cull Back
        ZWrite On

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            sampler2D _MainTex;
            float4 _MainTex_ST;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 wn : TEXCOORD0;
                float2 uv : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fixed key light from the upper-right, toward the camera so front + top faces catch it.
                float3 L = normalize(float3(0.4, 0.7, -0.55));
                float ndl = saturate(dot(normalize(i.wn), L));
                fixed3 tex = tex2D(_MainTex, i.uv).rgb;
                fixed3 col = _Color.rgb * tex * (0.35 + 0.75 * ndl);
                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}

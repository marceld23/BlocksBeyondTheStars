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
            float4 _Sc_LampPos;   // headlamp: xyz world pos, w range
            float4 _Sc_LampDir;   // headlamp: xyz forward dir, w cone cos
            fixed4 _Sc_LampColor; // headlamp: rgb colour*intensity, a = enabled

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
                float3 wp : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Fixed key light from the upper-right, toward the camera so front + top faces catch it.
                float3 N = normalize(i.wn);
                float3 L = normalize(float3(0.4, 0.7, -0.55));
                float ndl = saturate(dot(N, L));
                fixed3 tex = tex2D(_MainTex, i.uv).rgb;
                fixed3 col = _Color.rgb * tex * (0.35 + 0.75 * ndl);

                // Headlamp / flashlight (shared global with the block shader).
                if (_Sc_LampColor.a > 0.5)
                {
                    float3 toFrag = i.wp - _Sc_LampPos.xyz;
                    float ld = length(toFrag);
                    float3 dir = toFrag / max(ld, 1e-4);
                    float cone = saturate((dot(dir, normalize(_Sc_LampDir.xyz)) - _Sc_LampDir.w) / max(1e-3, 1.0 - _Sc_LampDir.w));
                    float atten = saturate(1.0 - ld / _Sc_LampPos.w);
                    col += _Color.rgb * tex * _Sc_LampColor.rgb * cone * atten * atten * saturate(dot(N, -dir));
                }

                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    Fallback "Unlit/Color"
}

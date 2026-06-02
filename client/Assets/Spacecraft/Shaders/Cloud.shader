// Soft alpha-blended cloud billboard / shell (weather). Used for the surface cloud layer (drifting
// puffs in the sky) and the cloud shell seen over a planet from space. Unlit, tinted by _Color (the
// per-planet cloud colour, darkened in storms), depth-tested so terrain/the planet front occludes it
// but it never writes depth (clouds blend among themselves). Built-in render pipeline; always-included.
Shader "Spacecraft/Cloud"
{
    Properties
    {
        _MainTex ("Cloud", 2D) = "white" {}
        _Color ("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
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
                return fixed4(_Color.rgb * t.rgb, t.a * _Color.a);
            }
            ENDCG
        }
    }
}

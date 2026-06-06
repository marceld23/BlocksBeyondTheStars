// Alpha-blended sibling of Spacecraft/BlockAtlas for see-through blocks (glass viewports + station
// force-field/energy barriers). Same atlas + sun globals, but drawn in the Transparent queue so the
// world behind shows through. Vertex colour: r=gloss, g=metal, b=face shade, a=emission (glow).
//   _Sc_Light  = system sun colour x day brightness x weather dim (a>0.5 = set)
//   _Sc_SunDir = world-space direction TO the sun
// Built-in render pipeline; no per-light passes.
Shader "Spacecraft/BlockAtlasTransparent"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _BaseAlpha ("Base Alpha", Range(0,1)) = 0.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off          // a single-layer pane/field reads from both sides
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Sc_Light;   // system sun colour x day brightness x weather (a>0.5 = set)
            float4 _Sc_SunDir;  // world-space direction TO the sun
            float _BaseAlpha;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR; // r=gloss, g=metal, b=face shade, a=emission
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                fixed4 mat : COLOR;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.mat = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                fixed3 albedo = tex.rgb;
                fixed3 light = (_Sc_Light.a < 0.5) ? fixed3(1, 1, 1) : _Sc_Light.rgb;

                float3 N = normalize(i.wn);
                float3 L = normalize(_Sc_SunDir.xyz);
                float ndl = saturate(dot(N, L));
                float shade = lerp(0.7, 1.0, i.mat.b); // per-face shading baked by the mesher
                float emission = i.mat.a;              // glow (energy fields shine; plain glass = 0)

                fixed3 col = albedo * light * (0.55 + 0.45 * ndl) * shade;
                col += albedo * emission * 2.0;        // emissive energy-field glow (bloom catches it)

                float alpha;
                if (tex.a < 0.95)
                {
                    // Water: a clear blue body (no milky frost), alpha straight from the tile, so you see into
                    // and through it while swimming. The tile alpha is < 1 only for water (set by the atlas).
                    alpha = tex.a;
                }
                else
                {
                    // Plain glass (no emission) reads as a frosted, milky pane — clearly glass, not an open hole
                    // — while emissive energy fields stay an airy, see-through curtain.
                    float isField = saturate(emission * 4.0);          // ~0 for glass, ~1 for energy fields
                    col = lerp(col + light * 0.16, col, isField);      // a soft white frost on glass only
                    alpha = lerp(0.72, _BaseAlpha, isField);           // milky glass vs. see-through field
                    alpha = saturate(alpha + emission * 0.15);
                }

                fixed4 outc = fixed4(col, alpha);
                UNITY_APPLY_FOG(i.fogCoord, outc);
                return outc;
            }
            ENDCG
        }
    }

    Fallback Off
}

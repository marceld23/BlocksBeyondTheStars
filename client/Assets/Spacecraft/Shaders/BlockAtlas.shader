// Lit + reflective opaque shader for the block atlas (M27 renderer). Samples the atlas, shades each
// face by the real sun direction (normal . sun) in the per-system sun colour, and adds per-block
// specular highlights + a cheap grazing-angle environment reflection. Three globals, set by Sky:
//   _Sc_Light  = system sun colour x day brightness x weather dim (a>0.5 marks it as set)
//   _Sc_SunDir = world-space direction TO the sun
//   _Sc_Sky    = current sky colour (sampled for environment reflections)
// Per-block material comes from the vertex colour: r=gloss, g=metal, b=per-face AO.
// Built-in render pipeline; no per-light passes (robust, no ForwardBase dependency).
Shader "Spacecraft/BlockAtlas"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
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

            sampler2D _MainTex;
            fixed4 _Sc_Light;   // system sun colour x day brightness x weather (a>0.5 = set)
            float4 _Sc_SunDir;  // world-space direction TO the sun
            fixed4 _Sc_Sky;     // sky colour for environment reflections

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;   // r=gloss, g=metal, b=face AO
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                fixed4 mat : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.mat = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed3 albedo = tex2D(_MainTex, i.uv).rgb;
                fixed3 light = (_Sc_Light.a < 0.5) ? fixed3(1, 1, 1) : _Sc_Light.rgb;

                float3 N = normalize(i.wn);
                float3 L = normalize(_Sc_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float ndl = saturate(dot(N, L));

                // Diffuse: ambient floor + directional term, coloured by the system sun. A subtle
                // per-face AO (vertex blue) keeps cube edges readable even on shaded faces.
                float faceAo = lerp(0.82, 1.0, i.mat.b);
                fixed3 col = albedo * light * (0.55 + 0.55 * ndl) * faceAo;

                float gloss = i.mat.r;
                float metal = i.mat.g;

                // Specular sun highlight (Blinn-Phong). Tighter + brighter as gloss rises; metals
                // tint the highlight with the albedo. Gated by ndl so only sunlit faces glint.
                float3 H = normalize(L + V);
                float specPow = lerp(8.0, 200.0, gloss);
                float spec = pow(saturate(dot(N, H)), specPow) * gloss * ndl;
                fixed3 specCol = lerp(fixed3(1, 1, 1), albedo, metal);
                col += light * specCol * spec * 1.2;

                // Grazing-angle environment reflection (sky colour); metals tint it by the albedo.
                fixed3 envCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                fixed3 reflTint = lerp(envCol, envCol * albedo, metal);
                float fres = pow(1.0 - saturate(dot(N, V)), 4.0);
                float reflK = saturate(gloss * (0.25 + 0.6 * metal)) * fres;
                col = lerp(col, reflTint, reflK);

                return fixed4(col, 1);
            }
            ENDCG
        }
    }

    Fallback "Spacecraft/VertexColorOpaque"
}

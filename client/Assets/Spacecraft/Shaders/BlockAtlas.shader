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
        _NormalTex ("Normal", 2D) = "bump" {}
        _LeafCutoff ("Leaf alpha cutoff", Range(0,1)) = 0.5
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
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _NormalTex; // tangent-space normal map (Sobel-derived from the atlas)
            fixed4 _Sc_Light;   // system sun colour x day brightness x weather (a>0.5 = set)
            float4 _Sc_SunDir;  // world-space direction TO the sun
            fixed4 _Sc_Sky;     // sky colour for environment reflections
            float4 _Sc_LampPos;   // headlamp: xyz world pos, w range
            float4 _Sc_LampDir;   // headlamp: xyz forward dir, w cone cos
            fixed4 _Sc_LampColor; // headlamp: rgb colour*intensity, a = enabled
            float _Sc_Indoor;     // ship-interior fill (0..1): lights skylight-occluded cabin faces
            fixed4 _Sc_FloraTint; // planet flora base hue (rgb); flora faces are desaturated + re-tinted to it
            float _LeafCutoff;    // alpha-test threshold for cutout foliage (leaf tiles carry a baked alpha mask)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                float2 sky : TEXCOORD1;  // x = skylight (1 sees sky, 0 underground/indoors); y = flora flag
                float2 leaf : TEXCOORD2; // x = foliage flag (1 → alpha-cutout this face)
                fixed4 color : COLOR;   // r=gloss, g=metal, b=face AO
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 wt : TEXCOORD4; // world tangent xyz + bitangent handedness w
                float2 skyl : TEXCOORD5; // x = skylight, y = flora flag
                float2 leaf : TEXCOORD6; // x = foliage flag
                fixed4 mat : COLOR;
                UNITY_FOG_COORDS(3)
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wt = float4(UnityObjectToWorldDir(v.tangent.xyz), v.tangent.w);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.skyl = v.sky;
                o.leaf = v.leaf;
                o.mat = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 texel = tex2D(_MainTex, i.uv);
                // Cutout foliage: the leaf tile's baked alpha mask punches the gaps between leaves, so tree
                // crowns + leafy plants are see-through instead of solid cubes. Only leaf-flagged faces clip
                // (every other tile is fully opaque and unaffected).
                if (i.leaf.x > 0.5)
                {
                    clip(texel.a - _LeafCutoff);
                }

                fixed3 albedo = texel.rgb;

                // Flora re-tint: where the flora flag is set, drop the tile to its luminance and re-colour it
                // by the planet's uniform flora hue, so all of a world's plant life shares one base colour
                // regardless of the underlying tile (a hint of the tile's own hue is kept for richness).
                if (i.skyl.y > 0.5 && _Sc_FloraTint.a > 0.5)
                {
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, lum * _Sc_FloraTint.rgb * 1.6, 0.85);
                }

                fixed3 light = (_Sc_Light.a < 0.5) ? fixed3(1, 1, 1) : _Sc_Light.rgb;

                // Per-pixel normal from the Sobel-derived map, lifted into world space (tangent basis),
                // so flat faces show micro-relief under the sun + lamp.
                float3 gN = normalize(i.wn);
                float3 T = normalize(i.wt.xyz);
                float3 B = normalize(cross(gN, T) * i.wt.w);
                float3 tn = tex2D(_NormalTex, i.uv).xyz * 2.0 - 1.0;
                float3 N = normalize(tn.x * T + tn.y * B + tn.z * gN);

                float3 L = normalize(_Sc_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float ndl = saturate(dot(N, L));

                // Skylight: 1 in the open, ~0 underground / indoors. The sun, sky-ambient, specular and
                // environment reflection are all gated by it, so caves + interiors are dark and rely on
                // the headlamp + emissive light blocks (added below, unaffected by skylight).
                float sky = saturate(i.skyl.x);

                // Diffuse: a tiny cave-ambient floor + (sky-ambient + directional) scaled by skylight,
                // coloured by the system sun. A subtle per-face AO keeps cube edges readable.
                float faceAo = lerp(0.88, 1.0, i.mat.b);
                // Ambient fill: a brighter floor so shadowed faces / overcast aren't crushed to black
                // (outdoors ~0.70, a readable cave floor of 0.24). The directional adds the sunny side on top.
                // A small flat term (sun/sky-independent) guarantees a minimum readable level, so blocks in a
                // dark hole or deep cave are dim but never pure black.
                float amb = lerp(0.24, 0.70, sky);
                fixed3 col = albedo * (light * (amb + 0.5 * ndl * sky) + 0.05) * faceAo;

                // Ship interior fill: a neutral, day/night-independent fill on skylight-occluded faces only
                // (so the cabin is lit but the sunlit outdoors seen through windows is untouched).
                col += albedo * (_Sc_Indoor * 0.5 * (1.0 - sky)) * faceAo;

                float gloss = i.mat.r;
                float metal = i.mat.g;

                // Specular sun highlight (Blinn-Phong). Tighter + brighter as gloss rises; metals
                // tint the highlight with the albedo. Gated by ndl + skylight (no sun glint in caves).
                float3 H = normalize(L + V);
                float specPow = lerp(8.0, 200.0, gloss);
                float spec = pow(saturate(dot(N, H)), specPow) * gloss * ndl * sky;
                fixed3 specCol = lerp(fixed3(1, 1, 1), albedo, metal);
                col += light * specCol * spec * 1.2;

                // Grazing-angle environment reflection (sky colour); metals tint it. No sky to reflect
                // underground, so it fades with skylight too.
                fixed3 envCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                fixed3 reflTint = lerp(envCol, envCol * albedo, metal);
                float fres = pow(1.0 - saturate(dot(N, V)), 4.0);
                float reflK = saturate(gloss * (0.25 + 0.6 * metal)) * fres * sky;
                col = lerp(col, reflTint, reflK);

                // Emissive blocks glow independently of sun + fog (lights/lava/crystals/ores), so they
                // shine at night and the bloom pass picks them up. Alpha carries the emission strength.
                col += albedo * i.mat.a * 2.0;

                // Headlamp / flashlight — a custom spotlight (this shader bypasses Unity's light passes,
                // so the lamp is fed in as globals by the player instead of a real Light).
                if (_Sc_LampColor.a > 0.5)
                {
                    float3 toFrag = i.wp - _Sc_LampPos.xyz;
                    float ld = length(toFrag);
                    float3 dir = toFrag / max(ld, 1e-4);
                    float cone = saturate((dot(dir, normalize(_Sc_LampDir.xyz)) - _Sc_LampDir.w) / max(1e-3, 1.0 - _Sc_LampDir.w));
                    float atten = saturate(1.0 - ld / _Sc_LampPos.w);
                    float ndl2 = saturate(dot(N, -dir));
                    col += albedo * _Sc_LampColor.rgb * cone * atten * atten * ndl2;
                }

                fixed4 outc = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, outc); // fade into the sky-coloured distance fog
                return outc;
            }
            ENDCG
        }
    }

    Fallback "Spacecraft/VertexColorOpaque"
}

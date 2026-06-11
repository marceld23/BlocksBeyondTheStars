// Lit + reflective opaque shader for the block atlas (M27 renderer). Samples the atlas, shades each
// face by the real sun direction (normal . sun) in the per-system sun colour, and adds per-block
// specular highlights + a cheap grazing-angle environment reflection. Three globals, set by Sky:
//   _Sc_Light  = system sun colour x day brightness x weather dim (a>0.5 marks it as set)
//   _Sc_SunDir = world-space direction TO the sun
//   _Sc_Sky    = current sky colour (sampled for environment reflections)
// Per-block material comes from the vertex colour: r=gloss, g=metal, b=per-face AO.
//
// DUAL-PIPELINE (URP migration): SubShader 1 is the URP port (HLSL, UniversalForward), SubShader 2 is the
// original Built-in RP pass (CG, unchanged). The active pipeline picks the matching SubShader, so the project
// renders identically in Built-in today and gains URP when the pipeline asset is assigned. The fragment maths
// is identical in both — both bypass the engine's light passes and read the same _Sc_* globals.
Shader "Spacecraft/BlockAtlas"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _NormalTex ("Normal", 2D) = "bump" {}
        _LeafCutoff ("Leaf alpha cutoff", Range(0,1)) = 0.5
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Back
        ZWrite On

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            // Receive the sun's real-time shadow map (URP main light).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

            // Globals fed by Sky/player via Shader.SetGlobal* — kept outside any per-material cbuffer.
            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float4 _Sc_Sky;
            float4 _Sc_LampPos;
            float4 _Sc_LampDir;
            float4 _Sc_LampColor;
            float  _Sc_Indoor;
            float4 _Sc_FloraTint;
            float  _LeafCutoff;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv : TEXCOORD0;
                float2 sky : TEXCOORD1;  // x = skylight, y = flora flag
                float4 leaf : TEXCOORD2; // x = foliage flag, yzw = per-species flora tint (black = use global)
                float4 color : COLOR;    // r=gloss, g=metal, b=face AO, a=emission
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 wt : TEXCOORD3;
                float2 skyl : TEXCOORD4;
                float4 leaf : TEXCOORD5;
                float4 mat : TEXCOORD6;
                float  fog : TEXCOORD7;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.wt = float4(TransformObjectToWorldDir(v.tangent.xyz), v.tangent.w);
                o.wp = wp;
                o.skyl = v.sky;
                o.leaf = v.leaf;
                o.mat = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 texel = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                if (i.leaf.x > 0.5)
                {
                    clip(texel.a - _LeafCutoff);
                }

                float3 albedo = texel.rgb;
                if (i.skyl.y > 0.5 && _Sc_FloraTint.a > 0.5)
                {
                    // Per-SPECIES tint from the mesh (TEXCOORD2.yzw) when present; meshes built without a
                    // tint resolver carry black there and keep the planet's uniform global hue.
                    float3 tint = dot(i.leaf.yzw, float3(1, 1, 1)) > 0.01 ? i.leaf.yzw : _Sc_FloraTint.rgb;
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, lum * tint * 1.6, 0.85);
                }

                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;

                float3 gN = normalize(i.wn);
                float3 T = normalize(i.wt.xyz);
                float3 B = normalize(cross(gN, T) * i.wt.w);
                float3 tn = SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, i.uv).xyz * 2.0 - 1.0;
                float3 N = normalize(tn.x * T + tn.y * B + tn.z * gN);

                float3 L = normalize(_Sc_SunDir.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float ndl = saturate(dot(N, L));
                float sky = saturate(i.skyl.x);

                // Sun shadow map (URP main light): 1 where lit, →0 in shadow. Only the direct-sun terms are
                // shadowed (ambient/sky fill + emissive stay), so shadowed faces dim but never go black.
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.wp));

                float faceAo = lerp(0.88, 1.0, i.mat.b);
                float amb = lerp(0.24, 0.70, sky);
                float3 col = albedo * (light * (amb + 0.5 * ndl * sky * shadow) + 0.05) * faceAo;
                col += albedo * (_Sc_Indoor * 0.5 * (1.0 - sky)) * faceAo;

                float gloss = i.mat.r;
                float metal = i.mat.g;
                float3 H = normalize(L + V);
                float specPow = lerp(8.0, 200.0, gloss);
                float spec = pow(saturate(dot(N, H)), specPow) * gloss * ndl * sky * shadow;
                float3 specCol = lerp(float3(1, 1, 1), albedo, metal);
                col += light * specCol * spec * 1.2;

                float3 envCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                float3 reflTint = lerp(envCol, envCol * albedo, metal);
                float fres = pow(1.0 - saturate(dot(N, V)), 4.0);
                float reflK = saturate(gloss * (0.25 + 0.6 * metal)) * fres * sky;
                col = lerp(col, reflTint, reflK);

                col += albedo * i.mat.a * 2.0;

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

                half4 outc = half4(col, 1);
                outc.rgb = MixFog(outc.rgb, i.fog);
                return outc;
            }
            ENDHLSL
        }

        // Cast the sun's shadows into the main-light shadow map (URP). Foliage punches holes via alpha-clip.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _LeafCutoff;
            float3 _LightDirection; // set by URP while rendering the shadow map

            struct SAttr { float4 positionOS : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct SVary { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            SVary shadowVert(SAttr v)
            {
                SVary o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 wn = TransformObjectToWorldNormal(v.normal);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(wp, wn, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = v.uv;
                return o;
            }

            half4 shadowFrag(SVary i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                clip(a - _LeafCutoff); // opaque tiles (a=1) always pass; foliage holes clip
                return 0;
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (original, unchanged) ----------------
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
                float4 leaf : TEXCOORD2; // x = foliage flag (1 → alpha-cutout); yzw = per-species flora tint
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
                float4 leaf : TEXCOORD6; // x = foliage flag, yzw = per-species flora tint
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

                // Flora re-tint: where the flora flag is set, drop the tile to its luminance and re-colour
                // it. The mesh carries a per-SPECIES x per-world tint (TEXCOORD2.yzw); meshes built without
                // a tint resolver carry black there and fall back to the planet's uniform global hue.
                if (i.skyl.y > 0.5 && _Sc_FloraTint.a > 0.5)
                {
                    float3 tint = dot(i.leaf.yzw, float3(1, 1, 1)) > 0.01 ? i.leaf.yzw : _Sc_FloraTint.rgb;
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, lum * tint * 1.6, 0.85);
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

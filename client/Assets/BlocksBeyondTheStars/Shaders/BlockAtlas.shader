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
Shader "BlocksBeyondTheStars/BlockAtlas"
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
            // Alpha-to-coverage smooths the hard foliage cutout edges against MSAA (opaque tiles have a=1 →
            // full coverage, unaffected); kills the shimmering on leaf/grass silhouettes for free.
            AlphaToMask On
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
            float4 _Sc_Fog;       // explicit distance haze (URP MixFog doesn't engage here): x=start, y=end, z=max, w=on
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
                float2 sky : TEXCOORD1;  // x = skylight, y = tint mode (1 flora, 2 hull paint, 3 player dye, 4 bark)
                float4 leaf : TEXCOORD2; // x = foliage flag, yzw = flora/hull/dye/bark tint (black = use global flora hue)
                float3 bl : TEXCOORD3;   // propagated coloured block-light (placed lights illuminate)
                float3 blDir : TEXCOORD4; // dominant block-light direction (toward source); 0 = none
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
                float3 bl : TEXCOORD8;
                float3 blDir : TEXCOORD9;
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
                o.bl = v.bl;
                o.blDir = v.blDir;
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
                if (i.skyl.y > 3.5)
                {
                    // Bark (mode 4): the tree trunk's per-world DARK bark hue (TEXCOORD2.yzw). Forced dark at
                    // the source so it always reads clearly darker than the bright leaf tint. A gentler blend
                    // than leaves keeps the bark grain. Black (no resolver / ship mesh) → keep natural bark.
                    if (dot(i.leaf.yzw, float3(1, 1, 1)) > 0.01)
                    {
                        float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                        albedo = lerp(albedo, lum * i.leaf.yzw * 1.4, 0.72);
                    }
                }
                else if (i.skyl.y > 2.5)
                {
                    // Player dye (mode 3): a luminance-based recolour applied everywhere (independent of the
                    // flora-tint global), so dyed building blocks read vividly on any world and in caves.
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, lum * i.leaf.yzw * 1.6, 0.85);
                }
                else if (i.skyl.y > 1.5)
                {
                    // Hull paint (item 32): multiply the per-ship tint (TEXCOORD2.yzw) into the albedo —
                    // the same look as the old tinted-silhouette hull (_Color * tex), independent of the
                    // flora globals so painted ships read identically on planets and in space.
                    albedo *= i.leaf.yzw;
                }
                else if (i.skyl.y > 0.5 && _Sc_FloraTint.a > 0.5)
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

                // Night/atmosphere ambient floor: a faint cool fill on open-sky faces, strongest when the sun
                // light is weak (night/storm) and fading out toward day, so worlds read as dim atmospheric blue
                // at night instead of crushing to pure black. Independent of the (near-black) night sky colour
                // and the sun tint; gated by skylight so caves/interiors stay dark and still need a lamp.
                float nightFloor = saturate(0.6 - dot(light, float3(0.299, 0.587, 0.114)));
                col += albedo * float3(0.10, 0.13, 0.20) * (sky * nightFloor) * faceAo;

                float gloss = i.mat.r;            // perceptual smoothness (0 = matte .. 1 = mirror)
                float metal = i.mat.g;            // metallic (0 = dielectric .. 1 = metal)
                float rough = clamp(1.0 - gloss, 0.045, 1.0);

                // Specular: Unity's stable GGX form (no NaN at low roughness), tinted by the metallic F0
                // (0.04 dielectric → albedo for metals). Tight, bright glints on smooth/metal faces feed the bloom.
                float3 H = normalize(L + V);
                float nh = saturate(dot(N, H));
                float lh = saturate(dot(L, H));
                float r2 = rough * rough;
                float dterm = nh * nh * (r2 - 1.0) + 1.00001;
                float specTerm = r2 / ((dterm * dterm) * max(0.1, lh * lh) * (rough * 4.0 + 2.0));
                float3 F0 = lerp(float3(0.04, 0.04, 0.04), albedo, metal);
                col += light * F0 * (specTerm * ndl * sky * shadow);

                // Environment reflection of the sky colour, roughness-aware: metals reflect strongly (tinted by
                // F0) even head-on, dielectrics mostly at grazing angles (Fresnel). Additive, faded by skylight.
                float3 envCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                float nv = saturate(dot(N, V));
                float fres = pow(1.0 - nv, 5.0);
                float gr = 1.0 - rough;
                float3 Fr = F0 + (max(float3(gr, gr, gr), F0) - F0) * fres;
                col += envCol * Fr * (saturate(gloss) * sky * 0.5);

                col += albedo * i.mat.a * 3.0; // HDR overdrive: push emitters past white so ACES + bloom give a real glow

                // Placed coloured lights (flood-filled per-vertex, TEXCOORD3): illuminate this surface in
                // their colour, regardless of sun/skylight, so lamps light caves + night builds. The baked
                // dominant direction (TEXCOORD4) lets us shade them like the sun — N·L diffuse shaping + a
                // GGX glint + normal-map relief — so lamps sculpt the surface instead of flat-washing
                // it. A fill floor keeps faces the light wrapped around lit; bright enough to feed the bloom.
                float blLen = length(i.blDir);
                if (blLen > 0.01)
                {
                    float3 blL = i.blDir / blLen;
                    float blNdl = saturate(dot(N, blL));
                    col += albedo * i.bl * (0.5 + 0.5 * blNdl) * 2.0;
                    float3 blH = normalize(blL + V);
                    float blNh = saturate(dot(N, blH));
                    float blLh = saturate(dot(blL, blH));
                    float blDterm = blNh * blNh * (r2 - 1.0) + 1.00001;
                    float blSpec = r2 / ((blDterm * blDterm) * max(0.1, blLh * blLh) * (rough * 4.0 + 2.0));
                    col += i.bl * F0 * (blSpec * blNdl);
                }
                else
                {
                    col += albedo * i.bl * 2.0; // no direction baked (uniform/none) → flat fallback
                }

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

                // Explicit distance haze toward the sky colour (Unity's MixFog path doesn't engage on this
                // unlit shader). Driven by _Sc_Fog (x=start, y=end, z=max already faded indoors, w=on). Blends in
                // shader space so it only tints distant terrain — it never darkens the frame like a full-screen pass.
                if (_Sc_Fog.w > 0.5)
                {
                    float camDist = distance(i.wp, _WorldSpaceCameraPos);
                    float haze = saturate((camDist - _Sc_Fog.x) / max(1.0, _Sc_Fog.y - _Sc_Fog.x)) * _Sc_Fog.z;
                    float3 hazeCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                    col = lerp(col, hazeCol, haze);
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
            AlphaToMask On // smooth foliage cutout edges against MSAA (opaque a=1 unaffected) — Built-in RP path
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
                float2 sky : TEXCOORD1;  // x = skylight (1 sees sky, 0 underground/indoors); y = tint mode (1 flora, 2 hull paint, 3 player dye)
                float4 leaf : TEXCOORD2; // x = foliage flag (1 → alpha-cutout); yzw = flora/hull/dye tint
                float3 bl : TEXCOORD3;   // propagated coloured block-light (placed lights illuminate)
                float3 blDir : TEXCOORD4; // dominant block-light direction (toward source); 0 = none
                fixed4 color : COLOR;   // r=gloss, g=metal, b=face AO
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 wt : TEXCOORD4; // world tangent xyz + bitangent handedness w
                float2 skyl : TEXCOORD5; // x = skylight, y = tint mode
                float4 leaf : TEXCOORD6; // x = foliage flag, yzw = per-species/hull/dye tint
                float3 bl : TEXCOORD7; // propagated coloured block-light
                float3 blDir : TEXCOORD8; // dominant block-light direction
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
                o.bl = v.bl;
                o.blDir = v.blDir;
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

                // Player dye (mode 3): a luminance-based recolour applied everywhere (independent of the
                // flora-tint global), so dyed building blocks read vividly on any world and in caves.
                if (i.skyl.y > 2.5)
                {
                    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
                    albedo = lerp(albedo, lum * i.leaf.yzw * 1.6, 0.85);
                }
                // Hull paint (item 32): multiply the per-ship tint (TEXCOORD2.yzw) into the albedo — the
                // same look as the old tinted-silhouette hull (_Color * tex), independent of flora globals.
                else if (i.skyl.y > 1.5)
                {
                    albedo *= i.leaf.yzw;
                }
                // Flora re-tint: where the flora flag is set, drop the tile to its luminance and re-colour
                // it. The mesh carries a per-SPECIES x per-world tint (TEXCOORD2.yzw); meshes built without
                // a tint resolver carry black there and fall back to the planet's uniform global hue.
                else if (i.skyl.y > 0.5 && _Sc_FloraTint.a > 0.5)
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

                // Night/atmosphere ambient floor (mirrors the URP path): a faint cool fill on open-sky faces,
                // strongest when the sun light is weak (night/storm) and fading out toward day, so worlds read as
                // dim atmospheric blue at night instead of crushing to pure black. Gated by skylight (caves stay dark).
                float nightFloor = saturate(0.6 - dot(light, fixed3(0.299, 0.587, 0.114)));
                col += albedo * fixed3(0.10, 0.13, 0.20) * (sky * nightFloor) * faceAo;

                float gloss = i.mat.r;            // perceptual smoothness
                float metal = i.mat.g;            // metallic
                float rough = clamp(1.0 - gloss, 0.045, 1.0);

                // Specular sun highlight: Unity's stable GGX form, tinted by the metallic F0 (0.04 dielectric →
                // albedo for metals). Tight, bright glints on smooth/metal faces; gated by ndl + skylight.
                float3 H = normalize(L + V);
                float nh = saturate(dot(N, H));
                float lh = saturate(dot(L, H));
                float r2 = rough * rough;
                float dterm = nh * nh * (r2 - 1.0) + 1.00001;
                float specTerm = r2 / ((dterm * dterm) * max(0.1, lh * lh) * (rough * 4.0 + 2.0));
                fixed3 F0 = lerp(fixed3(0.04, 0.04, 0.04), albedo, metal);
                col += light * F0 * (specTerm * ndl * sky);

                // Roughness-aware environment reflection of the sky colour: metals reflect strongly (tinted by
                // F0) even head-on, dielectrics mostly at grazing angles (Fresnel). Additive, faded by skylight.
                fixed3 envCol = (_Sc_Sky.a < 0.5) ? light : _Sc_Sky.rgb;
                float nv = saturate(dot(N, V));
                float fres = pow(1.0 - nv, 5.0);
                float gr = 1.0 - rough;
                fixed3 Fr = F0 + (max(float3(gr, gr, gr), F0) - F0) * fres;
                col += envCol * Fr * (saturate(gloss) * sky * 0.5);

                // Emissive blocks glow independently of sun + fog (lights/lava/crystals/ores), so they
                // shine at night and the bloom pass picks them up. Alpha carries the emission strength.
                col += albedo * i.mat.a * 3.0; // HDR overdrive: push emitters past white so ACES + bloom give a real glow

                // Placed coloured lights (flood-filled per-vertex, TEXCOORD3): illuminate this surface in
                // their colour, regardless of sun/skylight, so lamps light caves + night builds. The baked
                // dominant direction (TEXCOORD4) shades them like the sun — N·L diffuse + a GGX glint
                // + normal-map relief — so lamps sculpt the surface; a fill floor keeps wrapped-around faces lit.
                float blLen = length(i.blDir);
                if (blLen > 0.01)
                {
                    float3 blL = i.blDir / blLen;
                    float blNdl = saturate(dot(N, blL));
                    col += albedo * i.bl * (0.5 + 0.5 * blNdl) * 2.0;
                    float3 blH = normalize(blL + V);
                    float blNh = saturate(dot(N, blH));
                    float blLh = saturate(dot(blL, blH));
                    float blDterm = blNh * blNh * (r2 - 1.0) + 1.00001;
                    float blSpec = r2 / ((blDterm * blDterm) * max(0.1, blLh * blLh) * (rough * 4.0 + 2.0));
                    col += i.bl * F0 * (blSpec * blNdl);
                }
                else
                {
                    col += albedo * i.bl * 2.0; // no direction baked (uniform/none) → flat fallback
                }

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

    Fallback "BlocksBeyondTheStars/VertexColorOpaque"
}

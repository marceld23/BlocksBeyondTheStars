// Alpha-blended sibling of BlocksBeyondTheStars/BlockAtlas for see-through blocks (glass viewports + station
// force-field/energy barriers + water). Same atlas + sun globals, but drawn in the Transparent queue so the
// world behind shows through. Vertex colour: r=gloss, g=metal, b=face shade, a=emission (glow).
//   _Sc_Light  = system sun colour x day brightness x weather dim (a>0.5 = set)
//   _Sc_SunDir = world-space direction TO the sun
//
// DUAL-PIPELINE (URP migration): SubShader 1 is the URP port (HLSL, UniversalForward — also receives the sun's
// shadow on the directional term so water/glass dims under shadow); SubShader 2 is the original Built-in RP
// pass (CG, unchanged). Transparent surfaces cast no shadows (no ShadowCaster) by design.
Shader "BlocksBeyondTheStars/BlockAtlasTransparent"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _BaseAlpha ("Base Alpha", Range(0,1)) = 0.4
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off          // a single-layer pane/field reads from both sides
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float4 _Sc_Light;
            float4 _Sc_SunDir;
            float _BaseAlpha;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                // Water top faces: x=mode (1 lake, 2 open, 3 river), y=foam (corner-smoothed),
                // z=wave amplitude factor (corner-smoothed), w=flow axis (0=X, 1=Z).
                float4 water : TEXCOORD2;
                float4 color : COLOR; // r=gloss, g=metal, b=face shade, a=emission
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD2;
                float4 mat : TEXCOORD3;
                float fog : TEXCOORD4;
                float4 water : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);

                // Water bobs on three crossed sine waves — displaced DOWN only (the rest surface is the
                // block top). The amplitude factor is corner-smoothed by the mesher (open water 1, lake
                // 0.25, river/bank 0) and further flattened into the foam band, so shared block corners
                // displace identically — no cracks — and the shoreline stays flush with the terrain.
                float amp = 0.12 * v.water.z * (1.0 - saturate(v.water.y));
                if (amp > 0.0005)
                {
                    float t = _Time.y;
                    float w = sin(wp.x * 0.50 + t * 1.10) + sin(wp.z * 0.41 + t * 1.43)
                            + sin((wp.x + wp.z) * 0.27 + t * 0.70);
                    wp.y -= amp * (0.5 + w / 6.0);
                }

                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.wp = wp;
                o.mat = v.color;
                o.water = v.water;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 albedo = tex.rgb;
                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;

                float3 N = normalize(i.wn);
                float3 L = normalize(_Sc_SunDir.xyz);
                float ndl = saturate(dot(N, L));
                float shade = lerp(0.7, 1.0, i.mat.b); // per-face shading baked by the mesher
                float emission = i.mat.a;              // glow (energy fields shine; plain glass = 0)

                // Sun shadow on the directional half only — shadowed water/glass dims, never blacks out.
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(i.wp));

                float3 col = albedo * light * (0.55 + 0.45 * ndl * shadow) * shade;
                col += albedo * emission * 2.0;        // emissive energy-field glow (bloom catches it)

                float alpha;
                if (tex.a < 0.95)
                {
                    // Water: a clear blue body (no milky frost), alpha straight from the tile, so you see into
                    // and through it while swimming. The tile alpha is < 1 only for water (set by the atlas).
                    alpha = tex.a;

                    float mode = i.water.x;
                    float t = _Time.y;
                    if (mode > 2.5)
                    {
                        // River/brook: bright ripple bands + thin white streaks racing along the channel's
                        // flow axis. UVs cannot scroll inside an atlas tile, so the motion is procedural
                        // on world position.
                        float2 flow = i.water.w < 0.5 ? float2(1.0, 0.0) : float2(0.0, 1.0);
                        float along = dot(i.wp.xz, flow);
                        float across = dot(i.wp.xz, float2(-flow.y, flow.x));
                        float ph = along * 1.9 - t * 5.5;
                        float rip = 0.5 + 0.5 * sin(ph + sin(across * 2.7) * 1.2);
                        col += light * 0.10 * rip;
                        float streak = smoothstep(0.86, 1.0, sin(ph * 1.31 + across * 3.1));
                        col = lerp(col, light * float3(0.95, 0.97, 1.0), streak * 0.35);
                        alpha = saturate(alpha + streak * 0.20 + rip * 0.04);
                    }
                    else if (mode > 1.5)
                    {
                        // Open water: a soft moving sun glint, plus an animated rippled foam band where
                        // the surface meets the shore (i.water.y fades over the last three blocks).
                        float glint = pow(0.5 + 0.5 * sin(i.wp.x * 1.7 + i.wp.z * 1.3 + t * 1.9), 6.0);
                        col += light * 0.06 * ndl * glint;
                        float foam = i.water.y;
                        if (foam > 0.01)
                        {
                            float cell = frac(sin(dot(floor(i.wp.xz * 3.0), float2(12.9898, 78.233))) * 43758.5453);
                            float surge = 0.55 + 0.45 * sin(t * 1.6 + (i.wp.x + i.wp.z) * 0.9 + cell * 6.2832);
                            float f = saturate(foam * surge * (0.6 + 0.6 * cell));
                            col = lerp(col, light * float3(0.97, 0.99, 1.0), f * 0.85);
                            alpha = saturate(alpha + f * 0.45);
                        }
                    }
                    else if (mode > 0.5)
                    {
                        // Calm lake/pond: a barely-there slow shimmer, nothing more.
                        col += light * 0.03 * (0.5 + 0.5 * sin(t * 0.6 + i.wp.x * 0.8 + i.wp.z * 1.1));
                    }
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

                half4 outc = half4(col, alpha);
                outc.rgb = MixFog(outc.rgb, i.fog);
                return outc;
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (original, unchanged) ----------------
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
                // Water top faces: x=mode (1 lake, 2 open, 3 river), y=foam (corner-smoothed),
                // z=wave amplitude factor (corner-smoothed), w=flow axis (0=X, 1=Z).
                float4 water : TEXCOORD2;
                fixed4 color : COLOR; // r=gloss, g=metal, b=face shade, a=emission
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wn : TEXCOORD1;
                float3 wp : TEXCOORD3;
                float4 water : TEXCOORD4;
                fixed4 mat : COLOR;
                UNITY_FOG_COORDS(2)
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Same water motion as the URP pass: down-only crossed sines scaled by the corner-
                // smoothed amplitude factor (open 1, lake 0.25, river/bank 0) and flattened into the
                // foam band — shared corners displace identically, the shoreline stays flush.
                float amp = 0.12 * v.water.z * (1.0 - saturate(v.water.y));
                if (amp > 0.0005)
                {
                    float t = _Time.y;
                    float w = sin(wp.x * 0.50 + t * 1.10) + sin(wp.z * 0.41 + t * 1.43)
                            + sin((wp.x + wp.z) * 0.27 + t * 0.70);
                    wp.y -= amp * (0.5 + w / 6.0);
                }

                o.pos = UnityWorldToClipPos(wp);
                o.uv = v.uv;
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = wp;
                o.water = v.water;
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

                    float mode = i.water.x;
                    float t = _Time.y;
                    if (mode > 2.5)
                    {
                        // River/brook: ripple bands + white streaks racing along the flow axis (procedural
                        // on world position — atlas UVs cannot scroll).
                        float2 flow = i.water.w < 0.5 ? float2(1.0, 0.0) : float2(0.0, 1.0);
                        float along = dot(i.wp.xz, flow);
                        float across = dot(i.wp.xz, float2(-flow.y, flow.x));
                        float ph = along * 1.9 - t * 5.5;
                        float rip = 0.5 + 0.5 * sin(ph + sin(across * 2.7) * 1.2);
                        col += light * 0.10 * rip;
                        float streak = smoothstep(0.86, 1.0, sin(ph * 1.31 + across * 3.1));
                        col = lerp(col, light * fixed3(0.95, 0.97, 1.0), streak * 0.35);
                        alpha = saturate(alpha + streak * 0.20 + rip * 0.04);
                    }
                    else if (mode > 1.5)
                    {
                        // Open water: moving sun glint + animated rippled foam against the shore.
                        float glint = pow(0.5 + 0.5 * sin(i.wp.x * 1.7 + i.wp.z * 1.3 + t * 1.9), 6.0);
                        col += light * 0.06 * ndl * glint;
                        float foam = i.water.y;
                        if (foam > 0.01)
                        {
                            float cell = frac(sin(dot(floor(i.wp.xz * 3.0), float2(12.9898, 78.233))) * 43758.5453);
                            float surge = 0.55 + 0.45 * sin(t * 1.6 + (i.wp.x + i.wp.z) * 0.9 + cell * 6.2832);
                            float f = saturate(foam * surge * (0.6 + 0.6 * cell));
                            col = lerp(col, light * fixed3(0.97, 0.99, 1.0), f * 0.85);
                            alpha = saturate(alpha + f * 0.45);
                        }
                    }
                    else if (mode > 0.5)
                    {
                        // Calm lake/pond: a barely-there slow shimmer.
                        col += light * 0.03 * (0.5 + 0.5 * sin(t * 0.6 + i.wp.x * 0.8 + i.wp.z * 1.1));
                    }
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

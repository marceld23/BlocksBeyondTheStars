// Smoke particles (#3274): textured, sun-lit, eroding puffs for the shared FxKit smoke emitters (campfires, the defeat
// puff, blasts, vent steam and eruptions, volcano columns). The cheap stylised recipe that runs everywhere WebGL2 runs —
// no flipbook simulation, no six-way lighting (that needs VFX Graph):
//   * shape   — a procedural 2x2 atlas of billowy puffs (SmokePuffs, Client.Core); each particle picks a tile and a mirror
//               from its stable random and turns with its own rotation (the emitter's start rotation + spin);
//   * light   — a fake sphere normal on the billboard (the vertex's offset from the particle centre + the view axis), lit
//               by the sun with soft wrap lighting: a bright sun side, a darker far side tinted by the sky, a thin silver
//               lining when the sun is behind the puff. The sun's hue comes from _Sc_Light (warm at sunset — the Sky warms
//               the light there), the level from _Sc_ModelLight (dark at night and in caves, like every model). The
//               billows (atlas G) catch the light unevenly;
//   * erosion — the puff's alpha is cut against a noise (atlas R) with a threshold that rises with the particle's age,
//               so the edges fray and holes open instead of the whole puff fading evenly;
//   * colour  — a slight warm-to-cool shift over the lifetime; the haze (AtmosphereCommon) per vertex; soft particles on
//               Medium+ (fade distance grows with the puff, so a big volcano puff never cuts a hard line into a slope),
//               and a near fade so a puff drifting through the camera never fills the screen.
// Inputs (custom vertex streams set by FxKit): POSITION, COLOR, TEXCOORD0 = (uv.x, uv.y, age 0..1, stable random 0..1),
// TEXCOORD1.xyz = the particle centre. Alpha-blended, ZWrite Off, Cull Off, Transparent queue.
Shader "BlocksBeyondTheStars/ParticleSmoke"
{
    Properties
    {
        _MainTex ("Puff atlas (2x2: R erosion noise, G billows, A density)", 2D) = "white" {}
        _Erosion ("Erosion over the lifetime", Range(0, 1)) = 0.75
        _Lighting ("Sun lighting", Range(0, 1)) = 1
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "AtmosphereCommon.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            // _Sc_* globals stay outside UnityPerMaterial (#573): one value per frame for every material.
            float _Sc_ScreenFx;      // 1 when the depth texture exists (Medium+): soft particles (#2403)
            float _Sc_SoftParticles; // the player's switch (0/1)
            float4 _Sc_Light;        // sun colour x day brightness x weather dim (a > 0.5 = set) — Sky.cs
            float4 _Sc_SunDir;       // world direction TO the sun — Sky.cs (the system's star in the flight view)
            float _Sc_ModelLight;    // the models' share of the world light (#3265; 0 = unset → 1)

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Erosion;
                float _Lighting;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float4 uvAgeRand : TEXCOORD0; // xy = quad UV, z = age 0..1 (AgePercent), w = stable random (StableRandomX)
                float3 center : TEXCOORD1;    // the particle centre (Center)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float4 uv : TEXCOORD0;        // xy = atlas UV, z = age, w = the puff's half size (metres)
                float4 haze : TEXCOORD1;      // rgb = haze colour, a = haze amount
                float4 screenPos : TEXCOORD2;
                float3 sphere : TEXCOORD3;    // offset from the centre / half size: the fake normal's in-plane part
                float3 toCam : TEXCOORD4;     // from the centre toward the camera
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                float3 c = TransformObjectToWorld(v.center);
                o.positionCS = TransformWorldToHClip(wp);

                // A square billboard's corner sits half-size x sqrt(2) from its centre; the offset interpolates linearly
                // across the quad, so the fragment gets its exact place on the disc whatever the particle's rotation.
                float3 off = wp - c;
                float halfSize = max(length(off) * 0.70710678, 1e-4);
                o.sphere = off / halfSize;
                o.toCam = _WorldSpaceCameraPos - c;

                // One of four puffs, mirrored or not, from the particle's stable random.
                float r = saturate(v.uvAgeRand.w) * 3.999;
                float tile = floor(r);
                float2 quv = v.uvAgeRand.xy;
                quv.x = frac(r) > 0.5 ? 1.0 - quv.x : quv.x;
                float2 cell = float2(tile - 2.0 * floor(tile * 0.5), floor(tile * 0.5));
                o.uv = float4((quv + cell) * 0.5, saturate(v.uvAgeRand.z), halfSize);

                o.color = v.color;
                o.haze = BbtsHaze(wp, 1.0, 1.0); // per vertex, like ParticleAlpha: amount + colour in one pass (#3271)
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            // The light falling on the puff at this fragment: wrap-lit fake sphere, billows, silver lining (linear RGB).
            float3 SmokeLight(float3 sphere, float3 V, float density, float billows)
            {
                float3 L = _Sc_SunDir.xyz;
                L = dot(L, L) > 1e-6 ? normalize(L) : float3(0.0, 1.0, 0.0);
                float r2 = dot(sphere, sphere);
                float3 N = normalize(sphere * 0.9 + V * (sqrt(saturate(1.0 - r2)) + 0.25));

                bool lightSet = _Sc_Light.a > 0.5;
                float peak = max(max(_Sc_Light.r, _Sc_Light.g), max(_Sc_Light.b, 1e-3));
                float3 sunHue = lightSet ? _Sc_Light.rgb / peak : float3(1.0, 1.0, 1.0);
                // 0 at night … 1 by day (and in the flight view, where the light is plain white): the sun side only counts
                // while there is a sun to speak of.
                float day = lightSet ? saturate(dot(_Sc_Light.rgb, float3(0.299, 0.587, 0.114)) * 1.6) : 1.0;
                float direct = day * _Lighting;

                float3 sky = _Sc_FogSky.a > 0.5 ? _Sc_FogSky.rgb : float3(0.6, 0.7, 0.9);
                float3 skyHue = sky / max(max(sky.r, sky.g), max(sky.b, 0.05));
                float3 shade = lerp(float3(1.0, 1.0, 1.0), skyHue, 0.3) * 0.55;

                float wrap = saturate((dot(N, L) + 0.6) / 1.6);
                float lumps = lerp(0.82, 1.06, billows);
                float3 lit = lerp(shade, sunHue, wrap) * lumps; // white steam in full sun stays near 1: no bloom halo
                lit = lerp(float3(0.8, 0.8, 0.82) * lumps, lit, direct); // no sun: evenly dim, only the billows show

                float behind = pow(saturate(dot(-V, L)), 6.0);
                lit += sunHue * behind * (1.0 - density) * direct * 0.8; // the thin edges glow against a low sun
                return lit;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv.xy);
                float age = i.uv.z;

                // Erosion: the field is low at the puff's edge and in the noise's valleys; the threshold rises with age.
                float field = t.a * (0.55 + 0.45 * t.r);
                float threshold = -0.2 + 0.95 * _Erosion * pow(saturate(age), 1.3);
                float alpha = i.color.a * t.a * saturate((field - threshold) / 0.22);

                float3 V = normalize(i.toCam);
                float level = _Sc_ModelLight > 0.0 ? _Sc_ModelLight : 1.0;
                float3 ageTint = lerp(float3(1.05, 0.98, 0.92), float3(0.97, 0.99, 1.03), saturate(age * 1.5));
                float3 col = i.color.rgb * SmokeLight(i.sphere, V, t.a, t.g) * ageTint * level;
                col = lerp(col, i.haze.rgb, i.haze.a);

                float fragEye = i.screenPos.w;
                alpha *= saturate((fragEye - 0.4) / 1.6); // a puff drifting through the camera fades instead of filling the view
                // #2403: soft particles — fade where the puff meets the surface behind it (Medium+; a no-op on Potato/Low).
                if (_Sc_ScreenFx > 0.5 && _Sc_SoftParticles > 0.5)
                {
                    float2 suv = i.screenPos.xy / i.screenPos.w;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    alpha *= saturate((sceneEye - fragEye) / max(0.5, i.uv.w * 0.6));
                }

                if (_Sc_AtmoDebug > 3.5 && _Sc_AtmoDebug < 4.5)
                {
                    return half4(1, 0, 1, 1); // capture diagnostics: every alpha particle solid magenta
                }

                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (fallback) ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Erosion;
            float _Lighting;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float4 uvAgeRand : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float3 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float tile = floor(saturate(v.uvAgeRand.w) * 3.999);
                float2 cell = float2(tile - 2.0 * floor(tile * 0.5), floor(tile * 0.5));
                o.uv = float3((v.uvAgeRand.xy + cell) * 0.5, saturate(v.uvAgeRand.z));
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv.xy);
                float threshold = -0.2 + 0.95 * _Erosion * pow(saturate(i.uv.z), 1.3);
                float alpha = i.color.a * t.a * saturate((t.a * (0.55 + 0.45 * t.r) - threshold) / 0.22);
                return fixed4(i.color.rgb * lerp(0.8, 1.1, t.g), alpha);
            }
            ENDCG
        }
    }

    Fallback Off
}

// Lit, vertex-coloured opaque shader for MESH particles (VFX overhaul #2152): the block chips and break cubes
// of mining, robot parts and asteroid rubble. The ParticleSystem renders a cube mesh per particle and feeds the
// per-particle start colour as COLOR and the rotated mesh normal as NORMAL, so a chip is shaded like a tiny
// block — the world's sun (_Sc_SunDir / _Sc_Light), an ambient floor, the headlamp-independent FX lights — and
// never reads as a flat sticker. Opaque, so no sorting and no overdraw beyond the chip itself.
// DUAL-PIPELINE: URP SubShader; the Built-in RP falls back to VertexColorOpaque.
Shader "BlocksBeyondTheStars/FxDebris"
{
    Properties
    {
        _Ambient ("Ambient floor", Range(0, 1)) = 0.45
        _Emission ("Self glow", Range(0, 4)) = 0
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FxCommon.hlsl"
            #include "AtmosphereCommon.hlsl" // atmosphere package (#2408): chips haze like the block they came from (#2393)

            CBUFFER_START(UnityPerMaterial)
                float _Ambient;
                float _Emission;
            CBUFFER_END

            float4 _Sc_Light;  // global day/night × sun colour (a > 0.5 = set)
            float4 _Sc_SunDir; // global world-space direction to the sun

            // Textured debris (FxKit.Debris.cs): the block atlas and the textured-debris flag (1 = on). A cube then shows the
            // piece of a tile its particle carries; a particle without one (and every particle with the flag off) is
            // the plain coloured cube.
            TEXTURE2D(_Sc_FxAtlas); SAMPLER(sampler_Sc_FxAtlas);
            float _Sc_LookDebris;

            // The particle system's vertex streams (FxKit.DebrisStreams): uv.xy = the cube face's 0..1, uv.zw + tile.xy =
            // the piece's rectangle in the atlas (x, y, width, height; width 0 = none), tile.zw = the recolour by
            // luminance (its share, its gain; share 0 = the tile times the particle's colour), extra.x = the piece's own
            // light 0..1 (the emission of the block it came from: a glowing mushroom, a crystal, a blossom, an ore, a lamp).
            struct Attributes { float4 positionOS : POSITION; float3 normal : NORMAL; float4 color : COLOR; float4 uv : TEXCOORD0; float4 tile : TEXCOORD1; float4 extra : TEXCOORD2; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; float4 color : COLOR;
                float2 uv : TEXCOORD2; float4 rect : TEXCOORD3; float3 tone : TEXCOORD4; // tone.z = the piece's own light
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.wn = TransformObjectToWorldNormal(v.normal);
                o.wp = wp;
                o.color = v.color;
                o.uv = v.uv.xy;
                o.rect = _Sc_LookDebris > 0.5 ? float4(v.uv.zw, v.tile.xy) : float4(0.0, 0.0, 0.0, 0.0);
                o.tone = float3(v.tile.zw, _Sc_LookDebris > 0.5 ? saturate(v.extra.x) : 0.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.wn);
                float3 light = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;
                float3 L = (dot(_Sc_SunDir.xyz, _Sc_SunDir.xyz) > 0.01) ? normalize(_Sc_SunDir.xyz) : normalize(float3(0.4, 0.8, -0.45));
                float ndl = saturate(dot(N, L));
                float3 albedo = i.color.rgb;
                // Explicit gradients, taken outside the branch: WebGPU allows no implicit derivatives in one.
                float2 dUx = ddx(i.uv) * i.rect.zw;
                float2 dUy = ddy(i.uv) * i.rect.zw;
                if (i.rect.z > 0.00001)
                {
                    // The piece of the tile. A cut-out tile (a plant, leaves) has holes: they take the tile's mean colour
                    // (the atlas mip with one texel per tile, divided by its coverage — as the block shader does), so a
                    // piece is always a whole cube.
                    float2 uv = i.rect.xy + saturate(i.uv) * i.rect.zw;
                    float4 texel = SAMPLE_TEXTURE2D_GRAD(_Sc_FxAtlas, sampler_Sc_FxAtlas, uv, dUx, dUy);
                    float meanMip = max(_Sc_DistSoft.w, _Sc_Look2.x);
                    float4 mean = SAMPLE_TEXTURE2D_LOD(_Sc_FxAtlas, sampler_Sc_FxAtlas, (floor(uv * 32.0) + 0.5) / 32.0, meanMip < 0.5 ? 6.0 : meanMip);
                    float3 tile = texel.a < 0.5 ? saturate(mean.rgb / max(mean.a, 0.25)) : texel.rgb;
                    float lum = dot(tile, float3(0.299, 0.587, 0.114));
                    albedo = i.tone.x > 0.001 ? lerp(tile, lum * i.color.rgb * i.tone.y, i.tone.x) : tile * i.color.rgb;
                }

                float3 col = albedo * light * (_Ambient + (1.0 - _Ambient) * ndl * BbtsCloudShade(i.wp));
                col += BbtsFxLights(i.wp, N, albedo);
                col += albedo * (_Emission + i.tone.z * 1.5);
                col = BbtsApplyHaze(col, i.wp, 1.0);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/VertexColorOpaque"
}

// Holographic visor HUD composite (VisorHud.cs): the diegetic HUD is rendered separately into _HudTex;
// this fullscreen pass lays it over the post-processed world styled as a hologram projected
// onto the inside of a curved space-suit visor — barrel curvature, chromatic-edge fringing, scanlines,
// a faux-fresnel rim glow, a blurred hologram glow (URP: a quarter-res chain; Built-in: a 4-tap sample),
// a damage glitch, and a faint world reflection.
// always-included (registered in GraphicsSettings m_AlwaysIncludedShaders).
// DUAL-PIPELINE: SubShader 1 is the URP port — a render-graph Blit pass (world arrives as _BlitTexture via
// Blit.hlsl); SubShader 2 is the original Built-in OnRenderImage pass (_MainTex). Same visor maths in both.
Shader "BlocksBeyondTheStars/Visor"
{
    Properties { _MainTex ("Tex", 2D) = "white" {} }

    // ---------------- URP (render-graph blit; world = _BlitTexture) ----------------
    // Pass 0: composite (world + HUD + HUD glow). Passes 1-3: the HUD glow chain (VisorUrpCompositor) —
    // threshold + 4-tap downsample to quarter res, then a separable 9-tap gaussian. The old 4-tap glow
    // could only halo a pixel or two; this reads as light bleeding off the hologram. Pass 4: the one-pixel
    // HUD orientation probe (#2843).
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        // URP Core first: it defines the texture macros (TEXTURE2D_X etc.) that Blit.hlsl expects.
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        TEXTURE2D(_HudTex); SAMPLER(sampler_HudTex);
        TEXTURE2D(_HudGlowTex); SAMPLER(sampler_HudGlowTex);
        TEXTURE2D(_HudProbe);   // 1×1, written by pass 4 this frame: r = the HUD texture lies upside down (#2843)
        float _Intensity;      // master strength (0 = plain HUD overlay, no styling)
        float _Curvature;      // barrel warp of the HUD (outward-curved visor)
        float _Chroma;         // chromatic aberration, grows toward the edge
        float _ScanCount;      // scanline frequency
        float _VisorTime;      // animates scanlines + flicker
        float4 _Parallax;      // xy: HUD sample offset from head motion
        float _Aspect;         // width / height, for radial symmetry
        float _HudOpacity;     // how solid the HUD reads over the world
        float _Glow;           // strength of the blurred HUD glow (hologram bloom)
        float _GlowThreshold;  // brightness below which HUD pixels do not bloom
        float _Reflect;        // faint visor glass reflection of the world
        float4 _RimColor;      // visor edge glow tint
        float _RimIntensity;
        float _Glitch;         // 0..1 damage glitch: row jitter + chroma burst on the HUD
        float4 _HudTexel;      // xy: 1/size of the HUD RT (glow downsample input)
        float4 _HudGlowTexel;  // xy: 1/size of the quarter-res glow targets (blur passes)

        float hash11(float p)
        {
            p = frac(p * 0.1031);
            p *= p + 33.33;
            return frac(p * (p + p));
        }

        // HUD orientation marker (#2843, after #2512). In the browser (WebGPU) the HUD camera's texture
        // sometimes lay the other way up than the camera colour this pass reads, and the whole HUD came out mirrored
        // vertically. VisorHud therefore draws a small opaque magenta square into the HUD texture's top-left corner
        // WITH THE HUD CAMERA ITSELF, so it lies however the HUD lies: at uv.y≈1 (the top, Unity's convention) when
        // the two textures agree, at uv.y≈0 when the HUD came out flipped. Pass 4 reads both left corners into one
        // pixel (_HudProbe) — a fragment stage reading, which was seen to work in the browser — and the composite turns
        // the HUD (and its glow, which is built from it) the right way up when that pixel says so. The marker never
        // shows: both left corners are masked here and in the glow downsample.
        #define HUD_MARKER_PX 4.0
        #define HUD_MASK_PX 8.0

        float IsHudMarker(float4 m)
        {
            return (m.a > 0.9 && m.r > 0.9 && m.g < 0.1 && m.b > 0.9) ? 1.0 : 0.0;
        }

        // 0 inside either left corner of the HUD texture (the marker and the bilinear seam around it), else 1.
        // Unclamped coordinates count too: a curved sample past the corner would clamp onto the marker.
        float HudCornerMask(float2 texUv, float4 texel, float pad)
        {
            float2 px = texUv * texel.zw;
            bool corner = px.x < HUD_MASK_PX + pad && (px.y < HUD_MASK_PX + pad || px.y > texel.w - HUD_MASK_PX - pad);
            return corner ? 0.0 : 1.0;
        }

        // Screen-space HUD coordinates → the HUD texture's own, mirrored when the marker said so.
        float2 HudTexUv(float2 screenUv, float flip)
        {
            return float2(screenUv.x, lerp(screenUv.y, 1.0 - screenUv.y, flip));
        }
        ENDHLSL

        Pass
        {
            Name "VisorComposite"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                // The orientation verdict pass 4 wrote this frame (one point sample of a 1×1 texture).
                float flip = SAMPLE_TEXTURE2D_LOD(_HudProbe, sampler_PointClamp, float2(0.5, 0.5), 0).r > 0.5 ? 1.0 : 0.0;
                float3 world = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb;

                float2 p = uv - 0.5;
                float2 pc = float2(p.x * _Aspect, p.y);
                float r2 = dot(pc, pc);

                // HUD sample coords: barrel curvature + a small parallax lag from head movement.
                float2 hudUv = 0.5 + p * (1.0 + _Curvature * r2) + _Parallax.xy;

                // Damage glitch: a few horizontal bands slip sideways for a frame or two.
                if (_Glitch > 0.001)
                {
                    float band = floor(uv.y * 36.0) + floor(_VisorTime * 24.0) * 7.0;
                    float pick = hash11(band);
                    float shift = (hash11(band + 0.37) - 0.5) * 0.035 * _Glitch * step(0.72, pick);
                    hudUv.x += shift;
                }

                // Chromatic fringe: split R/B sample positions along the radius (boosted while glitching). All the
                // offsets are worked out on screen, then mapped into the HUD texture the way it lies.
                float2 ca = p * (_Chroma * r2) + float2(0.004 * _Glitch, 0.0);
                float2 texUv = HudTexUv(hudUv, flip);
                float4 hg = SAMPLE_TEXTURE2D(_HudTex, sampler_HudTex, texUv);
                float a = hg.a * HudCornerMask(texUv, _HudTexel, 0.0);
                float hr = SAMPLE_TEXTURE2D(_HudTex, sampler_HudTex, HudTexUv(hudUv + ca, flip)).r;
                float hb = SAMPLE_TEXTURE2D(_HudTex, sampler_HudTex, HudTexUv(hudUv - ca, flip)).b;
                // Un-premultiply (the HUD was blended over transparent black) to recover true colour.
                float3 hud = float3(hr, hg.g, hb) / max(hg.a, 0.0001);

                // Blurred HUD glow (quarter-res chain, passes 1-3), sampled through the same curvature.
                float3 glow = SAMPLE_TEXTURE2D(_HudGlowTex, sampler_HudGlowTex, texUv).rgb;

                // Scanlines + a faint flicker, scaled by the master intensity.
                float scan = 1.0 - 0.10 * _Intensity * (0.5 + 0.5 * sin((uv.y * _ScanCount + _VisorTime * 2.0) * 6.2831853));
                float flick = 1.0 - 0.04 * _Intensity * (0.5 + 0.5 * sin(_VisorTime * 40.0));

                // Composite: alpha-blend the styled HUD over the world (stays readable), then add glow.
                float3 col = lerp(world, hud * scan * flick, a * _HudOpacity);
                col += glow * _Glow * (1.0 + 0.6 * _Glitch);

                // Faux-fresnel visor rim glow toward the glass edge.
                float rim = smoothstep(0.55, 1.05, length(pc));
                col += _RimColor.rgb * (rim * _RimIntensity * _Intensity);

                // Faint glass reflection: a soft, scaled mirror of the world plus diagonal glints up top.
                float3 env = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, 0.5 + p * 0.85).rgb;
                float topMask = smoothstep(0.45, 1.0, uv.y);
                float glint = smoothstep(0.85, 1.0, sin((uv.x * 3.0 + uv.y * 6.0) * 1.5) * 0.5 + 0.5);
                col += env * (_Reflect * _Intensity * (topMask * 0.6 + glint * 0.4));

                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HudGlowDownsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            // 4 bilinear taps (each already a 2x2 average) → a 16-pixel box, thresholded on brightness.
            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                // The orientation marker sits in one of the HUD texture's left corners: keep it (and a few pixels
                // round it, the reach of the taps) out of the glow.
                if (HudCornerMask(uv, _HudTexel, 4.0) < 0.5)
                {
                    return half4(0.0, 0.0, 0.0, 1.0);
                }

                float2 o = _HudTexel.xy;
                float4 c = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(-o.x, -o.y))
                         + SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2( o.x, -o.y))
                         + SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2(-o.x,  o.y))
                         + SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + float2( o.x,  o.y));
                c *= 0.25;
                // The HUD RT is premultiplied over transparent black, so c.rgb already scales with coverage.
                float lum = max(c.r, max(c.g, c.b));
                float k = saturate((lum - _GlowThreshold) / max(1.0 - _GlowThreshold, 0.001));
                return half4(c.rgb * k, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HudGlowBlurH"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 o = float2(_HudGlowTexel.x * 1.5, 0.0);
                float3 c = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb * 0.227027;
                c += (SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + o).rgb + SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - o).rgb) * 0.316216;
                c += (SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + o * 2.0).rgb + SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - o * 2.0).rgb) * 0.070270;
                c += (SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + o * 3.0).rgb + SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - o * 3.0).rgb) * 0.020;
                return half4(c, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HudGlowBlurV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 o = float2(0.0, _HudGlowTexel.y * 1.5);
                float3 c = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv).rgb * 0.227027;
                c += (SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + o).rgb + SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - o).rgb) * 0.316216;
                c += (SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + o * 2.0).rgb + SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - o * 2.0).rgb) * 0.070270;
                c += (SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + o * 3.0).rgb + SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv - o * 3.0).rgb) * 0.020;
                return half4(c, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "HudOrientationProbe"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag

            // The orientation verdict (#2843/#2844), one pixel: r = flip (what the composite does with it), g = marker at
            // the top, b = marker at the bottom of the HUD texture (_BlitTexture here). The composite samples it, and
            // VisorHud reads it back for the diagnostics — a single pixel has no row order, so no API can misread it.
            half4 frag(Varyings input) : SV_Target
            {
                float2 c = _HudTexel.xy * (HUD_MARKER_PX * 0.5);
                float top = IsHudMarker(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, float2(c.x, 1.0 - c.y), 0));
                float bottom = IsHudMarker(SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, c, 0));
                return half4(bottom * (1.0 - top), top, bottom, 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (original, unchanged) ----------------
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _HudTex;     // the separately-rendered HUD (transparent background)
            float _Intensity;      // master strength (0 = plain HUD overlay, no styling)
            float _Curvature;      // barrel warp of the HUD (outward-curved visor)
            float _Chroma;         // chromatic aberration, grows toward the edge
            float _ScanCount;      // scanline frequency
            float _VisorTime;      // animates scanlines + flicker
            float4 _Parallax;      // xy: HUD sample offset from head motion
            float _Aspect;         // width / height, for radial symmetry
            float _HudOpacity;     // how solid the HUD reads over the world
            float _Glow;           // additive bloom of bright HUD pixels
            float _Reflect;        // faint visor glass reflection of the world
            float4 _RimColor;      // visor edge glow tint
            float _RimIntensity;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float3 world = tex2D(_MainTex, uv).rgb;

                float2 p = uv - 0.5;
                float2 pc = float2(p.x * _Aspect, p.y);
                float r2 = dot(pc, pc);

                // HUD sample coords: barrel curvature + a small parallax lag from head movement.
                float2 hudUv = 0.5 + p * (1.0 + _Curvature * r2) + _Parallax.xy;

                // Chromatic fringe: split R/B sample positions along the radius.
                float2 ca = p * (_Chroma * r2);
                float4 hg = tex2D(_HudTex, hudUv);
                float a = hg.a;
                float hr = tex2D(_HudTex, hudUv + ca).r;
                float hb = tex2D(_HudTex, hudUv - ca).b;
                // Un-premultiply (the HUD was blended over transparent black) to recover true colour.
                float3 hud = float3(hr, hg.g, hb) / max(a, 0.0001);

                // Cheap 4-tap glow of the HUD (additive hologram bloom).
                float2 o = 1.5 / _ScreenParams.xy;
                float3 glow = tex2D(_HudTex, hudUv + float2(o.x, 0)).rgb
                            + tex2D(_HudTex, hudUv - float2(o.x, 0)).rgb
                            + tex2D(_HudTex, hudUv + float2(0, o.y)).rgb
                            + tex2D(_HudTex, hudUv - float2(0, o.y)).rgb;
                glow *= 0.25;

                // Scanlines + a faint flicker, scaled by the master intensity.
                float scan = 1.0 - 0.10 * _Intensity * (0.5 + 0.5 * sin((uv.y * _ScanCount + _VisorTime * 2.0) * 6.2831853));
                float flick = 1.0 - 0.04 * _Intensity * (0.5 + 0.5 * sin(_VisorTime * 40.0));

                // Composite: alpha-blend the styled HUD over the world (stays readable), then add glow.
                float3 col = lerp(world, hud * scan * flick, a * _HudOpacity);
                col += glow * (_Glow * _Intensity);

                // Faux-fresnel visor rim glow toward the glass edge.
                float rim = smoothstep(0.55, 1.05, length(pc));
                col += _RimColor.rgb * (rim * _RimIntensity * _Intensity);

                // Faint glass reflection: a soft, scaled mirror of the world plus diagonal glints up top.
                float3 env = tex2D(_MainTex, 0.5 + p * 0.85).rgb;
                float topMask = smoothstep(0.45, 1.0, uv.y);
                float glint = smoothstep(0.85, 1.0, sin((uv.x * 3.0 + uv.y * 6.0) * 1.5) * 0.5 + 0.5);
                col += env * (_Reflect * _Intensity * (topMask * 0.6 + glint * 0.4));

                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}

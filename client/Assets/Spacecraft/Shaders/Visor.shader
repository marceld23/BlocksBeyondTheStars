// Holographic visor HUD composite (VisorHud.cs): the diegetic HUD is rendered separately into _HudTex;
// this fullscreen pass lays it over the post-processed world (_MainTex) styled as a hologram projected
// onto the inside of a curved space-suit visor — barrel curvature, chromatic-edge fringing, scanlines,
// a faux-fresnel rim glow, a cheap additive glow, and a faint world reflection. Built-in RP;
// always-included (registered in GraphicsSettings m_AlwaysIncludedShaders).
Shader "Spacecraft/Visor"
{
    Properties { _MainTex ("Tex", 2D) = "white" {} }
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

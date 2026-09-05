// Shader-drawn HUD chrome (UiHolo.cs): a signed-distance rounded rectangle / ring rendered on a plain uGUI
// Image quad, so panels, rings and bars stay crisp at any resolution and can animate (border sweep, boot
// reveal) without bitmap sprites. Per-element parameters ride in the extra vertex channels written by
// UiHolo.Shape (Canvas.additionalShaderChannels TexCoord1 + TexCoord2):
//   uv0     : 0..1 across the (padded) quad
//   uv1.xy  : logical width / height in canvas units, uv1.z corner radius, uv1.w border width
//   uv2.x   : style (0 panel, 1 ring, 2 bar)   uv2.y glow strength   uv2.z outer padding   uv2.w reveal 0..1
//   uv3.x   : fill opacity (the fill alone)
//   color   : fill colour (rgb); alpha scales the WHOLE element (Image.color.a × CanvasGroup), so fades work.
// Pipeline-agnostic (uGUI's own UI/Default is the same kind of CG shader under URP); always-included via
// BuildScript.RuntimeShaders because every material is created in code.
Shader "BlocksBeyondTheStars/UiHolo"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _EdgeColor ("Edge Color", Color) = (0.40, 0.82, 1.00, 1)
        _Scan ("Scanline Strength", Range(0, 1)) = 0.06
        _SweepSpeed ("Border Sweep Speed", Float) = 0.12
        _GlowWidth ("Outer Glow Width", Float) = 10
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha // premultiplied: the glow adds light without a dark halo
        ColorMask [_ColorMask]

        Pass
        {
            Name "UiHolo"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 params   : TEXCOORD1; // w, h, radius, border
                float4 style    : TEXCOORD2; // style, glow, pad, reveal
                float4 extra    : TEXCOORD3; // fill opacity
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 uv            : TEXCOORD0;
                float4 params        : TEXCOORD1;
                float4 style         : TEXCOORD2;
                float4 extra         : TEXCOORD3;
                float4 worldPosition : TEXCOORD4;
            };

            fixed4 _EdgeColor;
            float _Scan;
            float _SweepSpeed;
            float _GlowWidth;
            float4 _ClipRect;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color;
                o.params = v.params;
                o.style = v.style;
                o.extra = v.extra;
                return o;
            }

            // Signed distance to a rounded rectangle of half-size b and corner radius r (0 on the edge, <0 inside).
            float sdRoundRect(float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            float hash11(float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                return frac(p * (p + p));
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float w = max(i.params.x, 1.0), h = max(i.params.y, 1.0);
                float radius = i.params.z, border = max(i.params.w, 0.5);
                float style = i.style.x, glowK = i.style.y, pad = i.style.z, reveal = i.style.w;

                // Position in canvas units, centred on the logical rect (the quad is bigger by `pad` all round).
                float2 full = float2(w, h) + 2.0 * pad;
                float2 p = (i.uv - 0.5) * full;
                float2 half = float2(w, h) * 0.5;

                float d;      // signed distance to the shape's outer edge
                float inner;  // distance from the edge inward (for the border band)
                if (style > 0.5 && style < 1.5)
                {
                    // Ring: the border IS the shape; a faint disc fill inside.
                    float R = min(half.x, half.y);
                    float rr = length(p);
                    d = rr - R;
                }
                else
                {
                    d = sdRoundRect(p, half, min(radius, min(half.x, half.y)));
                }

                float aa = max(fwidth(d), 0.75); // ~1 screen px of anti-aliasing in canvas units
                float insideMask = 1.0 - smoothstep(-aa, aa, d);                    // 1 inside the shape
                float borderMask = insideMask * smoothstep(-border - aa, -border + aa, d); // 1 on the edge band
                float fillMask = insideMask - borderMask;

                // Fill: the vertex colour, a soft vertical gradient (brighter at the top) and faint scanlines.
                float2 lp = i.uv * full; // canvas-unit coords for the pattern
                float grad = 1.0 + 0.10 * saturate(1.0 - (p.y + half.y) / max(h, 1.0)) - 0.05;
                float scan = 1.0 - _Scan * (0.5 + 0.5 * sin(lp.y * 3.1415926 * 0.5));
                float3 fillRgb = i.color.rgb * grad * scan;
                float fillA = i.extra.x;
                float groupA = i.color.a; // whole-element alpha (fades, caller tints)

                // Corner brackets: the edge brightens toward the corners (sci-fi frame accents), rings get a
                // uniform edge. Plus an animated highlight travelling along the perimeter.
                float bracket = 1.0;
                if (style < 0.5 || style > 1.5)
                {
                    float2 corner = saturate(1.0 - (half - abs(p)) / max(min(half.x, half.y) * 0.55, 1.0));
                    bracket = 0.55 + 0.45 * max(corner.x, corner.y);
                }

                float ang = atan2(p.y, p.x) / 6.2831853 + 0.5;           // 0..1 around the shape
                float sweepT = frac(ang - _Time.y * _SweepSpeed);
                float sweep = pow(saturate(1.0 - sweepT * 6.0), 2.0);   // a short bright comet
                float3 edgeRgb = lerp(_EdgeColor.rgb, saturate(i.color.rgb * 2.2 + _EdgeColor.rgb * 0.6), 0.25);
                float edgeA = _EdgeColor.a * (0.70 * bracket + 0.55 * sweep);

                // Outer glow: soft falloff outside the edge, additive-ish (premultiplied with alpha 0).
                float glowW = _GlowWidth * max(glowK, 0.0);
                float glow = glowK > 0.0 ? exp(-max(d, 0.0) / max(glowW * 0.35, 0.01)) * (1.0 - insideMask) : 0.0;
                glow *= 0.35 * glowK * (0.8 + 0.4 * sweep);

                // Reveal wipe (boot-up): left → right with a bright leading edge.
                float revealX = i.uv.x;
                float rv = smoothstep(reveal + 0.01, reveal - 0.01, revealX);
                float lead = reveal < 0.999 ? exp(-abs(revealX - reveal) * 60.0) : 0.0;

                float3 rgb = fillRgb * fillA * fillMask + edgeRgb * edgeA * borderMask;
                float a = fillA * fillMask + edgeA * borderMask;
                rgb += _EdgeColor.rgb * glow;   // glow carries no alpha → pure light over the world
                a += glow * 0.35;
                rgb *= rv; a *= rv;
                rgb += _EdgeColor.rgb * lead * insideMask * 1.5;
                a += lead * insideMask * 0.6;

                rgb *= groupA; a *= groupA;

                #ifdef UNITY_UI_CLIP_RECT
                float2 insideClip = step(_ClipRect.xy, i.worldPosition.xy) * step(i.worldPosition.xy, _ClipRect.zw);
                float clipK = insideClip.x * insideClip.y; // (never name this `clip` — it shadows the intrinsic)
                rgb *= clipK; a *= clipK;
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(a - 0.001);
                #endif

                return fixed4(rgb, a);
            }
            ENDCG
        }
    }
    Fallback Off
}

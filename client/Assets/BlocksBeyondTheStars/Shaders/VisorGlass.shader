// A flat "visor glass" overlay for menus (VisorMenuGlass / UiKit.AddVisorGlass): the same helmet styling as
// the diegetic HUD's BlocksBeyondTheStars/Visor pass — a faux-fresnel cyan rim glow, faint animated scanlines and a top
// glass glint — but WITHOUT the barrel curvature, so the menu reads as "inside the helmet" while its buttons
// stay exactly where they're drawn (clicks aren't displaced). Drawn additively over the already-rendered menu
// as a full-screen UI quad whose uv spans the screen. Built-in RP; always-included (GraphicsSettings).
Shader "BlocksBeyondTheStars/VisorGlass"
{
    Properties
    {
        _RimColor ("Rim Color", Color) = (0.4, 0.85, 1, 1)
        _RimIntensity ("Rim Intensity", Float) = 0.5
        _ScanCount ("Scan Count", Float) = 220
        _VisorTime ("Time", Float) = 0
        _Aspect ("Aspect", Float) = 1.78
        _Intensity ("Intensity", Float) = 0.55
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off ZWrite Off ZTest Always
        Blend One One // additive glow over the menu

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _RimColor;
            float _RimIntensity;
            float _ScanCount;
            float _VisorTime;
            float _Aspect;
            float _Intensity;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 p = uv - 0.5;
                float2 pc = float2(p.x * _Aspect, p.y);

                // Faux-fresnel rim glow toward the visor edge (same falloff as the HUD pass).
                float rim = smoothstep(0.55, 1.05, length(pc));
                // Faint animated scanline shimmer + a soft glint across the top of the glass.
                float scan = 0.5 + 0.5 * sin((uv.y * _ScanCount + _VisorTime * 2.0) * 6.2831853);
                float top = smoothstep(0.45, 1.0, uv.y);

                float3 col = _RimColor.rgb * (rim * _RimIntensity);
                col += _RimColor.rgb * (0.018 * scan);
                col += _RimColor.rgb * (0.030 * top);
                return fixed4(col * _Intensity, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}

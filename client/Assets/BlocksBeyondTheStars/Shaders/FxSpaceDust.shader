// Camera-wrapped space dust (VFX overhaul #2157) — the speed cue of flight (the Elite "space dust" idea). One mesh
// of N quads drawn in ONE call: every quad's four vertices share a random point in the unit cube (POSITION) and
// carry their corner in TEXCOORD0 (-1..1). The vertex shader wraps that point into a box of _DustParams.x blocks
// around the camera (frac), so the motes never run out however far the ship flies, then stretches each quad along
// the ship's velocity (_DustVel.xyz, streak seconds in w): idle → faint round motes, cruising → short streaks,
// hyperjump → long star lines. Motes fade near the camera and at the box edge, so the wrap never pops. Additive.
// DUAL-PIPELINE: URP SubShader; the Built-in RP falls back to the additive Particle shader.
Shader "BlocksBeyondTheStars/FxSpaceDust"
{
    Properties
    {
        _Color ("Colour (a = strength)", Color) = (0.75, 0.85, 1, 1)
        _DustVel ("Velocity (xyz) + streak seconds (w)", Vector) = (0, 0, 0, 0.06)
        _DustParams ("x box size, y mote size, z idle brightness, w speed for full brightness", Vector) = (60, 0.06, 0.25, 40)
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _DustVel;
                float4 _DustParams;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float alpha : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float box = max(4.0, _DustParams.x);
                float3 cam = _WorldSpaceCameraPos;
                float3 p = (frac(v.positionOS.xyz - cam / box) - 0.5) * box + cam;

                float3 toCam = cam - p;
                float dist = length(toCam);
                float3 view = toCam / max(dist, 1e-3);
                float3 vel = _DustVel.xyz;
                float speed = length(vel);
                float3 dir = speed > 0.05 ? vel / speed : float3(0, 1, 0);
                float3 side = cross(dir, view);
                float sideLen = length(side);
                side = sideLen > 1e-3 ? side / sideLen : float3(1, 0, 0);

                float size = _DustParams.y;
                float len = max(size, speed * _DustVel.w);
                float3 wp = p + side * (v.uv.x * size) + dir * (v.uv.y * len);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;

                float nearFade = saturate((dist - 1.5) / 4.0);
                float farFade = saturate((box * 0.5 - dist) / (box * 0.18));
                float bright = lerp(_DustParams.z, 1.0, saturate(speed / max(1.0, _DustParams.w)));
                o.alpha = nearFade * farFade * bright * v.color.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float across = 1.0 - i.uv.x * i.uv.x;
                float along = 1.0 - i.uv.y * i.uv.y;
                float a = across * across * along * i.alpha;
                return half4(_Color.rgb * (a * _Color.a), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/Particle"
}

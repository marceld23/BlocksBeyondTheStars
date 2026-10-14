// Selection outline on the block the player aims at (#2556). Twelve camera-facing ribbons, one per cube edge, in ONE
// mesh (MiningFx.BuildOutlineMesh). Thin and hologram blue with a slow, subtle "digital" shimmer: a gentle breathing,
// a data packet running along each edge, and now and then one dimmed cell of an edge. Mining tints it towards orange
// (MiningFx lerps _Color with the server's MiningProgress fraction).
// Readable on any background: a bright HDR core (it blooms a little at night) on a thin deep-blue backing, drawn with
// premultiplied alpha (Blend One OneMinusSrcAlpha), so the backing darkens a bright background (snow, sand, sky)
// while the core adds light on a dark one (night, caves, foliage).
// Geometry: every vertex sits on its edge's centre line; the vertex shader spreads it sideways (perpendicular to the
// edge and to the view ray) and past the edge's ends by the half-width, which is the world width up close and never
// less than _MinPixels on screen far away, so the line does not alias into nothing at range. Vertex inputs:
//   POSITION  a point on the centre line (one end of the edge), object space
//   NORMAL    the edge's direction (unit length, start to end)
//   TEXCOORD0 x = 0 at the start, 1 at the end; y = the side, -1 or +1
//   TEXCOORD1 x = the edge's phase (0..1) for the shimmer; y = the edge's length (world units)
// _Shimmer 0 freezes every animation (Reduce flashes / Reduced effects).
// URP only (every quality level runs URP). MiningFx falls back to the old Unlit/Color cube frame when this shader is
// missing or unsupported, so Fallback is Off on purpose (another shader could not draw the collapsed ribbons).
Shader "BlocksBeyondTheStars/FxOutline"
{
    Properties
    {
        _Color ("Colour (linear, a = fade)", Color) = (0.133, 0.638, 1, 1)
        _Intensity ("HDR intensity of the core", Range(0, 4)) = 1.1
        _Width ("Ribbon width (world units)", Range(0.005, 0.2)) = 0.045
        _MinPixels ("Minimum half-width (pixels at 1080p)", Range(0, 8)) = 2.2
        _Backing ("Opacity of the dark backing", Range(0, 1)) = 0.7
        _Shimmer ("Shimmer (0 = steady)", Range(0, 1)) = 1
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+40" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FxCommon.hlsl"

            // SRP Batcher (#573): every material property in UnityPerMaterial.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Width;
                float _MinPixels;
                float _Backing;
                float _Shimmer;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 strip : TEXCOORD0; // x across (-1..1), y along (world units from the start), z half-width, w phase
                float edgeLength : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 centre = TransformObjectToWorld(v.positionOS.xyz);
                float3 axis = TransformObjectToWorldDir(v.normalOS);
                float3 toEye = _WorldSpaceCameraPos - centre;
                float dist = max(length(toEye), 0.001);

                // One screen pixel at this distance, counted at no more than 1080 lines so the minimum keeps its
                // share of the screen on a 4K display.
                float4x4 proj = UNITY_MATRIX_P;
                float pixel = 2.0 * dist / (max(abs(proj[1][1]), 0.001) * clamp(_ScreenParams.y, 1.0, 1080.0));
                float hw = max(_Width * 0.5, _MinPixels * pixel);

                // Camera-facing ribbon: spread perpendicular to both the edge and the view ray. An edge seen exactly
                // end-on collapses to nothing, which is right: its neighbours draw that corner.
                float3 side = cross(axis, toEye / dist);
                float sideLen = length(side);
                side *= step(0.0001, sideLen) / max(sideLen, 0.0001);
                float endSign = v.uv.x * 2.0 - 1.0;
                float3 wp = centre + side * (v.uv.y * hw) + axis * (endSign * hw);

                // A depth bias that does not move the line on screen: slide the vertex along its own view ray towards
                // the eye, so it never z-fights the face it lies on and the half that dips into the neighbouring
                // ground still shows. Small enough that the hidden back edges stay hidden.
                float3 ray = _WorldSpaceCameraPos - wp;
                float rayLen = max(length(ray), 0.001);
                float pull = min(clamp(dist * 0.006, 0.006, 0.05), rayLen * 0.5);
                wp += ray * (pull / rayLen);

                o.positionCS = TransformWorldToHClip(wp);
                o.strip = float4(v.uv.y, v.uv.x * v.uv2.y + endSign * hw, hw, v.uv2.x);
                o.edgeLength = v.uv2.y;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Distance from the centre line in half-widths; past either end it is the distance from the end
                // point, which rounds the caps so the three edges meeting at a corner join into one soft knot.
                float hw = max(i.strip.z, 0.00001);
                float over = max(max(-i.strip.y, i.strip.y - i.edgeLength) / hw, 0.0);
                float a = length(float2(over, i.strip.x));
                float aa = max(fwidth(a), 0.0001);
                float core = 1.0 - smoothstep(0.36 - aa, 0.36 + aa, a);
                float backing = 1.0 - smoothstep(0.62 - aa, 0.62 + aa, a);
                float halo = saturate(1.0 - a);
                halo *= halo * 0.3;

                // The shimmer. Slow on purpose: the outline is on screen for every minute of mining.
                float t = _Time.y;
                float alongN = i.strip.y / max(i.edgeLength, 0.001);
                // Breathing: plus/minus 10 percent, one cycle every three seconds (wrapped, so sin stays precise).
                float breathe = 1.0 + 0.1 * sin(frac(t / 3.0) * 6.2831853);
                // A data packet: a bright head with a short fading tail runs along an edge at under half an edge per
                // second; a given edge carries one about a third of the time, each edge on its own phase so they
                // never march in step.
                float x = frac(alongN / 3.0 + i.strip.w - t * 0.15);
                float packet = saturate((x - 0.9) / 0.1);
                packet *= packet * packet;
                // Digital flicker: each edge is cut into eight cells; now and then one dims for half a second.
                // The time step wraps at 97 so the hash keeps its precision in long sessions.
                float cellIndex = floor(alongN * 8.0);
                float tick = floor(frac(t * (2.0 / 97.0)) * 97.0);
                float h = BbtsHash21(float2(cellIndex + i.strip.w * 37.0, tick));
                float flicker = 1.0 - 0.25 * step(0.94, h);
                float glow = lerp(1.0, breathe * flicker * (1.0 + 0.8 * packet), saturate(_Shimmer));

                // Premultiplied: the backing is a deep shade of the tint at `opacity`, the core and halo add light.
                float opacity = backing * _Backing * _Color.a;
                float3 rgb = _Color.rgb * (0.12 * opacity + (core * glow + halo) * (_Intensity * _Color.a));
                return half4(rgb, opacity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

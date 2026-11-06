// Blocks Beyond the Stars — the light of the High preset's meadow (#3056): the shell-grass carpet and the meadow flowers.
//
// The carpet lies on a grass block and must not change the meadow's brightness, so this is the block shader's light for
// an upward face (BlockAtlas.shader, UniversalForward), reduced to what a blade needs: the sun in the system colour over the
// skylight ambient, the real-time shadow map with its distance fade and the cloud shade on the direct-sun term only, the
// cool night floor, the sun glowing through a backlit blade, the player's headlamp, placed block light, and the shared haze.
// The normal is "up" like the ground's (the blades are too thin to light as cylinders). Forward passes only: include it after
// the URP Core.hlsl + Lighting.hlsl (shadows) and AtmosphereCommon.hlsl.
#ifndef BBTS_MEADOW_LIGHT_INCLUDED
#define BBTS_MEADOW_LIGHT_INCLUDED

// #3229: the headlamp globals and their light (shared with the block shader). Unshadowed here: the grass carpet draws
// many shell layers per pixel, and a blade's shadow under the lamp would not read anyway.
#define BBTS_LAMP_NO_SHADOW
#include "Headlamp.hlsl"

// Per-frame globals set by Sky / the player (Shader.SetGlobal*), the same ones the block shader reads.
float4 _Sc_Light;     // sun colour x day brightness x weather (a > 0.5 = set)
float4 _Sc_SunDir;    // world-space direction TO the sun

// The lit, hazed colour of a meadow fragment. `sky` is the skylight of the air above the carpet cell (the mesher's value
// for the ground's top face, so the ambient matches it exactly), `bl` the placed block light there (0 = none), and
// `translucency` how much sun a backlit fragment lets through (0 solid … 1 a thin petal or blade tip).
float3 BbtsMeadowShade(float3 albedo, float3 wp, float sky, float3 bl, float translucency)
{
    float3 light = (_Sc_Light.a < 0.5) ? float3(1.0, 1.0, 1.0) : _Sc_Light.rgb;
    float3 L = normalize(_Sc_SunDir.xyz);
    float ndl = saturate(L.y);
    sky = saturate(sky);

    // The block shader's shadow term: shadow map blended toward lit by the shadow-distance fade (#1612), times the
    // drifting cloud shade (#2394); skipped where the sun cannot reach anyway.
    float shadow = (ndl * sky > 0.0)
        ? lerp(MainLightRealtimeShadow(TransformWorldToShadowCoord(wp)), 1.0, GetMainLightShadowFade(wp)) * BbtsCloudShade(wp)
        : 0.0;

    float amb = BbtsSkyAmbient(sky); // #3265: the block shader's ramp and dark floor (AtmosphereCommon)
    float sunOpen = saturate(sky * 2.0);
    float3 col = albedo * (light * (amb + 0.5 * ndl * sunOpen * shadow) + BbtsDarkFloor());

    // The night floor: a faint cool fill on open-sky surfaces while the sun light is weak.
    float nightFloor = saturate(0.6 - dot(light, float3(0.299, 0.587, 0.114)));
    col += albedo * float3(0.10, 0.13, 0.20) * (sky * nightFloor);

    // Thin things let the sun through when it stands behind them (the plants' #2403 term).
    float3 V = normalize(_WorldSpaceCameraPos - wp);
    float back = pow(saturate(dot(-V, L)), 3.0);
    col += albedo * light * (back * 0.45 * translucency) * sunOpen * shadow * sky;

    // Placed lights and the headlamp light the meadow like the ground under it.
    col += albedo * BbtsBlockLightFlicker(bl, wp) * (BbtsCozyLightGain() * 1.6);
    // #3229: blades stand up, so they catch a lamp better than the flat ground — the normal leans toward the viewer.
    col += BbtsHeadlamps(wp, normalize(float3(0.0, 1.0, 0.0) + 0.5 * V), V, albedo, float3(0.04, 0.04, 0.04), 0.8);

    return BbtsApplyHaze(col, wp, sky);
}

#endif

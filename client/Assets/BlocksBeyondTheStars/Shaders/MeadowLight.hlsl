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
// The sun's shadow at a world position: the shadow map blended toward lit by the shadow-distance fade (#1612).
float BbtsMeadowSunShadow(float3 pos)
{
    return lerp(MainLightRealtimeShadow(TransformWorldToShadowCoord(pos)), 1.0, GetMainLightShadowFade(pos));
}

// `shadowPos` + `soften`. A blade or a carpet strand is a few centimetres wide; the shadow of a swaying crown
// has edges that travel 7 to 10 cm to and fro, and a face that crosses one jumps in brightness by a factor of 1.5
// (evening) to 2.2 (noon) — every blade on its own, about once a second: grass under a tree flickers
// (2026-11-21). With `soften` > 0 the grass takes the shadow of the GROUND beneath it instead (`shadowPos`, the point
// under the fragment at rest, so the wind does not move it through the pattern), averaged over three taps 7 cm apart:
// the sun spots lie on the meadow as on bare ground and pass over a blade as a soft ramp. `soften` = 0 is the plain
// lookup at the fragment itself.
float3 BbtsMeadowShadeAt(float3 albedo, float3 wp, float3 shadowPos, float soften, float sky, float3 bl, float translucency)
{
    float3 light = (_Sc_Light.a < 0.5) ? float3(1.0, 1.0, 1.0) : _Sc_Light.rgb;
    float3 L = normalize(_Sc_SunDir.xyz);
    float ndl = saturate(L.y);
    sky = saturate(sky);

    // The block shader's shadow term, times the drifting cloud shade (#2394); skipped where the sun cannot reach anyway.
    float shadow = 0.0;
    if (ndl * sky > 0.0)
    {
        if (soften > 0.0)
        {
            shadow = (BbtsMeadowSunShadow(shadowPos + float3(0.07, 0.0, 0.0))
                    + BbtsMeadowSunShadow(shadowPos + float3(-0.035, 0.0, 0.06))
                    + BbtsMeadowSunShadow(shadowPos + float3(-0.035, 0.0, -0.06))) / 3.0;
        }
        else
        {
            shadow = BbtsMeadowSunShadow(wp);
        }

        shadow *= BbtsCloudShade(wp);
    }

    float amb = BbtsSkyAmbient(sky); // #3265: the block shader's ramp and dark floor (AtmosphereCommon)
    float sunOpen = saturate(sky * 2.0);
    float3 col = albedo * (BbtsWorldLight(light, amb, ndl * sunOpen * shadow, float3(0.0, 1.0, 0.0)) + BbtsDarkFloor()); // The block shader's world light

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

// The classic meadow light: the shadow looked up at the fragment itself.
float3 BbtsMeadowShade(float3 albedo, float3 wp, float sky, float3 bl, float translucency)
{
    return BbtsMeadowShadeAt(albedo, wp, wp, 0.0, sky, bl, translucency);
}

#endif

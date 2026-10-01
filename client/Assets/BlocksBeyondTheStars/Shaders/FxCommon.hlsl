// Blocks Beyond the Stars — shared effect globals for the URP passes (VFX overhaul #2152).
//
// Two families of per-frame GLOBALS (Shader.SetGlobal*, set by FxLights / FxScanWave on the client). They are
// deliberately NOT material properties: they live outside every UnityPerMaterial cbuffer (SRP Batcher rule —
// a global inside the per-material buffer would be served stale per material, see #573).
//
// 1. FX lights (_Sc_FxLight*). URP additional lights are off by design (ADR 0003 — the voxels use custom lighting),
//    so a muzzle flash, a plasma bolt or an explosion cannot spawn a Unity Light. Instead up to 8 short-lived point
//    lights are uploaded as arrays and the block / model shaders add them like the headlamp (_Sc_Lamp*): a wrapped
//    N·L with a squared falloff. Count 0 costs one uniform compare.
// 2. Scan wave (_Sc_ScanWave*). An expanding shell around a scanner (No Man's Sky / Broxxar "NoMansScanner" look):
//    a sharp bright lead edge, a soft trailing gradient and faint horizontal lines, drawn by every surface that
//    includes this file from its own world position — no full-screen pass and no depth texture needed, so it also
//    runs on Potato/Low and in the browser.
#ifndef BBTS_FX_COMMON_INCLUDED
#define BBTS_FX_COMMON_INCLUDED

#define BBTS_FX_LIGHTS 8

float4 _Sc_FxLightPos[BBTS_FX_LIGHTS]; // xyz world position, w radius (blocks)
float4 _Sc_FxLightCol[BBTS_FX_LIGHTS]; // rgb linear colour × intensity, a unused
float  _Sc_FxLightCount;               // number of live slots (0..8)

float4 _Sc_ScanWave;       // xyz origin (world), w current radius
float4 _Sc_ScanWaveCol;    // rgb linear colour, a strength (0 = no wave running)
float4 _Sc_ScanWaveParams; // x band width, y max radius, z line density (lines per block), w unused

// Light from the live FX lights onto a surface with albedo `albedo` and normal `N` at world position `wp`.
float3 BbtsFxLights(float3 wp, float3 N, float3 albedo)
{
    float3 acc = float3(0, 0, 0);
    int count = (int)_Sc_FxLightCount;
    [loop]
    for (int k = 0; k < BBTS_FX_LIGHTS; k++)
    {
        if (k >= count)
        {
            break;
        }

        float3 d = _Sc_FxLightPos[k].xyz - wp;
        float dist = length(d);
        float att = saturate(1.0 - dist / max(0.01, _Sc_FxLightPos[k].w));
        att *= att;
        // Wrapped N·L: faces turned away still catch a little, so a flash reads as light in the room, not a spotlight.
        float ndl = saturate(dot(N, d / max(dist, 1e-4)) * 0.7 + 0.3);
        acc += _Sc_FxLightCol[k].rgb * (att * ndl);
    }

    return albedo * acc;
}

// The scan-wave band at world position `wp` (additive colour; 0 outside the band or when no wave runs).
float3 BbtsScanWave(float3 wp)
{
    if (_Sc_ScanWaveCol.a <= 0.001)
    {
        return float3(0, 0, 0);
    }

    float d = distance(wp, _Sc_ScanWave.xyz);
    float radius = _Sc_ScanWave.w;
    float width = max(0.1, _Sc_ScanWaveParams.x);
    float behind = radius - d; // > 0 inside the shell, 0 at the lead edge
    if (behind < 0.0 || behind > width)
    {
        return float3(0, 0, 0);
    }

    float t = 1.0 - behind / width;                 // 1 at the lead edge → 0 at the tail
    float lead = pow(t, 10.0);                      // the razor-thin bright front
    float trail = t * t * 0.45;                     // the soft glow it drags behind it
    float lines = pow(abs(sin(wp.y * 3.14159 * _Sc_ScanWaveParams.z)), 24.0) * t * 0.35; // faint horizontal bars
    float fade = 1.0 - saturate(radius / max(1.0, _Sc_ScanWaveParams.y)); // dies out toward the max radius
    return _Sc_ScanWaveCol.rgb * (lead * 2.2 + trail + lines) * (_Sc_ScanWaveCol.a * (0.35 + 0.65 * fade));
}

// Cheap value noise (no texture) for the procedural FX shaders.
float BbtsHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

float BbtsNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = BbtsHash21(i);
    float b = BbtsHash21(i + float2(1, 0));
    float c = BbtsHash21(i + float2(0, 1));
    float e = BbtsHash21(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, e, u.x), u.y);
}

#endif

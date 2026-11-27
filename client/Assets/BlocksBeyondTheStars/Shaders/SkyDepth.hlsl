// Blocks Beyond the Stars — "sky depth" for the objects of the surface sky (player report 2026-11-27).
//
// The moons and planets in the sky (SkyBodiesView, ≤ 460 m), their ring discs and the planet's own ring band (RingBand,
// 0.24–0.42 × far) are meshes at a FINITE distance, but the far view draws terrain to 1024 m (2048 m on High) — so
// every mountain behind them was painted over: a ring "growing out of the landscape", a setting moon in front of the
// hills. The stars, nebula and atmosphere dome had the same bug and moved to the far plane in #1582. Sky bodies cannot
// simply follow them: a moon passes in front of its planet and a ring disc passes in front of and behind its sphere, so
// they need depth AMONG themselves. This helper keeps that order but squeezes it into a thin band just in front of the
// far plane — w depth-buffer units from it, 2e-6..8e-6 — so ALL terrain, at any distance the far view reaches (with the
// 0.1 near plane: < ~2200 m at farClip 2688, < ~1270 m at 1408, < ~720 m at the browser's 768), is nearer and covers them,
// while the domes (#1582, 1e-6 from the far plane) stay behind them.
//
// Mode (a material float, 0 by default so every other user — the space view, the cockpit hologram — keeps real depth):
//   0 = off, 1 = by distance (bodies and their rings: nearer parts nearer), 2 = the nearest sky layer (the planet's
//   own ring band, which circles in front of every other body).
// Pure HLSL, no pipeline include, so the URP (HLSL) and Built-in (CG) passes share it.
#ifndef BBTS_SKY_DEPTH_INCLUDED
#define BBTS_SKY_DEPTH_INCLUDED

// Distance from the far plane in depth-buffer units ([0,1]) for a vertex `dist` metres from the eye.
float BbtsSkyDepthGap(float mode, float dist)
{
    return mode > 1.5 ? 8.0e-6 : 2.0e-6 * (1.0 + 460.0 / max(dist, 1.0));
}

// Moves a clip-space position into the sky band (mode 0 leaves it untouched). 24-bit depth still separates a moon's
// front from its back by ~9 steps (2e-6 × 0.27 / 6e-8), enough for a ring disc against its sphere.
float4 BbtsSkyDepth(float4 positionCS, float3 worldPos, float3 cameraPos, float mode)
{
    if (mode > 0.5)
    {
        float gap = BbtsSkyDepthGap(mode, distance(worldPos, cameraPos));
        #if UNITY_REVERSED_Z
            positionCS.z = positionCS.w * gap;               // reversed-Z: far plane = 0
        #else
            positionCS.z = positionCS.w * (1.0 - 2.0 * gap); // OpenGL / WebGL: far plane = w, depth window [-1, 1]
        #endif
    }

    return positionCS;
}

#endif

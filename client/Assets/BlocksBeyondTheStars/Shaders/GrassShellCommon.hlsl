// Blocks Beyond the Stars — the shell-grass carpet of the High preset (#3056), shared by the colour and the depth pass of
// GrassShell.shader so both cut exactly the same strands.
//
// GrassShells.cs draws one instance per open carpet top (grass, alien grass, mycelium): a stack of BBTS_SHELL_LAYERS
// horizontal 1×1 quads whose object-space y is the layer (0 … 1). The vertex stage lifts each layer to its height, lets the
// wind lean it more the higher it sits, and flattens the carpet toward the ground over the last metres of its range and
// under snow. A layer that would sit on the ground collapses to one point (no fragments), so the far carpet costs nothing.
// The fragment stage cuts the strands: a BBTS_SHELL_STRANDS² strand grid per block, every strand with a hashed height, root
// offset and lean, a little thinner toward its tip — but never thinner than about a pixel, and with 4× MSAA (High) its
// edge becomes sample coverage instead of a hard cut (#3078: thin tips popped in and out as the camera moved, the carpet
// "crawled"). Per instance (MaterialPropertyBlock arrays):
//   _ShellTile  xy = the atlas rect's min, zw = its size — the tile the block's top face shows in this cell (variant included)
//   _ShellInfo  x = skylight of the air above, y = the top face's UV turns (0..3), z = the cell's strand seed, w unused
//   _ShellLight rgb = placed block light at the cell (0 = none)
// Include after the URP Core.hlsl and AtmosphereCommon.hlsl.
#ifndef BBTS_GRASS_SHELL_COMMON_INCLUDED
#define BBTS_GRASS_SHELL_COMMON_INCLUDED

#define BBTS_SHELL_HEIGHT 0.22     // metres: the top layer over the ground (GrassShells.ShellLayers layers up to here)
#define BBTS_SHELL_STRANDS 14.0    // strands along one block edge — coarse enough to read as blades a few metres away
                                   // (at 32 the first capture showed a flat recoloured ground: a strand was under a pixel)
#define BBTS_SHELL_FADE_END 16.0   // metres: the carpet lies flat on the ground from here on (GrassShells.CarpetRange)
#define BBTS_SHELL_FADE_WIDTH 8.0  // metres over which it flattens (#3078: was 14 … 20 m — far strands were only noise)
#define BBTS_SHELL_MIN_LIFT 0.012  // metres: a layer lower than this is dropped (no z-fight with the ground)
#define BBTS_SHELL_SWAY 0.15       // share of the plants' slow wind wave at the top layer (#3078: was 0.4 with the flutter)
#define BBTS_SHELL_MIN_WIDTH 0.6   // the thinnest strand radius, in pixels (a strand never falls below about a pixel)

UNITY_INSTANCING_BUFFER_START(BbtsGrassShellProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _ShellTile)
    UNITY_DEFINE_INSTANCED_PROP(float4, _ShellInfo)
    UNITY_DEFINE_INSTANCED_PROP(float4, _ShellLight)
UNITY_INSTANCING_BUFFER_END(BbtsGrassShellProps)

struct ShellAttributes
{
    float4 positionOS : POSITION; // xz = -0.5 … 0.5 across the cell, y = the layer 0 … 1 (lifted here)
    float2 uv : TEXCOORD0;        // the cell's own 0 … 1 along world x and z
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct ShellVaryings
{
    float4 positionCS : SV_POSITION;
    float2 cellUv : TEXCOORD0;
    float4 tile : TEXCOORD1;  // _ShellTile
    float4 info : TEXCOORD2;  // x skylight, y UV turns, z strand seed, w layer 0 … 1
    float3 wp : TEXCOORD3;
    float3 bl : TEXCOORD4;    // placed block light
    float fade : TEXCOORD5;   // 1 near … 0 where the carpet lies flat
};

// A cheap, sine-free 2D hash (stable across GPUs): 0 … 1.
float BbtsMeadowHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// The carpet's breeze (#3078): only the slow wave of the plants' sway (BbtsWindSway) — its fast flutter term, a 2.9 rad/s
// sine, made a 22 cm carpet crawl. The phase reads the ground position only, so every layer of a cell leans as one piece
// and a corner shared by two cells moves the same way in both.
float3 BbtsShellSway(float3 wp, float tip)
{
    if (_Sc_Wind.w < 0.5)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float strength = length(_Sc_Wind.xz);
    float2 dir = strength > 1e-4 ? _Sc_Wind.xz / strength : float2(0.7, 0.7);
    float t = _Sc_Wind.y;
    float phase = dot(wp.xz, dir) * 0.35;
    float wave = sin(t * 1.3 + phase);
    float swell = 0.5 + 0.5 * sin(t * 0.37 + phase * 0.2);
    float amp = (0.03 + 0.22 * strength) * (0.6 + 0.4 * swell); // the plants' tip amplitude, metres
    return float3(dir.x, 0.0, dir.y) * (wave * amp * saturate(tip));
}

ShellVaryings BbtsShellVert(ShellAttributes v)
{
    ShellVaryings o = (ShellVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    float4 tile = UNITY_ACCESS_INSTANCED_PROP(BbtsGrassShellProps, _ShellTile);
    float4 info = UNITY_ACCESS_INSTANCED_PROP(BbtsGrassShellProps, _ShellInfo);
    float4 light = UNITY_ACCESS_INSTANCED_PROP(BbtsGrassShellProps, _ShellLight);

    // The whole stack of one cell shares its fade (per instance), so a layer never tears: it flattens as one piece.
    float layer = v.positionOS.y;
    float3 root = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float fade = saturate((BBTS_SHELL_FADE_END - distance(root, _WorldSpaceCameraPos)) / BBTS_SHELL_FADE_WIDTH);
    float snow = _Sc_Surface.z > 0.5 ? saturate(_Sc_Surface.y) : 0.0; // snowfall presses the carpet down (#2398)
    float lift = layer * BBTS_SHELL_HEIGHT * fade * (1.0 - 0.85 * snow);
    if (lift < BBTS_SHELL_MIN_LIFT)
    {
        o.positionCS = float4(2.0, 2.0, 2.0, 1.0); // all four corners on one point outside the view: no fragments
        return o;
    }

    float3 wp = TransformObjectToWorld(float3(v.positionOS.x, lift, v.positionOS.z));
    // The wind leans the upper layers more (rooted at the ground); a corner shared by two cells moves the same way in
    // both, so the carpet never tears along the block edges. Sheltered cells stand still like the plants (#2519).
    wp += BbtsShellSway(wp, layer * layer) * (BBTS_SHELL_SWAY * BbtsSwayShelter(info.x) * fade);

    o.positionCS = TransformWorldToHClip(wp);
    o.cellUv = v.uv;
    o.tile = tile;
    o.info = float4(info.x, info.y, info.z, layer);
    o.wp = wp;
    o.bl = light.rgb;
    o.fade = fade;
    return o;
}

// The strand under a carpet fragment: x > 0 where the fragment is part of a strand (the distance inside its edge, in
// strand cells), y = its height within the strand (0 root … 1 tip), zw = the strand's root in cell UV (its colour is the
// texel there). `minRadius` keeps far strands about a pixel wide.
float4 BbtsShellStrand(float2 cellUv, float seed, float layer, float minRadius)
{
    float2 g = cellUv * BBTS_SHELL_STRANDS;
    float2 id = floor(g);
    float2 f = g - id;
    float2 key = id + seed * 613.0;
    float hH = BbtsMeadowHash(key);
    float2 jit = float2(BbtsMeadowHash(key + 17.17), BbtsMeadowHash(key + 41.3)) - 0.5;

    // About one strand in eight is missing (the ground shows through); the rest reach 30 … 100 % of the carpet.
    float strandH = hH < 0.12 ? 0.0 : lerp(0.3, 1.0, (hH - 0.12) / 0.88);
    float t = layer / max(strandH, 1e-3);
    float2 rootUv = 0.5 + jit * 0.3;
    float2 centre = rootUv + jit.yx * (t * t * 0.45); // each strand leans its own way, more toward the tip
    float radius = max(lerp(0.42, 0.1, saturate(t)), minRadius); // a tuft at the root with gaps between, thin at the tip
    float keep = (t <= 1.0) ? radius - length(f - centre) : -1.0;
    return float4(keep, saturate(t), (id + rootUv) / BBTS_SHELL_STRANDS);
}

// The strand under the fragment and `px`, one pixel in strand cells. The footprint comes from the cell UV, which runs
// smoothly across the strand grid — the cut itself jumps at every strand cell's border, so its own derivative would not do.
float4 BbtsShellStrandPx(ShellVaryings i, out float px)
{
    float2 d = fwidth(i.cellUv) * BBTS_SHELL_STRANDS;
    px = max(max(d.x, d.y), 1e-4);
    return BbtsShellStrand(i.cellUv, i.info.z, i.info.w, min(BBTS_SHELL_MIN_WIDTH * px, 0.35));
}

// The carpet's hard cut — the depth pass, and the colour pass without MSAA.
float4 BbtsShellClip(ShellVaryings i)
{
    float px;
    float4 s = BbtsShellStrandPx(i, px);
    clip(s.x);
    return s;
}

// The colour pass's cut (#3078): with MSAA the strand edge becomes coverage — a one-pixel ramp across the edge that
// alpha-to-coverage turns into samples, so a thin strand covers part of a pixel instead of popping between all and nothing.
// URP raises _AlphaToMaskAvailable only for an MSAA target (High: 4×); on a single-sample target alpha-to-coverage is
// undefined across GPUs, so there the hard cut stays and the alpha is 1. Its 50 % line is the depth pass's cut.
float4 BbtsShellCover(ShellVaryings i, out float alpha)
{
    float px;
    float4 s = BbtsShellStrandPx(i, px);
    bool a2c = _AlphaToMaskAvailable != 0.0;
    float coverage = saturate(s.x / px + 0.5);
    clip(a2c ? coverage - 0.0001 : s.x);
    alpha = a2c ? coverage : 1.0;
    return s;
}

// The cell UV → the top face's tile UV, turned like the mesher turns a natural block's top (ChunkMesher.AddFace cycles the
// corners by `uvRot`; MeadowField.TopVariant picks the same turn), so a strand takes the colour of the texel under it.
float2 BbtsShellTileSt(float2 c, float turn)
{
    if (turn < 0.5)
    {
        return c;
    }

    if (turn < 1.5)
    {
        return float2(c.y, 1.0 - c.x);
    }

    if (turn < 2.5)
    {
        return 1.0 - c;
    }

    return float2(1.0 - c.y, c.x);
}

#endif

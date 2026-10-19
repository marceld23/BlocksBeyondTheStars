// Blocks Beyond the Stars — the atmosphere every world-space shader shares (atmosphere package #2408):
// the one haze (#2393, #2406), the cloud shadows (#2394) and the breathing torch light (#2396).
//
// Unity's fog never engages on the project's custom unlit voxel shaders, so the haze is an explicit blend toward
// the sky colour, driven by per-frame GLOBALS that Sky.cs sets (Shader.SetGlobal*; outside every UnityPerMaterial
// cbuffer — the SRP Batcher rule of #573). Before this file the blend lived in BlockAtlas and FarTerrain only, so
// creatures, ships, particles and clouds stood razor-sharp in front of a hazed landscape. Two terms:
//   * distance haze — linear from _Sc_Fog.x to _Sc_Fog.y metres, the chunk-ring veil the game always had;
//   * height fog    — mist that lies in the low places: density exp(-k·(y − floor)) above a floor height, integrated
//                     along the view ray (3-point Simpson, cheap and good enough), masked by the fragment's skylight
//                     (caves stay clear) and by the camera's own exposure (underground nothing mists up).
// The haze colour is the sky colour warmed toward the sun when looking at it (in-scatter), so a low sun reads
// golden through the mist and the opposite side stays cool.
//
// Globals:
//   _Sc_Fog         x = start, y = end (metres), z = max haze (0 indoors / switched off), w = on
//   _Sc_FogHeight   x = floor height (world Y the mist is densest at), y = 1/falloff per metre above it,
//                   z = strength 0..1 (0 = no height fog), w = camera exposure 0..1 (1 open sky, 0 underground)
//   _Sc_FogSun      rgb = sun in-scatter colour, a = strength 0..1
//   _Sc_FogSky      rgb = sky colour (the same value as _Sc_Sky; declared here so a shader that does not read _Sc_Sky
//                   itself needs no extra declaration), a > 0.5 = set
//   _Sc_FogSunDir   xyz = world direction TO the sun (the same value as _Sc_SunDir)
//   _Sc_CloudShadow x = cover 0..1, y,z = scroll offset (world blocks, follows the wind), w = strength 0..1 (0 = off)
//   _Sc_CloudNoise  a small tiling noise texture (Sky.cs bakes it once)
//   _Sc_FlickerScale 0 = steady light (Reduce flashes / switched off) … 1 = full breathing
// Cozy package (#2802):
//   _Sc_IndoorColor rgb = the colour of the interior fill (warm white at home, amber in the evening), a > 0.5 = set
//   _Sc_CozyLight    the block-light gain of the cosiness slider (1 … 1.3; 0 = unset, read as 1)
//   _Sc_GlowPulse    1 = glowing plants breathe, 0 = steady (Reduce flashes)
#ifndef BBTS_ATMOSPHERE_COMMON_INCLUDED
#define BBTS_ATMOSPHERE_COMMON_INCLUDED

float4 _Sc_Fog;
float4 _Sc_FogHeight;
float4 _Sc_FogSun;
float4 _Sc_FogSky;
float4 _Sc_FogSunDir;
float4 _Sc_CloudShadow;
float  _Sc_FlickerScale;
float4 _Sc_Wind; // xz = wind direction × strength 0..1, y = world time (pauses with the world), w = sway on (0/1)
float4 _Sc_Surface; // x = wet 0..1 (after rain), y = snow 0..1 (after snowfall), z = on (0/1), w unused (#2398)
float4 _Sc_Underwater; // x = camera under water (0/1), y = the water surface's world Y there, z = time, w unused (#2401)
float  _Sc_Caustics;   // the player's switch × preset (0/1)
float  _Sc_SeaLevel;   // the sea surface's world Y (#2414): faces below it lie under the sea; -1e9 when the world has none
float  _Sc_AtmoDebug;  // capture diagnostics (-atmoDebug N): 1 = show _Sc_Surface, 2 = skylight / up / cloud shade, 3 = haze amount
float4 _Sc_IndoorColor; // #2814: the interior fill's colour (a > 0.5 = set)
float  _Sc_CozyLight;   // #2820: block-light gain of the cosiness slider (0 = unset)
float  _Sc_GlowPulse;   // #2822: glowing plants breathe (1) or stay steady (0)
TEXTURE2D(_Sc_CloudNoise); SAMPLER(sampler_Sc_CloudNoise);

// #2814: the colour the interior fill lights the cabin in — white where nothing set it (a station, the menu, a preview).
float3 BbtsIndoorColor()
{
    return _Sc_IndoorColor.a > 0.5 ? _Sc_IndoorColor.rgb : float3(1.0, 1.0, 1.0);
}

// #2820: placed lights a touch stronger at the warm end of the cosiness slider; never below the plain light.
float BbtsCozyLightGain()
{
    return max(_Sc_CozyLight, 1.0);
}

// #2822: glowing plants and mushrooms breathe — one slow breath every ~6 s (≈ 0.17 Hz, far below the 3-per-second flash
// limit) with a second, slower sine so it never reads mechanical. The phase runs smoothly through space, so a glowing
// forest breathes as a slow wave instead of in unison, and a swaying plant never crosses a phase seam. Averages 1.0, so
// the bloom stays balanced; Reduce flashes holds it at 1.
float BbtsGlowBreath(float3 wp)
{
    if (_Sc_GlowPulse < 0.5)
    {
        return 1.0;
    }

    float phase = dot(wp, float3(0.21, 0.13, 0.17));
    float t = _Time.y;
    float s = 0.7 * sin(t * 1.047 + phase) + 0.3 * sin(t * 0.739 + phase * 1.7);
    return 1.0 + 0.28 * s;
}


// Caustics (#2401): while the camera is under water, sunlight dances on every lit, upward face below the surface — two
// drifting copies of the noise sheet multiplied and sharpened. Returns 0..1 to add as light; 0 above water.
float BbtsCaustics(float3 wp, float3 N, float skylight)
{
    if (_Sc_Underwater.x < 0.5 || _Sc_Caustics < 0.5 || wp.y > _Sc_Underwater.y)
    {
        return 0.0;
    }

    float t = _Sc_Underwater.z;
    float2 uv = wp.xz * 0.09;
    float a = SAMPLE_TEXTURE2D_LOD(_Sc_CloudNoise, sampler_Sc_CloudNoise, uv + float2(t * 0.03, t * 0.021), 0).r;
    float b = SAMPLE_TEXTURE2D_LOD(_Sc_CloudNoise, sampler_Sc_CloudNoise, uv * 1.7 - float2(t * 0.027, -t * 0.019), 0).r;
    float web = saturate(a * b * 4.0 - 0.55);
    web = web * web;
    float depthFade = saturate(1.0 - (_Sc_Underwater.y - wp.y) / 14.0); // fades out in the deep
    return web * saturate(N.y) * saturate(skylight) * depthFade;
}

// Weather leaves a trace (#2398): rain wets the open ground (darker, glossier), snowfall caps the upward faces with a
// white layer whose edge follows the cloud noise so it never reads as a flat sheet. Skylit, upward faces only, so
// roofs, caves and interiors stay dry. Modifies the albedo and the gloss in place; returns the snow cover 0..1.
float BbtsWeatherSurface(inout float3 albedo, inout float gloss, float3 wp, float3 N, float skylight)
{
    if (_Sc_Surface.z < 0.5)
    {
        return 0.0;
    }

    // #2414: nothing under the sea gets wet or snowed on. The seabed under shallow water still carries some mesher
    // skylight (water counts as cover, so the shade floor of #1608 applies), and without this the shallows turned
    // patchily white after snowfall. Lakes above sea level keep the traces (rare, accepted).
    if (wp.y < _Sc_SeaLevel - 0.5)
    {
        return 0.0;
    }

    float exposed = saturate(N.y) * saturate(skylight);
    float wet = _Sc_Surface.x * exposed;
    albedo *= 1.0 - 0.35 * wet;
    gloss = max(gloss, wet * 0.7);

    float snowAmt = _Sc_Surface.y * exposed;
    float cap = 0.0;
    if (snowAmt > 0.001)
    {
        float sn = SAMPLE_TEXTURE2D_LOD(_Sc_CloudNoise, sampler_Sc_CloudNoise, wp.xz * 0.11, 0).r;
        cap = smoothstep(0.2, 0.7, snowAmt * 1.25 - (1.0 - sn) * 0.45);
        albedo = lerp(albedo, float3(0.90, 0.93, 0.98), cap);
        gloss = lerp(gloss, 0.25, cap);
    }

    return cap;
}

// Vertex sway for leaves, grass and flora (#2397). `foliage` = a tree crown / cutout leaf (TEXCOORD2.x > 0.5), `flora` =
// a plant (tint mode 1); `tip` = 0 at the root … 1 at the tip (a plant bends at the top and stays rooted; crowns pass a
// constant). World-position phases keep chunk borders seamless and let neighbours drift apart; two slow waves plus a
// swell, so a calm day breathes and a storm whips. Returns the world-space offset to add before projection.
float3 BbtsWindSway(float3 wp, bool foliage, bool flora, float tip)
{
    if (_Sc_Wind.w < 0.5 || !(foliage || flora))
    {
        return float3(0.0, 0.0, 0.0);
    }

    float strength = length(_Sc_Wind.xz);
    float2 dir = strength > 1e-4 ? _Sc_Wind.xz / strength : float2(0.7, 0.7);
    float t = _Sc_Wind.y;
    float phase = dot(wp.xz, dir) * 0.35 + wp.y * 0.15;
    float wave = sin(t * 1.3 + phase) * 0.6 + sin(t * 2.9 + phase * 1.7 + wp.x * 0.5) * 0.4;
    float swell = 0.5 + 0.5 * sin(t * 0.37 + phase * 0.2);
    float amp = (0.03 + 0.22 * strength) * (0.6 + 0.4 * swell); // metres at the tip
    float reach = flora ? saturate(tip) : 0.7;
    float3 offset = float3(dir.x, 0.0, dir.y) * (wave * amp * reach);
    offset.y = -abs(wave) * amp * reach * 0.15; // a bent stalk also dips a little
    return offset;
}

// #2519: how much wind reaches a plant, from its skylight (0 cave … 1 open sky). The mesher gives every cell up to six
// blocks under cover — a roof, an overhang, a shallow cave, a thick canopy — at least 0.55 (its shade floor), so the
// ramp starts there: plants indoors and in caves stand still, plants in the open keep the full sway (the small calm-day
// breathing included), and the edge of a canopy or a cave mouth fades between the two.
float BbtsSwayShelter(float skylight)
{
    return smoothstep(0.55, 0.9, skylight);
}

// #2819: where the wind does not reach (a cabin, a hut, a cave), a plant still is not frozen: a faint draught moves its tip
// about a centimetre, slow and independent of the weather outside, so a potted plant aboard breathes instead of standing
// like a prop. Off with the wind-sway switch (and in airless skies, which switch the sway off as a whole).
float3 BbtsDraught(float3 wp, float tip)
{
    if (_Sc_Wind.w < 0.5)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float t = _Sc_Wind.y;
    float phase = wp.x * 0.35 + wp.z * 0.27 + wp.y * 0.15;
    float wave = sin(t * 0.9 + phase) * 0.6 + sin(t * 2.1 + phase * 1.7) * 0.4;
    return float3(0.7, 0.0, 0.7) * (wave * 0.012 * saturate(tip));
}

// #2519: the wind sway of one block-atlas vertex — the forward, shadow and depth passes all call this, so the shadow
// follows the leaf. `leafCode` is TEXCOORD2.x, the cutout code the chunk mesher writes:
//   1 = a tree-crown shell face: sways as ONE rigid piece (constant reach). Every crown face at a shared corner gets the
//       same offset (it depends on the world position only), so the leaf cubes of a crown never tear apart.
//   2 = a rigid cutout prop (torch, lantern): clipped like foliage, never sways.
//   3 = a cross-billboard plant: bends from its root (`tip` = the tile's 0 root … 1 tip).
//   4 = a cross-billboard plant the water surrounds (kelp, seagrass): bends like 3, never sheltered (see below).
// Solid flora (no cutout, tint mode 1) bends like a plant. Plants and solid flora are scaled by BbtsSwayShelter: each of
// them carries ONE skylight value on all its vertices (the open sky above the plant), so the scale cannot split a plant.
// For solid flora that holds because the mesher always draws it as one shaped form (AddShapedBlock, a single skylight
// for the whole block) — never through the cube-face path, where the skylight is per face; the only cube faces in tint
// mode 1 are crown faces, which carry code 1. ChunkMesherCutoutCodeEditModeTests pins this. Leaving solid flora
// unscaled instead would set the cave caps, mushrooms and spore pods swaying underground again.
// Crown faces are NOT scaled: they carry a skylight per FACE (the air each face looks into — a crown's underside sits
// in the shade floor, its top in the open), and scaling by it would move the top of a leaf cube but not its underside,
// opening cracks along the edges. Crowns grow under the open sky anyway, and the "wind in leaves" switch stops them.
// Aquatic plants are NOT scaled either: the skylight counts the water above them as cover, but under water the sway
// reads as current, so kelp and seagrass keep it.
float3 BbtsBlockSway(float3 wp, float leafCode, float tintMode, float skylight, float tip)
{
    bool crown = leafCode > 0.5 && leafCode < 1.5;
    bool plant = leafCode > 2.5 && leafCode < 3.5;
    bool aquatic = leafCode > 3.5 && leafCode < 4.5;
    bool solidFlora = abs(leafCode) < 0.5 && tintMode > 0.5 && tintMode < 1.5;
    if (!(crown || plant || aquatic || solidFlora))
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 offset = BbtsWindSway(wp, true, !crown, tip);
    if (crown || aquatic)
    {
        return offset;
    }

    // #2819: sheltered plants fade from the wind into the indoor draught (one skylight per plant, so it cannot split).
    return lerp(BbtsDraught(wp, tip), offset, BbtsSwayShelter(skylight));
}

// Mist density at height y: 1 at and below the floor, falling off exponentially above it.
float BbtsMistDensity(float y)
{
    return exp(-_Sc_FogHeight.y * max(y - _Sc_FogHeight.x, 0.0));
}

// 0..1 haze at world position `wp`. `skylight` is the fragment's own sky exposure (0 cave … 1 open); surfaces that
// carry none pass 1 and rely on the camera-exposure term. `heightScale` scales the height-fog term only: a water
// surface passes 0.5 (#2412) — the mist floor lies ON the water (the probe's lowest column tops are the water itself),
// and at full strength the surface dissolved into the sky from dusk on. The distance veil is never scaled.
float BbtsHazeAmountScaled(float3 wp, float skylight, float heightScale)
{
    if (_Sc_Fog.w < 0.5)
    {
        return 0.0;
    }

    float3 cam = _WorldSpaceCameraPos;
    float camDist = distance(wp, cam);
    float dist = saturate((camDist - _Sc_Fog.x) / max(1.0, _Sc_Fog.y - _Sc_Fog.x));

    float height = 0.0;
    if (_Sc_FogHeight.z > 0.001)
    {
        float3 mid = (cam + wp) * 0.5;
        float depth = camDist * (BbtsMistDensity(cam.y) + 4.0 * BbtsMistDensity(mid.y) + BbtsMistDensity(wp.y)) / 6.0;
        // 0.035 per metre at full strength: ~40 m through dense mist leaves a quarter of the scene.
        height = 1.0 - exp(-depth * 0.035 * _Sc_FogHeight.z);
        height *= saturate(skylight) * _Sc_FogHeight.w * heightScale;
    }

    return (1.0 - (1.0 - dist) * (1.0 - height)) * _Sc_Fog.z;
}

float BbtsHazeAmount(float3 wp, float skylight)
{
    return BbtsHazeAmountScaled(wp, skylight, 1.0);
}

// The colour the haze blends toward at `wp`: the sky, warmed toward the sun when looking at it.
float3 BbtsHazeColor(float3 wp)
{
    float3 sky = (_Sc_FogSky.a < 0.5) ? float3(1.0, 1.0, 1.0) : _Sc_FogSky.rgb;
    float3 V = normalize(wp - _WorldSpaceCameraPos);
    float toSun = pow(saturate(dot(V, normalize(_Sc_FogSunDir.xyz))), 8.0);
    return lerp(sky, _Sc_FogSun.rgb, toSun * _Sc_FogSun.a);
}

// Opaque surfaces: blend the lit colour toward the haze.
float3 BbtsApplyHaze(float3 col, float3 wp, float skylight)
{
    return lerp(col, BbtsHazeColor(wp), BbtsHazeAmount(wp, skylight));
}

// Sky objects (the cloud billboards sit far beyond the chunk ring, so the distance term would always swallow them):
// only a view-killing weather — fog, a sandstorm — hides them, by how short the visible range has become.
float BbtsSkyObjectHaze()
{
    if (_Sc_Fog.w < 0.5)
    {
        return 0.0;
    }

    return saturate(1.0 - _Sc_Fog.y / 90.0) * _Sc_Fog.z;
}

// Cloud shadow factor 0..1 for the direct-sun term at `wp` (1 = in the sun). A wind-scrolled, two-octave noise sheet
// in world XZ; the cover sets how much of the ground sits under cloud, the strength how dark it gets (capped by
// Sky.cs so no scene turns gloomy). LOD-0 sample: legal inside any branch on every API (no derivatives).
float BbtsCloudShade(float3 wp)
{
    if (_Sc_CloudShadow.w < 0.001)
    {
        return 1.0;
    }

    float2 uv = (wp.xz + _Sc_CloudShadow.yz) * (1.0 / 96.0); // one noise tile per 96 blocks
    float n = SAMPLE_TEXTURE2D_LOD(_Sc_CloudNoise, sampler_Sc_CloudNoise, uv, 0).r;
    float n2 = SAMPLE_TEXTURE2D_LOD(_Sc_CloudNoise, sampler_Sc_CloudNoise, uv * 2.7 + 0.37, 0).r;
    float v = n * 0.65 + n2 * 0.35;
    float edge0 = 1.0 - _Sc_CloudShadow.x;
    float cloud = smoothstep(edge0, edge0 + 0.3, v);
    return 1.0 - cloud * _Sc_CloudShadow.w;
}

// Warm block light (torches, campfires, lanterns) breathes: two slow sines — 0.37 Hz and 0.81 Hz, far below the
// 3-per-second flash limit — phased by position in 6-block cells so neighbouring torches drift apart. Cold lamps
// and crystals (blue share ≥ red) stay steady; Reduce flashes zeroes the scale.
float3 BbtsBlockLightFlicker(float3 bl, float3 wp)
{
    // #2816: only FLAME-coloured light breathes. Since the light colours went linear (#2804) a torch, lantern or campfire
    // keeps a blue share of ≤ ~10 % of its red, while a warm-white electric lamp (the ship's living-room lamps, the
    // palette's warm white) keeps ~40 % — it must glow steadily, not flicker like fire.
    float flame = saturate((0.25 - bl.b / max(bl.r, 1e-4)) / 0.15);
    float warmth = flame * saturate((bl.r - bl.b) * 2.5) * saturate(dot(bl, float3(1.0, 1.0, 1.0)) * 2.0);
    if (warmth <= 0.001 || _Sc_FlickerScale <= 0.001)
    {
        return bl;
    }

    float seed = dot(floor(wp / 6.0), float3(12.9898, 37.719, 78.233));
    float t = _Time.y;
    float f = 0.6 * sin(t * 2.3 + seed) + 0.4 * sin(t * 5.1 + seed * 1.7);
    return bl * (1.0 + 0.12 * f * warmth * _Sc_FlickerScale);
}

// Capture diagnostics (#2405): replaces the lit colour with the atmosphere inputs a surface sees, so a look-check
// frame says whether a global reached the shader at all. Off (0) in play; the clip recorder sets it.
float3 BbtsAtmoDebug(float3 col, float3 wp, float3 N, float skylight)
{
    if (_Sc_AtmoDebug < 0.5)
    {
        return col;
    }

    if (_Sc_AtmoDebug < 1.5)
    {
        return _Sc_Surface.xyz; // r = wet, g = snow, b = on
    }

    if (_Sc_AtmoDebug < 2.5)
    {
        return float3(saturate(skylight), saturate(N.y), BbtsCloudShade(wp));
    }

    float h = BbtsHazeAmount(wp, skylight);
    return float3(h, _Sc_FogHeight.z, _Sc_FogHeight.w);
}

#endif

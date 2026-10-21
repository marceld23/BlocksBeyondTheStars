// Blocks Beyond the Stars — the flora re-tint of the block shaders (both pipelines), with the accent colours of #2834.
//
// A wild plant drops its tile to luminance and takes its species' per-world colour (TEXCOORD2.yzw). Before #2834 that
// made every plant ONE colour: a red blossom on a green leaf, a berry or a spotted cap all turned into the species hue.
// An accent species (BlockDefinition.Accent) now gives its blossoms, berries and caps a colour of their own:
//   * the accent texels are found in the tile itself — bright, saturated and not green (leaves and stems stay tinted);
//   * the accent colour derives from the species tint (its hue turned by a species-dependent amount), so every planet
//     differs and no extra vertex channel is needed;
//   * each plant picks one of three related accent colours (ChunkMesher.AccentCodeAt): the fraction of its cutout code
//     (TEXCOORD2.x: 0.1 / 0.2 / 0.3 on top of 0 solid, 3 plant, 4 aquatic) — every other test of that channel is a ±0.5
//     window, so the codes keep their meaning. A meadow of one species shows a mix (pink, violet and pale lupines).
// The analysis runs on gamma-encoded values (sqrt of the linear albedo), so the thresholds read like the tile's art.
// Also here because both block passes include this file: the daylight fade of the plants' self-glow (#2858, below).
// Plain float HLSL — included by the URP pass (HLSLPROGRAM) and the Built-in pass (CGPROGRAM) alike.
#ifndef BBTS_FLORA_ACCENT_INCLUDED
#define BBTS_FLORA_ACCENT_INCLUDED

float3 BbtsHsvToRgb(float3 c)
{
    float3 p = abs(frac(c.xxx + float3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return c.z * lerp(float3(1.0, 1.0, 1.0), saturate(p - 1.0), c.y);
}

float3 BbtsRgbToHsv(float3 c)
{
    float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = c.g < c.b ? float4(c.bg, k.wz) : float4(c.gb, k.xy);
    float4 q = c.r < p.x ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-5;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

// 0 = no accent, 1..3 = which of the species' three accent colours the plant wears.
float BbtsAccentPick(float leafCode)
{
    return round((leafCode - round(leafCode)) * 10.0);
}

// The re-tinted albedo of a wild plant: luminance x species tint (85 %), accent texels in their own colour.
float3 BbtsFloraTint(float3 albedo, float3 tint, float leafCode)
{
    float lum = dot(albedo, float3(0.299, 0.587, 0.114));
    float3 col = lum * tint * 1.6;
    float pick = BbtsAccentPick(leafCode);
    if (pick > 0.5)
    {
#ifdef UNITY_COLORSPACE_GAMMA
        float3 g = albedo;
        float3 tg = tint;
#else
        float3 g = sqrt(max(albedo, 0.0));
        float3 tg = sqrt(max(tint, 0.0));
#endif
        float mx = max(g.r, max(g.g, g.b));
        float mn = min(g.r, min(g.g, g.b));
        float sat = mx > 1.0e-4 ? (mx - mn) / mx : 0.0;
        float green = g.g - max(g.r, g.b);
        float m = smoothstep(0.28, 0.5, sat) * smoothstep(0.32, 0.55, mx) * (1.0 - smoothstep(-0.04, 0.06, green));

        float3 hsv = BbtsRgbToHsv(tg);
        float baseHue = frac(hsv.x + 0.33 + (hsv.y - 0.45) * 0.8);
        float3 acc = pick < 1.5 ? BbtsHsvToRgb(float3(baseHue, 0.80, 1.0))
            : pick < 2.5 ? BbtsHsvToRgb(float3(frac(baseHue + 0.08), 0.70, 1.0))
            : BbtsHsvToRgb(float3(frac(baseHue + 0.94), 0.28, 1.0));
        acc = saturate(acc * (0.25 + 0.95 * mx));
#ifndef UNITY_COLORSPACE_GAMMA
        acc = acc * acc; // back to linear (the inverse of the sqrt above)
#endif
        col = lerp(col, acc, m);
    }

    return lerp(albedo, col, 0.85);
}

// #2858: a glowing plant's self-glow by the time of day — about a quarter in full daylight, full at dusk, at night and
// wherever the sky does not reach (caves, under a roof), so a glowing wood stands out at night and not at noon. Only the
// plant branch of the emission calls this (flora tint mode or a cross plant — the condition the #2822 breathing uses);
// lamps, lava, ores and crystals keep their glow. sunHeight = the y of the direction TO the sun (_Sc_SunDir, which Sky.cs
// turns with the day: 1 at noon, 0 on the horizon, below 0 at night); skylight = the face's sky exposure (TEXCOORD1.x).
// The sun counts as "day" from ~3° above the horizon and fully from ~20°, so the whole golden hour still glows.
float BbtsPlantGlowDaylight(float sunHeight, float skylight)
{
    float day = smoothstep(0.05, 0.35, sunHeight) * saturate(skylight);
    return lerp(1.0, 0.25, day);
}

#endif

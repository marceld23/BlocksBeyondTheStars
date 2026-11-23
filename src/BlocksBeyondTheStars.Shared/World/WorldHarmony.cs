// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>The mood of a world's colours (the colour harmony, terrain generation 48): how saturated and how bright its
/// sky, clouds, mist, water and plants are. Rolled per world, or named by its type (<see cref="PlanetType.ColourMood"/>).</summary>
public enum WorldMood
{
    /// <summary>Clear middle colours — the most common mood.</summary>
    Balanced = 0,

    /// <summary>Strong, bright colours: the colourful worlds.</summary>
    Vivid = 1,

    /// <summary>Pale, light colours.</summary>
    Pastel = 2,

    /// <summary>Muted, slightly darker colours.</summary>
    Earthy = 3,

    /// <summary>Dim and grey: a darker, duller sky, dark cloud decks, dull plants.</summary>
    Gloomy = 4,
}

/// <summary>How a world's plant species spread over the colour wheel (the colour harmony, terrain generation 48).</summary>
public enum HarmonyScheme
{
    /// <summary>Three colour families: the tree crowns' hue, a neighbour of it and its contrast.</summary>
    Triad = 0,

    /// <summary>One colour family: every species near the crowns' hue, differing in saturation and brightness.</summary>
    Monochrome = 1,

    /// <summary>Six colour families evenly round the wheel, starting at the crowns' hue: the many-coloured worlds.</summary>
    Rainbow = 2,
}

/// <summary>
/// The colour harmony of one world (terrain generation <see cref="WorldDescription.ColourHarmonyGeneration"/>).
/// <para>
/// Up to that generation a world rolls five colours that know nothing of each other: the sky and the clouds
/// (<see cref="AtmosphereTints"/>), the valley mist (<see cref="MistProfiles"/>), the water (<see cref="FluidTints"/>) and
/// every plant species (<see cref="FloraTints"/>). From it the sky is the anchor and the others answer it:
/// </para>
/// <list type="bullet">
/// <item>the tree crowns take ONE hue per world — a neighbour of the sky's hue, a third of the colour wheel away, or its
/// opposite (rolled per world), so a violet sky carries pink, amber or green crowns and never an unrelated colour;</item>
/// <item>every other wild species takes a hue from the world's <see cref="HarmonyScheme"/>: three families around the
/// crowns' hue (most worlds), one family, or six round the whole wheel;</item>
/// <item>a tinted cloud deck sits right beside the sky's hue, a tinted mist on the sky's other side, tinted water takes
/// almost the sky's own hue, and the plants' base hue is the crowns';</item>
/// <item>the world's <see cref="WorldMood"/> sets how saturated and how bright all of it is — balanced, vivid, pastel,
/// earthy or gloomy — so two worlds under the same sky family still read differently.</item>
/// </list>
/// <para>
/// What a world rolls from follows the planet's kind (<see cref="WorldPalettes"/>: its sky colours, the odds of its
/// moods and schemes), and the choices multiply (eight to fifteen skies × five moods × six crown answers × three schemes,
/// each with its own jitter), so worlds rarely look alike. What is white or grey keeps its hue — white fits every palette. Authored colours
/// are not touched by the callers: a type's sky and the clouds painted against it, its flora palette, its authored mist,
/// a fixed or rainbow water tint, and toxic water (its poison colours are a warning).
/// </para>
/// <para>
/// A pure function of (save seed, location id, planet type), like every other per-world look: the server and every client
/// build the same value without traffic. <see cref="None"/> (the default value) is the harmony of every older world — the
/// colour methods then return their input unchanged.
/// </para>
/// </summary>
public readonly record struct WorldHarmony
{
    /// <summary>How far a cloud deck's hue sits from the sky's (share of the colour wheel).</summary>
    public const float CloudStep = 0.07f;

    /// <summary>The strongest saturation a harmonised cloud deck keeps.</summary>
    public const float MaxCloudSaturation = 0.42f;

    /// <summary>How far the mist's hue sits from the sky's, on the other side than the clouds.</summary>
    public const float MistStep = 0.09f;

    /// <summary>How far tinted water's hue sits from the sky's — almost the sky's own.</summary>
    public const float WaterStep = 0.03f;

    /// <summary>The arc of the colour wheel clean water stays in (sea green … indigo, 0..1): water answers the sky's hue
    /// only inside it. Outside lie the poison colours (sulphur, yellow-green, rust — <see cref="FluidTints"/>), which must
    /// stay a warning: under a yellow-green sky clean water turns sea green, never sulphur.</summary>
    public const float WaterHueMin = 0.42f, WaterHueMax = 0.72f;

    /// <summary>A colour with less saturation than this counts as white or grey and keeps its hue.</summary>
    public const float MinTintSaturation = 0.10f;

    /// <summary>The same for the mist, whose tints are paler.</summary>
    public const float MinMistSaturation = 0.08f;

    /// <summary>No plant is darker than this (brightness 0..1): the darkest trunk (<see cref="FloraTints.ForWood(long, string)"/>,
    /// 0.58) stays darker than every leaf, in every mood.</summary>
    public const float MinSpeciesValue = 0.6f;

    private const ulong Salt = 0x4800_0000_C0A5_0001UL;
    private const ulong MoodSalt = 0x4800_0000_300D_0002UL;
    private const ulong SchemeSalt = 0x4800_0000_5C4E_0003UL;

    private readonly float _skyValue;
    private readonly float _neighbour;
    private readonly float _contrast;
    private readonly float _side;

    private WorldHarmony(int skyRgb, uint word, WorldMood mood, HarmonyScheme scheme)
    {
        ToHsv(Red(skyRgb), Green(skyRgb), Blue(skyRgb), out float skyHue, out _, out float skyValue);
        Active = true;
        Mood = mood;
        Scheme = scheme;
        SkyHue = skyHue;
        _skyValue = skyValue;
        float treeSide = ((word >> 4) & 1u) == 0u ? 1f : -1f;
        float step = (word % 3u) switch { 0u => 0.10f, 1u => 0.33f, _ => 0.5f };
        TreeHue = Wrap(skyHue + (treeSide * step) + ((Byte(word, 8) - 0.5f) * 0.04f));
        _neighbour = 0.12f + (Byte(word, 16) * 0.05f);
        _contrast = 0.36f + (Byte(word, 24) * 0.1f);
        _side = ((word >> 5) & 1u) == 0u ? 1f : -1f;
    }

    /// <summary>The harmony of every world older than <see cref="WorldDescription.ColourHarmonyGeneration"/>: none.</summary>
    public static WorldHarmony None => default;

    /// <summary>False for <see cref="None"/> — the colour methods then return their input.</summary>
    public bool Active { get; }

    /// <summary>The world's mood: how saturated and how bright its colours are.</summary>
    public WorldMood Mood { get; }

    /// <summary>How the world's plant species spread over the colour wheel.</summary>
    public HarmonyScheme Scheme { get; }

    /// <summary>The hue of the world's sky, 0..1 — the anchor.</summary>
    public float SkyHue { get; }

    /// <summary>The hue of the world's tree crowns and of its plants' base colour, 0..1.</summary>
    public float TreeHue { get; }

    /// <summary>The harmony of one world: <see cref="None"/> below <see cref="WorldDescription.ColourHarmonyGeneration"/>,
    /// else the one around that world's sky (<see cref="AtmosphereTints.SkyRgb"/>). <paramref name="terrainGeneration"/>
    /// is the SAVE's generation, <paramref name="locationId"/> the body's id (not its display name). A void world — a
    /// ship's cabin, a station's deck — has no sky to answer: its plants keep their classic colours.</summary>
    public static WorldHarmony For(long worldSeed, string? locationId, PlanetType? planet, int terrainGeneration)
        => terrainGeneration >= WorldDescription.ColourHarmonyGeneration && planet is not { Void: true }
            ? Around(worldSeed, locationId, AtmosphereTints.SkyRgb(worldSeed, locationId, planet, terrainGeneration), planet)
            : default;

    /// <summary>The rule itself: the harmony of the world (<paramref name="worldSeed"/>, <paramref name="locationId"/>)
    /// around a given sky colour (0xRRGGBB), in the mood of <see cref="MoodOf"/> and a plant scheme rolled with the odds
    /// of the planet's kind (<see cref="WorldPalettes.SchemeWeightsOf"/>), whatever its generation.</summary>
    public static WorldHarmony Around(long worldSeed, string? locationId, int skyRgb, PlanetType? planet)
    {
        var scheme = (HarmonyScheme)WorldPalettes.Pick(
            WorldPalettes.SchemeWeightsOf(MistProfiles.FamilyOf(planet)), AtmosphereTints.MixedWord(worldSeed, locationId, SchemeSalt));
        return new WorldHarmony(skyRgb, AtmosphereTints.MixedWord(worldSeed, locationId, Salt), MoodOf(worldSeed, locationId, planet), scheme);
    }

    /// <summary>The mood of one world: the one its type names (<see cref="PlanetType.ColourMood"/> — the school club's
    /// Arena Nigra is gloomy on every save), else a roll per world with the odds of the planet's kind
    /// (<see cref="WorldPalettes.MoodWeightsOf"/>): an ice world is often pastel, a volcanic one often gloomy or earthy, a
    /// lush or exotic one often vivid — and every kind can roll every mood.</summary>
    public static WorldMood MoodOf(long worldSeed, string? locationId, PlanetType? planet)
    {
        string authored = planet?.ColourMood?.Trim() ?? string.Empty;
        if (authored.Length > 0 && System.Enum.TryParse(authored, ignoreCase: true, out WorldMood named)
            && System.Enum.IsDefined(typeof(WorldMood), named))
        {
            return named;
        }

        return (WorldMood)WorldPalettes.Pick(
            WorldPalettes.MoodWeightsOf(MistProfiles.FamilyOf(planet)), AtmosphereTints.MixedWord(worldSeed, locationId, MoodSalt));
    }

    /// <summary>A rolled sky colour (0xRRGGBB) in a mood: stronger on a vivid world, paler and lighter on a pastel one,
    /// a little muted on an earthy one, clearly darker and greyer on a gloomy one. The hue stays.</summary>
    public static int Sky(int rgb, WorldMood mood)
    {
        ToHsv(Red(rgb), Green(rgb), Blue(rgb), out float h, out float s, out float v);
        switch (mood)
        {
            case WorldMood.Vivid:
                s = System.Math.Min(s * 1.2f, System.Math.Max(s, 0.5f));
                break;
            case WorldMood.Pastel:
                s *= 0.65f;
                v += (1f - v) * 0.5f;
                break;
            case WorldMood.Earthy:
                s *= 0.8f;
                v *= 0.93f;
                break;
            case WorldMood.Gloomy:
                s *= 0.55f;
                v *= 0.64f;
                break;
            default:
                return rgb;
        }

        return Pack(ToRgb(h, s, v));
    }

    /// <summary>The saturation and brightness bands a wild plant species is drawn into, by mood.</summary>
    public static (float SatLo, float SatHi, float ValLo, float ValHi) SpeciesBands(WorldMood mood) => mood switch
    {
        WorldMood.Vivid => (0.72f, 0.95f, 0.90f, 1.00f),
        WorldMood.Pastel => (0.24f, 0.42f, 0.92f, 1.00f),
        WorldMood.Earthy => (0.35f, 0.58f, 0.66f, 0.80f),
        WorldMood.Gloomy => (0.15f, 0.38f, MinSpeciesValue, 0.70f),
        _ => (0.50f, 0.80f, 0.85f, 0.95f),
    };

    /// <summary>A wild plant species' colour on this world. A tree <paramref name="crown"/> always takes the crowns' hue
    /// (<see cref="TreeHue"/>); every other species picks its family by its <paramref name="blockKey"/> — in a
    /// <see cref="HarmonyScheme.Triad"/> two in five the neighbour, two in five the contrast, one in five the crowns' own;
    /// in a <see cref="HarmonyScheme.Rainbow"/> one of six round the wheel; in a <see cref="HarmonyScheme.Monochrome"/>
    /// the crowns' own — with a few degrees of its own on top, so two species of one family still differ.
    /// <paramref name="saturationRoll"/> and <paramref name="valueRoll"/> (0..1, the species' own per-world rolls) place it
    /// inside the mood's bands (<see cref="SpeciesBands"/>). Only meaningful while <see cref="Active"/>.</summary>
    public (float R, float G, float B) Species(string? blockKey, bool crown, float saturationRoll, float valueRoll)
    {
        ulong k = StableHashing.Mix64(FloraTints.Hash(blockKey ?? string.Empty));
        float offset = 0f;
        if (!crown)
        {
            offset = Scheme switch
            {
                HarmonyScheme.Monochrome => 0f,
                HarmonyScheme.Rainbow => (int)(k % 6UL) / 6f,
                _ => (k % 5UL) switch { 0UL or 1UL => -_neighbour, 2UL or 3UL => _contrast, _ => 0f },
            };
        }

        float hue = Wrap(TreeHue + offset + ((((k >> 8) & 0xFFUL) / 255f - 0.5f) * 0.05f));
        var (satLo, satHi, valLo, valHi) = SpeciesBands(Mood);
        if (Scheme == HarmonyScheme.Monochrome)
        {
            // One hue only: the species tell apart by how strong and how bright they are.
            satLo *= 0.7f;
            valLo = System.Math.Max(MinSpeciesValue, valLo - 0.1f);
        }

        return ToRgb(hue, satLo + ((satHi - satLo) * Clamp(saturationRoll, 0f, 1f)), valLo + ((valHi - valLo) * Clamp(valueRoll, 0f, 1f)));
    }

    /// <summary>A cloud tint (0xRRGGBB) in this world: a tinted deck takes a hue beside the sky's, a white or grey deck
    /// stays as it is — and a gloomy world wears a dark deck, darker than its sky, whatever the tint was.</summary>
    public int Cloud(int rgb)
    {
        if (!Active)
        {
            return rgb;
        }

        ToHsv(Red(rgb), Green(rgb), Blue(rgb), out float h, out float s, out float v);
        bool tinted = s >= MinTintSaturation;
        if (!tinted && Mood != WorldMood.Gloomy)
        {
            return rgb;
        }

        if (tinted)
        {
            // Beside the sky's hue, and never stronger than a tint: a type's strongly coloured dust deck (a red desert's
            // orange) would read as a neon cloud in another hue.
            h = Wrap(SkyHue + (_side * CloudStep));
            s = System.Math.Min(s, MaxCloudSaturation);
        }

        if (Mood == WorldMood.Gloomy)
        {
            s = System.Math.Min(s, 0.22f);
            v = Clamp(_skyValue * 0.62f, 0.22f, 0.42f);
        }

        return Pack(ToRgb(h, s, v));
    }

    /// <summary>A mist colour (0xRRGGBB) in this world: its hue set on the sky's other side, paler on a pastel, an earthy
    /// and above all a gloomy world, then lifted back to <see cref="MistProfiles.MinLuma"/>; a white mist, and "none"
    /// (below 0), stay as they are.</summary>
    public int Mist(int rgb)
    {
        if (!Active || rgb < 0)
        {
            return rgb;
        }

        ToHsv(Red(rgb), Green(rgb), Blue(rgb), out _, out float s, out float v);
        if (s < MinMistSaturation)
        {
            return rgb;
        }

        s *= Mood switch { WorldMood.Gloomy => 0.3f, WorldMood.Pastel => 0.8f, WorldMood.Earthy => 0.85f, _ => 1f };
        var (r, g, b) = ToRgb(Wrap(SkyHue - (_side * MistStep)), s, v);
        return MistProfiles.Lift(r, g, b);
    }

    /// <summary>A water tint (0xRRGGBB) in this world: its hue set to almost the sky's own — as far as the sky's hue lies
    /// in the arc of clean water (<see cref="WaterHueMin"/>..<see cref="WaterHueMax"/>), else the arc's nearer end; duller
    /// and darker on a gloomy world, a little so on an earthy one.</summary>
    public int Water(int rgb)
    {
        if (!Active)
        {
            return rgb;
        }

        ToHsv(Red(rgb), Green(rgb), Blue(rgb), out _, out float s, out float v);
        if (s < MinTintSaturation)
        {
            return rgb;
        }

        (float satK, float valK) = Mood switch
        {
            WorldMood.Gloomy => (0.7f, 0.72f),
            WorldMood.Earthy => (0.85f, 0.9f),
            WorldMood.Pastel => (0.8f, 1f),
            _ => (1f, 1f),
        };
        return Pack(ToRgb(InWaterArc(Wrap(SkyHue + (_side * WaterStep))), s * satK, v * valK));
    }

    /// <summary>The world's base flora hue (0xRRGGBB) in this world — the colour of its grass and of every plant face
    /// without a species colour: the hue of the tree crowns, in the mood's strength and brightness.</summary>
    public int FloraBase(int rgb)
    {
        if (!Active)
        {
            return rgb;
        }

        ToHsv(Red(rgb), Green(rgb), Blue(rgb), out _, out float s, out float v);
        if (s < MinTintSaturation)
        {
            return rgb;
        }

        switch (Mood)
        {
            case WorldMood.Vivid:
                s = System.Math.Min(s * 1.1f, 0.8f); // the ground of a whole world: strong, not neon
                v = System.Math.Min(v * 1.08f, 1f);
                break;
            case WorldMood.Pastel:
                s *= 0.55f;
                v += (1f - v) * 0.5f;
                break;
            case WorldMood.Earthy:
                s *= 0.75f;
                v *= 0.92f;
                break;
            case WorldMood.Gloomy:
                s *= 0.5f;
                v *= 0.72f;
                break;
        }

        return Pack(ToRgb(TreeHue, s, v));
    }

    /// <summary>A hue inside the arc of clean water, or the end of the arc that is nearer on the wheel.</summary>
    private static float InWaterArc(float hue)
    {
        if (hue >= WaterHueMin && hue <= WaterHueMax)
        {
            return hue;
        }

        float toMin = System.Math.Abs(hue - WaterHueMin), toMax = System.Math.Abs(hue - WaterHueMax);
        toMin = System.Math.Min(toMin, 1f - toMin);
        toMax = System.Math.Min(toMax, 1f - toMax);
        return toMin <= toMax ? WaterHueMin : WaterHueMax;
    }

    private static float Red(int rgb) => ((rgb >> 16) & 0xFF) / 255f;

    private static float Green(int rgb) => ((rgb >> 8) & 0xFF) / 255f;

    private static float Blue(int rgb) => (rgb & 0xFF) / 255f;

    private static int Pack((float R, float G, float B) c) => (Channel(c.R) << 16) | (Channel(c.G) << 8) | Channel(c.B);

    private static float Byte(uint word, int shift) => ((word >> shift) & 0xFFu) / 255f;

    private static float Wrap(float hue) => hue - (float)System.Math.Floor(hue);

    private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;

    private static int Channel(float v) => System.Math.Clamp((int)((v * 255f) + 0.5f), 0, 255);

    /// <summary>RGB (0..1, clamped) to hue (0..1), saturation and value. A grey has hue 0.</summary>
    public static void ToHsv(float r, float g, float b, out float h, out float s, out float v)
    {
        r = Clamp(r, 0f, 1f);
        g = Clamp(g, 0f, 1f);
        b = Clamp(b, 0f, 1f);
        float max = System.Math.Max(r, System.Math.Max(g, b));
        float min = System.Math.Min(r, System.Math.Min(g, b));
        float delta = max - min;
        v = max;
        s = max <= 0f ? 0f : delta / max;
        if (delta <= 1e-6f)
        {
            h = 0f;
            return;
        }

        float sector = max == r ? (g - b) / delta : max == g ? 2f + ((b - r) / delta) : 4f + ((r - g) / delta);
        h = Wrap(sector / 6f);
    }

    /// <summary>Hue (0..1), saturation and value to RGB 0..1.</summary>
    public static (float R, float G, float B) ToRgb(float h, float s, float v)
    {
        s = Clamp(s, 0f, 1f);
        v = Clamp(v, 0f, 1f);
        float hp = Wrap(h) * 6f;
        float c = v * s;
        float x = c * (1f - System.Math.Abs((hp % 2f) - 1f));
        (float r, float g, float b) = ((int)hp % 6) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        float m = v - c;
        return (Clamp(r + m, 0f, 1f), Clamp(g + m, 0f, 1f), Clamp(b + m, 0f, 1f));
    }
}

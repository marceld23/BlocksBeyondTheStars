// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Weather;

namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// One world's mist (#3269): how much valley mist it breeds (<see cref="Amount"/>, 0..1, the air density already folded in),
/// the colour of its mist and cloud fog (<see cref="ColorRgb"/>, 0xRRGGBB, −1 = none → the sky colour), how torn the mist is
/// (<see cref="Wispiness"/>, 0 a smooth blanket … 1 ragged wisps), how often fog weather comes compared with the type's own
/// weights (<see cref="FogFrequency"/>, a multiplier) and the cloud band at altitude (<see cref="CloudBaseY"/> ..
/// <see cref="CloudTopY"/>, absolute world Y; top ≤ base = no band). Built by <see cref="MistProfiles.For"/>.
/// </summary>
public readonly record struct MistProfile(float Amount, int ColorRgb, float Wispiness, float FogFrequency, float CloudBaseY, float CloudTopY)
{
    /// <summary>No mist, no colour of its own, the type's own fog chances and no cloud band — airless bodies, space-sky
    /// surfaces, void decks and the gas sea.</summary>
    public static MistProfile None => new(0f, -1, 0f, 1f, 0f, 0f);

    /// <summary>Whether the world has a cloud band (at least <see cref="MistProfiles.MinBandThickness"/> blocks thick).</summary>
    public bool HasCloudBand => CloudTopY - CloudBaseY >= MistProfiles.MinBandThickness;
}

/// <summary>A world's measured surface altitudes (#3272), world Y: the median ground and the upper tenth of the land (90th
/// percentile), from the generator's whole-world height sample. The cloud band is placed against them.</summary>
public readonly record struct TerrainAltitudes(int MedianY, int UpperY);

/// <summary>The palette family a type's mist colours come from (#3269), read from its data traits.</summary>
public enum MistFamily
{
    /// <summary>The general pastel set (pale blue, mint, lavender, peach, pale yellow, rose).</summary>
    Pastel,

    /// <summary>Hot and volcanic worlds: sulphur yellow, amber, rose-grey.</summary>
    Volcanic,

    /// <summary>Corrosive, contaminated or acid worlds: toxic green, yellow-green, violet.</summary>
    Toxic,

    /// <summary>Cold and glacial worlds: ice blue, cyan, lilac.</summary>
    Ice,

    /// <summary>Wet, green and flowering worlds: pale green, teal, pink, violet.</summary>
    Lush,

    /// <summary>Deserts and dry grassland: sand, peach, apricot.</summary>
    Arid,

    /// <summary>Crystal, alien and rainbow worlds: violet, cyan, rose, magenta, turquoise.</summary>
    Exotic,
}

/// <summary>
/// The per-world mist profile (#3269) — a pure function of (save seed, location id, planet type, air density), like
/// <see cref="AtmosphereTints"/> and <see cref="FloraTints"/>. The server ships the active world's values in the environment
/// message and uses the band's base as its "summits are one step wetter" line; the client can call the same function for any
/// other body. Every value draws from the well-mixed hash (<see cref="StableHashing"/>) with its own salt, so neighbouring
/// bodies ("sys0-p1", "sys0-p2") differ. A new look: nothing is persisted, and there is no older formula to preserve.
/// </summary>
public static class MistProfiles
{
    /// <summary>The mist of a type without its own <see cref="PlanetType.Mist"/> value.</summary>
    public const double DefaultMist = 0.45;

    /// <summary>How far one world strays from its type's mist value (±).</summary>
    public const double MistJitter = 0.15;

    /// <summary>The thinnest cloud band that still counts as one (blocks) — the client's <c>CloudBandRules</c> agrees.</summary>
    public const float MinBandThickness = 4f;

    /// <summary>The range of <see cref="MistProfile.FogFrequency"/>: an almost fog-free world … a fog world.</summary>
    public const float MinFogFrequency = 0.35f, MaxFogFrequency = 2.2f;

    /// <summary>The share of worlds whose mist is white or light grey (slightly cool or warm); the rest is clearly tinted.</summary>
    public const double WhiteShare = 0.4;

    /// <summary>Every mist colour is at least this light (Rec. 601 luma, 0..1) — it tints the view in front of the whole
    /// landscape, so it must never read dark.</summary>
    public const float MinLuma = 0.74f;

    private const ulong AmountSalt = 0x3269_0000_A307_0001UL;
    private const ulong WhiteSalt = 0x3269_0000_C010_0002UL;
    private const ulong HueSalt = 0x3269_0000_C010_0003UL;
    private const ulong ShadeSalt = 0x3269_0000_C010_0004UL;
    private const ulong WispSalt = 0x3269_0000_3159_0005UL;
    private const ulong FogSalt = 0x3269_0000_F06F_0006UL;
    private const ulong BandSalt = 0x3269_0000_BA4D_0007UL;

    // Tinted anchors per family: hue (degrees), saturation range and weight. Saturation stays moderate (≤ 0.45) and the
    // value high, so a tint reads clearly but never neon; MinLuma lifts the darker hues (blue, violet) toward white.
    private static readonly (float Hue, float SatLo, float SatHi, int Weight)[] PastelSet =
    {
        (210f, 0.20f, 0.32f, 3), // pale blue
        (150f, 0.20f, 0.32f, 2), // mint
        (262f, 0.18f, 0.30f, 2), // lavender
        (24f, 0.20f, 0.32f, 2),  // peach
        (52f, 0.22f, 0.34f, 2),  // pale yellow
        (342f, 0.18f, 0.30f, 2), // rose
    };

    private static readonly (float Hue, float SatLo, float SatHi, int Weight)[] VolcanicSet =
    {
        (56f, 0.30f, 0.45f, 4),  // sulphur yellow
        (37f, 0.28f, 0.40f, 3),  // amber
        (352f, 0.18f, 0.26f, 3), // rose-grey
    };

    private static readonly (float Hue, float SatLo, float SatHi, int Weight)[] ToxicSet =
    {
        (105f, 0.30f, 0.44f, 4), // toxic green
        (78f, 0.32f, 0.45f, 3),  // yellow-green
        (280f, 0.24f, 0.36f, 2), // violet
    };

    private static readonly (float Hue, float SatLo, float SatHi, int Weight)[] IceSet =
    {
        (205f, 0.20f, 0.34f, 4), // ice blue
        (186f, 0.22f, 0.36f, 3), // cyan
        (268f, 0.18f, 0.30f, 2), // lilac
    };

    private static readonly (float Hue, float SatLo, float SatHi, int Weight)[] LushSet =
    {
        (125f, 0.22f, 0.36f, 3), // pale green
        (168f, 0.22f, 0.36f, 3), // teal
        (335f, 0.20f, 0.32f, 2), // pink
        (285f, 0.20f, 0.32f, 2), // violet
    };

    private static readonly (float Hue, float SatLo, float SatHi, int Weight)[] AridSet =
    {
        (42f, 0.22f, 0.36f, 4), // sand
        (22f, 0.22f, 0.34f, 3), // peach
        (30f, 0.28f, 0.40f, 3), // apricot
    };

    private static readonly (float Hue, float SatLo, float SatHi, int Weight)[] ExoticSet =
    {
        (275f, 0.25f, 0.40f, 2), // violet
        (188f, 0.25f, 0.40f, 2), // cyan
        (345f, 0.22f, 0.36f, 2), // rose
        (312f, 0.25f, 0.38f, 2), // magenta
        (170f, 0.25f, 0.40f, 2), // turquoise
    };

    /// <summary>The mist profile of one world. <paramref name="atmosphereDensity"/> is that world's 0..1 air thickness (the
    /// server's per-world value; thicker air breeds more mist), <paramref name="airless"/> whether it has no air weather
    /// (an airless body or a space-sky surface). Airless, space-sky, void and gas worlds get <see cref="MistProfile.None"/>.</summary>
    public static MistProfile For(long worldSeed, string? locationId, PlanetType? planet, double atmosphereDensity, bool airless,
        TerrainAltitudes? terrain = null)
    {
        if (airless || planet is { IsAirless: true } || planet is { SpaceSky: true } || planet is { Void: true } || planet is { IsGasWorld: true })
        {
            return MistProfile.None;
        }

        float amount = AmountOf(worldSeed, locationId, planet, atmosphereDensity);
        var (baseY, topY) = CloudBand(worldSeed, locationId, planet, airless: false, terrain);
        return new MistProfile(
            amount,
            ColorOf(worldSeed, locationId, planet),
            WispinessOf(worldSeed, locationId, planet),
            FogFrequencyOf(worldSeed, locationId, amount),
            baseY,
            topY);
    }

    /// <summary>The band's base stands at least this far above the world's median ground (blocks), so a flat world never
    /// stands in its own clouds — only hills and summits that rise well above the land reach them.</summary>
    public const double MinBandClearance = 26.0;

    /// <summary>Without measured altitudes the type's base height stands in for the median ground; the real median of a world
    /// sits up to ~16 blocks above it (the height function scales the amplitude per style and drama roll), so the stand-in
    /// keeps a larger clearance.</summary>
    public const double FallbackBandClearance = 42.0;

    /// <summary>The cloud band of one world, absolute world Y, 14..34 blocks thick (growing with the type's cloud cover) and
    /// lifted 0..12 blocks per world. With the world's measured <paramref name="terrain"/> (the server's generator
    /// calibration) its base sits just above the upper tenth of the land and at least <see cref="MinBandClearance"/> above
    /// the median ground — mountain summits reach into it, a flat world's land never does. Without it (any view that has no
    /// generator, and the weather's summit line) the type's relief stands in: the classic cloud line, at least
    /// <see cref="FallbackBandClearance"/> above the base height. (0, 0) = none: airless, space-sky, void and gas worlds and
    /// types without cloud cover. Independent of the air density on purpose.</summary>
    public static (float BaseY, float TopY) CloudBand(long worldSeed, string? locationId, PlanetType? planet, bool airless,
        TerrainAltitudes? terrain = null)
    {
        if (airless || planet is null || planet.IsAirless || planet.SpaceSky || planet.Void || planet.IsGasWorld
            || planet.CloudDensity < 0.05)
        {
            return (0f, 0f);
        }

        ulong h = Word64(worldSeed, locationId, BandSalt);
        int lift = StableHashing.Index(h, 13); // 0..12 blocks above the clearance
        double cover = System.Math.Clamp(planet.CloudDensity, 0.0, 1.0);
        double shape = 0.75 + 0.5 * ((h & 0xFFFF) / 65535.0); // ×0.75..1.25 per world
        double thickness = System.Math.Clamp(14.0 + 20.0 * cover * shape, 14.0, 34.0);
        double floorY = terrain is { } t
            ? System.Math.Max(t.UpperY + 4.0, t.MedianY + MinBandClearance)
            : System.Math.Max(WeatherProjection.CloudLineY(planet), planet.BaseHeight + FallbackBandClearance);
        float baseY = (float)(floorY + lift);
        return (baseY, baseY + (float)thickness);
    }

    /// <summary>The altitude above which a position reads one weather ladder step wetter (summits in the cloud): the base of
    /// the type-relief cloud band (no measured altitudes, so every view agrees) where the world has a band, else the type's
    /// classic <see cref="WeatherProjection.CloudLineY"/>. The server's surface weather, its landing-pad weather and the orbit
    /// map all use this one line; the visible band of the active world is measured and may sit a little apart.</summary>
    public static double SummitLineY(long worldSeed, string? locationId, PlanetType? planet)
    {
        bool airless = planet is null || planet.IsAirless || planet.SpaceSky;
        var (baseY, topY) = CloudBand(worldSeed, locationId, planet, airless);
        return topY - baseY >= MinBandThickness ? baseY : WeatherProjection.CloudLineY(planet);
    }

    /// <summary>The palette family of a type, read from its data traits (never a key list): corrosive or contaminated air →
    /// toxic; crystal, alien or rainbow → exotic; cold or glacial → ice; hot or volcanic (not a sand desert) → volcanic;
    /// wet, flowering or densely green → lush; desert or savanna flora → arid; anything else → pastel.</summary>
    public static MistFamily FamilyOf(PlanetType? planet)
    {
        if (planet is null)
        {
            return MistFamily.Pastel;
        }

        string flora = planet.FloraTheme ?? string.Empty;
        if (planet.ContaminatedFauna || planet.CorrosiveAirChance > 0 || planet.AirDamagePerSecond > 0
            || string.Equals(planet.Precipitation, "acid", System.StringComparison.OrdinalIgnoreCase))
        {
            return MistFamily.Toxic;
        }

        if (planet.HasTag(TerrainTag.Crystal) || flora is "crystal" or "alien"
            || string.Equals(planet.WaterTint, "rainbow", System.StringComparison.OrdinalIgnoreCase))
        {
            return MistFamily.Exotic;
        }

        if (planet.BaseTemperature <= -5 || planet.HasTag(TerrainTag.Glacial))
        {
            return MistFamily.Ice;
        }

        if (planet.BaseTemperature >= 50 || flora == "ashen" || (planet.HasTag(TerrainTag.Volcanic) && flora != "desert"))
        {
            return MistFamily.Volcanic;
        }

        if (flora is "tropical" or "swamp" or "fungal" or "floral" || planet.HasTag(TerrainTag.Wetland) || planet.FloraDensity >= 0.15)
        {
            return MistFamily.Lush;
        }

        return flora is "desert" or "savanna" ? MistFamily.Arid : MistFamily.Pastel;
    }

    /// <summary>0..1: the type's mist ± <see cref="MistJitter"/>, times the air-density factor (0.5 in thin air … 1.2 in soupy
    /// air — the factor the client used to apply itself). A type with no mist (0) stays at 0.</summary>
    private static float AmountOf(long worldSeed, string? locationId, PlanetType? planet, double atmosphereDensity)
    {
        double typeMist = System.Math.Clamp(planet?.Mist ?? DefaultMist, 0.0, 1.0);
        if (typeMist <= 0)
        {
            return 0f;
        }

        double jitter = (Unit(worldSeed, locationId, AmountSalt) * 2.0 - 1.0) * MistJitter;
        double air = 0.5 + 0.7 * System.Math.Clamp(atmosphereDensity, 0.0, 1.0);
        return (float)System.Math.Clamp((typeMist + jitter) * air, 0.0, 1.0);
    }

    /// <summary>The mist colour (0xRRGGBB): the type's authored <see cref="PlanetType.MistColor"/>, else
    /// <see cref="WhiteShare"/> of the worlds white or light grey (cool or warm) and the rest a clear tint from the type's
    /// family palette. Always at least <see cref="MinLuma"/> light.</summary>
    private static int ColorOf(long worldSeed, string? locationId, PlanetType? planet)
    {
        if (planet is { MistColor: > 0 })
        {
            int authored = planet.MistColor & 0xFFFFFF;
            return Lift(((authored >> 16) & 0xFF) / 255f, ((authored >> 8) & 0xFF) / 255f, (authored & 0xFF) / 255f);
        }

        ulong shade = Word64(worldSeed, locationId, ShadeSalt);
        float u1 = (shade & 0xFFFF) / 65535f;
        float u2 = ((shade >> 16) & 0xFFFF) / 65535f;
        float u3 = ((shade >> 32) & 0xFFFF) / 65535f;
        if (Unit(worldSeed, locationId, WhiteSalt) < WhiteShare)
        {
            float hue = u3 < 0.5f ? 210f : 38f; // slightly cool or slightly warm
            return HsvToRgb(hue, 0.02f + 0.04f * u1, 0.86f + 0.12f * u2);
        }

        var palette = PaletteOf(FamilyOf(planet));
        var anchor = Pick(palette, Word64(worldSeed, locationId, HueSalt));
        float h = anchor.Hue + (u3 - 0.5f) * 12f; // ±6° per world
        float s = anchor.SatLo + (anchor.SatHi - anchor.SatLo) * u1;
        float v = 0.88f + 0.10f * u2;
        return HsvToRgb(h, s, v);
    }

    /// <summary>0..1 how torn the mist is: a per-world roll 0.15..1, a little higher on volatile and wind-carved worlds.</summary>
    private static float WispinessOf(long worldSeed, string? locationId, PlanetType? planet)
    {
        double w = 0.15 + 0.85 * Unit(worldSeed, locationId, WispSalt);
        double volatility = planet?.WeatherVolatility ?? 1.0;
        w += System.Math.Max(0.0, volatility - 1.0) * 0.3;
        if (planet is not null && planet.HasTag(TerrainTag.Wind))
        {
            w += 0.1;
        }

        return (float)System.Math.Clamp(w, 0.0, 1.0);
    }

    /// <summary>The multiplier on the fog and ground-fog chances, <see cref="MinFogFrequency"/>..<see cref="MaxFogFrequency"/>:
    /// a per-world roll skewed toward the mist amount (misty worlds fog more often), 1.0 at the middle of the range.</summary>
    private static float FogFrequencyOf(long worldSeed, string? locationId, float amount)
    {
        double t = System.Math.Clamp(0.6 * Unit(worldSeed, locationId, FogSalt) + 0.4 * amount, 0.0, 1.0);
        double f = t < 0.5
            ? MinFogFrequency + (1.0 - MinFogFrequency) * (t / 0.5)
            : 1.0 + (MaxFogFrequency - 1.0) * ((t - 0.5) / 0.5);
        return (float)f;
    }

    private static (float Hue, float SatLo, float SatHi, int Weight)[] PaletteOf(MistFamily family) => family switch
    {
        MistFamily.Volcanic => VolcanicSet,
        MistFamily.Toxic => ToxicSet,
        MistFamily.Ice => IceSet,
        MistFamily.Lush => LushSet,
        MistFamily.Arid => AridSet,
        MistFamily.Exotic => ExoticSet,
        _ => PastelSet,
    };

    private static (float Hue, float SatLo, float SatHi, int Weight) Pick((float Hue, float SatLo, float SatHi, int Weight)[] palette, ulong h)
    {
        int total = 0;
        foreach (var entry in palette)
        {
            total += entry.Weight;
        }

        int roll = StableHashing.Index(h, total);
        foreach (var entry in palette)
        {
            roll -= entry.Weight;
            if (roll < 0)
            {
                return entry;
            }
        }

        return palette[palette.Length - 1];
    }

    /// <summary>HSV (hue in degrees, saturation and value 0..1) to a packed colour, lifted to <see cref="MinLuma"/>.</summary>
    private static int HsvToRgb(float hueDeg, float s, float v)
    {
        float h = ((hueDeg % 360f) + 360f) % 360f / 60f;
        float c = v * s;
        float x = c * (1f - System.Math.Abs(h % 2f - 1f));
        (float r, float g, float b) = ((int)h % 6) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };
        float m = v - c;
        return Lift(r + m, g + m, b + m);
    }

    /// <summary>Blends a colour toward white until its luma reaches <see cref="MinLuma"/> (the hue stays), then packs it.</summary>
    private static int Lift(float r, float g, float b)
    {
        float luma = 0.299f * r + 0.587f * g + 0.114f * b;
        if (luma < MinLuma)
        {
            float t = (MinLuma - luma) / System.Math.Max(1e-4f, 1f - luma);
            r += (1f - r) * t;
            g += (1f - g) * t;
            b += (1f - b) * t;
        }

        return (Channel(r) << 16) | (Channel(g) << 8) | Channel(b);
    }

    private static int Channel(float v) => System.Math.Clamp((int)System.Math.Ceiling(v * 255f - 1e-3f), 0, 255);

    private static ulong Word64(long worldSeed, string? locationId, ulong salt)
        => StableHashing.Mix64(StableHashing.Mixed(locationId ?? string.Empty, worldSeed) ^ salt);

    private static double Unit(long worldSeed, string? locationId, ulong salt) => StableHashing.Unit(Word64(worldSeed, locationId, salt));
}

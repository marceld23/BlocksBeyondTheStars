// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>Authoritative day/night + weather + sun colour for the current planet (World systems).</summary>
public sealed class WorldEnvironment
{
    /// <summary>Time of day, 0..1 (0 = midnight, 0.5 = noon).</summary>
    public float TimeOfDay { get; set; }

    /// <summary>Full day length in seconds (the client advances time locally between updates).</summary>
    public float DayLengthSeconds { get; set; } = 600f;

    /// <summary>Monotonic total elapsed in-game days since world start — the shared, authoritative clock the
    /// client uses to place orbital bodies and compute their sun-lit phase (θ = 2π·SystemTimeDays/OrbitPeriodDays).
    /// Unlike <see cref="TimeOfDay"/> (which wraps 0..1 per day) this keeps counting, so phases drift over days.</summary>
    public double SystemTimeDays { get; set; }

    /// <summary>The active weather state. Ladder: clear / clouds / rain / storm. Events (#900): fog /
    /// ground_fog / drizzle / gale / blizzard / heatwave / acid_rain / ion_storm / meteor_shower /
    /// ember_fall / spore_bloom.</summary>
    public string Weather { get; set; } = "clear";

    /// <summary>0..1 weather strength. Since #900 a continuous envelope (attack → plateau → decay) around a
    /// per-episode peak, not a constant per state — so no two storms come out equally strong.</summary>
    public float Intensity { get; set; }

    /// <summary>How fast <see cref="Intensity"/> is changing, per second. The client extrapolates with it
    /// between the 5 s environment broadcasts, so a swelling storm ramps instead of stepping.</summary>
    public float IntensityRate { get; set; }

    /// <summary>Coarse grouping of <see cref="Weather"/> — "calm", "cloudy", "wet", "violent", "obscuring",
    /// "windy", "exotic". One switch for screen washes, audio beds and the HUD icon.</summary>
    public string WeatherFamily { get; set; } = "calm";

    /// <summary>Wind strength 0..1 (#900): cloud speed, precipitation slant, flora sway, the wind audio bed
    /// and the jetpack drift all scale with it.</summary>
    public float WindSpeed { get; set; }

    /// <summary>Wind direction in radians on the world XZ plane, drifting slowly. Precipitation and blown
    /// dust travel this way.</summary>
    public float WindDirection { get; set; }

    /// <summary>Where this world's season currently sits: 0 = dry season, 1 = wet season. A slow swing over
    /// the shared clock, seeded per world; biases storm likelihood and how fast planted flora regrows.</summary>
    public float SeasonWetness { get; set; } = 0.5f;

    /// <summary>Air temperature at the player, in °C (base per world + weather + day/night). Shown in the HUD.</summary>
    public float Temperature { get; set; } = 15f;

    /// <summary>Active precipitation form: none / rain / drizzle / sleet / snow / hail / ash / sandstorm /
    /// dust / acid / meteor / spores (climate- and event-driven).</summary>
    public string Precipitation { get; set; } = "none";

    /// <summary>Sun/star light colour, packed 0xRRGGBB.</summary>
    public int SunColor { get; set; } = 0xFFF6E8;

    /// <summary>Base cloud tint for this planet, packed 0xRRGGBB (storms darken it on the client).</summary>
    public int CloudColor { get; set; } = 0xEDEFF2;

    /// <summary>This world's daytime sky/atmosphere base hue, packed 0xRRGGBB. Worlds WITH an atmosphere
    /// (not <see cref="SpaceSky"/>) get a seeded per-world colour spanning blue→green→yellow→red, so the sky
    /// isn't always blue. The client uses it as the daytime sky base (and thus fog/ambient/reflections follow).
    /// Airless bodies show a space sky and ignore this.</summary>
    public int SkyColor { get; set; } = 0x8CBFF2;

    /// <summary>The planet's uniform flora base hue, packed 0xRRGGBB. All flora on the planet is desaturated
    /// to its texture luminance and re-tinted by this colour, so a world's plant life shares one base colour
    /// (green / brown / pink / purple …) regardless of the underlying tile. White = no tint.</summary>
    public int FloraTint { get; set; } = 0xFFFFFF;

    /// <summary>This world's water colour, packed 0xRRGGBB (#1758, school club wave 3), read only when
    /// <see cref="WaterTintMode"/> is 1. Additive: an older client ignores it, an older server sends the default.</summary>
    public int WaterTint { get; set; } = 0x336BD9;

    /// <summary>How the client colours the water (#1758): 0 = the classic blue (every existing world), 1 = multiply
    /// <see cref="WaterTint"/> in, 2 = static rainbow bands by position (the rainbow planet). Additive.</summary>
    public int WaterTintMode { get; set; }

    /// <summary>This world's walkable east–west circumference in blocks (longitude wrap + day/night span).
    /// Varies by body size — asteroids small, planets large — so the client wraps/renders at the right size.</summary>
    public int Circumference { get; set; } = 6000;

    /// <summary>Latitude (Z) bound from the equator (the invisible pole barrier), = Circumference / 4.</summary>
    public int LatitudeLimit { get; set; } = 1500;

    /// <summary>0..1 cloud cover for this planet (frequency + thickness; 0 = clear skies).</summary>
    public float CloudDensity { get; set; } = 0.45f;

    /// <summary>0..1 how thick/hazy the air is — drives the volumetric fog density (denser air → shorter view
    /// distance) and, server-side, the chance of fog weather. 0 on airless bodies.</summary>
    public float AtmosphereDensity { get; set; } = 0.4f;

    /// <summary>Whether the planet's atmosphere is breathable (no suit-oxygen drain on the surface).</summary>
    public bool Breathable { get; set; }

    /// <summary>Space sky on the surface (black + stars) — landable asteroids / airless bodies.</summary>
    public bool SpaceSky { get; set; }

    /// <summary>Active planet/biome key (e.g. "jungle", "desert", "ice", "lava") for client ambience.</summary>
    public string Biome { get; set; } = "rock";

    /// <summary>This world's gravity multiplier (1.0 = Earth-like baseline). Seeded per world from its size class
    /// (asteroids feather-light, moons low, planets full — occasionally heavy), so worlds feel physically
    /// different. The client scales jump height, walk speed, jetpack thrust and the fall-damage threshold from
    /// it; a ≥1-block jump is always preserved (lighter worlds jump higher, heavier worlds walk slower).</summary>
    public float GravityFactor { get; set; } = 1.0f;
}

/// <summary>Client → server: read the sky (#900). Sent by the weather scanner gadget; the server answers
/// with a <see cref="WeatherForecast"/> for the world the player is standing on.</summary>
public sealed class WeatherForecastRequest
{
}

/// <summary>One predicted weather episode: what is coming, when it starts and how long it holds.</summary>
public sealed class WeatherForecastEntry
{
    /// <summary>The predicted state key (same vocabulary as <see cref="WorldEnvironment.Weather"/>).</summary>
    public string State { get; set; } = "clear";

    /// <summary>Seconds from now until this episode begins.</summary>
    public float StartsInSeconds { get; set; }

    /// <summary>How long the episode is expected to hold, in seconds.</summary>
    public float DurationSeconds { get; set; }
}

/// <summary>
/// Server → client: the sky ahead (#900), as read by the weather scanner. Carries what is happening now,
/// the next few episodes, and how far off the nearest front is — enough to decide whether to set out or
/// sit this one out.
/// </summary>
public sealed class WeatherForecast
{
    /// <summary>The state right where the player is standing.</summary>
    public string Current { get; set; } = "clear";

    /// <summary>Seconds until the current episode gives way to the next.</summary>
    public float CurrentEndsInSeconds { get; set; }

    /// <summary>The coming episodes, soonest first.</summary>
    public List<WeatherForecastEntry> Upcoming { get; set; } = new();

    /// <summary>Blocks to the nearest weather front's edge, or −1 when this world carries none.</summary>
    public float FrontDistance { get; set; } = -1f;

    /// <summary>Where this world's season sits, 0 (dry) .. 1 (wet).</summary>
    public float SeasonWetness { get; set; } = 0.5f;
}

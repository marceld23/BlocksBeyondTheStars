// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>Relative frequency for procedural features (maps to a spawn weight).</summary>
public enum Frequency
{
    Off,
    VeryRare,
    Rare,
    Normal,
    Frequent,
}

/// <summary>
/// Maps a <see cref="Frequency"/> level to the numeric knobs the generators consume. The exact
/// constants are tuning values and may be retuned; the intended invariants are: every mapping is
/// monotone non-decreasing in enum order, <see cref="Frequency.Off"/> is exactly 0 wherever
/// "off must mean off" (<see cref="OreFactor"/> is the documented exception), and factor-style
/// mappings return 1.0 at the owning property's default level so existing worlds are unchanged.
/// </summary>
public static class FrequencyExtensions
{
    /// <summary>Relative weight for weighted selection — consumed by the universe generator's
    /// planet-type roll (<see cref="WorldDescription.PlanetTypeFrequencies"/>); 0 means the
    /// feature never spawns.</summary>
    public static int Weight(this Frequency f) => f switch
    {
        Frequency.Off => 0,
        Frequency.VeryRare => 1,
        Frequency.Rare => 3,
        Frequency.Normal => 8,
        Frequency.Frequent => 16,
        _ => 0,
    };

    /// <summary>Probability in [0,1] for independent chance rolls — e.g. the universe
    /// generator's space-station and wreck gates (<see cref="WorldDescription.SpaceStations"/>,
    /// <see cref="WorldDescription.Wrecks"/>) and the settlement/station template-use rolls.</summary>
    public static double Probability(this Frequency f) => f switch
    {
        Frequency.Off => 0.0,
        Frequency.VeryRare => 0.05,
        Frequency.Rare => 0.15,
        Frequency.Normal => 0.4,
        Frequency.Frequent => 0.75,
        _ => 0.0,
    };

    /// <summary>Flora/tree density multiplier applied to per-planet flora generation
    /// (world options / <see cref="WorldDescription.FloraDensity"/>; Normal = 1.0 = unchanged,
    /// Off = barren).</summary>
    public static double FloraFactor(this Frequency f) => f switch
    {
        Frequency.Off => 0.0,
        Frequency.VeryRare => 0.45,
        Frequency.Rare => 0.7,
        Frequency.Normal => 1.0,
        Frequency.Frequent => 1.6,
        _ => 1.0,
    };

    /// <summary>Ore-richness factor (world options). The DEFAULT of <see cref="WorldDescription.RareResources"/>
    /// is Rare, so Rare maps to 1.0 (existing worlds unchanged); Off still leaves lean veins — a world
    /// with no ore at all would dead-end the progression.</summary>
    public static double OreFactor(this Frequency f) => f switch
    {
        Frequency.Off => 0.55,
        Frequency.VeryRare => 0.75,
        Frequency.Rare => 1.0,
        Frequency.Normal => 1.35,
        Frequency.Frequent => 1.8,
        _ => 1.0,
    };

    /// <summary>Structure-density factor (settlements/wrecks/vaults; Normal = today's moderate density).</summary>
    public static double StructureFactor(this Frequency f) => f switch
    {
        Frequency.Off => 0.0,
        Frequency.VeryRare => 0.4,
        Frequency.Rare => 0.7,
        Frequency.Normal => 1.0,
        Frequency.Frequent => 1.7,
        _ => 1.0,
    };

    /// <summary>Danger multiplier (#547) for <see cref="WorldDescription.Danger"/>: scales hostile
    /// encounter odds (space ambush chance, bandit-camp presence) on top of the per-archetype gates.
    /// Normal = 1.0 so existing saves (and the default) behave exactly as before; Off disables them.</summary>
    public static double DangerFactor(this Frequency f) => f switch
    {
        Frequency.Off => 0.0,
        Frequency.VeryRare => 0.5,
        Frequency.Rare => 0.75,
        Frequency.Normal => 1.0,
        Frequency.Frequent => 1.5,
        _ => 1.0,
    };
}

/// <summary>
/// The admin-defined structured description of a world's universe (technical requirements /
/// `anf_admin_einstellungen.md` §8): how many systems, how dense the bodies, which planet
/// types appear and how often. Combined with the seed it deterministically produces the
/// galaxy layout.
/// </summary>
public sealed class WorldDescription
{
    public int StarSystemCount { get; set; } = 8;

    public int PlanetsPerSystemMin { get; set; } = 2;
    public int PlanetsPerSystemMax { get; set; } = 6;

    public int MoonsPerPlanetMin { get; set; }
    public int MoonsPerPlanetMax { get; set; } = 3;

    public Frequency AsteroidFields { get; set; } = Frequency.Normal;
    public Frequency SpaceStations { get; set; } = Frequency.Rare;
    public Frequency Wrecks { get; set; } = Frequency.Normal;

    /// <summary>Planet-type key → frequency. Empty means "use all known planet types at Normal".</summary>
    public Dictionary<string, Frequency> PlanetTypeFrequencies { get; set; } = new();

    public Frequency RareResources { get; set; } = Frequency.Rare;
    public Frequency Danger { get; set; } = Frequency.Normal;

    /// <summary>Per-system archetype variance (#546): when true, every star system rolls a character
    /// class (lone giant, swarm, belt, hub, desolate, pirate haven, twin worlds …) that shapes its
    /// planet/moon/asteroid/station rolls, sizes and inhabitants. MUST default to false: the galaxy is
    /// re-derived from the seed on every start, so flipping this on an existing save would change body
    /// counts and orphan already-visited worlds. New worlds switch it on at creation (ServerConfig's
    /// default description carries true; a loaded save keeps whatever its metadata stored).</summary>
    public bool SystemVariance { get; set; }

    /// <summary>Asteroid-belt layout (#683): when true, a system's landable asteroids share 1–2 orbit
    /// annuli (real BELTS — big systems may roll a second, inner one) instead of scattering across the
    /// whole disc. MUST default to false for the same reason as <see cref="SystemVariance"/>: the galaxy
    /// is re-derived from the seed on every start, so flipping this on an existing save would move the
    /// asteroid bodies players have visited or set waypoints to. New worlds switch it on at creation
    /// (ServerConfig's default description carries true; a loaded save keeps whatever it stored).</summary>
    public bool AsteroidBelts { get; set; }

    /// <summary>Continents &amp; real oceans (#704): when true, large planets (circumference ≥ 8000) may
    /// roll a bimodal continental-platform/ocean-basin terrain regime (~50 % of eligible bodies) — real
    /// continents separated by real oceans, basalt continents in lava oceans on the volcanic types. MUST
    /// default to false for the same reason as <see cref="SystemVariance"/>: terrain is re-derived from
    /// the seed, and flipping this on an existing save would relocate the oceans wholesale — a player
    /// base could wake up on the seabed. New worlds switch it on at creation (ServerConfig's default
    /// description carries true; a loaded save keeps whatever it stored).</summary>
    public bool TerrainContinents { get; set; }

    // --- World options (creation-time; baked into the save's metadata — they shape worldgen) ---

    /// <summary>Flora/tree density factor applied on top of each world's seeded variation.</summary>
    public Frequency FloraDensity { get; set; } = Frequency.Normal;

    /// <summary>How readily inhabited/ruined settlements stamp on suitable worlds.</summary>
    public Frequency Settlements { get; set; } = Frequency.Normal;

    /// <summary>How readily a crashed-ship wreck stamps on a world's surface.</summary>
    public Frequency PlanetWrecks { get; set; } = Frequency.Normal;

    /// <summary>How readily buried vault ruins stamp (0–2 per world at Normal).</summary>
    public Frequency Vaults { get; set; } = Frequency.Normal;

    /// <summary>Scales the spawn weights of EXOTIC planet types (data flag `exotic` in planets.json)
    /// relative to the common ones — one slider instead of 17 (an advanced per-type page can still
    /// fill <see cref="PlanetTypeFrequencies"/>, which always wins when non-empty).</summary>
    public Frequency ExoticWorlds { get; set; } = Frequency.Normal;

    // --- Hand-designed structure templates (editor output; rolled in place of the procedural generator) ---

    /// <summary>How readily a space station slot stamps a hand-designed template instead of a procedural
    /// one (uses <see cref="FrequencyExtensions.Probability"/> as the chance; Off ⇒ always procedural).
    /// Only ever fires when the matching tier sub-pool has at least one enabled template.</summary>
    public Frequency StationTemplateUse { get; set; } = Frequency.Rare;

    /// <summary>Settlement twin of <see cref="StationTemplateUse"/>.</summary>
    public Frequency SettlementTemplateUse { get; set; } = Frequency.Rare;

    /// <summary>Structure-template packs enabled for this world (matches <c>StructureTemplate.Pack</c>).
    /// Empty ⇒ all packs allowed. Lets a world opt into a subset of authored stations/settlements.</summary>
    public List<string> EnabledStructurePacks { get; set; } = new();
}

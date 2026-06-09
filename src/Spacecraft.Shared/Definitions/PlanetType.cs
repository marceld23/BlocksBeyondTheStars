namespace Spacecraft.Shared.Definitions;

/// <summary>One biome's surface make-up within a planet (World systems).</summary>
public sealed class Biome
{
    public string SurfaceBlock { get; set; } = "dirt";
    public string SubSurfaceBlock { get; set; } = "dirt";
}

/// <summary>An ore vein generation rule for a planet type.</summary>
public sealed class OreVein
{
    /// <summary>Block key placed when the vein noise threshold is met (e.g. "iron_ore").</summary>
    public string Block { get; set; } = string.Empty;

    /// <summary>0..1 — higher means more common. Compared against vein noise.</summary>
    public double Rarity { get; set; } = 0.1;

    /// <summary>Depth band below the surface in which this ore can spawn.</summary>
    public int MinDepth { get; set; }
    public int MaxDepth { get; set; } = 256;
}

/// <summary>
/// Data-driven planet generation profile, loaded from <c>data/planets.json</c>. Drives
/// the deterministic, seed-based world generator (block palette, terrain shape, ores,
/// caves). Same data is used on client (preview) and server (authoritative).
/// </summary>
public sealed class PlanetType
{
    public string Key { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Average surface height (world Y).</summary>
    public int BaseHeight { get; set; } = 64;

    /// <summary>Maximum deviation of the surface from <see cref="BaseHeight"/>.</summary>
    public int Amplitude { get; set; } = 16;

    /// <summary>Horizontal feature scale; larger = smoother, broader terrain.</summary>
    public double TerrainScale { get; set; } = 48.0;

    /// <summary>Airless barren bodies (landable asteroids, and — set per-world — airless moons): replace the
    /// rolling terrain with mostly flat regolith pocked with round impact craters (item 33).</summary>
    public bool Cratered { get; set; }

    public string SurfaceBlock { get; set; } = "dirt";
    public string SubSurfaceBlock { get; set; } = "dirt";
    public string DeepBlock { get; set; } = "stone";

    /// <summary>Thickness of the surface/sub-surface layer before deep blocks begin.</summary>
    public int SurfaceDepth { get; set; } = 4;

    public List<OreVein> Ores { get; set; } = new();

    /// <summary>3D-noise threshold above which a solid cell is carved into a cave (0 disables caves).</summary>
    public double CaveThreshold { get; set; } = 0.0;

    /// <summary>Probability per eligible deep cell of spawning a rare data cache (0 disables).</summary>
    public double DataCacheRarity { get; set; } = 0.0;

    // --- Day/night & weather (World systems) ---

    /// <summary>Length of a full day on this planet, in seconds.</summary>
    public double DayLengthSeconds { get; set; } = 600.0;

    /// <summary>0..1 bias toward rain/storm weather on this planet (deserts low, ocean worlds high).</summary>
    public double StormChance { get; set; } = 0.35;

    /// <summary>Base air temperature for this planet type, in °C. Each world adds a small seeded variation, plus
    /// weather + day/night, so there are also "especially hot/cold" worlds. Drives the precipitation form.</summary>
    public double BaseTemperature { get; set; } = 15.0;

    /// <summary>
    /// Weather behaviour: "dynamic" (changes over time), "clear" (no clouds, never changes) or
    /// "overcast" (always cloudy). Lets some planets have no weather at all.
    /// </summary>
    public string Weather { get; set; } = "dynamic";

    // --- World variety (size & biomes) ---

    /// <summary>
    /// Biomes blended across the surface. Empty = single biome from <see cref="SurfaceBlock"/>;
    /// one entry = single biome; several = a multi-biome world (chosen per column by noise).
    /// </summary>
    public List<Biome> Biomes { get; set; } = new();

    /// <summary>Soft world radius in blocks (0 = effectively unbounded). Informational for now.</summary>
    public int WorldRadius { get; set; }

    /// <summary>0..1 chance of surface flora per eligible column (0 = no plants). Bounded: one plant per cell.</summary>
    public double FloraDensity { get; set; }

    /// <summary>0..1 — how much surface water this world has (raises the sea level so more basins flood); only
    /// worlds with an atmosphere get water. <c>null</c> = auto (atmosphere worlds get a moderate amount).</summary>
    public double? WaterAbundance { get; set; }

    /// <summary>0..1 — how much surface lava this world has (lava seas in basins on volcanic/airless worlds).
    /// <c>null</c> = auto (volcanic worlds get a moderate amount). Watery worlds get no surface lava.</summary>
    public double? LavaAbundance { get; set; }

    /// <summary>Per-column chance of a multi-block tree (trunk + leaf crown) on grass/dirt ground. <c>null</c>
    /// = auto (a small density on worlds that have flora). 0 = no trees.</summary>
    public double? TreeDensity { get; set; }

    /// <summary>Base cloud tint, packed 0xRRGGBB (storms darken it client-side). Ash worlds are grey-brown,
    /// deserts sandy, ice pale blue. Used for both the surface cloud layer and the view from space.</summary>
    public int CloudColor { get; set; } = 0xEDEFF2;

    /// <summary>0..1 cloud cover: how many/how thick the clouds are (frequency + how far they cut visibility).
    /// 0 = clear skies. Airless bodies have none.</summary>
    public double CloudDensity { get; set; } = 0.45;

    /// <summary>
    /// How much life this world has: "none" (barren), "few" or "many". Drives how many
    /// procedural <see cref="CreatureSpecies"/> the world derives and the live spawn caps.
    /// </summary>
    public string CreatureAbundance { get; set; } = "few";

    /// <summary>
    /// Atmosphere type: "breathable" (no suit-oxygen drain on the surface), "toxic"
    /// (non-breathable — drains, the default) or "none" (airless — drains). Aboard the ship
    /// always regenerates regardless.
    /// </summary>
    public string Atmosphere { get; set; } = "toxic";

    /// <summary>True for airless bodies (atmosphere "none"): landable asteroids + airless moons/planets. No
    /// native flora or fauna grow/live here (it's a barren, lifeless surface).</summary>
    public bool IsAirless => string.Equals(Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// When true, the sky is always space (black + stars) even on the surface — the system's sun
    /// is still visible. Used by landable asteroids / airless bodies; normal planets keep a sky.
    /// </summary>
    public bool SpaceSky { get; set; }

    /// <summary>
    /// 0..1 — how much extractable oxygen a non-breathable atmosphere holds. The suit's oxygen
    /// extractor reduces the drain proportionally; airless ("none") worlds are 0 (nothing to
    /// extract). Ignored on breathable worlds (no drain anyway).
    /// </summary>
    public double OxygenExtractability { get; set; } = 0.5;

    /// <summary>
    /// Absolute world Y above which an on-foot player has climbed out of the atmosphere and is "in
    /// space" — zero-g float + suit-oxygen drain + a space sky (item 10). Set well above the tallest
    /// terrain so it can only be reached by building a tower; thick/breathable worlds sit high, airless
    /// bodies low (a short climb). 0 (the default) disables the feature for that body (e.g. void worlds).
    /// </summary>
    public double AtmosphereHeight { get; set; }

    /// <summary>
    /// Whether the universe generator may pick this type for a random system planet. Special
    /// bodies (e.g. landable asteroids) set this false so they don't appear as ordinary planets.
    /// </summary>
    public bool Selectable { get; set; } = true;

    /// <summary>Relative likelihood this (Selectable) type is chosen for a system planet/moon — higher = more
    /// common. Mirrors the <c>Frequency</c> weights (VeryRare 1, Rare 3, Normal 8, Frequent 16). Lets common
    /// worlds (rock/ice/desert/ocean) dominate while exotic ones (fungal/crystalline/floating) stay a find.</summary>
    public int SpawnWeight { get; set; } = 8;

    /// <summary>
    /// When true the world generates as pure empty space (all air, no terrain/caves/ore/flora). Used by
    /// orbital space stations, which exist as their own free-floating location with only their stamped
    /// structure in the void (space sky, life support — see the station planet type).
    /// </summary>
    public bool Void { get; set; }
}

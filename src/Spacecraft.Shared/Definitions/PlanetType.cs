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
}

namespace Spacecraft.Shared.Definitions;

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
}
